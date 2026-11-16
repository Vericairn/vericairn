// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr
//
// vericairn-enclave: Secure Enclave custody for Vericairn device keys on macOS. A key is generated inside the Secure Enclave and
// kept as its enclave-wrapped blob, which only this Mac's enclave can use, so a copied file signs nothing anywhere else. A key
// created with --presence asks for Touch ID or the login password before it signs. The command line calls this helper, which
// keeps no state between its processes. It uses CryptoKit directly, so it needs no keychain entitlement.
//
//   vericairn-enclave available
//   vericairn-enclave create <blob> [--presence]        prints the public key (SubjectPublicKeyInfo, base64)
//   vericairn-enclave public <blob>                     prints the public key (SubjectPublicKeyInfo, base64)
//   vericairn-enclave sign <blob> <digest-hex> [reason] prints the ECDSA P-256 signature of the 32-byte digest (IEEE P1363, base64);
//                                                       its dialog opens with "sign an unread Vericairn digest", since it cannot
//                                                       read what the digest is of
//   vericairn-enclave sign-record <blob> [reason]       reads one record's canonical bytes on standard input, checks they are in the
//                                                       compact canonical form, hashes them itself and prints the signature of that
//                                                       content hash; its dialog opens with "sign Vericairn record <type> <short
//                                                       hash>", the reason following, cut to fit
//   vericairn-enclave session <blob>                    a presence session (design/presence-sessions.md §§ 6 to 9): one presence
//                                                       check, then signatures only on records the announced list covers, for at
//                                                       most its limit, over the protocol below
//
// Exit codes: 0 done (a session closed by its runtime), 2 usage, 3 no Secure Enclave on this machine, 4 the operation failed or
// was declined (a session ended for any reason but close), 5 a record was refused as not being in the compact canonical form.
//
// The session protocol (vericairn.presence-session/1). The runtime and the helper exchange one compact JSON object per line on
// standard input and output, UTF-8, ended by a line feed, at most 8 MiB; standard error carries diagnostics only. Each message
// names itself in its "type" member; byte strings are base64url without padding. The helper writes every message in the compact
// canonical form; it reads the runtime's as strict JSON (no repeated member, no fraction or exponent, nothing after the object).
//
//   runtime  open      terms (the terms' canonical bytes), bytes (each exact entry's record bytes, then each reference's)
//   helper   prompting nonce (16 bytes), fingerprint; sent before the dialog opens
//   helper   opened    openedAt (the wall clock as a record timestamp), limitSeconds
//   runtime  sign      record (the record's canonical bytes, without hash and signatures)
//   helper   signed    signature (64 bytes, IEEE P1363), entry (its index), left, seconds
//   runtime  status    (none); the helper answers status with left and seconds
//   runtime  close     (none)
//   helper   refused   code, detail
//   helper   ended     reason: closed, limit, lock, sleep, refusal, parent, input or declined
//
// A message the protocol does not expect is refused as terms.invalid before opening and as record.form after it. The session
// ends at close, at its limit (on a clock that counts through sleep), when the screen locks, the user session is switched away,
// the display or the Mac sleeps, at its first refusal, when its input closes and when its parent process exits; the helper then
// invalidates its authentication context and exits.
//
// The release helper. A release build links its identity into the section __TEXT,__vericairn as the canonical JSON object
// {"identifier":"<code-signing identifier>","team":"<Developer ID team identifier>"} (swiftc -Xlinker -sectcreate -Xlinker
// __TEXT -Xlinker __vericairn -Xlinker <file>), so the code signature covers it. A build without that section, such as a build
// from source, is a development build and opens no session (helper.unsigned); one with it opens a session only while its own
// running code meets the release requirement: Apple's Developer ID anchor, that team, that identifier, the hardened runtime, and
// no get-task-allow entitlement nor an entitlement that would let another process inject code (design/presence-sessions.md § 11).
//
// Compiled with -D VERICAIRN_SESSION_TESTS, the helper also takes the test hooks its automated tests use: a controlled clock, a
// skipped self-check, a simulated presence check and signal, and the dialog's text on standard error. A release build is
// compiled without that flag, so none of them exists in it.

import AppKit
import CoreGraphics
import CryptoKit
import Foundation
import IOKit
import IOKit.pwr_mgt
import LocalAuthentication
import Security

/// A digest computed elsewhere: Vericairn signs the 32 bytes of a record's content hash and never hashes them again.
struct Precomputed: Digest {
    static var byteCount: Int { 32 }
    let bytes: [UInt8]

    func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try bytes.withUnsafeBytes(body) }

    func makeIterator() -> IndexingIterator<[UInt8]> { bytes.makeIterator() }

    var description: String { bytes.map { String(format: "%02x", $0) }.joined() }
}

func writeAll(_ descriptor: Int32, _ bytes: [UInt8]) -> Bool {
    var offset = 0
    while offset < bytes.count {
        let written = bytes[offset...].withUnsafeBytes { write(descriptor, $0.baseAddress, $0.count) }
        if written < 0 {
            if errno == EINTR { continue }
            return false
        }
        offset += written
    }
    return true
}

func diagnose(_ message: String) {
    _ = writeAll(2, Array((message + "\n").utf8))
}

func fail(_ code: Int32, _ message: String) -> Never {
    diagnose(message)
    exit(code)
}

func requireEnclave() {
    if !SecureEnclave.isAvailable {
        fail(3, "This Mac has no Secure Enclave available to this process.")
    }
}

func readBlob(_ path: String) -> Data {
    guard let blob = FileManager.default.contents(atPath: path) else {
        fail(4, "No key blob at the path given.")
    }
    return blob
}

func hexBytes(_ text: String) -> [UInt8]? {
    guard text.utf8.count == 64 else { return nil }
    var bytes = [UInt8]()
    var index = text.startIndex
    while index < text.endIndex {
        let next = text.index(index, offsetBy: 2)
        guard let byte = UInt8(text[index..<next], radix: 16) else { return nil }
        bytes.append(byte)
        index = next
    }
    return bytes
}

func hex(_ bytes: [UInt8]) -> String { bytes.map { String(format: "%02x", $0) }.joined() }

func sha256(_ bytes: [UInt8]) -> [UInt8] { Array(SHA256.hash(data: bytes)) }

#if VERICAIRN_SESSION_TESTS
let testEnvironment = ProcessInfo.processInfo.environment
#endif

// MARK: - Strict JSON

/// A JSON value. Strings and member names are kept as their UTF-8 bytes and compared byte for byte, never by Unicode equivalence,
/// so two values the canonical form tells apart are never taken for one.
indirect enum JSON {
    case object([(key: [UInt8], value: JSON)])
    case array([JSON])
    case string([UInt8])
    case integer(Int64)
    case bool(Bool)
    case null

    subscript(_ name: String) -> JSON? {
        guard case .object(let members) = self else { return nil }
        let key = Array(name.utf8)
        return members.first(where: { $0.key == key })?.value
    }

    var members: [(key: [UInt8], value: JSON)]? { if case .object(let m) = self { return m }; return nil }
    var items: [JSON]? { if case .array(let a) = self { return a }; return nil }
    var bytes: [UInt8]? { if case .string(let s) = self { return s }; return nil }
    var integer: Int64? { if case .integer(let n) = self { return n }; return nil }
    var bool: Bool? { if case .bool(let b) = self { return b }; return nil }

    /// A string value as text, only when it is ASCII: every name the helper compares as text is ASCII, and Swift compares text by
    /// Unicode equivalence (the Kelvin sign equals K), which must never make two different values one.
    var text: String? {
        guard let value = bytes, value.allSatisfy({ $0 < 0x80 }) else { return nil }
        return String(decoding: value, as: UTF8.self)
    }

    /// The member names, each as text when it is ASCII and otherwise as a name no member of the protocol has.
    var keys: [String] { (members ?? []).map { $0.key.allSatisfy { $0 < 0x80 } ? String(decoding: $0.key, as: UTF8.self) : "\u{0}" } }
}

func utf16Units(_ utf8: [UInt8]) -> [UInt16] { Array(String(decoding: utf8, as: UTF8.self).utf16) }

struct JSONFailure: Error { let detail: String }

/// Parses JSON strictly. In canonical mode the bytes must be exactly the compact canonical form of the record formats: members
/// sorted by UTF-16 code units, no member twice, no white space, only RFC 8785's escapes and plain integers within ±(2^53 − 1).
/// Otherwise any JSON white space, member order and escape is read, but a repeated member, a fraction, an exponent, a lone
/// surrogate and anything after the value are still refused.
struct JSONParser {
    static let maxDepth = 64
    static let maxSafeInteger: Int64 = 9_007_199_254_740_991
    private let b: [UInt8]
    private let canonical: Bool
    private var i = 0
    private var depth = 0

    private init(_ bytes: [UInt8], canonical: Bool) {
        b = bytes
        self.canonical = canonical
    }

    static func parse(_ bytes: [UInt8], canonical: Bool) throws -> JSON {
        var parser = JSONParser(bytes, canonical: canonical)
        parser.space()
        let value = try parser.value()
        parser.space()
        guard parser.i == bytes.count else { throw JSONFailure(detail: "bytes follow the value") }
        return value
    }

    private mutating func space() {
        guard !canonical else { return }
        while i < b.count, b[i] == 0x20 || b[i] == 0x09 || b[i] == 0x0A || b[i] == 0x0D { i += 1 }
    }

    private mutating func value() throws -> JSON {
        guard i < b.count else { throw JSONFailure(detail: "the value ends early") }
        switch b[i] {
        case 0x7B: return try object()
        case 0x5B: return try array()
        case 0x22: return .string(try string())
        case 0x74: try literal("true"); return .bool(true)
        case 0x66: try literal("false"); return .bool(false)
        case 0x6E: try literal("null"); return .null
        case 0x2D, 0x30...0x39: return .integer(try number())
        default: throw JSONFailure(detail: "a value is expected")
        }
    }

    private mutating func literal(_ word: String) throws {
        let expected = Array(word.utf8)
        guard i + expected.count <= b.count, Array(b[i..<i + expected.count]) == expected else { throw JSONFailure(detail: "an unknown literal") }
        i += expected.count
    }

    private mutating func object() throws -> JSON {
        depth += 1
        defer { depth -= 1 }
        guard depth <= JSONParser.maxDepth else { throw JSONFailure(detail: "the value is nested too deeply") }
        i += 1
        space()
        var members: [(key: [UInt8], value: JSON)] = []
        if i < b.count, b[i] == 0x7D {
            i += 1
            return .object(members)
        }
        var previous: [UInt16]?
        var seen = Set<[UInt8]>()
        while true {
            space()
            guard i < b.count, b[i] == 0x22 else { throw JSONFailure(detail: "a member name is expected") }
            let key = try string()
            if canonical {
                let units = utf16Units(key)
                if let before = previous, !before.lexicographicallyPrecedes(units) {
                    throw JSONFailure(detail: "the members are not sorted by UTF-16 code units, or one is repeated")
                }
                previous = units
            } else if !seen.insert(key).inserted {
                throw JSONFailure(detail: "a member is repeated")
            }
            space()
            guard i < b.count, b[i] == 0x3A else { throw JSONFailure(detail: "a colon is expected") }
            i += 1
            space()
            members.append((key, try value()))
            space()
            guard i < b.count else { throw JSONFailure(detail: "the object ends early") }
            if b[i] == 0x2C {
                i += 1
                continue
            }
            if b[i] == 0x7D {
                i += 1
                return .object(members)
            }
            throw JSONFailure(detail: "a comma or the object's end is expected")
        }
    }

