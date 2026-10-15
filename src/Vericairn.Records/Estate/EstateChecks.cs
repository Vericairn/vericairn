// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Repository-level cleanliness and portability checks that go beyond individual records.</summary>
public static partial class EstateChecks
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".md", ".txt", ".yml", ".yaml", ".sh", ".ps1", ".toml", ".csv", ".xml", ".html", ".css", ".js", ".ts", ".cs", ".py", ".gitignore", ".gitattributes",
    };

    /// <summary>Runs every check against a loaded estate.</summary>
    public static FindingList Run(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var findings = new FindingList();
        string root = model.Root;
        foreach (string entry in Directory.EnumerateFileSystemEntries(root).OrderBy(e => e, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(entry);
            if (!EstateLayout.AllowedTopLevel.Contains(name))
            {
                findings.Error("layout.unexpected", name, "Not an allowed top-level entry of an estate repository. Scratch, builds and tooling live outside the repository.");
            }
        }

        foreach (string file in Walk(root))
        {
            string relative = EstateLayout.Relative(root, file);
            var info = new FileInfo(file);
            if (info.LinkTarget is not null)
            {
                findings.Error("layout.symlink", relative, "Symbolic links are not portable; store the content itself.");
                continue;
            }

            if (Path.GetFileName(file) == ".DS_Store")
            {
                findings.Warning("layout.residue", relative, "Operating-system residue; delete it and keep it ignored.");
                continue;
            }

            bool isObject = relative.StartsWith(EstateLayout.ObjectsDirectory + "/", StringComparison.Ordinal);
            long limit = isObject ? EstateLayout.MaxObjectBytes : EstateLayout.MaxFileBytes;
            if (info.Length > limit)
            {
                findings.Error("layout.size", relative, $"{info.Length:N0} bytes exceeds the {limit:N0}-byte limit; large artifacts go to releases and are referenced by hash.");
                continue;
            }

            if (isObject)
            {
                CheckObject(file, relative, findings);
                continue;
            }

            if (IsTextFile(file))
            {
                ScanText(file, relative, findings);
            }
        }

        CheckRetirements(model, findings);
        CheckDocuments(model, findings);
        CheckLivingDocuments(model, findings);
        return findings;
    }

    private static IEnumerable<string> Walk(string root)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(root).OrderBy(e => e, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(entry);
            if (EstateLayout.IgnoredTopLevel.Contains(name))
            {
                continue;
            }

            if (Directory.Exists(entry) && new FileInfo(entry).LinkTarget is null)
            {
                foreach (string file in Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    yield return file;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    private static bool IsTextFile(string file)
    {
        string name = Path.GetFileName(file);
        return name is "bootstrap" or "LICENSE" or "README" || TextExtensions.Contains(Path.GetExtension(name)) || TextExtensions.Contains(name);
    }

    private static void CheckObject(string file, string relative, FindingList findings)
    {
        string name = Path.GetFileName(file);
        if (!ContentHash.IsValid("sha256:" + name))
        {
            findings.Error("objects.name", relative, "Objects are named by their 64-hex-digit SHA-256.");
            return;
        }

        string actual = ContentHash.Sha256File(file);
        if (actual != "sha256:" + name)
        {
            findings.Error("objects.hash", relative, "The object's content does not match its name.");
        }
    }

    private static void ScanText(string file, string relative, FindingList findings)
    {
        byte[] bytes = File.ReadAllBytes(file);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            findings.Warning("layout.binary", relative, "Looks binary despite its name; binary content belongs under objects/ or in a release.");
            return;
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            findings.Error("layout.encoding", relative, "Text files are UTF-8.");
            return;
        }

        if (text.Contains('\r', StringComparison.Ordinal))
        {
            findings.Warning("layout.line-endings", relative, "Use LF line endings so files are byte-identical on every platform.");
        }

        int lineNumber = 0;
        foreach (string line in text.Split('\n'))
        {
            lineNumber++;
            if (AbsolutePath().IsMatch(line))
            {
                findings.Error("portability.absolute-path", $"{relative}:{lineNumber}", "Machine-specific absolute path. Use repository-relative paths or placeholders.");
            }

            if (SecretPattern().IsMatch(line))
            {
                findings.Error("secrets.pattern", $"{relative}:{lineNumber}", "Looks like a credential. Credentials never enter the estate; reference them by purpose instead.");
            }
        }

        if (relative.StartsWith(EstateLayout.RecordsDirectory + "/", StringComparison.Ordinal) && relative.EndsWith(".json", StringComparison.Ordinal))
        {
            ScanRecordKeys(bytes, relative, findings);
            ScanRecordProse(bytes, relative, findings);
        }
        else if (relative.EndsWith(".md", StringComparison.Ordinal) && (relative.StartsWith(EstateLayout.DocsDirectory + "/", StringComparison.Ordinal) || relative.StartsWith(EstateLayout.DesignDirectory + "/", StringComparison.Ordinal)))
        {
            ScanProse(text, relative, findings);
        }
    }

    private static readonly HashSet<string> SpacingAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "base", "http", "https", "html", "python", "sqlite", "float", "uint", "utf", "unicode", "windows", "macos", "ipv", "mp", "ecdsa", "sha",
    };

    /// <summary>Flags a word run into a number ("October13", "checkpoint2693") outside code spans, identifiers and paths.</summary>
    private static void ScanProse(string text, string location, FindingList findings)
    {
        int lineNumber = 0;
        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = CodeSpan().Replace(rawLine, " ");
            foreach (Match match in WordRunIntoNumber().Matches(line))
            {
                if (!SpacingAllowlist.Contains(match.Groups["word"].Value))
                {
                    findings.Warning("writing.spacing", $"{location}:{lineNumber}", $"'{match.Value}': put a space between the word and the number.");
                }
            }
        }
    }

    private static void ScanRecordProse(byte[] bytes, string relative, FindingList findings)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("body", out JsonElement body) || body.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (string field in new[] { "title", "summary", "synthesis", "note", "intent", "exitQuestion", "rationale", "nextStep" })
            {
                if (body.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                {
                    ScanProse(value.GetString() ?? string.Empty, $"{relative}#body.{field}", findings);
                }
            }
        }
        catch (JsonException)
        {
            // The loader already reported the parse error.
        }
    }

    private static void ScanRecordKeys(byte[] bytes, string relative, FindingList findings)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            ScanKeys(document.RootElement, relative, "$", findings);
        }
        catch (JsonException)
        {
            // The loader already reported the parse error.
        }
    }

    private static void ScanKeys(JsonElement element, string relative, string path, FindingList findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (SecretKeyName().IsMatch(property.Name))
                    {
                        findings.Error("secrets.key", $"{relative}#{path}.{property.Name}", "Records never hold secret values; reference a credential by id and purpose.");
                    }

                    ScanKeys(property.Value, relative, path + "." + property.Name, findings);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ScanKeys(item, relative, $"{path}[{index++}]", findings);
                }

                break;
            default:
                break;
        }
    }

    /// <summary>A living document due at level warn or refuse is a warning naming its triggers; one that names a contribution no installed pack provides is never skipped in silence.</summary>
    private static void CheckLivingDocuments(EstateModel model, FindingList findings)
    {
        foreach (LivingDocuments.State state in LivingDocuments.Derive(model))
        {
            string location = state.Repository is null ? state.Path : state.Declaration.ExpectedPath;
            if (state.Level is "warn" or "refuse")
            {
                findings.Warning("document.due", location, $"Due at {state.Level}: {string.Join("; ", state.Reasons.Where(r => r.Level != "remind").Select(r => r.Summary))}. Revise it or record a document review.");
            }

            if (state.Unavailable.Count > 0)
            {
                findings.Warning("document.unavailable", location, $"Its declaration names {string.Join(", ", state.Unavailable)}, which no installed pack provides, so it cannot be evaluated{(state.DeclaresRefuse ? " and closes its product's release gate" : string.Empty)}. Install the pack or revise the declaration.");
            }
        }
    }

    private static void CheckRetirements(EstateModel model, FindingList findings)
    {
        foreach (RecordEnvelope retirement in model.Current("retirement"))
        {
            string? path = EstateModel.BodyString(retirement, "path");
            if (path is null)
            {
                continue;
            }

            bool exists = File.Exists(Path.Combine(model.Root, path)) || Directory.Exists(Path.Combine(model.Root, path));
            string? retiredAt = EstateModel.BodyString(retirement, "retiredAt");
            if (retiredAt is not null && exists)
            {
                findings.Error("retirement.present", path, $"Retired at {retiredAt} according to {retirement.Id}, yet it still exists. Remove it.");
            }
            else if (retiredAt is null && !exists)
            {
                findings.Warning("retirement.absent", path, $"The artifact registered by {retirement.Id} is gone; record its retirement with a superseding record that sets retiredAt.");
            }
            else if (retiredAt is null)
            {
                findings.Info("retirement.pending", path, $"Temporary artifact; condition for removal: {EstateModel.BodyString(retirement, "condition")}");
            }
        }
    }

    private static void CheckDocuments(EstateModel model, FindingList findings)
    {
        var retired = new HashSet<string>(model.Current("retirement").Where(r => EstateModel.BodyString(r, "retiredAt") is not null).Select(r => EstateModel.BodyString(r, "path") ?? string.Empty), StringComparer.Ordinal);
        foreach (RecordEnvelope revision in model.Current("document-revision"))
        {
            string? path = EstateModel.BodyString(revision, "path");
            string? hash = EstateModel.BodyString(revision, "hash");
            if (path is null || hash is null)
            {
                continue;
            }

            string full = Path.Combine(model.Root, path);
            if (!File.Exists(full))
            {
                if (retired.Contains(path))
                {
                    findings.Info("document.retired", path, $"Pinned by {revision.Id} and deliberately retired; its last revision remains in history.");
                    continue;
                }

                findings.Error("document.missing", path, $"Pinned by {revision.Id} but the file does not exist.");
                continue;
            }

            string actual = ContentHash.Sha256File(full);
            if (actual != hash)
            {
                findings.Error("document.drift", path, $"The file changed after revision record {revision.Id} pinned it ({actual}). Record a new document-revision that supersedes it.");
            }
        }
    }

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"(?<![A-Za-z0-9_./:\\-])(?<word>[A-Za-z][a-z]{3,})(?<number>[0-9]+)(?![A-Za-z0-9_/:-]|\.[0-9A-Za-z])")]
    private static partial Regex WordRunIntoNumber();

    [GeneratedRegex(@"(?<![A-Za-z0-9_./-])(?:/Users/|/home/[A-Za-z0-9_-]+/|/Volumes/|/private/var/|/mnt/[a-z]/|[A-Za-z]:\\)")]
    private static partial Regex AbsolutePath();

    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{10,}|\bsk-[A-Za-z0-9_-]{32,}|AIza[0-9A-Za-z_-]{35}|eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"^(?i)(secret|token|password|passphrase|api[_-]?key|private[_-]?key|access[_-]?key|client[_-]?secret)$")]
    private static partial Regex SecretKeyName();
}
