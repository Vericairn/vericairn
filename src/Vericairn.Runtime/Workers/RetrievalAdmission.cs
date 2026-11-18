// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// Public retrieval for research runs (RUN-04; decision 01M6CKCRMAJEAJ9KF3P3A6KEC7, with the interim check of decision
/// 01M7J80BQRFM3J7CS9BZ82BK8J). A run gets its harness's web tools only when its assignment's brief permits retrieval, and then only
/// when the records show that nothing it could carry out is private: the workspace's repository resource and the estate's own
/// repository resource are both public, and none of the run's records (its assignment's sources and step inputs, its work order's and
/// its goal's inputs) names a credential. Unknown counts as private: an estate repository no resource records, or one the runtime
/// cannot observe, refuses. What the run's operating-system user can read on this machine, its same-user reach, is not checked here
/// until confined reads are qualified (RUN-07); CODE-82's reach shows it in the run's record.
/// </summary>
internal static class RetrievalAdmission
{
    /// <summary>The one retrieval a brief may permit.</summary>
    internal const string Public = AssignmentType.PublicRetrieval;

    /// <summary>The logical route of a retrieval run's web tools in its inventory.</summary>
    internal const string Destination = ExecutionRecord.PublicWebDestination;

    /// <summary>Every reason the interim check refuses retrieval for this run, empty when it admits it.</summary>
    internal static IReadOnlyList<string> Refusals(EstateModel model, RecordEnvelope assignment, RecordEnvelope? order, RecordEnvelope? goal, RecordEnvelope resource,
        RepositoryObservation? estateRepository)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(resource);
        var reasons = new List<string>();
        RecordEnvelope workspace = model.Latest(resource);
        if (EstateModel.BodyString(workspace, "visibility") != "public")
            reasons.Add($"the workspace's repository resource {Name(workspace)} is {Visibility(workspace)}, not public");

        if (estateRepository is null)
        {
            reasons.Add("the estate's own repository could not be observed here, so the records cannot show that its repository resource is public");
        }
        else
        {
            RecordEnvelope[] matches = EstateResources(model, estateRepository);
            if (matches.Length == 0)
                reasons.Add("no repository resource records the estate's own repository by its remote or its root commit, so its visibility is unknown");
            else if (matches.Length > 1)
                reasons.Add($"several repository resources record the estate's own repository ({string.Join(", ", matches.Select(Name))}), so its visibility is ambiguous");
            else if (EstateModel.BodyString(matches[0], "visibility") != "public")
                reasons.Add($"the estate's repository resource {Name(matches[0])} is {Visibility(matches[0])}, not public");
        }

        foreach ((string where, string id) in Credentials(model, assignment, order, goal))
            reasons.Add($"{where} names credential reference {id}");