    private mutating func array() throws -> JSON {
        depth += 1
        defer { depth -= 1 }
        guard depth <= JSONParser.maxDepth else { throw JSONFailure(detail: "the value is nested too deeply") }
        i += 1
        space()
        var items: [JSON] = []
        if i < b.count, b[i] == 0x5D {
            i += 1
            return .array(items)
        }
        while true {
            space()
            items.append(try value())
            space()
            guard i < b.count else { throw JSONFailure(detail: "the array ends early") }
            if b[i] == 0x2C {
                i += 1
                continue
            }
            if b[i] == 0x5D {
                i += 1
                return .array(items)
            }
            throw JSONFailure(detail: "a comma or the array's end is expected")
        }
    }

    private mutating func hex4() throws -> UInt32 {
        guard i + 4 <= b.count else { throw JSONFailure(detail: "an escape ends early") }
        var value: UInt32 = 0
        for _ in 0..<4 {
            let c = b[i]
            let digit: UInt32
            switch c {
            case 0x30...0x39: digit = UInt32(c - 0x30)
            case 0x61...0x66: digit = UInt32(c - 0x61 + 10)
            case 0x41...0x46 where !canonical: digit = UInt32(c - 0x41 + 10)
            default: throw JSONFailure(detail: canonical ? "a \\u escape is written with lower-case hexadecimal digits" : "a \\u escape needs four hexadecimal digits")
            }
            value = value * 16 + digit
            i += 1
        }
        return value
    }

    private static func appendScalar(_ scalar: UInt32, _ out: inout [UInt8]) {
        switch scalar {
        case 0..<0x80:
            out.append(UInt8(scalar))
        case 0x80..<0x800:
            out.append(UInt8(0xC0 | (scalar >> 6)))
            out.append(UInt8(0x80 | (scalar & 0x3F)))
        case 0x800..<0x10000:
            out.append(UInt8(0xE0 | (scalar >> 12)))
            out.append(UInt8(0x80 | ((scalar >> 6) & 0x3F)))
            out.append(UInt8(0x80 | (scalar & 0x3F)))
        default:
            out.append(UInt8(0xF0 | (scalar >> 18)))
            out.append(UInt8(0x80 | ((scalar >> 12) & 0x3F)))
            out.append(UInt8(0x80 | ((scalar >> 6) & 0x3F)))
            out.append(UInt8(0x80 | (scalar & 0x3F)))
        }
    }

    /// The length of the well-formed UTF-8 sequence at the index (The Unicode Standard, table 3-7); it refuses overlong forms,
    /// encoded surrogates and anything beyond U+10FFFF.
    private func sequenceLength() throws -> Int {
        func continuation(_ offset: Int, _ low: UInt8 = 0x80, _ high: UInt8 = 0xBF) -> Bool {
            i + offset < b.count && b[i + offset] >= low && b[i + offset] <= high
        }
        let lead = b[i]
        let ok: Bool
        let length: Int
        switch lead {
        case 0xC2...0xDF: length = 2; ok = continuation(1)
        case 0xE0: length = 3; ok = continuation(1, 0xA0, 0xBF) && continuation(2)
        case 0xE1...0xEC, 0xEE...0xEF: length = 3; ok = continuation(1) && continuation(2)
        case 0xED: length = 3; ok = continuation(1, 0x80, 0x9F) && continuation(2)
        case 0xF0: length = 4; ok = continuation(1, 0x90, 0xBF) && continuation(2) && continuation(3)
        case 0xF1...0xF3: length = 4; ok = continuation(1) && continuation(2) && continuation(3)
        case 0xF4: length = 4; ok = continuation(1, 0x80, 0x8F) && continuation(2) && continuation(3)
        default: length = 0; ok = false
        }
        guard ok else { throw JSONFailure(detail: "the bytes are not well-formed UTF-8") }
        return length
    }

    private mutating func string() throws -> [UInt8] {
        i += 1
        var out: [UInt8] = []
        while true {
            guard i < b.count else { throw JSONFailure(detail: "a string ends early") }
            let c = b[i]
            if c == 0x22 {
                i += 1
                return out
            }
            if c == 0x5C {
                i += 1
                guard i < b.count else { throw JSONFailure(detail: "an escape ends early") }
                let escape = b[i]
                i += 1
                switch escape {
                case 0x22: out.append(0x22)
                case 0x5C: out.append(0x5C)
                case 0x62: out.append(0x08)
                case 0x66: out.append(0x0C)
                case 0x6E: out.append(0x0A)
                case 0x72: out.append(0x0D)
                case 0x74: out.append(0x09)
                case 0x2F where !canonical: out.append(0x2F)
                case 0x75:
                    let unit = try hex4()
                    if canonical {
                        guard unit < 0x20, ![0x08, 0x09, 0x0A, 0x0C, 0x0D].contains(unit) else {
                            throw JSONFailure(detail: "a \\u escape is canonical only for a control character without a short escape")
                        }
                        out.append(UInt8(unit))
                    } else if (0xD800...0xDBFF).contains(unit) {
                        guard i + 1 < b.count, b[i] == 0x5C, b[i + 1] == 0x75 else { throw JSONFailure(detail: "a lone surrogate") }
                        i += 2
                        let low = try hex4()
                        guard (0xDC00...0xDFFF).contains(low) else { throw JSONFailure(detail: "a lone surrogate") }
                        JSONParser.appendScalar(0x10000 + ((unit - 0xD800) << 10) + (low - 0xDC00), &out)
                    } else if (0xDC00...0xDFFF).contains(unit) {
                        throw JSONFailure(detail: "a lone surrogate")
                    } else {
                        JSONParser.appendScalar(unit, &out)
                    }
                default:
                    throw JSONFailure(detail: canonical ? "an escape outside RFC 8785's" : "an unknown escape")
                }
                continue
            }
            if c < 0x20 { throw JSONFailure(detail: "a control character is written unescaped") }
            if c < 0x80 {
                out.append(c)
                i += 1
                continue
            }
            let length = try sequenceLength()
            out.append(contentsOf: b[i..<i + length])
            i += length
        }
    }

    private mutating func number() throws -> Int64 {
        var negative = false
        if b[i] == 0x2D {
            negative = true
            i += 1
        }
        let start = i
        guard i < b.count, (0x30...0x39).contains(b[i]) else { throw JSONFailure(detail: "a number needs digits") }
        if b[i] == 0x30 {
            i += 1
            if i < b.count, (0x30...0x39).contains(b[i]) { throw JSONFailure(detail: "a number has a leading zero") }
        } else {
            while i < b.count, (0x30...0x39).contains(b[i]) { i += 1 }
        }
        if i < b.count, b[i] == 0x2E || b[i] == 0x65 || b[i] == 0x45 {
            throw JSONFailure(detail: "only plain integers are allowed, without a fraction or an exponent")
        }
        let digits = b[start..<i]
        guard digits.count <= 16 else { throw JSONFailure(detail: "an integer is outside ±(2^53 − 1)") }
        var value: Int64 = 0
        for digit in digits { value = value * 10 + Int64(digit - 0x30) }
        guard value <= JSONParser.maxSafeInteger else { throw JSONFailure(detail: "an integer is outside ±(2^53 − 1)") }
        if negative {
            guard value != 0 else { throw JSONFailure(detail: "minus zero is not a canonical integer") }
            value = -value
        }
        return value
    }
}

func canonicalBytes(_ value: JSON) -> [UInt8] {
    var out = [UInt8]()
    writeCanonical(value, &out)
    return out
}

func writeCanonical(_ value: JSON, _ out: inout [UInt8]) {
    switch value {
    case .object(let members):
        out.append(0x7B)
        let sorted = members.map { (units: utf16Units($0.key), member: $0) }.sorted { $0.units.lexicographicallyPrecedes($1.units) }
        for (index, entry) in sorted.enumerated() {
            if index > 0 { out.append(0x2C) }
            writeCanonicalString(entry.member.key, &out)
            out.append(0x3A)
            writeCanonical(entry.member.value, &out)
        }
        out.append(0x7D)
    case .array(let items):
        out.append(0x5B)
        for (index, item) in items.enumerated() {
            if index > 0 { out.append(0x2C) }
            writeCanonical(item, &out)
        }
        out.append(0x5D)
    case .string(let bytes):
        writeCanonicalString(bytes, &out)
    case .integer(let number):
        out.append(contentsOf: Array(String(number).utf8))
    case .bool(let flag):
        out.append(contentsOf: Array((flag ? "true" : "false").utf8))
    case .null:
        out.append(contentsOf: Array("null".utf8))
    }
}

func writeCanonicalString(_ bytes: [UInt8], _ out: inout [UInt8]) {
    let digits = Array("0123456789abcdef".utf8)
    out.append(0x22)
    for c in bytes {
        switch c {
        case 0x22: out.append(contentsOf: [0x5C, 0x22])
        case 0x5C: out.append(contentsOf: [0x5C, 0x5C])
        case 0x08: out.append(contentsOf: [0x5C, 0x62])
        case 0x0C: out.append(contentsOf: [0x5C, 0x66])
        case 0x0A: out.append(contentsOf: [0x5C, 0x6E])
        case 0x0D: out.append(contentsOf: [0x5C, 0x72])
        case 0x09: out.append(contentsOf: [0x5C, 0x74])
        case 0..<0x20: out.append(contentsOf: [0x5C, 0x75, 0x30, 0x30, digits[Int(c >> 4)], digits[Int(c & 0xF)]])
        default: out.append(c)
        }
    }
    out.append(0x22)
}

func same(_ left: JSON?, _ right: JSON?) -> Bool {
    switch (left, right) {
    case (nil, nil): return true
    case (let l?, let r?): return canonicalBytes(l) == canonicalBytes(r)
    default: return false
    }
}

func json(_ text: String) -> JSON { .string(Array(text.utf8)) }

func jsonObject(_ members: [(String, JSON)]) -> JSON { .object(members.map { (Array($0.0.utf8), $0.1) }) }

// MARK: - Encodings, patterns and time

let base64urlAlphabet = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_".utf8)

func base64urlEncode(_ bytes: [UInt8]) -> String {
    var out = [UInt8]()
    var index = 0
    while index + 3 <= bytes.count {
        let n = UInt32(bytes[index]) << 16 | UInt32(bytes[index + 1]) << 8 | UInt32(bytes[index + 2])
        out.append(contentsOf: [base64urlAlphabet[Int(n >> 18 & 63)], base64urlAlphabet[Int(n >> 12 & 63)], base64urlAlphabet[Int(n >> 6 & 63)], base64urlAlphabet[Int(n & 63)]])
        index += 3
    }
    let rest = bytes.count - index
    if rest == 1 {
        let n = UInt32(bytes[index]) << 16
        out.append(contentsOf: [base64urlAlphabet[Int(n >> 18 & 63)], base64urlAlphabet[Int(n >> 12 & 63)]])
    } else if rest == 2 {
        let n = UInt32(bytes[index]) << 16 | UInt32(bytes[index + 1]) << 8
        out.append(contentsOf: [base64urlAlphabet[Int(n >> 18 & 63)], base64urlAlphabet[Int(n >> 12 & 63)], base64urlAlphabet[Int(n >> 6 & 63)]])
    }
    return String(decoding: out, as: UTF8.self)
}

/// Decodes base64url without padding strictly: only its alphabet, no padding, and no bits set beyond the last byte.
func base64urlDecode(_ text: [UInt8]) -> [UInt8]? {
    var values = [UInt32]()
    values.reserveCapacity(text.count)
    for c in text {
        switch c {
        case 0x41...0x5A: values.append(UInt32(c - 0x41))
        case 0x61...0x7A: values.append(UInt32(c - 0x61 + 26))
        case 0x30...0x39: values.append(UInt32(c - 0x30 + 52))
        case 0x2D: values.append(62)
        case 0x5F: values.append(63)
        default: return nil
        }
    }
    if values.count % 4 == 1 { return nil }
    var out = [UInt8]()
    out.reserveCapacity(values.count * 3 / 4)
    var index = 0
    while index + 4 <= values.count {
        let n = values[index] << 18 | values[index + 1] << 12 | values[index + 2] << 6 | values[index + 3]
        out.append(contentsOf: [UInt8(n >> 16 & 0xFF), UInt8(n >> 8 & 0xFF), UInt8(n & 0xFF)])
        index += 4
    }
    let rest = values.count - index
    if rest == 2 {
        guard values[index + 1] & 0xF == 0 else { return nil }
        out.append(UInt8((values[index] << 2 | values[index + 1] >> 4) & 0xFF))
    } else if rest == 3 {
        guard values[index + 2] & 0x3 == 0 else { return nil }
        let n = values[index] << 18 | values[index + 1] << 12 | values[index + 2] << 6
        out.append(contentsOf: [UInt8(n >> 16 & 0xFF), UInt8(n >> 8 & 0xFF)])
    }
    return out
}

