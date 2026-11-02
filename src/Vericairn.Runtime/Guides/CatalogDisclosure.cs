// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Guides;

/// <summary>Fail-closed checks on every proposed public file, supplementing exact source clearance and rights review.</summary>
public static partial class CatalogDisclosure
{
    /// <summary>Scans UTF-8 content and names without reporting matched private text. Only verified public manifest digests are admitted.</summary>
    public static IReadOnlyList<GuideFinding> Check(IReadOnlyDictionary<string, byte[]> files, IReadOnlyList<string>? restrictedText = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        restrictedText ??= [];
        var findings = new List<GuideFinding>();
        foreach ((string name, byte[] bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            try
            {
                string text = GuideMarkdown.Utf8.GetString(bytes);
                string content = name.EndsWith(".json", StringComparison.Ordinal) ? JsonText(text) : text;
                string original = Decode(name + "\n" + content);
                bool forbidden = restrictedText.Any(t => !string.IsNullOrEmpty(t) &&
                    (original.Contains(Decode(t), StringComparison.OrdinalIgnoreCase) ||
                     t.StartsWith("sha256:", StringComparison.Ordinal) && original.Contains(t[7..], StringComparison.OrdinalIgnoreCase)));
                // Hash permission is local to a verified manifest field, never a blanket exception for a digest string.
                string scanned = Decode(name + "\n" + (name is "manifests/inputs.json" or "manifests/outputs.json"
                    ? ManifestText(name, text, files) : content));
                string withoutUrls = Url().Replace(scanned, m => PublicUrl(m.Value.TrimEnd('.', ',', ')')) ? "public-link" : m.Value);
                string? code = !GuideGenerator.SafePath(name) ? "disclosure-path" : forbidden ? "disclosure-restricted" :
                    OpaqueIdentity().IsMatch(scanned) ? "disclosure-identity" : Credential().IsMatch(scanned) ? "disclosure-credential" :
                    MachineLocation().IsMatch(withoutUrls) ? "disclosure-location" :
                    scanned.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ? "disclosure-control" : null;
                if (code is not null)
                    findings.Add(new("inventory", code, "A public file contains unapproved identity, location, credential or restricted content."));
            }
            catch (Exception error) when (error is DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
            { findings.Add(new("inventory", "disclosure-format", "A public file is not a valid, bounded rendition or verified manifest.")); }
        }
        return findings.Distinct().ToArray();
    }

    internal static bool PublicUrl(string? value) => value is not null && value.Length <= 2048 &&
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && !uri.IsLoopback && uri.HostNameType == UriHostNameType.Dns &&
        !value.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or '"' or '\\');

    private static string ManifestText(string name, string text, IReadOnlyDictionary<string, byte[]> files)
    {
        if (name is not ("manifests/inputs.json" or "manifests/outputs.json")) return text;
        PublicIdentity[]? identities;
        IEnumerable<string> expected;
        if (name == "manifests/inputs.json")
        {
            PublicInputs? manifest = JsonSerializer.Deserialize<PublicInputs>(text, GuideJson.Options);
            if (manifest is null || manifest.Format != CatalogExporter.Version || manifest.Generator != GuideGenerator.GeneratorVersion ||
                manifest.Template != GuideGenerator.TemplateVersion || manifest.PublicationState != "local-preparation") throw new FormatException();
            identities = manifest.Inputs;
            expected = files.Keys.Where(p => p.StartsWith("sources/", StringComparison.Ordinal) ||
                p.StartsWith("templates/", StringComparison.Ordinal) || p == "indexes/tracking-issues.json");
        }
        else
        {
            PublicOutputs? manifest = JsonSerializer.Deserialize<PublicOutputs>(text, GuideJson.Options);
            if (manifest is null || manifest.Format != CatalogExporter.Version) throw new FormatException();
            identities = manifest.Files;
            expected = files.Keys.Where(p => p != name);
        }
        if (identities is not { Length: > 0 and <= 100000 } || identities.Any(i => i is null || i.Path is null || i.Sha256 is null) ||
            !identities.Select(i => i.Path).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new FormatException();
        foreach (PublicIdentity identity in identities)
            if (!files.TryGetValue(identity.Path, out byte[]? value) || identity.Sha256 != GuideGenerator.Hash(value)) throw new FormatException();
        // All other fields have fixed values and unknown/duplicate properties are refused. Scan the remaining public paths.
        return string.Join('\n', identities.Select(i => i.Path));
    }

    private sealed record PublicIdentity(string Path, string Sha256);
    private sealed record PublicInputs(string Format, string Generator, string Template, string PublicationState, PublicIdentity[] Inputs);
    private sealed record PublicOutputs(string Format, PublicIdentity[] Files);

    private static string JsonText(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        var result = new StringBuilder();
        Visit(document.RootElement);
        return result.ToString();

        void Visit(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty property in node.EnumerateObject())
                    {
                        if (!names.Add(property.Name)) throw new FormatException();
                        result.Append(property.Name).Append(": ");
                        Visit(property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in node.EnumerateArray()) Visit(item);
                    break;
                case JsonValueKind.String:
                    result.Append(node.GetString()).Append('\n');
                    break;
                default:
                    result.Append(node.GetRawText()).Append('\n');
                    break;
            }
        }
    }

    private static string Decode(string text)
    {
        for (int i = 0; i < 6; i++)
        {
            string next = JsonEscape().Replace(Uri.UnescapeDataString(WebUtility.HtmlDecode(text)),
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            if (next == text) return text;
            text = next;
        }
        throw new FormatException("Nested encodings exceed the disclosure bound.");
    }

    [GeneratedRegex(@"(?i)[0-7][0-9A-HJKMNP-TV-Z]{25}|[0-9a-f]{40}|\bsha256:|\bvericairn\.record/\d+", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueIdentity();
    [GeneratedRegex(@"(?i)(?:password|secret|access[_ -]?token|api[_ -]?key|authorization)\s*[:=]\s*\S+|-----BEGIN [^-]*(?:PRIVATE KEY|CREDENTIAL)|\bBearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex Credential();
    [GeneratedRegex(@"(?i)(?<![\w:/.])(?:[a-z]:[\\/]|\\\\|/(?:[^/\s<>""']+/)+)|\bfile:", RegexOptions.CultureInvariant)]
    private static partial Regex MachineLocation();
    [GeneratedRegex(@"https://[^\s<>""']+", RegexOptions.CultureInvariant)]
    private static partial Regex Url();
    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex JsonEscape();
}
