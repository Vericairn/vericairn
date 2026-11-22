// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Canonical;

namespace Vericairn.Records.Signing;

/// <summary>
/// From 0.9.4 (ID-16): the presence session's protocol, as the runtime holds it (design/presence-sessions.md §§ 5, 6 and 10): the terms'
/// format, the action vocabulary, the allow-list of exact types, the closed set of kind entries, and the protocol's bounds.
/// </summary>
internal static class PresenceSessionProtocol
{
    /// <summary>The terms' format and the protocol's name.</summary>
    internal const string Format = "vericairn.presence-session/1";

    /// <summary>The shortest limit a session may have, in seconds.</summary>
    internal const int MinLimitSeconds = 60;

    /// <summary>The longest limit a session may have, in seconds; also how long a check waits for the person.</summary>
    internal const int MaxLimitSeconds = 300;

    /// <summary>The most signatures one list covers.</summary>
    internal const int MaxSignatures = 128;

    /// <summary>The most exact entries one list holds.</summary>
    internal const int MaxExact = 16;

    /// <summary>The action vocabulary: only these name a session.</summary>
    internal static readonly FrozenSet<string> Actions = FrozenSet.ToFrozenSet(
    [
        "operator close", "operator record", "operator unfinished", "changes push", "changes open", "changes merge", "changes observe",
        "release submit", "release observe", "release withdraw", "decision from ask", "docs pass assign", "work move-plan", "goal step",
        // From 0.9.4 (OPS-24, decision 01M8001M0T9XRGF7SKHW3B45KF): the one reviewed act that signs every ready operator item selected.
        "operator act",
    ], StringComparer.Ordinal);

    /// <summary>The record types an exact entry may have (§ 10); a release only when withdrawn, an operation only of <see cref="ExactActions"/>.</summary>
    internal static readonly FrozenSet<string> ExactTypes = FrozenSet.ToFrozenSet(
    [
        "acceptance", "review", "decision", "ask", "assignment", "document-revision", "document-review", "work-order", "work-status",
        "operator-request", "operator-request-close", "release", "operation",
    ], StringComparer.Ordinal);

    /// <summary>The operation actions of the closed set of kind entries.</summary>
    internal static readonly FrozenSet<string> KindActions = FrozenSet.ToFrozenSet(
        ["change-push", "change-open", "change-merge", "change-observe", "dispatch-release-workflow"], StringComparer.Ordinal);

    /// <summary>The operation actions an exact entry may carry: the closed set's, an agent run and a withdrawn draft's deletion.</summary>
    internal static readonly FrozenSet<string> ExactActions = FrozenSet.ToFrozenSet(
        ["change-push", "change-open", "change-merge", "change-observe", "dispatch-release-workflow", "agent-run", "delete-release-draft"], StringComparer.Ordinal);

    /// <summary>The six statuses an operation may state.</summary>
    internal static readonly string[] OperationStatuses = ["canceled", "dispatched", "known-failure", "not-started", "observed-success", "uncertain"];

    /// <summary>The statuses a release kind entry may allow.</summary>
    internal static readonly FrozenSet<string> ReleaseStatuses = FrozenSet.ToFrozenSet(["candidate", "published"], StringComparer.Ordinal);

    /// <summary>The observations of the closed set.</summary>
    internal static readonly FrozenSet<string> Observations = FrozenSet.ToFrozenSet(["push-head", "pull-request", "target-run"], StringComparer.Ordinal);

    /// <summary>The change actions, whose records state a head.</summary>
    internal static readonly FrozenSet<string> ChangeActions = FrozenSet.ToFrozenSet(["change-push", "change-open", "change-merge", "change-observe"], StringComparer.Ordinal);