func matches(_ bytes: [UInt8], min: Int = 1, max: Int, _ allowed: (UInt8) -> Bool) -> Bool {
    bytes.count >= min && bytes.count <= max && bytes.allSatisfy(allowed)
}

func isLetterOrDigit(_ c: UInt8) -> Bool { (0x30...0x39).contains(c) || (0x41...0x5A).contains(c) || (0x61...0x7A).contains(c) }

let crockford = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZ".utf8)

func isUlid(_ bytes: [UInt8]?) -> Bool {
    guard let bytes, bytes.count == 26, (0x30...0x37).contains(bytes[0]) else { return false }
    return bytes.allSatisfy { crockford.contains($0) }
}

func isHash(_ bytes: [UInt8]?) -> Bool {
    guard let bytes, bytes.count == 71, Array(bytes[0..<7]) == Array("sha256:".utf8) else { return false }
    return bytes[7...].allSatisfy { (0x30...0x39).contains($0) || (0x61...0x66).contains($0) }
}

func isCommit(_ bytes: [UInt8]?) -> Bool {
    guard let bytes else { return false }
    return matches(bytes, min: 40, max: 40) { (0x30...0x39).contains($0) || (0x61...0x66).contains($0) }
}

/// A slug: lower-case letters and digits in groups joined by single hyphens, as record types and provider identifiers are.
func isSlug(_ bytes: [UInt8]?, max: Int = 100) -> Bool {
    guard let bytes, bytes.count >= 1, bytes.count <= max, bytes.first != 0x2D, bytes.last != 0x2D else { return false }
    var previousHyphen = false
    for c in bytes {
        if c == 0x2D {
            if previousHyphen { return false }
            previousHyphen = true
        } else if (0x30...0x39).contains(c) || (0x61...0x7A).contains(c) {
            previousHyphen = false
        } else {
            return false
        }
    }
    return true
}

func isBranch(_ bytes: [UInt8]?) -> Bool {
    guard let bytes else { return false }
    return matches(bytes, max: 100) { isLetterOrDigit($0) || $0 == 0x2E || $0 == 0x5F || $0 == 0x2F || $0 == 0x2D }
}

func isVersion(_ bytes: [UInt8]?) -> Bool {
    guard let bytes else { return false }
    return matches(bytes, max: 40) { isLetterOrDigit($0) || $0 == 0x2E || $0 == 0x2B || $0 == 0x2D }
}

func isPullRequest(_ bytes: [UInt8]?) -> Bool {
    guard let bytes else { return false }
    return matches(bytes, max: 10) { (0x30...0x39).contains($0) }
}

func daysFromCivil(_ year: Int64, _ month: Int64, _ day: Int64) -> Int64 {
    let y = month <= 2 ? year - 1 : year
    let era = (y >= 0 ? y : y - 399) / 400
    let yearOfEra = y - era * 400
    let dayOfYear = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1
    let dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear
    return era * 146_097 + dayOfEra - 719_468
}

/// The seconds since 1970 of a record timestamp, YYYY-MM-DDTHH:MM:SSZ; nil for anything else.
func parseTimestamp(_ bytes: [UInt8]?) -> Int64? {
    guard let bytes, bytes.count == 20, bytes[4] == 0x2D, bytes[7] == 0x2D, bytes[10] == 0x54, bytes[13] == 0x3A, bytes[16] == 0x3A, bytes[19] == 0x5A else { return nil }
    func field(_ start: Int, _ length: Int) -> Int64? {
        var value: Int64 = 0
        for c in bytes[start..<start + length] {
            guard (0x30...0x39).contains(c) else { return nil }
            value = value * 10 + Int64(c - 0x30)
        }
        return value
    }
    guard let year = field(0, 4), let month = field(5, 2), let day = field(8, 2), let hour = field(11, 2), let minute = field(14, 2), let second = field(17, 2),
          (1...12).contains(month), hour < 24, minute < 60, second < 60 else { return nil }
    let leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)
    let lengths: [Int64] = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    guard day >= 1, day <= lengths[Int(month - 1)] else { return nil }
    return daysFromCivil(year, month, day) * 86_400 + hour * 3_600 + minute * 60 + second
}

func formatTimestamp(_ epoch: Int64) -> String {
    let days = epoch >= 0 ? epoch / 86_400 : (epoch - 86_399) / 86_400
    let seconds = epoch - days * 86_400
    let z = days + 719_468
    let era = (z >= 0 ? z : z - 146_096) / 146_097
    let dayOfEra = z - era * 146_097
    let yearOfEra = (dayOfEra - dayOfEra / 1_460 + dayOfEra / 36_524 - dayOfEra / 146_096) / 365
    let dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100)
    let mp = (5 * dayOfYear + 2) / 153
    let day = dayOfYear - (153 * mp + 2) / 5 + 1
    let month = mp < 10 ? mp + 3 : mp - 9
    let year = yearOfEra + era * 400 + (month <= 2 ? 1 : 0)
    return String(format: "%04lld-%02lld-%02lldT%02lld:%02lld:%02lldZ", year, month, day, seconds / 3_600, seconds % 3_600 / 60, seconds % 60)
}

var timebase: mach_timebase_info_data_t = {
    var info = mach_timebase_info_data_t()
    mach_timebase_info(&info)
    return info
}()

/// Seconds on the clock that keeps counting while the Mac sleeps.
func continuousSeconds() -> Double {
    Double(mach_continuous_time()) * Double(timebase.numer) / Double(timebase.denom) / 1_000_000_000
}

// MARK: - Records

struct Refusal: Error {
    let code: String
    let detail: String
}

struct Subject {
    let type: [UInt8]
    let id: [UInt8]
    let hash: [UInt8]?
}

/// A record's canonical bytes, as signed: the envelope without hash and signatures, read only from the compact canonical form.
struct Record {
    let bytes: [UInt8]
    let digest: [UInt8]
    let json: JSON
    let type: String
    let id: [UInt8]
    let at: Int64
    let by: [UInt8]
    let subject: Subject?
    let supersedes: [UInt8]?
    let body: JSON

    var hash: String { "sha256:" + hex(digest) }
    var hashBytes: [UInt8] { Array(hash.utf8) }
    var shortHash: String { String(hex(digest).prefix(12)) }
    func field(_ name: String) -> JSON? { body[name] }
    func target(_ name: String) -> JSON? { body["target"]?[name] }

    static let members: Set<String> = ["at", "body", "by", "format", "id", "subject", "supersedes", "type"]

    /// Reads a record, or throws the reason it is not one; the caller names the refusal.
    static func read(_ bytes: [UInt8]) throws -> Record {
        let json: JSON
        do {
            json = try JSONParser.parse(bytes, canonical: true)
        } catch let failure as JSONFailure {
            throw JSONFailure(detail: "not the compact canonical form: " + failure.detail)
        }
        guard json.members != nil else { throw JSONFailure(detail: "a record is a JSON object") }
        for key in json.keys where !Record.members.contains(key) {
            throw JSONFailure(detail: "a record signed under a check holds no member \(key == "hash" || key == "signatures" ? key : "outside its envelope")")
        }
        guard let format = json["format"]?.text, format.hasPrefix("vericairn.record/"), format.count > 17, format.dropFirst(17).allSatisfy({ $0.isASCII && $0.isNumber }) else {
            throw JSONFailure(detail: "the record's format is not a record format")
        }
        guard let type = json["type"]?.bytes, isSlug(type, max: 64) else { throw JSONFailure(detail: "the record's type is not a record type") }
        guard let id = json["id"]?.bytes, isUlid(id) else { throw JSONFailure(detail: "the record's id is not a ULID") }
        guard let at = parseTimestamp(json["at"]?.bytes) else { throw JSONFailure(detail: "the record's at is not a record timestamp") }
        guard let by = json["by"]?.bytes, isUlid(by) else { throw JSONFailure(detail: "the record's by is not a ULID") }
        guard let body = json["body"], body.members != nil else { throw JSONFailure(detail: "the record's body is not an object") }
        var subject: Subject?
        if let value = json["subject"] {
            guard value.members != nil, Set(value.keys).isSubset(of: ["type", "id", "hash"]), let type = value["type"]?.bytes, isSlug(type, max: 64),
                  let id = value["id"]?.bytes, isUlid(id) else { throw JSONFailure(detail: "the record's subject is not {type, id, hash}") }
            let hash = value["hash"]?.bytes
            if value["hash"] != nil, !isHash(hash) { throw JSONFailure(detail: "the record's subject hash is not a content hash") }
            subject = Subject(type: type, id: id, hash: hash)
        }
        var supersedes: [UInt8]?
        if let value = json["supersedes"] {
            guard isUlid(value.bytes) else { throw JSONFailure(detail: "the record's supersedes is not a ULID") }
            supersedes = value.bytes
        }
        return Record(bytes: bytes, digest: sha256(bytes), json: json, type: String(decoding: type, as: UTF8.self), id: id, at: at, by: by,
                      subject: subject, supersedes: supersedes, body: body)
    }
}

// MARK: - The terms

let termsFormat = "vericairn.presence-session/1"

let actionVocabulary: Set<String> = [
    "operator close", "operator record", "operator unfinished", "changes push", "changes open", "changes merge", "changes observe",
    "release submit", "release observe", "release withdraw", "decision from ask", "docs pass assign", "work move-plan", "goal step",
]

/// The record types an exact entry may have (design/presence-sessions.md § 10); a release only with status withdrawn.
let exactTypes: Set<String> = [
    "acceptance", "review", "decision", "ask", "assignment", "document-revision", "document-review", "work-order", "work-status",
    "operator-request", "operator-request-close", "release", "operation",
]

let kindActions: Set<String> = ["change-push", "change-open", "change-merge", "change-observe", "dispatch-release-workflow"]
let exactActions: Set<String> = kindActions.union(["agent-run", "delete-release-draft"])
let operationStatuses: Set<String> = ["not-started", "dispatched", "observed-success", "known-failure", "canceled", "uncertain"]
let releaseStatuses: Set<String> = ["candidate", "published"]
let observations: Set<String> = ["push-head", "pull-request", "target-run"]
let changeActions: Set<String> = ["change-push", "change-open", "change-merge", "change-observe"]

/// The names each kind entry's fixed values may carry: those it must and those it may.
let fixedNames: [String: (required: [String], optional: [String])] = [
    "change-push": (["provider", "repository", "product", "productHash", "branch"], ["head"]),
    "change-open": (["provider", "repository", "product", "productHash", "branch"], ["head"]),
    "change-merge": (["provider", "repository", "product", "productHash"], ["pullRequest", "head"]),
    "change-observe": ([], ["provider", "repository", "product", "productHash", "head"]),
    "dispatch-release-workflow": (["provider", "destination", "version", "product", "commit"], []),
    "release": (["product", "version", "destination", "commit"], []),
]

func validFixed(_ name: String, _ value: [UInt8]?) -> Bool {
    switch name {
    case "provider": return isSlug(value)
    case "repository", "product": return isUlid(value)
    case "productHash": return isHash(value)
    case "branch", "destination": return isBranch(value)
    case "pullRequest": return isPullRequest(value)
    case "head", "commit": return isCommit(value)
    case "version": return isVersion(value)
    default: return false
    }
}