        return reasons;
    }

    /// <summary>
    /// Every credential reference a run's own documents name (its assembled context package, its intent, the searches the intent fixes):
    /// any object whose type is credential-reference, and any value that is the id of a credential-reference record here. An original's
    /// base64 content is not decoded, so a credential named only inside a pinned record's own text, two hops away, is not found.
    /// </summary>
    internal static IReadOnlyList<string> NamedCredentials(EstateModel model, params JsonElement[] documents)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(documents);
        var named = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<JsonElement>(documents);
        while (pending.Count > 0)
        {
            JsonElement value = pending.Pop();
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    if (Text(value, "type") == "credential-reference" && Text(value, "id") is string declared) named.Add(declared);
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        if (property.Name != "content") pending.Push(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray()) pending.Push(item);
                    break;
                case JsonValueKind.String:
                    if (value.GetString() is { Length: Records.Identity.Ulid.Length } id && model.ById.TryGetValue(id, out RecordEnvelope? target) && target.Type == "credential-reference")
                        named.Add(id);
                    break;
            }
        }

        return [.. named];
    }

    /// <summary>
    /// The repository resources whose recorded identity is the estate's own repository. Where the estate's repository has a network
    /// remote, a resource matches only by that remote (its github or url), and a root commit it also records must agree; a root commit
    /// alone never stands in for it, since a fork shares its upstream's. Where it has no remote, only a resource that records no
    /// remote matches, by root commit. Nothing else counts, so an estate no resource records this way is unknown, which refuses.
    /// </summary>
    internal static RecordEnvelope[] EstateResources(EstateModel model, RepositoryObservation estate)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(estate);
        string? remote = Address(estate.Remote);
        return [.. model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository" && r.Body.TryGetProperty("location", out JsonElement location)
            && location.ValueKind == JsonValueKind.Object && Matches(location))
            .GroupBy(r => model.Chain(r)[0], StringComparer.Ordinal).Select(g => g.Last())];

        bool Matches(JsonElement location)
        {
            string? github = Text(location, "github") is string named ? "github.com/" + named.ToLowerInvariant() : null;
            string? url = Address(Text(location, "url"));
            string? root = Text(location, "rootCommit");
            if (remote is not null) return (github == remote || url == remote) && (root is null || root == estate.RootCommit);
            return github is null && url is null && root is not null && root == estate.RootCommit && estate.Remote is null;
        }
    }

    /// <summary>
    /// Whether an address a web tool was asked for is a public one, judged as a WHATWG URL parser, which a harness's fetch uses, would
    /// read its host: ASCII tab and newline removed, leading and trailing controls and spaces trimmed, backslashes as slashes, the host
    /// percent-decoded, trailing dots removed, and IPv4 in every form that parser accepts (decimal, octal and hexadecimal parts, one to
    /// four of them) or IPv6 with its embedded IPv4 forms. A host that parser would map through Unicode normalization and IDNA, any
    /// non-ASCII host (full-width or ideographic dots, enclosed or full-width digits), is refused outright rather than mapped here. So
    /// are user information, a scheme other than http or https, loopback, private, link-local, shared, metadata, unique-local,
    /// multicast, unspecified, documentation and reserved ranges, names only a local resolver answers, and anything that does not
    /// parse. A public name that resolves to a private address is not caught here.
    /// </summary>
    internal static bool PublicAddress(string? address)
    {
        if (string.IsNullOrEmpty(address) || address.Length > 2000) return false;
        string input = new([.. address.Trim().Where(c => c is not ('\t' or '\n' or '\r'))]);
        input = input.Trim(TrimmedControls);
        int colon = input.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || !char.IsAsciiLetter(input[0]) || !input[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return false;
        if (input[..colon].ToLowerInvariant() is not ("http" or "https")) return false;
        string rest = input[(colon + 1)..].TrimStart('/', '\\');
        int end = rest.IndexOfAny(['/', '\\', '?', '#']);
        string authority = end < 0 ? rest : rest[..end];
        if (authority.Contains('@', StringComparison.Ordinal)) return false;
        string host;
        string? port = null;
        if (authority.StartsWith('['))
        {
            int close = authority.IndexOf(']', StringComparison.Ordinal);
            if (close < 0) return false;
            host = authority[..(close + 1)];
            if (close + 1 < authority.Length)
            {
                if (authority[close + 1] != ':') return false;
                port = authority[(close + 2)..];
            }
        }
        else
        {
            int separator = authority.IndexOf(':', StringComparison.Ordinal);
            host = separator < 0 ? authority : authority[..separator];
            port = separator < 0 ? null : authority[(separator + 1)..];
        }

        if (port is not null && !port.All(char.IsAsciiDigit)) return false;
        if (host.Length == 0) return false;
        if (host[0] == '[') return host.Length > 2 && PublicIpv6(host[1..^1]);
        string? decoded = PercentDecoded(host);
        // A non-ASCII host is one a WHATWG parser maps by NFKC and IDNA (full-width dots, enclosed digits); it is refused, not mapped.
        if (decoded is null || decoded.Any(c => c > '\u007f')) return false;
        string name = decoded.ToLowerInvariant().TrimEnd('.');
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')) return false;
        string[] labels = name.Split('.');
        if (labels.Any(l => l.Length == 0)) return false;
        if (EndsInNumber(labels[^1])) return Ipv4(labels) is uint ipv4 && PublicIpv4(ipv4);
        return labels.Length > 1 && !LocalNames.Any(local => name == local || name.EndsWith("." + local, StringComparison.Ordinal));
    }

    private static readonly char[] TrimmedControls = [.. Enumerable.Range(0, 0x21).Select(c => (char)c)];

    // Names only a local resolver answers, and their subdomains.
    private static readonly string[] LocalNames = ["localhost", "localdomain", "local", "internal", "intranet", "lan", "home.arpa", "corp", "private"];

    // WHATWG's ends-in-a-number: a last label of digits, or a hexadecimal number with its 0x prefix.
    private static bool EndsInNumber(string label) => label.All(char.IsAsciiDigit)
        || (label.StartsWith("0x", StringComparison.Ordinal) && label[2..].All(char.IsAsciiHexDigit));

    // WHATWG's IPv4 parser: one to four parts, each decimal, octal (a leading 0) or hexadecimal (0x); every part but the last at most 255,
    // the last filling the remaining bytes. Null for any other text, which that parser refuses and this check treats as not public.
    private static uint? Ipv4(string[] parts)
    {
        if (parts.Length is 0 or > 4) return null;
        var numbers = new ulong[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (Ipv4Number(parts[i]) is not ulong number) return null;
            numbers[i] = number;
        }

        if (numbers[..^1].Any(n => n > 255) || numbers[^1] >= 1UL << (8 * (5 - parts.Length))) return null;
        ulong value = numbers[^1];
        for (int i = 0; i < parts.Length - 1; i++) value += numbers[i] << (8 * (3 - i));
        return (uint)value;
    }

    private static ulong? Ipv4Number(string part)
    {
        int radix = 10;
        string digits = part;
        if (digits.StartsWith("0x", StringComparison.Ordinal)) { radix = 16; digits = digits[2..]; }
        else if (digits.Length > 1 && digits[0] == '0') { radix = 8; digits = digits[1..]; }
        if (digits.Length == 0) return 0;
        ulong value = 0;
        foreach (char c in digits)
        {
            int digit = char.IsAsciiDigit(c) ? c - '0' : char.IsAsciiHexDigitLower(c) ? c - 'a' + 10 : -1;
            if (digit < 0 || digit >= radix) return null;
            value = value * (ulong)radix + (ulong)digit;
            if (value > uint.MaxValue) return value; // Too large for any part; the range check refuses it.
        }

        return value;
    }

    private static string? PercentDecoded(string host)
    {
        if (!host.Contains('%', StringComparison.Ordinal)) return host;
        var bytes = new List<byte>();
        for (int i = 0; i < host.Length; i++)
        {
            if (host[i] == '%' && i + 2 < host.Length && char.IsAsciiHexDigit(host[i + 1]) && char.IsAsciiHexDigit(host[i + 2]))
            {
                bytes.Add(Convert.ToByte(host.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (host[i] > '\u007f')
            {
                return null;
            }
            else
            {
                bytes.Add((byte)host[i]);
            }
        }

        try { return new System.Text.UTF8Encoding(false, true).GetString([.. bytes]); }
        catch (System.Text.DecoderFallbackException) { return null; }
    }

    private static bool PublicIpv4(uint address)
    {
        byte a = (byte)(address >> 24), b = (byte)(address >> 16), c = (byte)(address >> 8);
        return !(a is 0 or 10 or 127 || a >= 224 || (a == 100 && b is >= 64 and <= 127) || (a == 169 && b == 254) || (a == 172 && b is >= 16 and <= 31)
            || (a == 192 && b == 168) || (a == 192 && b == 0 && c is 0 or 2) || (a == 192 && b == 88 && c == 99) || (a == 198 && b is 18 or 19)
            || (a == 198 && b == 51 && c == 100) || (a == 203 && b == 0 && c == 113));
    }

    // IPv6 as .NET parses it; a form WHATWG accepts and .NET does not is refused. Zone identifiers are refused, as WHATWG refuses them.
    private static bool PublicIpv6(string text)
    {
        if (text.Contains('%', StringComparison.Ordinal) || !IPAddress.TryParse(text, out IPAddress? ip) || ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
        byte[] b = ip.GetAddressBytes();
        bool Zero(int from, int to) => b[from..to].All(x => x == 0);
        uint Embedded(int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
        if (Zero(0, 10) && b[10] == 0xff && b[11] == 0xff) return PublicIpv4(Embedded(12)); // IPv4-mapped
        if (Zero(0, 12)) return false; // unspecified, loopback and the IPv4-compatible forms
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b) return Zero(4, 12) && PublicIpv4(Embedded(12)); // NAT64, and its local-use /48
        if (b[0] == 0x20 && b[1] == 0x02) return PublicIpv4(Embedded(2)); // 6to4
        if (b[0] == 0x20 && b[1] == 0x01 && (b[2] == 0x00 && b[3] == 0x00 || b[2] == 0x0d && b[3] == 0xb8)) return false; // Teredo, documentation
        if (b[0] == 0x01 && Zero(1, 8)) return false; // discard-only
        return !((b[0] & 0xfe) == 0xfc || b[0] == 0xfe && (b[1] & 0xc0) is 0x80 or 0xc0 || b[0] == 0xff); // unique-local, link- and site-local, multicast
    }

    // Every record reference the run's records carry, judged by its declared type and by what it resolves to here.
    private static IEnumerable<(string Where, string Id)> Credentials(EstateModel model, RecordEnvelope assignment, RecordEnvelope? order, RecordEnvelope? goal)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string where, JsonElement reference) in References(assignment, order, goal))
        {
            string? id = Text(reference, "id");
            if (id is null) continue;
            bool credential = Text(reference, "type") == "credential-reference"
                || (model.ById.TryGetValue(id, out RecordEnvelope? target) && target.Type == "credential-reference");
            if (credential && named.Add(where + " " + id)) yield return (where, id);
        }
    }

    private static IEnumerable<(string Where, JsonElement Reference)> References(RecordEnvelope assignment, RecordEnvelope? order, RecordEnvelope? goal)
    {
        string brief = $"the brief of assignment {assignment.Id}";
        if (assignment.Body.TryGetProperty("brief", out JsonElement body) && body.ValueKind == JsonValueKind.Object)
            foreach (JsonElement source in Items(body, "sources")) yield return (brief, source);
        if (assignment.Body.TryGetProperty("step", out JsonElement step) && step.ValueKind == JsonValueKind.Object)
            foreach (JsonElement input in Items(step, "inputs")) yield return ($"the step inputs of assignment {assignment.Id}", input);
        if (order is not null)
            foreach (JsonElement input in Items(order.Body, "inputs")) yield return ($"work order {EstateModel.BodyString(order, "key") ?? order.Id}", input);
        if (goal is not null)
            foreach (JsonElement input in Items(goal.Body, "inputs")) yield return ($"goal {goal.Id}", input);
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement items)
        && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object) : [];

    private static string Name(RecordEnvelope resource) => $"{EstateModel.BodyString(resource, "name") ?? "unnamed"} ({resource.Id})";

    private static string Visibility(RecordEnvelope resource) => EstateModel.BodyString(resource, "visibility") switch
    {
        "private" => "private",
        "n/a" => "of no stated visibility (n/a)",
        _ => "of unrecorded visibility",
    };

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found)
        && found.ValueKind == JsonValueKind.String ? found.GetString() : null;

    // A remote or location as host and path, lower-case, without scheme, user, port, trailing slash or .git: comparable across https and ssh forms.
    private static string? Address(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = value.Trim();
        string host;
        string path;
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http" or "ssh" or "git")
        {
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else if (text.IndexOf(':', StringComparison.Ordinal) is int colon and > 0 && !text.Contains("://", StringComparison.Ordinal))
        {
            // The scp-like form: user@host:owner/name.git
            string left = text[..colon];
            host = left[(left.LastIndexOf('@') + 1)..];
            path = text[(colon + 1)..];
        }
        else
        {
            return null;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return host.Length == 0 || path.Length == 0 ? null : (host + "/" + path).ToLowerInvariant();
    }
}
