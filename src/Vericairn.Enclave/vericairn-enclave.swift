// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr
//
// vericairn-enclave: Secure Enclave custody for Vericairn device keys on macOS. A key is generated inside the Secure Enclave and
// kept as its enclave-wrapped blob, which only this Mac's enclave can use, so a copied file signs nothing anywhere else. A key
// created with --presence asks for Touch ID or the login password at every signature. The command line calls this helper and it
// keeps no state of its own. It uses CryptoKit directly, so it needs no keychain entitlement and no Apple developer signing.
//
//   vericairn-enclave available
//   vericairn-enclave create <blob> [--presence]       prints the public key (SubjectPublicKeyInfo, base64)
//   vericairn-enclave public <blob>                    prints the public key (SubjectPublicKeyInfo, base64)
//   vericairn-enclave sign <blob> <digest-hex> [reason] prints the ECDSA P-256 signature of the 32-byte digest (IEEE P1363, base64)
//
// Exit codes: 0 done, 2 usage, 3 no Secure Enclave on this machine, 4 the operation failed or was declined.

import CryptoKit
import Foundation
import LocalAuthentication

/// A digest computed elsewhere: Vericairn signs the 32 bytes of a record's content hash and never hashes them again.
struct Precomputed: Digest {
    static var byteCount: Int { 32 }
    let bytes: [UInt8]

    func withUnsafeBytes<R>(_ body: (UnsafeRawBufferPointer) throws -> R) rethrows -> R { try bytes.withUnsafeBytes(body) }

    func makeIterator() -> IndexingIterator<[UInt8]> { bytes.makeIterator() }

    var description: String { bytes.map { String(format: "%02x", $0) }.joined() }
}

func fail(_ code: Int32, _ message: String) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
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
    guard text.count == 64 else { return nil }
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

let usage = "usage: vericairn-enclave available | create <blob> [--presence] | public <blob> | sign <blob> <digest-hex> [reason]"
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
        let context = LAContext()
        context.localizedReason = arguments.count >= 5 ? arguments[4] : "sign a Vericairn record"
        let key = try SecureEnclave.P256.Signing.PrivateKey(dataRepresentation: readBlob(arguments[2]), authenticationContext: context)
        let signature = try key.signature(for: Precomputed(bytes: digest))
        print(signature.rawRepresentation.base64EncodedString())

    default:
        fail(2, usage)
    }
} catch {
    fail(4, "The Secure Enclave refused: \(error.localizedDescription)")
}