struct Entry {
    let index: Int
    let exact: Bool
    let type: String
    let action: String?
    let hash: [UInt8]?
    let count: Int
    let atMost: Bool
    let statuses: [String]
    let fixed: [String: [UInt8]]
    let observation: [UInt8]?
    let observes: Int?
    let observesReference: [UInt8]?
    let pullRequestFrom: Int?
    var record: Record?
}

struct Reference {
    let id: [UInt8]
    let type: String
    let hash: [UInt8]
    var record: Record?
}

struct Terms {
    let bytes: [UInt8]
    let digest: [UInt8]
    let action: String
    let principal: [UInt8]
    let publicKey: [UInt8]
    let limitSeconds: Int
    var entries: [Entry]
    var references: [Reference]

    var exactCount: Int { entries.filter { $0.exact }.count }
    var total: Int { entries.reduce(0) { $0 + ($1.exact ? 1 : $1.count) } }
    var upTo: Bool { entries.contains { !$0.exact && $0.atMost } }

    func reference(_ id: [UInt8]?) -> Reference? { references.first { $0.id == id } }
}

func refuse(_ code: String, _ detail: String) -> Refusal { Refusal(code: code, detail: String(detail.prefix(200))) }

/// Reads and checks the terms (design/presence-sessions.md §§ 6 and 8): their form and members first (terms.invalid, or
/// terms.not-allowed for a name outside the vocabulary, the allow-list or the closed set), then their size (terms.size).
func readTerms(_ bytes: [UInt8]) throws -> Terms {
    let json: JSON
    do {
        json = try JSONParser.parse(bytes, canonical: true)
    } catch let failure as JSONFailure {
        throw refuse("terms.invalid", "The terms are not in the compact canonical form: " + failure.detail + ".")
    }
    let members: Set<String> = ["format", "action", "actionIdentity", "principal", "device", "publicKey", "session", "limitSeconds", "entries", "references"]
    guard json.members != nil else { throw refuse("terms.invalid", "The terms are a JSON object.") }
    for key in json.keys where !members.contains(key) { throw refuse("terms.invalid", "The terms hold a member outside the protocol.") }
    for key in members.sorted() where json[key] == nil { throw refuse("terms.invalid", "The terms lack their \(key).") }
    guard json["format"]?.text == termsFormat else { throw refuse("terms.invalid", "The terms' format is not \(termsFormat).") }
    guard let action = json["action"]?.text else { throw refuse("terms.invalid", "The terms' action is not a name.") }
    guard actionVocabulary.contains(action) else { throw refuse("terms.not-allowed", "The terms' action is outside the action vocabulary.") }
    guard isHash(json["actionIdentity"]?.bytes) else { throw refuse("terms.invalid", "The terms' actionIdentity is not a SHA-256 hash.") }
    for key in ["principal", "device", "session"] where !isUlid(json[key]?.bytes) { throw refuse("terms.invalid", "The terms' \(key) is not a ULID.") }
    let spkiPrefix: [UInt8] = [0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04]
    guard let key = json["publicKey"]?.bytes.flatMap(base64urlDecode), key.count == 91, Array(key[0..<27]) == spkiPrefix else {
        throw refuse("terms.invalid", "The terms' publicKey is not a P-256 public key in base64url.")
    }
    guard let limit = json["limitSeconds"]?.integer else { throw refuse("terms.invalid", "The terms' limitSeconds is not an integer.") }

    guard let referenceItems = json["references"]?.items else { throw refuse("terms.invalid", "The terms' references are not an array.") }
    var references: [Reference] = []
    for item in referenceItems {
        guard item.members != nil, Set(item.keys) == ["id", "type", "hash"], let id = item["id"]?.bytes, isUlid(id), let type = item["type"]?.bytes, isSlug(type, max: 64),
              let hash = item["hash"]?.bytes, isHash(hash) else { throw refuse("terms.invalid", "A reference is not {id, type, hash}.") }
        if let last = references.last, !last.id.lexicographicallyPrecedes(id) { throw refuse("terms.invalid", "The references are not sorted by id, or one is repeated.") }
        let typeName = String(decoding: type, as: UTF8.self)
        guard typeName == "operation" || typeName == "release" else { throw refuse("terms.not-allowed", "A reference is of a type no kind entry supersedes or observes.") }
        references.append(Reference(id: id, type: typeName, hash: hash, record: nil))
    }

    guard let entryItems = json["entries"]?.items, !entryItems.isEmpty else { throw refuse("terms.invalid", "The terms' entries are not a non-empty array.") }
    var entries: [Entry] = []
    for (index, item) in entryItems.enumerated() {
        let at = "Entry \(index)"
        guard item.members != nil, let kind = item["entry"]?.text, kind == "exact" || kind == "kind" else { throw refuse("terms.invalid", "\(at) is neither exact nor kind.") }
        guard let type = item["type"]?.bytes, isSlug(type, max: 64) else { throw refuse("terms.invalid", "\(at) has no record type.") }
        let typeName = String(decoding: type, as: UTF8.self)
        if kind == "exact" {
            guard exactTypes.contains(typeName) else { throw refuse("terms.not-allowed", "\(at) is of a type that keeps one check per signature.") }
            var action: String?
            if typeName == "operation" {
                guard let named = item["action"]?.text else { throw refuse("terms.invalid", "\(at) is an operation without its action.") }
                guard exactActions.contains(named) else { throw refuse("terms.not-allowed", "\(at) is an operation whose action keeps one check per signature.") }
                action = named
            }
            let allowed: Set<String> = typeName == "operation" ? ["entry", "type", "hash", "action"] : ["entry", "type", "hash"]
            guard Set(item.keys) == allowed else { throw refuse("terms.invalid", "\(at) holds members other than an exact entry's.") }
            guard let hash = item["hash"]?.bytes, isHash(hash) else { throw refuse("terms.invalid", "\(at) has no content hash.") }
            entries.append(Entry(index: index, exact: true, type: typeName, action: action, hash: hash, count: 1, atMost: false, statuses: [], fixed: [:],
                                 observation: nil, observes: nil, observesReference: nil, pullRequestFrom: nil, record: nil))
            continue
        }

        var allowed: Set<String> = ["entry", "type", "count", "atMost"]
        var action: String?
        switch typeName {
        case "operation":
            guard let named = item["action"]?.text else { throw refuse("terms.invalid", "\(at) is an operation without its action.") }
            guard kindActions.contains(named) else { throw refuse("terms.not-allowed", "\(at) is an operation outside the closed set of kind entries.") }
            action = named
            allowed.formUnion(["action", "statuses", "fixed"])
            if named == "change-observe" { allowed.formUnion(["observation", "observes", "observesReference"]) }
            if named == "change-merge" { allowed.insert("pullRequestFrom") }
        case "history-checkpoint":
            guard let trigger = item["trigger"]?.text else { throw refuse("terms.invalid", "\(at) is a history checkpoint without its trigger.") }
            guard trigger == "release" else { throw refuse("terms.not-allowed", "\(at) is a history checkpoint of a trigger that keeps one check per signature.") }
            allowed.insert("trigger")
        case "release":
            allowed.formUnion(["statuses", "fixed"])
        default:
            throw refuse("terms.not-allowed", "\(at) is of a type outside the closed set of kind entries.")
        }
        let keys = Set(item.keys)
        guard keys.isSubset(of: allowed) else { throw refuse("terms.invalid", "\(at) holds a member outside its kind's.") }
        let required = allowed.subtracting(["observes", "observesReference", "pullRequestFrom"])
        guard required.isSubset(of: keys) else { throw refuse("terms.invalid", "\(at) lacks a member its kind needs.") }
        guard let stated = item["count"]?.integer, stated >= 1 else { throw refuse("terms.invalid", "\(at) has no count from 1.") }
        let count = min(stated, 129)
        guard let atMost = item["atMost"]?.bool else { throw refuse("terms.invalid", "\(at)'s atMost is not a boolean.") }
        if typeName == "history-checkpoint", count != 1 { throw refuse("terms.invalid", "\(at) is a history checkpoint, whose count is one.") }

        var statuses: [String] = []
        if let list = item["statuses"] {
            let set = typeName == "release" ? releaseStatuses : operationStatuses
            guard let values = list.items, !values.isEmpty else { throw refuse("terms.invalid", "\(at)'s statuses are not a non-empty array.") }
            var previous: [UInt8]?
            for value in values {
                guard let status = value.text, set.contains(status) else { throw refuse("terms.invalid", "\(at) names a status outside its set.") }
                if let before = previous, !before.lexicographicallyPrecedes(Array(status.utf8)) { throw refuse("terms.invalid", "\(at)'s statuses are not sorted, or one is repeated.") }
                previous = Array(status.utf8)
                statuses.append(status)
            }
        }

        var fixed: [String: [UInt8]] = [:]
        if let values = item["fixed"] {
            guard let pairs = values.members, let names = fixedNames[action ?? typeName] else { throw refuse("terms.invalid", "\(at)'s fixed values are not an object.") }
            for pair in pairs {
                let name = pair.key.allSatisfy({ $0 < 0x80 }) ? String(decoding: pair.key, as: UTF8.self) : ""
                guard names.required.contains(name) || names.optional.contains(name) else { throw refuse("terms.invalid", "\(at) fixes a value its kind does not fix.") }
                guard validFixed(name, pair.value.bytes) else { throw refuse("terms.invalid", "\(at)'s \(name) is outside its character set.") }
                fixed[name] = pair.value.bytes
            }
            for name in names.required where fixed[name] == nil { throw refuse("terms.invalid", "\(at) does not fix its \(name).") }
        }

        var observation: [UInt8]?
        var observes: Int?
        var observesReference: [UInt8]?
        var pullRequestFrom: Int?
        if action == "change-observe" {
            guard let named = item["observation"]?.text, observations.contains(named) else { throw refuse("terms.invalid", "\(at) names no observation of the closed set.") }
            observation = Array(named.utf8)
            switch (item["observes"], item["observesReference"]) {
            case (let index?, nil):
                guard let value = index.integer, value >= 0, value < Int64(entryItems.count) else { throw refuse("terms.invalid", "\(at) observes no entry of the list.") }
                observes = Int(value)
            case (nil, let reference?):
                guard isUlid(reference.bytes) else { throw refuse("terms.invalid", "\(at) observes no reference of the terms.") }
                observesReference = reference.bytes
            default:
                throw refuse("terms.invalid", "\(at) observes one entry or one reference.")
            }
        }
        if action == "change-merge" {
            if let from = item["pullRequestFrom"] {
                guard fixed["pullRequest"] == nil, let value = from.integer, value >= 0, value < Int64(entryItems.count) else {
                    throw refuse("terms.invalid", "\(at) takes its pull request from the terms or from one opening entry.")
                }
                pullRequestFrom = Int(value)
            } else if fixed["pullRequest"] == nil {
                throw refuse("terms.invalid", "\(at) fixes no pull request.")
            }
        }
        entries.append(Entry(index: index, exact: false, type: typeName, action: action, hash: nil, count: Int(count), atMost: atMost, statuses: statuses, fixed: fixed,
                             observation: observation, observes: observes, observesReference: observesReference, pullRequestFrom: pullRequestFrom, record: nil))
    }

    // What an observation observes and where a merge's pull request comes from, now that every entry is read.
    for entry in entries {
        if let observed = entry.observes {
            let target = entries[observed]
            guard observed != entry.index, target.type == "operation", let action = target.action, ["change-push", "change-open", "change-merge"].contains(action) else {
                throw refuse("terms.invalid", "Entry \(entry.index) observes an entry that is not a push, an opening or a merge.")
            }
        }
        if let reference = entry.observesReference {
            guard references.contains(where: { $0.id == reference && $0.type == "operation" }) else {
                throw refuse("terms.invalid", "Entry \(entry.index) observes no operation among the references.")
            }
        }
        if let from = entry.pullRequestFrom {
            let opening = entries[from]
            guard !opening.exact, opening.action == "change-open" else { throw refuse("terms.invalid", "Entry \(entry.index) takes its pull request from an entry that is not an opening.") }
        }
    }

    guard (60...300).contains(limit) else { throw refuse("terms.size", "The terms' limit is outside 60 to 300 seconds.") }
    let terms = Terms(bytes: bytes, digest: sha256(bytes), action: action, principal: json["principal"]!.bytes!, publicKey: key, limitSeconds: Int(limit),
                      entries: entries, references: references)
    guard terms.total <= 128 else { throw refuse("terms.size", "The list covers more than 128 signatures.") }
    guard terms.exactCount <= 16 else { throw refuse("terms.size", "The list holds more than 16 exact entries.") }
    return terms
}