    /// <summary>The names each kind entry's fixed values must and may carry.</summary>
    internal static readonly FrozenDictionary<string, (string[] Required, string[] Optional)> FixedNames = new Dictionary<string, (string[], string[])>(StringComparer.Ordinal)
    {
        ["change-push"] = (["provider", "repository", "product", "productHash", "branch"], ["head"]),
        ["change-open"] = (["provider", "repository", "product", "productHash", "branch"], ["head"]),
        ["change-merge"] = (["provider", "repository", "product", "productHash"], ["pullRequest", "head"]),
        ["change-observe"] = ([], ["provider", "repository", "product", "productHash", "head"]),
        ["dispatch-release-workflow"] = (["provider", "destination", "version", "product", "commit", "workflow"], []),
        ["release"] = (["product", "version", "destination", "commit"], []),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Whether a record of this type, operation action and status may be an exact entry (§ 10's allow-list); every other type and action,
    /// and every one added later, keeps one check per signature.
    /// </summary>
    internal static bool ExactAllowed(string type, string? action, string? status) => ExactTypes.Contains(type) && type switch
    {
        "operation" => action is not null && ExactActions.Contains(action),
        "release" => status == "withdrawn",
        _ => true,
    };

    /// <summary>
    /// Whether a value is held to the character set of its fixed name (§ 6); from decision 01M7J7J6MRZPDMYDRGK0MDEZR7 a release build's
    /// workflow to the branch's, letters, digits and <c>._/-</c>, at most 100 characters.
    /// </summary>
    internal static bool ValidFixed(string name, string? value) => name switch
    {
        "provider" => PresenceText.IsSlug(value),
        "repository" or "product" => PresenceText.IsUlid(value),
        "productHash" => PresenceText.IsHash(value),
        "branch" or "destination" or "workflow" => PresenceText.IsBranch(value),
        "pullRequest" => PresenceText.IsPullRequest(value),
        "head" or "commit" => PresenceText.IsCommit(value),
        "version" => PresenceText.IsVersion(value),
        _ => false,
    };
}

/// <summary>A refusal of the session's protocol: its code (§ 6) and a detail of at most 200 characters, never record content.</summary>
internal sealed class PresenceSessionRefusal : InvalidOperationException
{
    internal PresenceSessionRefusal(string code, string detail)
        : base(detail.Length <= 200 ? detail : detail[..200])
    {
        Code = code;
    }

    /// <summary>The refusal code, such as record.outside.</summary>
    internal string Code { get; }
}

/// <summary>The character sets and forms the protocol holds values to, byte for byte and never by Unicode equivalence.</summary>
internal static class PresenceText
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    internal static bool IsAscii(string? text) => text is not null && text.All(c => c < 0x80);

    internal static bool IsUlid(string? text) => text is { Length: 26 } && text[0] is >= '0' and <= '7' && text.All(c => Crockford.Contains(c, StringComparison.Ordinal));

    internal static bool IsHash(string? text) => text is { Length: 71 } && text.StartsWith("sha256:", StringComparison.Ordinal) && text[7..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsCommit(string? text) => text is { Length: 40 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsSlug(string? text, int max = 100)
    {
        if (text is null || text.Length < 1 || text.Length > max || text[0] == '-' || text[^1] == '-') return false;
        bool hyphen = false;
        foreach (char c in text)
        {
            if (c == '-')
            {
                if (hyphen) return false;
                hyphen = true;
            }
            else if (c is >= '0' and <= '9' or >= 'a' and <= 'z') hyphen = false;
            else return false;
        }

        return true;
    }

    private static bool LetterOrDigit(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    internal static bool IsBranch(string? text) => text is { Length: >= 1 and <= 100 } && text.All(c => LetterOrDigit(c) || c is '.' or '_' or '/' or '-');

    internal static bool IsVersion(string? text) => text is { Length: >= 1 and <= 40 } && text.All(c => LetterOrDigit(c) || c is '.' or '+' or '-');

    internal static bool IsPullRequest(string? text) => text is { Length: >= 1 and <= 10 } && text.All(c => c is >= '0' and <= '9');

    /// <summary>The seconds since 1970 of a record timestamp, YYYY-MM-DDTHH:MM:SSZ exactly; null for anything else.</summary>
    internal static long? Timestamp(string? text)
    {
        if (text is not { Length: 20 } || text[4] != '-' || text[7] != '-' || text[10] != 'T' || text[13] != ':' || text[16] != ':' || text[19] != 'Z') return null;
        long? Field(int start, int length)
        {
            long value = 0;
            for (int i = start; i < start + length; i++)
            {
                if (text[i] is < '0' or > '9') return null;
                value = value * 10 + (text[i] - '0');
            }

            return value;
        }

        if (Field(0, 4) is not long year || Field(5, 2) is not long month || Field(8, 2) is not long day || Field(11, 2) is not long hour
            || Field(14, 2) is not long minute || Field(17, 2) is not long second || month is < 1 or > 12 || hour > 23 || minute > 59 || second > 59) return null;
        if (year < 1 || day < 1 || day > DateTime.DaysInMonth((int)year, (int)month)) return null;
        return new DateTimeOffset((int)year, (int)month, (int)day, (int)hour, (int)minute, (int)second, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    /// <summary>Base64url without padding, refusing any other alphabet, padding and trailing bits that are not zero.</summary>
    internal static byte[]? Base64Url(string? text)
    {
        if (text is null) return null;
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        if (text.Length % 4 == 1) return null;
        var values = new int[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            int index = alphabet.IndexOf(text[i], StringComparison.Ordinal);
            if (index < 0) return null;
            values[i] = index;
        }

        var output = new List<byte>(text.Length * 3 / 4);
        int whole = text.Length / 4 * 4;
        for (int i = 0; i < whole; i += 4)
        {
            int n = values[i] << 18 | values[i + 1] << 12 | values[i + 2] << 6 | values[i + 3];
            output.Add((byte)(n >> 16));
            output.Add((byte)(n >> 8));
            output.Add((byte)n);
        }

        int rest = text.Length - whole;
        if (rest == 2)
        {
            if ((values[whole + 1] & 0xF) != 0) return null;
            output.Add((byte)(values[whole] << 2 | values[whole + 1] >> 4));
        }
        else if (rest == 3)
        {
            if ((values[whole + 2] & 0x3) != 0) return null;
            int n = values[whole] << 18 | values[whole + 1] << 12 | values[whole + 2] << 6;
            output.Add((byte)(n >> 16));
            output.Add((byte)(n >> 8));
        }

        return [.. output];
    }

    /// <summary>Base64url without padding.</summary>
    internal static string Base64UrlText(ReadOnlySpan<byte> bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);

    /// <summary>
    /// The value parsed when the bytes are exactly the compact canonical form records use (members sorted by UTF-16 code units, no member
    /// twice, no white space, only RFC 8785's escapes, plain safe integers, well-formed UTF-8); otherwise why not.
    /// </summary>
    internal static JsonElement? Canonical(byte[] bytes, out string? problem)
    {
        problem = null;
        try
        {
            using JsonDocument document = CanonicalJson.Parse(bytes);
            byte[] again = CanonicalJson.SerializeUtf8(document.RootElement);
            if (again.AsSpan().SequenceEqual(bytes)) return document.RootElement.Clone();
            problem = "not the compact canonical form";
            return null;
        }
        catch (Exception exception) when (exception is CanonicalJsonException or InvalidOperationException or ArgumentException or JsonException or FormatException)
        {
            problem = "not the compact canonical form";
            return null;
        }
    }

    /// <summary>The SHA-256 content hash of bytes, as sha256:hex.</summary>
    internal static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>A record as the session reads it: the canonical bytes signed (the envelope without hash and signatures) and its fields.</summary>
internal sealed class PresenceRecord
{
    private static readonly FrozenSet<string> Members = FrozenSet.ToFrozenSet(["at", "body", "by", "format", "id", "subject", "supersedes", "type"], StringComparer.Ordinal);

    private PresenceRecord(byte[] bytes, JsonElement body, string type, string id, long at, string by, (string Type, string Id, string? Hash)? subject, string? supersedes)
    {
        Bytes = bytes;
        Hash = PresenceText.Hash(bytes);
        Body = body;
        Type = type;
        Id = id;
        At = at;
        By = by;
        Subject = subject;
        Supersedes = supersedes;
    }

    internal byte[] Bytes { get; }

    internal string Hash { get; }

    internal JsonElement Body { get; }

    internal string Type { get; }

    internal string Id { get; }

    internal long At { get; }

    internal string By { get; }

    internal (string Type, string Id, string? Hash)? Subject { get; }

    internal string? Supersedes { get; }

    internal string ShortHash => Hash[7..19];

    internal JsonElement? FieldValue(string name) => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty(name, out JsonElement value) ? value : null;

    internal string? Field(string name) => FieldValue(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    internal JsonElement? TargetValue(string name) => FieldValue("target") is { ValueKind: JsonValueKind.Object } target && target.TryGetProperty(name, out JsonElement value) ? value : null;

    internal string? Target(string name) => TargetValue(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>Reads a record from its bytes, or throws why it is not one (the caller names the refusal's code).</summary>
    internal static PresenceRecord Read(byte[] bytes, string code)
    {
        if (PresenceText.Canonical(bytes, out string? problem) is not JsonElement root) throw new PresenceSessionRefusal(code, "The record is refused: " + problem + ".");
        if (root.ValueKind != JsonValueKind.Object) throw new PresenceSessionRefusal(code, "The record is refused: a record is a JSON object.");
        foreach (JsonProperty member in root.EnumerateObject())
        {
            if (!Members.Contains(member.Name))
                throw new PresenceSessionRefusal(code, "The record is refused: a record signed under a check holds no member " + (member.Name is "hash" or "signatures" ? member.Name : "outside its envelope") + ".");
        }

        string? format = Text(root, "format");
        if (format is null || !format.StartsWith("vericairn.record/", StringComparison.Ordinal) || format.Length <= 17 || !format[17..].All(char.IsAsciiDigit))
            throw new PresenceSessionRefusal(code, "The record is refused: the record's format is not a record format.");
        string? type = Text(root, "type");
        if (!PresenceText.IsSlug(type, 64)) throw new PresenceSessionRefusal(code, "The record is refused: the record's type is not a record type.");
        string? id = Text(root, "id");
        if (!PresenceText.IsUlid(id)) throw new PresenceSessionRefusal(code, "The record is refused: the record's id is not a ULID.");
        if (PresenceText.Timestamp(Text(root, "at")) is not long at) throw new PresenceSessionRefusal(code, "The record is refused: the record's at is not a record timestamp.");
        string? by = Text(root, "by");
        if (!PresenceText.IsUlid(by)) throw new PresenceSessionRefusal(code, "The record is refused: the record's by is not a ULID.");
        if (!root.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
            throw new PresenceSessionRefusal(code, "The record is refused: the record's body is not an object.");
        (string, string, string?)? subject = null;
        if (root.TryGetProperty("subject", out JsonElement value))
        {
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(m => m.Name is not ("type" or "id" or "hash"))
                || Text(value, "type") is not string subjectType || !PresenceText.IsSlug(subjectType, 64) || Text(value, "id") is not string subjectId || !PresenceText.IsUlid(subjectId))
                throw new PresenceSessionRefusal(code, "The record is refused: the record's subject is not {type, id, hash}.");
            string? hash = Text(value, "hash");
            if (value.TryGetProperty("hash", out _) && !PresenceText.IsHash(hash)) throw new PresenceSessionRefusal(code, "The record is refused: the record's subject hash is not a content hash.");
            subject = (subjectType, subjectId, hash);
        }

        string? supersedes = null;
        if (root.TryGetProperty("supersedes", out JsonElement previous))
        {
            supersedes = previous.ValueKind == JsonValueKind.String ? previous.GetString() : null;
            if (!PresenceText.IsUlid(supersedes)) throw new PresenceSessionRefusal(code, "The record is refused: the record's supersedes is not a ULID.");
        }

        return new PresenceRecord(bytes, body.Clone(), type!, id!, at, by!, subject, supersedes);
    }

    internal static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>One entry of terms as the session reads them.</summary>
internal sealed class PresenceTermsEntry
{
    internal int Index { get; init; }

    internal bool Exact { get; init; }

    internal string Type { get; init; } = string.Empty;

    internal string? Action { get; init; }

    internal string? Hash { get; init; }

    internal int Count { get; init; }

    internal bool AtMost { get; init; }

    internal IReadOnlyList<string> Statuses { get; init; } = [];

    internal IReadOnlyDictionary<string, string> Fixed { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    internal string? Observation { get; init; }

    internal int? Observes { get; init; }

    internal string? ObservesReference { get; init; }

    internal int? PullRequestFrom { get; init; }

    internal string? Trigger { get; init; }

    internal PresenceRecord? Record { get; set; }
}

/// <summary>One reference of terms as the session reads them.</summary>
internal sealed class PresenceTermsReference
{
    internal string Id { get; init; } = string.Empty;

    internal string Type { get; init; } = string.Empty;

    internal string Hash { get; init; } = string.Empty;

    internal PresenceRecord? Record { get; set; }
}

/// <summary>
/// From 0.9.4 (ID-16): a presence session's checks, as the release helper applies them (design/presence-sessions.md §§ 6 and 8): the terms
/// and their bytes at opening, and before every signature the record's form, author, entry, references and time. The runtime applies them
/// before it sends a record, so it renews rather than send one the list does not cover; the helper applies them again by itself, and the
/// two read the same shared vectors, so they disagree only through a defect or an attack. Nothing here signs.
/// </summary>
internal sealed class PresenceSessionRules
{
    private readonly bool[] used;
    private readonly int[] left;
    private readonly Dictionary<string, (int Entry, PresenceRecord Record)> signed = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<string>> signedUnder = [];
    private readonly HashSet<string> superseded = new(StringComparer.Ordinal);
    private string? head;

    private PresenceSessionRules(byte[] bytes, string action, string principal, string device, string session, byte[] publicKey, int limit,
        List<PresenceTermsEntry> entries, List<PresenceTermsReference> references)
    {
        Bytes = bytes;
        Action = action;
        Principal = principal;
        Device = device;
        Session = session;
        PublicKey = publicKey;
        LimitSeconds = limit;
        Entries = entries;
        References = references;
        used = new bool[entries.Count];
        left = [.. entries.Select(e => e.Exact ? 0 : e.Count)];
    }

    /// <summary>The terms' canonical bytes.</summary>
    internal byte[] Bytes { get; }

    internal string Action { get; }

    internal string Principal { get; }

    internal string Device { get; }

    internal string Session { get; }

    /// <summary>The device's public key as SubjectPublicKeyInfo bytes.</summary>
    internal byte[] PublicKey { get; }

    internal int LimitSeconds { get; }

    internal IReadOnlyList<PresenceTermsEntry> Entries { get; }

    internal IReadOnlyList<PresenceTermsReference> References { get; }

    internal int ExactCount => Entries.Count(e => e.Exact);

    internal int Total => Entries.Sum(e => e.Exact ? 1 : e.Count);

    internal bool UpTo => Entries.Any(e => !e.Exact && e.AtMost);

    /// <summary>How many signatures the list still covers.</summary>
    internal int Left => Entries.Where(e => e.Exact && !used[e.Index]).Count() + left.Sum();

    /// <summary>The ids of the records signed under the session so far.</summary>
    internal IReadOnlyCollection<string> SignedIds => signed.Keys;

    /// <summary>Whether an exact entry is still unused.</summary>
    internal bool Unused(int entry) => !used[entry];

    /// <summary>How many signatures a kind entry still covers.</summary>
    internal int LeftOf(int entry) => left[entry];

    /// <summary>
    /// Reads and checks terms and the bytes that come with them, as the helper does at opening (§ 8): form and members, then size,
    /// then the bytes, each exact entry by the principal and dated within the window around <paramref name="received"/>.
    /// </summary>
    internal static PresenceSessionRules Open(byte[] terms, IReadOnlyList<byte[]> bytes, long received)
    {
        PresenceSessionRules rules = ReadTerms(terms);
        rules.ReadBytes(bytes, received);
        return rules;
    }

    /// <summary>Reads and checks terms alone (terms.invalid, terms.not-allowed, terms.size).</summary>
    internal static PresenceSessionRules ReadTerms(byte[] bytes)
    {
        if (PresenceText.Canonical(bytes, out string? problem) is not JsonElement json) throw Refuse("terms.invalid", "The terms are not in the compact canonical form: " + problem + ".");
        string[] members = ["format", "action", "actionIdentity", "principal", "device", "publicKey", "session", "limitSeconds", "entries", "references"];
        if (json.ValueKind != JsonValueKind.Object) throw Refuse("terms.invalid", "The terms are a JSON object.");
        foreach (JsonProperty member in json.EnumerateObject())
        {
            if (!members.Contains(member.Name, StringComparer.Ordinal)) throw Refuse("terms.invalid", "The terms hold a member outside the protocol.");
        }

        foreach (string key in members.Order(StringComparer.Ordinal))
        {
            if (!json.TryGetProperty(key, out _)) throw Refuse("terms.invalid", $"The terms lack their {key}.");
        }

        if (Ascii(json, "format") != PresenceSessionProtocol.Format) throw Refuse("terms.invalid", $"The terms' format is not {PresenceSessionProtocol.Format}.");
        string action = Ascii(json, "action") ?? throw Refuse("terms.invalid", "The terms' action is not a name.");
        if (!PresenceSessionProtocol.Actions.Contains(action)) throw Refuse("terms.not-allowed", "The terms' action is outside the action vocabulary.");
        if (!PresenceText.IsHash(PresenceRecord.Text(json, "actionIdentity"))) throw Refuse("terms.invalid", "The terms' actionIdentity is not a SHA-256 hash.");
        foreach (string key in new[] { "principal", "device", "session" })
        {
            if (!PresenceText.IsUlid(PresenceRecord.Text(json, key))) throw Refuse("terms.invalid", $"The terms' {key} is not a ULID.");
        }

        byte[] prefix = [0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04];
        byte[]? key2 = PresenceText.Base64Url(PresenceRecord.Text(json, "publicKey"));
        if (key2 is not { Length: 91 } || !key2.AsSpan(0, 27).SequenceEqual(prefix)) throw Refuse("terms.invalid", "The terms' publicKey is not a P-256 public key in base64url.");
        JsonElement limitValue = json.GetProperty("limitSeconds");
        if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt64(out long limit)) throw Refuse("terms.invalid", "The terms' limitSeconds is not an integer.");

        JsonElement referenceItems = json.GetProperty("references");
        if (referenceItems.ValueKind != JsonValueKind.Array) throw Refuse("terms.invalid", "The terms' references are not an array.");
        var references = new List<PresenceTermsReference>();
        foreach (JsonElement item in referenceItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !SameKeys(item, "id", "type", "hash") || PresenceRecord.Text(item, "id") is not string id || !PresenceText.IsUlid(id)
                || PresenceRecord.Text(item, "type") is not string type || !PresenceText.IsSlug(type, 64) || PresenceRecord.Text(item, "hash") is not string hash || !PresenceText.IsHash(hash))
                throw Refuse("terms.invalid", "A reference is not {id, type, hash}.");
            if (references.Count > 0 && string.CompareOrdinal(references[^1].Id, id) >= 0) throw Refuse("terms.invalid", "The references are not sorted by id, or one is repeated.");
            if (type is not ("operation" or "release")) throw Refuse("terms.not-allowed", "A reference is of a type no kind entry supersedes or observes.");
            references.Add(new PresenceTermsReference { Id = id, Type = type, Hash = hash });
        }

        JsonElement entryItems = json.GetProperty("entries");
        if (entryItems.ValueKind != JsonValueKind.Array || entryItems.GetArrayLength() == 0) throw Refuse("terms.invalid", "The terms' entries are not a non-empty array.");
        int itemCount = entryItems.GetArrayLength();
        var entries = new List<PresenceTermsEntry>();
        int index = 0;
        foreach (JsonElement item in entryItems.EnumerateArray())
        {
            entries.Add(ReadEntry(item, index, itemCount));
            index++;
        }

        foreach (PresenceTermsEntry entry in entries)
        {
            if (entry.Observes is int observed)
            {
                PresenceTermsEntry target = entries[observed];
                if (observed == entry.Index || target.Type != "operation" || target.Action is not ("change-push" or "change-open" or "change-merge"))
                    throw Refuse("terms.invalid", $"Entry {entry.Index} observes an entry that is not a push, an opening or a merge.");
            }

            if (entry.ObservesReference is string reference && !references.Any(r => r.Id == reference && r.Type == "operation"))
                throw Refuse("terms.invalid", $"Entry {entry.Index} observes no operation among the references.");
            if (entry.PullRequestFrom is int from && (entries[from].Exact || entries[from].Action != "change-open"))
                throw Refuse("terms.invalid", $"Entry {entry.Index} takes its pull request from an entry that is not an opening.");
        }

        if (limit is < PresenceSessionProtocol.MinLimitSeconds or > PresenceSessionProtocol.MaxLimitSeconds) throw Refuse("terms.size", "The terms' limit is outside 60 to 300 seconds.");
        var rules = new PresenceSessionRules(bytes, action, PresenceRecord.Text(json, "principal")!, PresenceRecord.Text(json, "device")!, PresenceRecord.Text(json, "session")!,
            key2, (int)limit, entries, references);
        if (rules.Total > PresenceSessionProtocol.MaxSignatures) throw Refuse("terms.size", "The list covers more than 128 signatures.");
        if (rules.ExactCount > PresenceSessionProtocol.MaxExact) throw Refuse("terms.size", "The list holds more than 16 exact entries.");
        return rules;
    }

    private static PresenceTermsEntry ReadEntry(JsonElement item, int index, int itemCount)
    {
        string at = "Entry " + index;
        string? kind = item.ValueKind == JsonValueKind.Object ? Ascii(item, "entry") : null;
        if (kind is not ("exact" or "kind")) throw Refuse("terms.invalid", $"{at} is neither exact nor kind.");
        string? type = PresenceRecord.Text(item, "type");
        if (!PresenceText.IsSlug(type, 64)) throw Refuse("terms.invalid", $"{at} has no record type.");
        if (kind == "exact")
        {
            if (!PresenceSessionProtocol.ExactTypes.Contains(type!)) throw Refuse("terms.not-allowed", $"{at} is of a type that keeps one check per signature.");
            string? exactAction = null;
            if (type == "operation")
            {
                exactAction = Ascii(item, "action") ?? throw Refuse("terms.invalid", $"{at} is an operation without its action.");
                if (!PresenceSessionProtocol.ExactActions.Contains(exactAction)) throw Refuse("terms.not-allowed", $"{at} is an operation whose action keeps one check per signature.");
            }

            bool shaped = type == "operation" ? SameKeys(item, "entry", "type", "hash", "action") : SameKeys(item, "entry", "type", "hash");
            if (!shaped) throw Refuse("terms.invalid", $"{at} holds members other than an exact entry's.");
            string? hash = PresenceRecord.Text(item, "hash");
            if (!PresenceText.IsHash(hash)) throw Refuse("terms.invalid", $"{at} has no content hash.");
            return new PresenceTermsEntry { Index = index, Exact = true, Type = type!, Action = exactAction, Hash = hash, Count = 1 };
        }

        var allowed = new HashSet<string>(["entry", "type", "count", "atMost"], StringComparer.Ordinal);
        string? action = null;
        switch (type)
        {
            case "operation":
                action = Ascii(item, "action") ?? throw Refuse("terms.invalid", $"{at} is an operation without its action.");
                if (!PresenceSessionProtocol.KindActions.Contains(action)) throw Refuse("terms.not-allowed", $"{at} is an operation outside the closed set of kind entries.");
                allowed.UnionWith(["action", "statuses", "fixed"]);
                if (action == "change-observe") allowed.UnionWith(["observation", "observes", "observesReference"]);
                if (action == "change-merge") allowed.Add("pullRequestFrom");
                break;
            case "history-checkpoint":
                string trigger = Ascii(item, "trigger") ?? throw Refuse("terms.invalid", $"{at} is a history checkpoint without its trigger.");
                if (trigger != "release") throw Refuse("terms.not-allowed", $"{at} is a history checkpoint of a trigger that keeps one check per signature.");
                allowed.Add("trigger");
                break;
            case "release":
                allowed.UnionWith(["statuses", "fixed"]);
                break;
            default:
                throw Refuse("terms.not-allowed", $"{at} is of a type outside the closed set of kind entries.");
        }

        var keys = item.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!keys.IsSubsetOf(allowed)) throw Refuse("terms.invalid", $"{at} holds a member outside its kind's.");
        if (!allowed.Except(["observes", "observesReference", "pullRequestFrom"]).All(keys.Contains)) throw Refuse("terms.invalid", $"{at} lacks a member its kind needs.");
        if (!item.TryGetProperty("count", out JsonElement countValue) || countValue.ValueKind != JsonValueKind.Number || !countValue.TryGetInt64(out long stated) || stated < 1)
            throw Refuse("terms.invalid", $"{at} has no count from 1.");
        int count = (int)Math.Min(stated, 129);
        JsonElement atMostValue = item.GetProperty("atMost");
        if (atMostValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Refuse("terms.invalid", $"{at}'s atMost is not a boolean.");
        if (type == "history-checkpoint" && count != 1) throw Refuse("terms.invalid", $"{at} is a history checkpoint, whose count is one.");

        var statuses = new List<string>();
        if (item.TryGetProperty("statuses", out JsonElement list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0) throw Refuse("terms.invalid", $"{at}'s statuses are not a non-empty array.");
            string? previous = null;
            foreach (JsonElement value in list.EnumerateArray())
            {
                string? status = value.ValueKind == JsonValueKind.String && PresenceText.IsAscii(value.GetString()) ? value.GetString() : null;
                bool known = status is not null && (type == "release" ? PresenceSessionProtocol.ReleaseStatuses.Contains(status) : PresenceSessionProtocol.OperationStatuses.Contains(status, StringComparer.Ordinal));
                if (!known) throw Refuse("terms.invalid", $"{at} names a status outside its set.");
                if (previous is not null && string.CompareOrdinal(previous, status) >= 0) throw Refuse("terms.invalid", $"{at}'s statuses are not sorted, or one is repeated.");
                previous = status;
                statuses.Add(status!);
            }
        }

        var fixedValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.TryGetProperty("fixed", out JsonElement values))
        {
            if (values.ValueKind != JsonValueKind.Object || !PresenceSessionProtocol.FixedNames.TryGetValue(action ?? type!, out var names)) throw Refuse("terms.invalid", $"{at}'s fixed values are not an object.");
            foreach (JsonProperty pair in values.EnumerateObject())
            {
                string name = PresenceText.IsAscii(pair.Name) ? pair.Name : string.Empty;
                if (!names.Required.Contains(name, StringComparer.Ordinal) && !names.Optional.Contains(name, StringComparer.Ordinal)) throw Refuse("terms.invalid", $"{at} fixes a value its kind does not fix.");
                string? value = pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() : null;
                if (!PresenceSessionProtocol.ValidFixed(name, value)) throw Refuse("terms.invalid", $"{at}'s {name} is outside its character set.");
                fixedValues[name] = value!;
            }

            foreach (string name in names.Required)
            {
                if (!fixedValues.ContainsKey(name)) throw Refuse("terms.invalid", $"{at} does not fix its {name}.");
            }
        }

        string? observation = null;
        int? observes = null;
        string? observesReference = null;
        int? pullRequestFrom = null;
        if (action == "change-observe")
        {
            observation = Ascii(item, "observation");
            if (observation is null || !PresenceSessionProtocol.Observations.Contains(observation)) throw Refuse("terms.invalid", $"{at} names no observation of the closed set.");
            bool hasIndex = item.TryGetProperty("observes", out JsonElement indexValue), hasReference = item.TryGetProperty("observesReference", out JsonElement referenceValue);
            if (hasIndex && !hasReference)
            {
                if (indexValue.ValueKind != JsonValueKind.Number || !indexValue.TryGetInt64(out long value) || value < 0 || value >= itemCount) throw Refuse("terms.invalid", $"{at} observes no entry of the list.");
                observes = (int)value;
            }
            else if (!hasIndex && hasReference)
            {
                string? reference = referenceValue.ValueKind == JsonValueKind.String ? referenceValue.GetString() : null;
                if (!PresenceText.IsUlid(reference)) throw Refuse("terms.invalid", $"{at} observes no reference of the terms.");
                observesReference = reference;
            }
            else
            {
                throw Refuse("terms.invalid", $"{at} observes one entry or one reference.");
            }
        }

        if (action == "change-merge")
        {
            if (item.TryGetProperty("pullRequestFrom", out JsonElement from))
            {
                if (fixedValues.ContainsKey("pullRequest") || from.ValueKind != JsonValueKind.Number || !from.TryGetInt64(out long value) || value < 0 || value >= itemCount)
                    throw Refuse("terms.invalid", $"{at} takes its pull request from the terms or from one opening entry.");
                pullRequestFrom = (int)value;
            }
            else if (!fixedValues.ContainsKey("pullRequest"))
            {
                throw Refuse("terms.invalid", $"{at} fixes no pull request.");
            }
        }

        return new PresenceTermsEntry
        {
            Index = index, Exact = false, Type = type!, Action = action, Count = count, AtMost = atMostValue.ValueKind == JsonValueKind.True, Statuses = statuses, Fixed = fixedValues,
            Observation = observation, Observes = observes, ObservesReference = observesReference, PullRequestFrom = pullRequestFrom,
            Trigger = type == "history-checkpoint" ? "release" : null,
        };
    }

    /// <summary>
    /// Checks the bytes that come with the terms: each exact entry's record, then each reference's, in the terms' order, each in the
    /// compact canonical form, of its hash and type, and each exact entry by the session's principal and dated no earlier than 120
    /// seconds before <paramref name="received"/> and no later than 60 seconds after it.
    /// </summary>
    internal void ReadBytes(IReadOnlyList<byte[]> bytes, long received)
    {
        List<PresenceTermsEntry> exact = [.. Entries.Where(e => e.Exact)];
        if (bytes.Count != exact.Count + References.Count) throw Refuse("terms.bytes", "The bytes are missing, or more than the terms list.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int offset = 0; offset < bytes.Count; offset++)
        {
            string what = offset < exact.Count ? "Exact entry " + exact[offset].Index : "Reference " + (offset - exact.Count);
            PresenceRecord record;
            try
            {
                record = PresenceRecord.Read(bytes[offset], "terms.bytes");
            }
            catch (PresenceSessionRefusal refusal)
            {
                throw Refuse("terms.bytes", $"{what}'s bytes are refused: {refusal.Message}");
            }

            if (!ids.Add(record.Id)) throw Refuse("terms.bytes", $"{what} repeats a record id.");
            if (offset < exact.Count)
            {
                PresenceTermsEntry entry = exact[offset];
                if (record.Hash != entry.Hash) throw Refuse("terms.bytes", $"{what}'s bytes are of another hash.");
                if (record.Type != entry.Type) throw Refuse("terms.bytes", $"{what}'s bytes are of another type.");
                if (entry.Action is string action && record.Field("action") != action) throw Refuse("terms.bytes", $"{what}'s bytes are of another action.");
                if (entry.Type == "release" && record.Field("status") != "withdrawn") throw Refuse("terms.not-allowed", $"{what} is a release whose status is not withdrawn.");
                if (record.By != Principal) throw Refuse("terms.bytes", $"{what} is by another principal.");
                if (record.At < received - 120 || record.At > received + 60) throw Refuse("terms.bytes", $"{what} is dated outside the window of its opening.");
                entry.Record = record;
            }
            else
            {
                PresenceTermsReference reference = References[offset - exact.Count];
                if (record.Id != reference.Id || record.Type != reference.Type || record.Hash != reference.Hash) throw Refuse("terms.bytes", $"{what}'s bytes are of another id, type or hash.");
                reference.Record = record;
            }
        }
    }

    /// <summary>
    /// The entry a record would be signed under now (§ 8, checks 1 to 5), without signing or changing the session; or the refusal that
    /// the helper would answer with. <paramref name="openedAt"/> is the helper's opening by the wall clock, <paramref name="elapsed"/> the
    /// seconds since, on a clock that counts through sleep.
    /// </summary>
    internal int Match(byte[] bytes, long openedAt, double elapsed, out PresenceRecord record)
    {
        record = PresenceRecord.Read(bytes, "record.form");
        if (record.By != Principal) throw Refuse("record.author", "The record is by another principal.");
        string id = record.Id;
        if (signed.ContainsKey(id)) throw Refuse("record.author", "A record with this id was already signed in the session.");
        if (References.Any(r => r.Id == id)) throw Refuse("record.author", "The record takes the id of a reference of the session.");
        string hash = record.Hash;
        if (Entries.Any(e => e.Exact && e.Record?.Id == id && e.Hash != hash)) throw Refuse("record.author", "The record takes the id of an exact entry of the session.");
        return MatchEntry(record, openedAt, elapsed);
    }

    /// <summary>Counts a record signed under an entry, as the helper does once it has signed it.</summary>
    internal void Signed(PresenceRecord record, int entry)
    {
        if (Entries[entry].Exact) used[entry] = true;
        else left[entry]--;
        signed[record.Id] = (entry, record);
        if (!signedUnder.TryGetValue(entry, out List<string>? ids)) signedUnder[entry] = ids = [];
        ids.Add(record.Id);
        if (record.Supersedes is string previous) superseded.Add(previous);
        if (head is null && record.Type == "operation" && record.Field("action") is string action && PresenceSessionProtocol.ChangeActions.Contains(action)
            && PresenceText.IsCommit(record.Target("head")))
            head = record.Target("head");
    }

    private int MatchEntry(PresenceRecord record, long openedAt, double elapsed)
    {
        if (Entries.FirstOrDefault(e => e.Exact && !used[e.Index] && e.Hash == record.Hash) is { } exact) return exact.Index;
        if (record.Supersedes == record.Id) throw Refuse("record.reference", "The record supersedes itself.");
        List<PresenceTermsEntry> candidates = [.. Entries.Where(e => !e.Exact && left[e.Index] > 0 && Carries(record, e))];
        if (candidates.Count == 0) throw Refuse("record.outside", "The record matches no unused entry of the list.");
        PresenceSessionRefusal? first = null;
        foreach (PresenceTermsEntry entry in candidates)
        {
            if (ReferenceProblem(record, entry) is string problem)
            {
                first ??= Refuse("record.reference", problem);
                continue;
            }

            double earliest = openedAt - 60, latest = openedAt + elapsed + 60;
            if (record.At < earliest || record.At > latest) throw Refuse("record.time", "The record is dated outside the session's window.");
            return entry.Index;
        }

        throw first!;
    }

    private static string? Value(PresenceRecord record, string name) => name == "provider" ? record.Field("provider") : record.Target(name);

    // Where a dispatch states its destination or its commit: each field that may state it states the entry's value, and one does.
    private static bool StatesOnly(PresenceRecord record, string[] fields, string expected)
    {
        List<JsonElement> stated = [.. fields.Select(record.TargetValue).OfType<JsonElement>()];
        return stated.Count > 0 && stated.All(v => v.ValueKind == JsonValueKind.String && v.GetString() == expected);
    }

    private static bool FixedHeld(PresenceRecord record, PresenceTermsEntry entry)
    {
        foreach ((string name, string expected) in entry.Fixed)
        {
            bool held = (entry.Action, name) switch
            {
                ("dispatch-release-workflow", "destination") => StatesOnly(record, ["destination", "repository"], expected),
                ("dispatch-release-workflow", "commit") => StatesOnly(record, ["commit", "ref"], expected),
                // The workflow the dispatch names, its workflow setting, byte for byte; a record naming another, or none, is outside.
                ("dispatch-release-workflow", "workflow") => record.Target("workflow") == expected,
                (null, "product") => true,
                (null, "destination") => record.Field("repository") == expected,
                (null, _) => record.Field(name) == expected,
                _ => Value(record, name) == expected,
            };
            if (!held) return false;
        }

        return true;
    }

    private bool Carries(PresenceRecord record, PresenceTermsEntry entry)
    {
        if (record.Type != entry.Type) return false;
        switch (entry.Type)
        {
            case "history-checkpoint":
                return record.Field("trigger") == "release" && record.FieldValue("anchored") is null && record.FieldValue("removed") is null && record.FieldValue("closes") is null;
            case "release":
                if (record.Field("status") is not string releaseStatus || !entry.Statuses.Contains(releaseStatus, StringComparer.Ordinal) || !FixedHeld(record, entry)) return false;
                return !(releaseStatus == "candidate" && record.Supersedes is not null);
        }

        if (entry.Action is not string action || record.Field("action") != action || record.Field("status") is not string status || !entry.Statuses.Contains(status, StringComparer.Ordinal)
            || !FixedHeld(record, entry)) return false;
        if (PresenceSessionProtocol.ChangeActions.Contains(action))
        {
            string? recorded = record.Target("head");
            string? expected = entry.Fixed.GetValueOrDefault("head") ?? head;
            if (expected is not null ? recorded != expected : !PresenceText.IsCommit(recorded)) return false;
        }

        if (action is "change-open" or "change-merge" && record.TargetValue("pullRequest") is JsonElement number
            && !(number.ValueKind == JsonValueKind.String && PresenceText.IsPullRequest(number.GetString()))) return false;
        if (action == "change-merge" && !entry.Fixed.ContainsKey("pullRequest"))
        {
            if (entry.PullRequestFrom is not int from || record.Target("pullRequest") is not string pull) return false;
            IEnumerable<string?> observed = signedUnder.GetValueOrDefault(from, []).Select(id => signed[id].Record.Target("pullRequest"));
            if (!observed.Contains(pull, StringComparer.Ordinal)) return false;
        }

        return action != "change-observe" || record.Target("observation") == entry.Observation;
    }

    private PresenceRecord? RecordNamed(string? id) =>
        id is null ? null : signed.TryGetValue(id, out var held) ? held.Record : References.FirstOrDefault(r => r.Id == id)?.Record;

    // § 5's rules for identity, supersedes, subject and target.operation, against the records signed and the references; null when they hold.
    private string? ReferenceProblem(PresenceRecord record, PresenceTermsEntry entry)
    {
        switch (entry.Type)
        {
            case "history-checkpoint":
                return record.Subject is null && record.Supersedes is null ? null : "A history checkpoint states no subject and supersedes nothing.";
            case "release":
                if (record.Subject is not { } releaseSubject || releaseSubject.Type != "product" || releaseSubject.Id != entry.Fixed.GetValueOrDefault("product") || releaseSubject.Hash is not null)
                    return "A release record's subject names the entry's product.";
                if (record.Field("status") != "published") return null;
                if (References.FirstOrDefault(r => r.Id == record.Supersedes)?.Record is not { Type: "release" } candidate || candidate.Field("status") != "candidate")
                    return "A publication supersedes the candidate listed in the references.";
                if (superseded.Contains(candidate.Id)) return "Another record of the session already superseded that candidate.";
                return Same(Kept(candidate.Body), Kept(record.Body)) ? null : "A publication differs from its candidate in more than status, publishedAt and url.";
        }

        if (entry.Action is not string action || record.Field("identity") is not string identity) return "The record states no identity.";
        if (action == "change-observe")
        {
            string? observedId = record.Target("operation");
            if (!PresenceText.IsUlid(observedId)) return "An observation names no operation.";
            bool named = entry.Observes is int observedEntry ? signedUnder.GetValueOrDefault(observedEntry, []).Contains(observedId!, StringComparer.Ordinal) : entry.ObservesReference == observedId;
            if (!named || RecordNamed(observedId) is not PresenceRecord operation) return "An observation names an operation neither signed under the entry it observes nor referenced.";
            if (record.Subject is not { } subject || subject.Type != "operation" || subject.Id != observedId || subject.Hash is not null && subject.Hash != operation.Hash)
                return "An observation's subject names the operation it observes.";
            if (operation.Field("identity") is not string baseIdentity) return "The observed operation states no identity.";
            string prefix = baseIdentity + ":observe:";
            if (identity.Length != prefix.Length + 26 || !identity.StartsWith(prefix, StringComparison.Ordinal) || !PresenceText.IsUlid(identity[prefix.Length..]))
                return "An observation's identity is not its operation's followed by :observe: and a ULID.";
            if (!Same(record.FieldValue("provider"), operation.FieldValue("provider"))) return "An observation's provider is not its operation's.";
            foreach (string name in new[] { "repository", "product", "productHash", "head" })
            {
                if (record.Target(name) is null || !Same(record.TargetValue(name), operation.TargetValue(name))) return "An observation's repository, product, product hash or head is not its operation's.";
            }

            foreach (string name in new[] { "branch", "pullRequest" })
            {
                if (record.TargetValue(name) is not null && operation.TargetValue(name) is not null && !Same(record.TargetValue(name), operation.TargetValue(name)))
                    return "An observation's branch or pull request is not its operation's.";
            }
        }
        else
        {
            if (record.Subject is not { } subject || subject.Type != "product" || entry.Fixed.GetValueOrDefault("product") is not string product || subject.Id != product)
                return "The record's subject does not name the entry's product.";
            if (subject.Hash is string pinned && pinned != entry.Fixed.GetValueOrDefault("productHash")) return "The record's subject pins a product revision the entry does not fix.";
            if (identity != ComposedIdentity(record, entry)) return "The record's identity is not the one the entry's fixed values compose.";
        }

        if (record.Supersedes is string previous)
        {
            if (RecordNamed(previous) is not { Type: "operation" } earlier) return "The record supersedes a record neither signed nor referenced.";
            if (superseded.Contains(previous)) return "Another record of the session already superseded the record this one supersedes.";
            if (earlier.Field("identity") != identity || !Same(earlier.FieldValue("provider"), record.FieldValue("provider")) || !Same(earlier.FieldValue("action"), record.FieldValue("action")))
                return "The record supersedes an operation of another identity, provider or action.";
            if (!signed.ContainsKey(previous) && !FixedHeld(earlier, entry)) return "The record supersedes a referenced operation that does not keep the entry's fixed values.";
            if (action == "change-observe" && !Same(earlier.TargetValue("operation"), record.TargetValue("operation"))) return "An observation's revision keeps the operation it observes.";
        }

        return null;
    }

    /// <summary>
    /// The identity an entry's fixed values compose: for a change, provider, repository, action, branch or pull request, head and product
    /// hash joined by colons, as the runtime's changes join them; for a release build, provider, destination and the v-prefixed version.
    /// </summary>
    internal string ComposedIdentity(PresenceRecord record, PresenceTermsEntry entry)
    {
        IReadOnlyDictionary<string, string> f = entry.Fixed;
        if (entry.Action == "dispatch-release-workflow") return $"{f.GetValueOrDefault("provider")}:{f.GetValueOrDefault("destination")}:v{f.GetValueOrDefault("version")}";
        string name = entry.Action == "change-merge" ? f.GetValueOrDefault("pullRequest") ?? record.Target("pullRequest") ?? string.Empty : f.GetValueOrDefault("branch") ?? string.Empty;
        string headValue = f.GetValueOrDefault("head") ?? head ?? record.Target("head") ?? string.Empty;
        string productHash = f.GetValueOrDefault("productHash") is { Length: > 7 } hash ? hash[7..] : string.Empty;
        return $"{f.GetValueOrDefault("provider")}:{f.GetValueOrDefault("repository")}:{entry.Action}:{name}:{headValue}:{productHash}";
    }

    private static JsonElement Kept(JsonElement body)
    {
        var kept = new System.Text.Json.Nodes.JsonObject();
        foreach (JsonProperty member in body.EnumerateObject().Where(m => m.Name is not ("status" or "publishedAt" or "url")))
            kept[member.Name] = System.Text.Json.Nodes.JsonNode.Parse(member.Value.GetRawText());
        return JsonSerializer.SerializeToElement(kept);
    }

    private static bool Same(JsonElement? left, JsonElement? right) =>
        left is null ? right is null : right is not null && CanonicalJson.Serialize(left.Value) == CanonicalJson.Serialize(right.Value);

    private static bool SameKeys(JsonElement item, params string[] names)
    {
        var keys = item.EnumerateObject().Select(p => p.Name).ToList();
        return keys.Count == names.Length && names.All(n => keys.Contains(n, StringComparer.Ordinal));
    }

    private static string? Ascii(JsonElement element, string name) => PresenceRecord.Text(element, name) is string text && PresenceText.IsAscii(text) ? text : null;

    private static PresenceSessionRefusal Refuse(string code, string detail) => new(code, detail);

    /// <summary>
    /// The list fingerprint (§ 6): the first 60 bits of SHA-256 over the format's ASCII name, a zero byte, the nonce and the terms'
    /// SHA-256, as twelve Crockford base32 characters in three groups of four.
    /// </summary>
    internal static string Fingerprint(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> terms)
    {
        byte[] input = [.. Encoding.ASCII.GetBytes(PresenceSessionProtocol.Format), 0, .. nonce, .. SHA256.HashData(terms)];
        ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(input)) >> 4;
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var builder = new StringBuilder(14);
        for (int index = 0; index < 12; index++)
        {
            if (index is 4 or 8) builder.Append('-');
            builder.Append(alphabet[(int)((value >> (55 - 5 * index)) & 31)]);
        }

        return builder.ToString();
    }
}