/// Checks the bytes that come with the terms: each exact entry's record, then each reference's, in the terms' order, each in the
/// compact canonical form, of its hash and type, and each exact entry by the session's principal and dated within the window
/// around the moment the helper received them (no earlier than 120 seconds before, no later than 60 seconds after).
func readBytes(_ terms: inout Terms, _ items: [JSON]?, received: Double) throws {
    let exact = terms.entries.filter { $0.exact }
    guard let items, items.count == exact.count + terms.references.count else { throw refuse("terms.bytes", "The bytes are missing, or more than the terms list.") }
    var ids = Set<[UInt8]>()
    for (offset, item) in items.enumerated() {
        let what = offset < exact.count ? "Exact entry \(exact[offset].index)" : "Reference \(offset - exact.count)"
        guard let bytes = item.bytes.flatMap(base64urlDecode) else { throw refuse("terms.bytes", "\(what)'s bytes are not base64url.") }
        let record: Record
        do {
            record = try Record.read(bytes)
        } catch let failure as JSONFailure {
            throw refuse("terms.bytes", "\(what)'s bytes are refused: \(failure.detail).")
        }
        guard ids.insert(record.id).inserted else { throw refuse("terms.bytes", "\(what) repeats a record id.") }
        if offset < exact.count {
            let entry = exact[offset]
            guard record.hashBytes == entry.hash else { throw refuse("terms.bytes", "\(what)'s bytes are of another hash.") }
            guard record.type == entry.type else { throw refuse("terms.bytes", "\(what)'s bytes are of another type.") }
            if let action = entry.action, record.field("action")?.text != action { throw refuse("terms.bytes", "\(what)'s bytes are of another action.") }
            if entry.type == "release", record.field("status")?.text != "withdrawn" { throw refuse("terms.not-allowed", "\(what) is a release whose status is not withdrawn.") }
            guard record.by == terms.principal else { throw refuse("terms.bytes", "\(what) is by another principal.") }
            guard Double(record.at) >= received - 120, Double(record.at) <= received + 60 else { throw refuse("terms.bytes", "\(what) is dated outside the window of its opening.") }
            terms.entries[entry.index].record = record
        } else {
            let index = offset - exact.count
            let reference = terms.references[index]
            guard record.id == reference.id, record.type == reference.type, record.hashBytes == reference.hash else {
                throw refuse("terms.bytes", "\(what)'s bytes are of another id, type or hash.")
            }
            terms.references[index].record = record
        }
    }
}

// MARK: - The fingerprint and the dialog

let crockfordText = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZ")

/// The list fingerprint: the first 60 bits of SHA-256 over the format's ASCII name, a zero byte, the nonce and the terms' SHA-256,
/// as twelve Crockford base32 characters in three groups of four.
func fingerprint(nonce: [UInt8], termsDigest: [UInt8]) -> String {
    let digest = sha256(Array(termsFormat.utf8) + [0] + nonce + termsDigest)
    var value: UInt64 = 0
    for byte in digest[0..<8] { value = value << 8 | UInt64(byte) }
    value >>= 4
    var characters: [Character] = []
    for index in 0..<12 {
        if index == 4 || index == 8 { characters.append("-") }
        characters.append(crockfordText[Int((value >> UInt64(55 - 5 * index)) & 31)])
    }
    return String(characters)
}

let exactNames: [String: (String, String)] = [
    "acceptance": ("acceptance", "acceptances"), "review": ("review", "reviews"), "decision": ("decision", "decisions"), "ask": ("ask", "asks"),
    "assignment": ("assignment", "assignments"), "document-revision": ("document revision", "document revisions"),
    "document-review": ("document review", "document reviews"), "work-order": ("work order", "work orders"), "work-status": ("work status", "work statuses"),
    "operator-request": ("operator request", "operator requests"), "operator-request-close": ("operator request closing", "operator request closings"),
    "release": ("release withdrawal", "release withdrawals"),
]

let exactOperationNames: [String: (String, String)] = [
    "agent-run": ("agent run", "agent runs"), "delete-release-draft": ("intent to delete a withdrawn draft", "intents to delete a withdrawn draft"),
    "change-push": ("push record", "push records"), "change-open": ("opening record", "opening records"), "change-merge": ("merge record", "merge records"),
    "change-observe": ("observation record", "observation records"), "dispatch-release-workflow": ("release build record", "release build records"),
]

/// The phrase the dialog uses for each kind of the closed set; a group's text ends with "record" or "records".
let kindPhrases: [String: String] = [
    "change-push": "push", "change-open": "opening", "change-merge": "merge", "change-observe": "observation",
    "dispatch-release-workflow": "release build", "history-checkpoint": "history checkpoint", "release": "release",
]

func joinedPhrases(_ phrases: [String]) -> String {
    switch phrases.count {
    case 0: return ""
    case 1: return phrases[0]
    default: return phrases.dropLast().joined(separator: ", ") + " and " + phrases.last!
    }
}

/// What an exact record names, for an observation that observes it: its branch or its pull request, held to their character sets.
func groupKey(of record: Record?) -> String? {
    guard let record else { return nil }
    if let branch = record.target("branch")?.bytes, isBranch(branch) { return "branch " + String(decoding: branch, as: UTF8.self) }
    if let number = record.target("pullRequest")?.bytes, isPullRequest(number) { return "pull request " + String(decoding: number, as: UTF8.self) }
    return nil
}

func groupKey(_ terms: Terms, _ entry: Entry, depth: Int = 0) -> String? {
    guard depth < 4 else { return nil }
    if entry.exact { return groupKey(of: entry.record) }
    if let branch = entry.fixed["branch"] { return "branch " + String(decoding: branch, as: UTF8.self) }
    if let number = entry.fixed["pullRequest"] { return "pull request " + String(decoding: number, as: UTF8.self) }
    if let version = entry.fixed["version"] { return "version " + String(decoding: version, as: UTF8.self) }
    if let from = entry.pullRequestFrom { return groupKey(terms, terms.entries[from], depth: depth + 1) }
    if let observed = entry.observes { return groupKey(terms, terms.entries[observed], depth: depth + 1) }
    if let reference = entry.observesReference { return groupKey(of: terms.reference(reference)?.record) }
    return nil
}

/// The dialog's text, the helper's own (design/presence-sessions.md § 8): "open Vericairn session", the fingerprint, the action,
/// the total and the limit, which are never cut, then the breakdown: exact entries by what they are, then kind entries grouped by
/// the branch, pull request or version they fix. At most 200 characters; a longer breakdown is cut at its end with an ellipsis.
func dialogText(_ terms: Terms, fingerprint: String) -> String {
    let noun = terms.total == 1 ? "signature" : "signatures"
    let head = "open Vericairn session \(fingerprint): \(terms.action), \(terms.upTo ? "up to " : "")\(terms.total) \(noun) in \(terms.limitSeconds) s"
    var exactOrder: [String] = []
    var exactCounts: [String: (Int, String, String)] = [:]
    for entry in terms.entries where entry.exact {
        var names = entry.type == "operation" ? exactOperationNames[entry.action ?? ""] : exactNames[entry.type]
        if entry.type == "acceptance", entry.record?.subject?.type == Array("release".utf8) { names = ("release acceptance", "release acceptances") }
        guard let (singular, plural) = names else { continue }
        if exactCounts[singular] == nil { exactOrder.append(singular) }
        let count = (exactCounts[singular]?.0 ?? 0) + 1
        exactCounts[singular] = (count, singular, plural)
    }
    var parts = exactOrder.map { name -> String in
        let (count, singular, plural) = exactCounts[name]!
        return "\(count) \(count == 1 ? singular : plural)"
    }
    var groupOrder: [String] = []
    var groups: [String: (key: String?, count: Int, upTo: Bool, phrases: [String])] = [:]
    for entry in terms.entries where !entry.exact {
        let key = groupKey(terms, entry)
        let name = key ?? ""
        let phrase = kindPhrases[entry.action ?? entry.type] ?? entry.type
        var group = groups[name] ?? (key: key, count: 0, upTo: false, phrases: [])
        if groups[name] == nil { groupOrder.append(name) }
        group.count += entry.count
        group.upTo = group.upTo || entry.atMost
        if !group.phrases.contains(phrase) { group.phrases.append(phrase) }
        groups[name] = group
    }
    for name in groupOrder {
        let group = groups[name]!
        let records = group.count == 1 ? "record" : "records"
        var text = "\(group.upTo ? "up to " : "")\(group.count) \(joinedPhrases(group.phrases)) \(records)"
        if let key = group.key { text += ", " + key }
        parts.append(text)
    }
    let breakdown = parts.map { "; " + $0 }.joined()
    let maxLength = 200
    if head.utf16.count + breakdown.utf16.count <= maxLength { return head + breakdown }
    let room = maxLength - head.utf16.count - 1
    var cut = String(breakdown.prefix(max(0, room)))
    while let last = cut.last, last == " " || last == ";" || last == "," { cut.removeLast() }
    return head + cut + "…"
}

/// A reason from the runtime, held to one line without control or format characters, and cut to fit after the helper's label.
func boundedReason(label: String, reason: String?) -> String {
    let maxLength = 200
    var cleaned = ""
    for scalar in (reason ?? "").unicodeScalars {
        switch scalar.properties.generalCategory {
        case .control, .format, .lineSeparator, .paragraphSeparator, .spaceSeparator: cleaned.unicodeScalars.append(" ")
        default: cleaned.unicodeScalars.append(scalar)
        }
    }
    let words = cleaned.split(separator: " ", omittingEmptySubsequences: true).joined(separator: " ")
    if words.isEmpty { return label }
    let text = label + ": " + words
    if text.utf16.count <= maxLength { return text }
    var cut = ""
    for character in text {
        if cut.utf16.count + String(character).utf16.count > maxLength - 1 { break }
        cut.append(character)
    }
    return cut.trimmingCharacters(in: .whitespaces) + "…"
}

// MARK: - The release helper's self-check

struct ReleaseIdentity {
    let team: String
    let identifier: String
}

/// The release identity the release build links into __TEXT,__vericairn; nil in a development build.
func releaseIdentity() -> ReleaseIdentity? {
    var size: UInt = 0
    let header = #dsohandle.assumingMemoryBound(to: mach_header_64.self)
    guard let start = getsectiondata(header, "__TEXT", "__vericairn", &size), size > 0, size <= 1_024 else { return nil }
    var bytes = Array(UnsafeBufferPointer(start: start, count: Int(size)))
    if bytes.last == 0x0A { bytes.removeLast() }
    guard let json = try? JSONParser.parse(bytes, canonical: true), Set(json.keys) == ["identifier", "team"],
          let team = json["team"]?.bytes, matches(team, min: 10, max: 10, { (0x30...0x39).contains($0) || (0x41...0x5A).contains($0) }),
          let identifier = json["identifier"]?.bytes, matches(identifier, max: 100, { isLetterOrDigit($0) || $0 == 0x2E || $0 == 0x2D }) else { return nil }
    return ReleaseIdentity(team: String(decoding: team, as: UTF8.self), identifier: String(decoding: identifier, as: UTF8.self))
}

/// Why this helper's own running code does not meet the release requirement, or nil when it does (design/presence-sessions.md
/// § 11): Apple's Developer ID anchor, the release team, the helper's identifier, the hardened runtime, and no get-task-allow.
/// Entitlements that let another process load code into it are refused as well.
func selfCheckProblem() -> String? {
    guard let identity = releaseIdentity() else {
        return "This helper is a development build: it carries no release identity, so it opens no presence session."
    }
    var code: SecCode?
    guard SecCodeCopySelf(SecCSFlags(), &code) == errSecSuccess, let code else { return "This helper cannot read its own code signature." }
    var text = "anchor apple generic and identifier \"\(identity.identifier)\" and certificate leaf[subject.OU] = \"\(identity.team)\""
        + " and certificate 1[field.1.2.840.113635.100.6.2.6] exists and certificate leaf[field.1.2.840.113635.100.6.1.13] exists"
    #if VERICAIRN_SESSION_TESTS
    // Test hook: an ad hoc signature stands in for the Developer ID, so the checks after the requirement can be exercised.
    if testEnvironment["VERICAIRN_TEST_ANCHOR"] == "ad-hoc" { text = "identifier \"\(identity.identifier)\"" }
    #endif
    var requirement: SecRequirement?
    guard SecRequirementCreateWithString(text as CFString, SecCSFlags(), &requirement) == errSecSuccess, let requirement else {
        return "This helper cannot form its release requirement."
    }
    guard SecCodeCheckValidity(code, SecCSFlags(rawValue: kSecCSStrictValidate), requirement) == errSecSuccess else {
        return "This helper is not signed with the release's Developer ID, team and identifier."
    }
    var staticCode: SecStaticCode?
    guard SecCodeCopyStaticCode(code, SecCSFlags(), &staticCode) == errSecSuccess, let staticCode else { return "This helper cannot read its own code signature." }
    var signing: CFDictionary?
    guard SecCodeCopySigningInformation(staticCode, SecCSFlags(rawValue: kSecCSSigningInformation), &signing) == errSecSuccess,
          let info = signing as? [String: Any] else { return "This helper cannot read its own signing information." }
    let codeFlags = (info[kSecCodeInfoFlags as String] as? NSNumber)?.uint32Value ?? 0
    guard codeFlags & 0x10000 != 0 else { return "This helper is not signed with the hardened runtime." }
    let entitlements = info[kSecCodeInfoEntitlementsDict as String] as? [String: Any] ?? [:]
    for name in ["com.apple.security.get-task-allow", "com.apple.security.cs.allow-dyld-environment-variables", "com.apple.security.cs.disable-library-validation"]
        where (entitlements[name] as? Bool) == true {
        return "This helper carries an entitlement that lets another process attach to it or load code into it."
    }
    var dynamic: CFDictionary?
    guard SecCodeCopySigningInformation(unsafeBitCast(code, to: SecStaticCode.self), SecCSFlags(rawValue: kSecCSDynamicInformation), &dynamic) == errSecSuccess,
          let status = ((dynamic as? [String: Any])?[kSecCodeInfoStatus as String] as? NSNumber)?.uint32Value else {
        return "This helper cannot read its own code signing status."
    }
    // CS_VALID, CS_RUNTIME; never CS_GET_TASK_ALLOW or CS_DEBUGGED.
    guard status & 0x1 != 0, status & 0x10000 != 0, status & 0x4 == 0, status & 0x1000_0000 == 0 else {
        return "This helper's running code is not valid under the hardened runtime, or may be debugged."
    }
    return nil
}

// MARK: - The presence session

let maxLine = 8 * 1024 * 1024

final class PresenceSession {
    enum Phase { case awaitingOpen, checking, open }

    private let blobPath: String
    private var phase = Phase.awaitingOpen
    private var terms: Terms?
    private var used: [Bool] = []
    private var left: [Int] = []
    private var signed: [[UInt8]: (entry: Int, record: Record)] = [:]
    private var signedUnder: [Int: [[UInt8]]] = [:]
    private var head: [UInt8]?
    private var superseded = Set<[UInt8]>()
    private var context: LAContext?
    private var key: SecureEnclave.P256.Signing.PrivateKey?
    private var blob = Data()
    private var openedContinuous = 0.0
    private var openedAt: Int64 = 0
    private var offset = 0.0
    private var waiting: [[UInt8]] = []
    private var timer: DispatchSourceTimer?
    private var ending = false
    var powerPort: io_connect_t = 0
    var watchers: [AnyObject] = []

    init(blobPath: String) {
        self.blobPath = blobPath
    }

    // MARK: Time

    private func wallNow() -> Double {
        #if VERICAIRN_SESSION_TESTS
        if let now = testEnvironment["VERICAIRN_TEST_NOW"].flatMap(Double.init) { return now }
        #endif
        return Date().timeIntervalSince1970
    }

    private func elapsed() -> Double {
        #if VERICAIRN_SESSION_TESTS
        if testEnvironment["VERICAIRN_TEST_NOW"] != nil { return offset }
        #endif
        return continuousSeconds() - openedContinuous + offset
    }

    private var secondsLeft: Int {
        guard let terms else { return 0 }
        return max(0, Int((Double(terms.limitSeconds) - elapsed()).rounded(.down)))
    }

    private var signaturesLeft: Int {
        zip(used, terms?.entries ?? []).filter { !$0.0 && $0.1.exact }.count + left.reduce(0, +)
    }

    // MARK: Output

    private func send(_ type: String, _ members: [(String, JSON)]) {
        if !writeAll(1, canonicalBytes(jsonObject([("type", json(type))] + members)) + [0x0A]) {
            context?.invalidate()
            exit(4)
        }
    }

    /// Ends the session: the refusal first when there is one, then the reason; the context is invalidated and the helper exits.
    func end(_ reason: String, refusal: Refusal? = nil, before: (() -> Void)? = nil) -> Never {
        ending = true
        context?.invalidate()
        context = nil
        key = nil
        if let refusal { send("refused", [("code", json(refusal.code)), ("detail", json(refusal.detail))]) }
        send("ended", [("reason", json(reason))])
        before?()
        exit(reason == "closed" ? 0 : 4)
    }

    // MARK: Input

    func receive(_ lines: [[UInt8]]) {
        for line in lines { receive(line) }
    }

    func receive(_ line: [UInt8]) {
        if phase == .checking {
            // Only close is acted on while the check waits for the person; the rest is read in order once the session opens.
            if let message = try? JSONParser.parse(line, canonical: false), message["type"]?.text == "close" { end("closed") }
            waiting.append(line)
            return
        }
        let message: JSON
        do {
            message = try JSONParser.parse(line, canonical: false)
        } catch {
            protocolError("The message is not one JSON object.")
        }
        guard message.members != nil, let type = message["type"]?.text else { protocolError("The message names no type.") }
        #if VERICAIRN_SESSION_TESTS
        if testMessage(type, message) { return }
        #endif
        switch (phase, type) {
        case (_, "close"):
            end("closed")
        case (.awaitingOpen, "open"):
            open(message)
        case (.open, "sign"):
            sign(message)
        case (.open, "status"):
            guard Set(message.keys) == ["type"] else { protocolError("A status message has no members.") }
            checkTime()
            send("status", [("left", .integer(Int64(signaturesLeft))), ("seconds", .integer(Int64(secondsLeft)))])
        default:
            protocolError(phase == .awaitingOpen ? "The first message opens the session." : "The session does not expect this message.")
        }
    }

    func oversize() -> Never {
        protocolError("A message is longer than 8 MiB.")
    }

    func inputClosed() -> Never { end("input") }

    private func protocolError(_ detail: String) -> Never {
        end("refusal", refusal: refuse(phase == .awaitingOpen ? "terms.invalid" : "record.form", detail))
    }

    // MARK: Opening

    private func open(_ message: JSON) {
        guard Set(message.keys).isSubset(of: ["type", "terms", "bytes"]) else { protocolError("An open message holds a member outside the protocol.") }
        let received = wallNow()
        #if VERICAIRN_SESSION_TESTS
        let checkSelf = testEnvironment["VERICAIRN_TEST_SKIP_SELF_CHECK"] != "1"
        #else
        let checkSelf = true
        #endif
        if checkSelf, let problem = selfCheckProblem() { end("refusal", refusal: refuse("helper.unsigned", problem)) }
        var opened: Terms
        do {
            guard let bytes = message["terms"]?.bytes.flatMap(base64urlDecode) else { throw refuse("terms.invalid", "The open message carries no terms in base64url.") }
            opened = try readTerms(bytes)
            try readBytes(&opened, message["bytes"]?.items, received: received)
        } catch let refusal as Refusal {
            end("refusal", refusal: refusal)
        } catch {
            end("refusal", refusal: refuse("terms.invalid", "The terms could not be read."))
        }

        // The device: the blob's public key is compared with the terms before anything is shown.
        guard let blob = FileManager.default.contents(atPath: blobPath), SecureEnclave.isAvailable,
              let probeKey = try? SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: blob) else {
            end("refusal", refusal: refuse("terms.key", "The key blob could not be read by this Mac's Secure Enclave."))
        }
        guard Array(probeKey.publicKey.derRepresentation) == opened.publicKey else {
            end("refusal", refusal: refuse("terms.key", "The blob's public key differs from the terms' publicKey."))
        }
        self.blob = blob
        terms = opened
        used = opened.entries.map { _ in false }
        left = opened.entries.map { $0.exact ? 0 : $0.count }

        var nonce = [UInt8](repeating: 0, count: 16)
        guard SecRandomCopyBytes(kSecRandomDefault, nonce.count, &nonce) == errSecSuccess else {
            end("refusal", refusal: refuse("check.unavailable", "The system's random source is unavailable."))
        }
        #if VERICAIRN_SESSION_TESTS
        if let fixed = testEnvironment["VERICAIRN_TEST_NONCE"].flatMap({ base64urlDecode(Array($0.utf8)) }), fixed.count == 16 { nonce = fixed }
        #endif
        let shown = fingerprint(nonce: nonce, termsDigest: opened.digest)
        let text = dialogText(opened, fingerprint: shown)
        #if VERICAIRN_SESSION_TESTS
        diagnose("dialog: " + text)
        #endif
        let presence = needsPresence(blob)
        phase = .checking
        send("prompting", [("nonce", json(base64urlEncode(nonce))), ("fingerprint", json(shown))])
        let context = LAContext()
        self.context = context
        #if VERICAIRN_SESSION_TESTS
        if let simulated = testEnvironment["VERICAIRN_TEST_CHECK"] {
            switch simulated {
            case "declined": checked(false, LAError(.userCancel))
            case "unavailable": checked(false, LAError(.passcodeNotSet))
            case "passed": checked(true, nil)
            default: break
            }
            return
        }
        #endif
        guard presence else {
            checked(true, nil)
            return
        }
        // The one presence check: the context is evaluated against the key's access control for signing, as the helper creates it.
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenUnlockedThisDeviceOnly, [.privateKeyUsage, .userPresence], nil) else {
            end("refusal", refusal: refuse("check.unavailable", "The access control could not be created."))
        }
        context.evaluateAccessControl(access, operation: .useKeySign, localizedReason: text) { success, error in
            DispatchQueue.main.async { self.checked(success, error) }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 300) {
            if self.phase == .checking { self.end("declined", refusal: refuse("check.declined", "The presence check was not answered within 300 seconds.")) }
        }
    }

    /// True when the blob's key asks for presence: a key without it signs a fixed probe digest with interaction not allowed, a key
    /// with it cannot, and no dialog is shown either way.
    private func needsPresence(_ blob: Data) -> Bool {
        let probe = LAContext()
        probe.interactionNotAllowed = true
        defer { probe.invalidate() }
        guard let key = try? SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: blob, authenticationContext: probe) else { return true }
        return (try? key.signature(for: SHA256.hash(data: Data((termsFormat + " probe").utf8)))) == nil
    }

    private func checked(_ success: Bool, _ error: Error?) {
        guard phase == .checking, !ending else { return }
        guard success, let context else {
            let code = (error as? LAError)?.code
            switch code {
            case .appCancel?:
                return
            case .userCancel?, .systemCancel?, .authenticationFailed?, .userFallback?:
                end("declined", refusal: refuse("check.declined", "The presence check was declined."))
            default:
                end("refusal", refusal: refuse("check.unavailable", "The presence check is unavailable on this Mac now."))
            }
        }
        // The check has passed: the context never shows a dialog again, so a signature its evaluation does not cover is refused
        // (check.unavailable) instead of opening a system dialog whose text the helper did not write.
        context.interactionNotAllowed = true
        guard let signing = try? SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: blob, authenticationContext: context) else {
            end("refusal", refusal: refuse("check.unavailable", "The key could not be used with the session's check."))
        }
        key = signing
        phase = .open
        openedContinuous = continuousSeconds()
        openedAt = Int64(wallNow().rounded(.down))
        send("opened", [("openedAt", json(formatTimestamp(openedAt))), ("limitSeconds", .integer(Int64(terms!.limitSeconds)))])
        startTimer()
        let queued = waiting
        waiting = []
        for line in queued { receive(line) }
    }

    private func startTimer() {
        #if VERICAIRN_SESSION_TESTS
        if testEnvironment["VERICAIRN_TEST_NO_TIMER"] == "1" { return }
        #endif
        let source = DispatchSource.makeTimerSource(queue: .main)
        source.schedule(deadline: .now() + 1, repeating: 1, leeway: .milliseconds(100))
        source.setEventHandler { [weak self] in self?.checkTime() }
        source.resume()
        timer = source
    }

    private func checkTime() {
        if phase == .open, let terms, elapsed() >= Double(terms.limitSeconds) { end("limit") }
    }

    // MARK: Signing

    private func sign(_ message: JSON) {
        guard Set(message.keys) == ["type", "record"] else { protocolError("A sign message carries one record.") }
        guard let terms, elapsed() < Double(terms.limitSeconds) else {
            end("limit", refusal: refuse("session.ended", "The session reached its limit."))
        }
        if screenLocked() { end("lock") }
        do {
            guard let bytes = message["record"]?.bytes.flatMap(base64urlDecode) else { throw refuse("record.form", "The record is not in base64url.") }
            let record: Record
            do {
                record = try Record.read(bytes)
            } catch let failure as JSONFailure {
                throw refuse("record.form", "The record is refused: " + failure.detail + ".")
            }
            guard record.by == terms.principal else { throw refuse("record.author", "The record is by another principal.") }
            // Check 3: no record takes the id of one the session holds: signed in it, listed as a reference, or an exact entry's, unless
            // these are that exact entry's own bytes.
            guard signed[record.id] == nil else { throw refuse("record.author", "A record with this id was already signed in the session.") }
            guard !terms.references.contains(where: { $0.id == record.id }) else { throw refuse("record.author", "The record takes the id of a reference of the session.") }
            guard !terms.entries.contains(where: { $0.exact && $0.record?.id == record.id && $0.hash != record.hashBytes }) else {
                throw refuse("record.author", "The record takes the id of an exact entry of the session.")
            }
            let index = try match(record, terms)
            guard let key else { throw refuse("check.unavailable", "The session holds no key.") }
            let signature: [UInt8]
            do {
                signature = Array(try key.signature(for: Precomputed(bytes: record.digest)).rawRepresentation)
            } catch {
                throw refuse("check.unavailable", "The Secure Enclave refused the signature.")
            }
            if terms.entries[index].exact { used[index] = true } else { left[index] -= 1 }
            signed[record.id] = (index, record)
            signedUnder[index, default: []].append(record.id)
            if let previous = record.supersedes { superseded.insert(previous) }
            if head == nil, record.type == "operation", let action = record.field("action")?.text, changeActions.contains(action),
               let value = record.target("head")?.bytes, isCommit(value) {
                head = value
            }
            send("signed", [("signature", json(base64urlEncode(signature))), ("entry", .integer(Int64(index))),
                            ("left", .integer(Int64(signaturesLeft))), ("seconds", .integer(Int64(secondsLeft)))])
        } catch let refusal as Refusal {
            end("refusal", refusal: refusal)
        } catch {
            end("refusal", refusal: refuse("record.form", "The record could not be read."))
        }
    }

    /// The entry a record is signed under (design/presence-sessions.md § 8, checks 4 and 5): an unused exact entry of its hash, or a
    /// kind entry with signatures left whose fixed values, status and shape it carries (record.outside otherwise), whose identity,
    /// supersedes, subject and target.operation follow § 5's rules (record.reference), dated within the window (record.time).
    private func match(_ record: Record, _ terms: Terms) throws -> Int {
        if let exact = terms.entries.first(where: { $0.exact && !used[$0.index] && $0.hash == record.hashBytes }) { return exact.index }
        if record.supersedes == record.id { throw refuse("record.reference", "The record supersedes itself.") }
        let candidates = terms.entries.filter { !$0.exact && left[$0.index] > 0 && carries(record, $0) }
        guard !candidates.isEmpty else { throw refuse("record.outside", "The record matches no unused entry of the list.") }
        var first: Refusal?
        for entry in candidates {
            if let problem = referenceProblem(record, entry, terms) {
                first = first ?? refuse("record.reference", problem)
                continue
            }
            let earliest = Double(openedAt) - 60
            let latest = Double(openedAt) + elapsed() + 60
            guard Double(record.at) >= earliest, Double(record.at) <= latest else { throw refuse("record.time", "The record is dated outside the session's window.") }
            return entry.index
        }
        throw first!
    }

    private func value(_ record: Record, _ name: String) -> [UInt8]? {
        name == "provider" ? record.field("provider")?.bytes : record.target(name)?.bytes
    }

    /// Where a dispatch states its destination or its commit: each of the fields that may state it must state the entry's value,
    /// and one must (a provider states its destination under its own setting, such as target.repository, and its commit as ref).
    private func statesOnly(_ record: Record, _ fields: [String], _ expected: [UInt8]) -> Bool {
        let stated = fields.compactMap { record.target($0) }
        return !stated.isEmpty && stated.allSatisfy { $0.bytes == expected }
    }

    private func fixedHeld(_ record: Record, _ entry: Entry) -> Bool {
        for (name, expected) in entry.fixed {
            switch (entry.action, name) {
            case ("dispatch-release-workflow"?, "destination"):
                if !statesOnly(record, ["destination", "repository"], expected) { return false }
            case ("dispatch-release-workflow"?, "commit"):
                if !statesOnly(record, ["commit", "ref"], expected) { return false }
            case (nil, "product"):
                continue
            case (nil, "destination"):
                if record.field("repository")?.bytes != expected { return false }
            case (nil, let field):
                if record.field(field)?.bytes != expected { return false }
            default:
                if value(record, name) != expected { return false }
            }
        }
        return true
    }

    /// Whether the record is of the entry's kind and carries its fixed values, status and shape.
    private func carries(_ record: Record, _ entry: Entry) -> Bool {
        guard record.type == entry.type else { return false }
        switch entry.type {
        case "history-checkpoint":
            return record.field("trigger")?.text == "release" && record.field("anchored") == nil && record.field("removed") == nil && record.field("closes") == nil
        case "release":
            guard let status = record.field("status")?.text, entry.statuses.contains(status), fixedHeld(record, entry) else { return false }
            return !(status == "candidate" && record.supersedes != nil)
        default:
            break
        }
        guard let action = entry.action, record.field("action")?.text == action, let status = record.field("status")?.text, entry.statuses.contains(status),
              fixedHeld(record, entry) else { return false }
        if changeActions.contains(action) {
            let recorded = record.target("head")?.bytes
            if let expected = entry.fixed["head"] ?? head {
                if recorded != expected { return false }
            } else if !isCommit(recorded) {
                return false
            }
        }
        if action == "change-open" || action == "change-merge", let number = record.target("pullRequest"), !isPullRequest(number.bytes) { return false }
        if action == "change-merge", entry.fixed["pullRequest"] == nil {
            guard let from = entry.pullRequestFrom, let number = record.target("pullRequest")?.bytes else { return false }
            let observed = (signedUnder[from] ?? []).compactMap { signed[$0]?.record.target("pullRequest")?.bytes }
            if !observed.contains(number) { return false }
        }
        if action == "change-observe", record.target("observation")?.bytes != entry.observation { return false }
        return true
    }

    private func recordNamed(_ id: [UInt8]?, _ terms: Terms) -> Record? {
        guard let id else { return nil }
        return signed[id]?.record ?? terms.reference(id)?.record
    }

    /// § 5's rules for identity, supersedes, subject and target.operation, against the records signed in the session and the
    /// references; nil when they hold.
    private func referenceProblem(_ record: Record, _ entry: Entry, _ terms: Terms) -> String? {
        switch entry.type {
        case "history-checkpoint":
            return record.subject == nil && record.supersedes == nil ? nil : "A history checkpoint states no subject and supersedes nothing."
        case "release":
            guard let subject = record.subject, subject.type == Array("product".utf8), subject.id == entry.fixed["product"], subject.hash == nil else {
                return "A release record's subject names the entry's product."
            }
            guard record.field("status")?.text == "published" else { return nil }
            guard let candidate = terms.reference(record.supersedes)?.record, candidate.type == "release", candidate.field("status")?.text == "candidate" else {
                return "A publication supersedes the candidate listed in the references."
            }
            if superseded.contains(candidate.id) { return "Another record of the session already superseded that candidate." }
            let kept: (JSON) -> [(key: [UInt8], value: JSON)] = { body in
                (body.members ?? []).filter { member in !["status", "publishedAt", "url"].contains { Array($0.utf8) == member.key } }
            }
            return same(.object(kept(candidate.body)), .object(kept(record.body))) ? nil : "A publication differs from its candidate in more than status, publishedAt and url."
        default:
            break
        }
        guard let action = entry.action, let identity = record.field("identity")?.bytes else { return "The record states no identity." }
        if action == "change-observe" {
            guard let observed = record.target("operation")?.bytes, isUlid(observed) else { return "An observation names no operation." }
            let named: Bool
            if let index = entry.observes { named = (signedUnder[index] ?? []).contains(observed) } else { named = entry.observesReference == observed }
            guard named, let operation = recordNamed(observed, terms) else { return "An observation names an operation neither signed under the entry it observes nor referenced." }
            guard let subject = record.subject, subject.type == Array("operation".utf8), subject.id == observed, subject.hash == nil || subject.hash == operation.hashBytes else {
                return "An observation's subject names the operation it observes."
            }
            guard let base = operation.field("identity")?.bytes else { return "The observed operation states no identity." }
            let prefix = base + Array(":observe:".utf8)
            guard identity.count == prefix.count + 26, Array(identity[0..<prefix.count]) == prefix, isUlid(Array(identity[prefix.count...])) else {
                return "An observation's identity is not its operation's followed by :observe: and a ULID."
            }
            // The observation is of that operation's change: its provider, repository, product, product hash and head, and its branch
            // or pull request where both state one, are the operation's, whatever the entry fixes.
            guard same(record.field("provider"), operation.field("provider")) else { return "An observation's provider is not its operation's." }
            for name in ["repository", "product", "productHash", "head"] where record.target(name)?.bytes == nil || !same(record.target(name), operation.target(name)) {
                return "An observation's repository, product, product hash or head is not its operation's."
            }
            for name in ["branch", "pullRequest"] where record.target(name) != nil && operation.target(name) != nil && !same(record.target(name), operation.target(name)) {
                return "An observation's branch or pull request is not its operation's."
            }
        } else {
            guard let subject = record.subject, subject.type == Array("product".utf8), let product = entry.fixed["product"], subject.id == product else {
                return "The record's subject does not name the entry's product."
            }
            if let pinned = subject.hash, pinned != entry.fixed["productHash"] { return "The record's subject pins a product revision the entry does not fix." }
            guard identity == composedIdentity(record, entry) else { return "The record's identity is not the one the entry's fixed values compose." }
        }
        if let previous = record.supersedes {
            guard let earlier = recordNamed(previous, terms), earlier.type == "operation" else { return "The record supersedes a record neither signed nor referenced." }
            if superseded.contains(previous) { return "Another record of the session already superseded the record this one supersedes." }
            guard earlier.field("identity")?.bytes == identity, same(earlier.field("provider"), record.field("provider")), same(earlier.field("action"), record.field("action")) else {
                return "The record supersedes an operation of another identity, provider or action."
            }
            if signed[previous] == nil, !fixedHeld(earlier, entry) { return "The record supersedes a referenced operation that does not keep the entry's fixed values." }
            if action == "change-observe", !same(earlier.target("operation"), record.target("operation")) { return "An observation's revision keeps the operation it observes." }
        }
        return nil
    }

    /// The identity the entry's fixed values compose: for a change, provider, repository, action, branch or pull request, head and
    /// product hash joined by colons; for a release build, provider, destination and v-prefixed version.
    private func composedIdentity(_ record: Record, _ entry: Entry) -> [UInt8] {
        let f = entry.fixed
        if entry.action == "dispatch-release-workflow" {
            return (f["provider"] ?? []) + [0x3A] + (f["destination"] ?? []) + Array(":v".utf8) + (f["version"] ?? [])
        }
        let name = entry.action == "change-merge" ? (f["pullRequest"] ?? record.target("pullRequest")?.bytes ?? []) : (f["branch"] ?? [])
        let headValue = f["head"] ?? head ?? record.target("head")?.bytes ?? []
        let productHash = Array((f["productHash"] ?? []).dropFirst(7))
        return (f["provider"] ?? []) + [0x3A] + (f["repository"] ?? []) + [0x3A] + Array((entry.action ?? "").utf8) + [0x3A] + name + [0x3A] + headValue + [0x3A] + productHash
    }

    /// Whether the screen is locked or the user's session is away from the console, read before each signature beside the lock,
    /// screen saver and switch notices. It fails closed: no session dictionary, or one that does not say the session is on the
    /// console, counts as locked. The lock key is present, and true, only while the screen is locked.
    private func screenLocked() -> Bool {
        var session = CGSessionCopyCurrentDictionary() as? [String: Any]
        #if VERICAIRN_SESSION_TESTS
        // Test hook: the session dictionary as each state of the screen gives it.
        switch testEnvironment["VERICAIRN_TEST_SCREEN"] {
        case "unlocked"?: session = [kCGSessionOnConsoleKey: true]
        case "locked"?: session = [kCGSessionOnConsoleKey: true, "CGSSessionScreenIsLocked": true]
        case "off-console"?: session = [kCGSessionOnConsoleKey: false]
        case "absent"?: session = nil
        default: break
        }
        #endif
        guard let session, (session[kCGSessionOnConsoleKey] as? Bool) == true else { return true }
        return (session["CGSSessionScreenIsLocked"] as? Bool) == true
    }

    // MARK: Test hooks

    #if VERICAIRN_SESSION_TESTS
    private func testMessage(_ type: String, _ message: JSON) -> Bool {
        switch type {
        case "test-advance":
            offset += Double(message["seconds"]?.integer ?? 0)
            return true
        case "test-signal":
            switch message["signal"]?.text {
            case "lock": end("lock")
            case "sleep": end("sleep")
            default: return true
            }
        default:
            return false
        }
    }
    #endif
}

// MARK: - Watching the session's surroundings

var presenceSession: PresenceSession?

/// The session ends when the Mac is about to sleep: IOKit's notice, answered after the session has ended so sleep is not held.
let powerCallback: IOServiceInterestCallback = { _, _, messageType, argument in
    guard let session = presenceSession else { return }
    switch messageType {
    case 0xE000_0270: // kIOMessageCanSystemSleep
        IOAllowPowerChange(session.powerPort, Int(bitPattern: argument))
    case 0xE000_0280: // kIOMessageSystemWillSleep
        session.end("sleep") { IOAllowPowerChange(session.powerPort, Int(bitPattern: argument)) }
    default:
        break
    }
}

func watch(_ session: PresenceSession) {
    let parent = getppid()
    let source = DispatchSource.makeProcessSource(identifier: parent, eventMask: .exit, queue: .main)
    source.setEventHandler { session.end("parent") }
    source.resume()
    session.watchers.append(source)
    if parent == 1 || getppid() != parent { DispatchQueue.main.async { session.end("parent") } }

    let distributed = DistributedNotificationCenter.default()
    for name in ["com.apple.screenIsLocked", "com.apple.screensaver.didstart"] {
        session.watchers.append(distributed.addObserver(forName: Notification.Name(name), object: nil, queue: .main) { _ in session.end("lock") })
    }
    let workspace = NSWorkspace.shared.notificationCenter
    session.watchers.append(workspace.addObserver(forName: NSWorkspace.sessionDidResignActiveNotification, object: nil, queue: .main) { _ in session.end("lock") })
    session.watchers.append(workspace.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { _ in session.end("sleep") })
    session.watchers.append(workspace.addObserver(forName: NSWorkspace.screensDidSleepNotification, object: nil, queue: .main) { _ in session.end("sleep") })

    var port: IONotificationPortRef?
    var notifier: io_object_t = 0
    session.powerPort = IORegisterForSystemPower(nil, &port, powerCallback, &notifier)
    if session.powerPort != 0, let port {
        CFRunLoopAddSource(CFRunLoopGetMain(), IONotificationPortGetRunLoopSource(port).takeUnretainedValue(), .commonModes)
    }
}

/// Reads standard input on its own thread, one line at a time, and hands complete lines to the session on the main queue.
func readInput(_ session: PresenceSession) {
    let thread = Thread {
        var pending = [UInt8]()
        var chunk = [UInt8](repeating: 0, count: 65_536)
        while true {
            let count = chunk.withUnsafeMutableBytes { read(0, $0.baseAddress, $0.count) }
            if count < 0, errno == EINTR { continue }
            if count <= 0 {
                DispatchQueue.main.async { session.inputClosed() }
                return
            }
            pending.append(contentsOf: chunk[0..<count])
            var lines: [[UInt8]] = []
            while let newline = pending.firstIndex(of: 0x0A) {
                lines.append(Array(pending[0..<newline]))
                pending.removeSubrange(0...newline)
            }
            let tooLong = pending.count > maxLine || lines.contains { $0.count > maxLine }
            DispatchQueue.main.async {
                if tooLong { session.oversize() }
                session.receive(lines)
            }
            if tooLong { return }
        }
    }
    thread.stackSize = 4 * 1024 * 1024
    thread.start()
}

func runSession(_ blobPath: String) -> Never {
    signal(SIGPIPE, SIG_IGN)
    let session = PresenceSession(blobPath: blobPath)
    presenceSession = session
    watch(session)
    readInput(session)
    CFRunLoopRun()
    exit(4)
}

// MARK: - One check per signature

/// Signs one record's content hash after one presence check whose dialog opens with the helper's own label.
func signRecord(_ blobPath: String, _ reason: String?) -> Never {
    requireEnclave()
    var input = [UInt8]()
    var chunk = [UInt8](repeating: 0, count: 65_536)
    while true {
        let count = chunk.withUnsafeMutableBytes { read(0, $0.baseAddress, $0.count) }
        if count < 0, errno == EINTR { continue }
        if count <= 0 { break }
        input.append(contentsOf: chunk[0..<count])
        if input.count > maxLine { fail(5, "record.form: the record is longer than 8 MiB.") }
    }
    let record: Record
    do {
        record = try Record.read(input)
    } catch let failure as JSONFailure {
        fail(5, "record.form: the record is refused: \(failure.detail).")
    } catch {
        fail(5, "record.form: the record could not be read.")
    }
    let text = boundedReason(label: "sign Vericairn record \(record.type) \(record.shortHash)", reason: reason)
    #if VERICAIRN_SESSION_TESTS
    diagnose("dialog: " + text)
    #endif
    do {
        let context = LAContext()
        context.localizedReason = text
        let key = try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: readBlob(blobPath), authenticationContext: context)
        let signature = try key.signature(for: Precomputed(bytes: record.digest))
        print(signature.rawRepresentation.base64EncodedString())
        exit(0)
    } catch {
        fail(4, "The Secure Enclave refused: \(error.localizedDescription)")
    }
}

// MARK: - Commands

let usage = "usage: vericairn-enclave available | create <blob> [--presence] | public <blob> | sign <blob> <digest-hex> [reason] | sign-record <blob> [reason] | session <blob>"
let arguments = CommandLine.arguments
guard arguments.count >= 2 else { fail(2, usage) }

do {
    switch arguments[1] {
    case "available":
        print(SecureEnclave.isAvailable ? "available" : "unavailable")
        exit(SecureEnclave.isAvailable ? 0 : 3)

    case "create":
        guard arguments.count >= 3 else { fail(2, usage) }
        requireEnclave()
        let path = arguments[2]
        if FileManager.default.fileExists(atPath: path) {
            fail(4, "A key blob already exists at the path given; keys are never overwritten.")
        }
        var flags: SecAccessControlCreateFlags = [.privateKeyUsage]
        if arguments.dropFirst(3).contains("--presence") {
            flags.insert(.userPresence)
        }
        var error: Unmanaged<CFError>?
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenUnlockedThisDeviceOnly, flags, &error) else {
            fail(4, "The access control could not be created.")
        }
        let key = try SecureEnclave.P256.Signing.PrivateKey(accessControl: access)
        guard FileManager.default.createFile(atPath: path, contents: key.dataRepresentation, attributes: [.posixPermissions: 0o600]) else {
            fail(4, "The key blob could not be written.")
        }
        print(key.publicKey.derRepresentation.base64EncodedString())

    case "public":
        guard arguments.count >= 3 else { fail(2, usage) }
        requireEnclave()
        let key = try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: readBlob(arguments[2]))
        print(key.publicKey.derRepresentation.base64EncodedString())

    case "sign":
        guard arguments.count >= 4, let digest = hexBytes(arguments[3]) else { fail(2, usage) }
        requireEnclave()
        let text = boundedReason(label: "sign an unread Vericairn digest", reason: arguments.count >= 5 ? arguments[4] : nil)
        #if VERICAIRN_SESSION_TESTS
        diagnose("dialog: " + text)
        #endif
        let context = LAContext()
        context.localizedReason = text
        let key = try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: readBlob(arguments[2]), authenticationContext: context)
        let signature = try key.signature(for: Precomputed(bytes: digest))
        print(signature.rawRepresentation.base64EncodedString())

    case "sign-record":
        guard arguments.count == 3 || arguments.count == 4 else { fail(2, usage) }
        signRecord(arguments[2], arguments.count == 4 ? arguments[3] : nil)

    case "session":
        guard arguments.count == 3 else { fail(2, usage) }
        runSession(arguments[2])

    #if VERICAIRN_SESSION_TESTS
    case "canonical":
        // Test hook: whether standard input is in the compact canonical form, with its SHA-256.
        let input = FileHandle.standardInput.readDataToEndOfFile()
        do {
            _ = try JSONParser.parse(Array(input), canonical: true)
            print("canonical sha256:" + hex(sha256(Array(input))))
        } catch let failure as JSONFailure {
            fail(5, "not canonical: " + failure.detail)
        }

    case "fingerprint":
        // Test hook: the fingerprint of the terms in a file for a base64url nonce.
        guard arguments.count == 4, let terms = FileManager.default.contents(atPath: arguments[2]), let nonce = base64urlDecode(Array(arguments[3].utf8)) else { fail(2, usage) }
        print(fingerprint(nonce: nonce, termsDigest: sha256(Array(terms))))
    #endif

    default:
        fail(2, usage)
    }
} catch {
    fail(4, "The Secure Enclave refused: \(error.localizedDescription)")
}
