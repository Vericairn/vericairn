// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.DocsTool;

/// <summary>
/// Vericairn's own reference generation, part of its build tooling and not of the shipped command line: the command-line, API
/// and record references written from the build they document, the manifest listing the documents the product declares, and the
/// comparison of a set with the build of its version.
/// </summary>
internal static partial class ReferenceSet
{
    /// <summary>The manifest format a set declares.</summary>
    public const string ManifestFormat = "vericairn.docs/1";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private sealed record CommandDoc(string Path, string Description, string Usage, List<(string Name, string Description)> Arguments, List<(string Name, string Description)> Options, List<string> Verbs);

    /// <summary>The version this build documents: the informational version without its suffix and commit.</summary>
    public static string ReleaseVersion
    {
        get
        {
            string text = InformationalVersion;
            int plus = text.IndexOf('+', StringComparison.Ordinal);
            if (plus >= 0)
            {
                text = text[..plus];
            }

            int dash = text.IndexOf('-', StringComparison.Ordinal);
            return dash >= 0 ? text[..dash] : text;
        }
    }

    private static string InformationalVersion =>
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static string? Commit
    {
        get
        {
            string text = InformationalVersion;
            int plus = text.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? text[(plus + 1)..] : null;
        }
    }

    /// <summary>Writes the command-line, API and record references and the manifest of this build into a directory; the manifest lists the documents the product declares.</summary>
    public static int Generate(string output, string declaration, bool json, string? provenanceAt = null)
    {
        try
        {
            RefuseFrozen(output);
            List<string> documents;
            using (JsonDocument declared = JsonDocument.Parse(File.ReadAllBytes(declaration)))
            {
                documents = declared.RootElement.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!).ToList();
            }

            // The window of the set's notes is written by `vericairn docs notes`; a set generated again keeps it.
            JsonNode? notes = null;
            string manifestPath = Path.Combine(output, "manifest.json");
            if (File.Exists(manifestPath))
            {
                notes = (JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject)?["notes"]?.DeepClone();
            }

            Directory.CreateDirectory(output);
            RootCommand root = CliApp.Build();
            List<CommandDoc> commands = Walk(root, string.Empty);
            using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
            List<string> operations = Operations(api.RootElement);
            List<string> types = RecordTypeRegistry.All.Select(t => t.Name).ToList();
            string at = ProvenanceAt(output, provenanceAt);
            string date = at[..10];
            Dictionary<string, byte[]> retained = BuildInputs(at);
            JsonObject companions = WriteCompanions(output, retained, at);

            SafeDestination(output, ReferenceInventoryFiles.Name);
            ReferenceInventoryFiles.Generate(output, Commit, commands.Select(c => "vericairn " + c.Path), operations, types);
            var manifest = new JsonObject
            {
                ["format"] = ManifestFormat,
                ["version"] = ReleaseVersion,
                ["commit"] = Commit,
                ["generatedAt"] = date,
                ["generator"] = "vericairn-docs generate " + InformationalVersion,
                ["recordFormat"] = BuildRecordFormat(),
                ["referenceInventory"] = ReferenceInventoryFiles.Name,
                ["companions"] = companions,
                ["documents"] = new JsonArray(documents.Select(d => (JsonNode)d).ToArray()),
                ["commands"] = new JsonArray(commands.Select(c => (JsonNode)c.Path).ToArray()),
                ["operations"] = new JsonArray(operations.Select(o => (JsonNode)o).ToArray()),
                ["recordTypes"] = new JsonArray(types.Select(t => (JsonNode)t).ToArray()),
            };
            if (notes is not null)
            {
                manifest["notes"] = notes;
            }

            WriteReferenceIndex(output, companions);
            Write(output, "manifest.json", manifest.ToJsonString(Indented));

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { output, version = ReleaseVersion, commands = commands.Count, operations = operations.Count, recordTypes = types.Count, documents = documents.Count }));
            }
            else
            {
                Console.Out.WriteLine($"{output}: cli-reference.md ({commands.Count} commands), api-reference.md ({operations.Count} operations), record-reference.md ({types.Count} types), manifest.json for {ReleaseVersion} listing {documents.Count} documents");
            }

            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>Refuses a set whose manifest does not match this build: a command, an operation or a record type the build has and the set lacks, or the reverse; every difference at once.</summary>
    public static int CompareWithBuild(string directory, bool json)
    {
        var findings = new List<FindingDto>();
        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Commands.Fail($"{manifestPath}: missing.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement manifest = document.RootElement;
        string version = Str(manifest, "version") ?? string.Empty;
        if (version != ReleaseVersion)
        {
            findings.Add(new FindingDto("error", "docs.version", "manifest.json", $"The set documents {version}; this build is {ReleaseVersion}. Compare a set with the build of the version it documents."));
        }
        else
        {
            Compare(Walk(CliApp.Build(), string.Empty).Select(c => c.Path).ToList(), Strings(manifest, "commands"), "command", "cli-reference.md", c => $"vericairn {c}", findings);
            using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
            Compare(Operations(api.RootElement), Strings(manifest, "operations"), "operation", "api-reference.md", o => o, findings);
            Compare(RecordTypeRegistry.All.Select(t => t.Name).ToList(), Strings(manifest, "recordTypes"), "record type", "record-reference.md", t => t, findings);

            if (Str(manifest, "referenceInventory") != ReferenceInventoryFiles.Name)
                findings.Add(new("error", "docs.reference-inventory", "manifest.json", "The set must name reference-inventory.json."));
            findings.AddRange(ReferenceInventoryFiles.Check(directory, Commit,
                Walk(CliApp.Build(), string.Empty).Select(c => "vericairn " + c.Path), Operations(api.RootElement), RecordTypeRegistry.All.Select(t => t.Name)));

            findings.AddRange(CompareCompanions(directory, manifest));

            // The release observer records the record formats the set states as the release's own, so they must be this build's.
            string built = DescribeFormats(BuildRecordFormat());
            string? stated = manifest.TryGetProperty("recordFormat", out JsonElement format) && format.ValueKind == JsonValueKind.Object ? DescribeFormats(JsonNode.Parse(format.GetRawText())!.AsObject()) : null;
            if (stated != built)
            {
                findings.Add(new FindingDto("error", "docs.stale", "manifest.json", stated is null
                    ? $"This build {built}, and the set states no record formats; regenerate the set from this build."
                    : $"The set states that the release {stated}, and this build {built}; regenerate the set from this build."));
            }
        }

        int errors = findings.Count(f => f.Severity == "error");
        return Commands.ReportFindings(new FindingsResponse(errors == 0 ? $"{directory}: matches this build" : $"{directory}: differs from this build", errors, 0, findings), json);
    }

    /// <summary>The record formats this build reads and writes, as a set's manifest states them for the release it documents.</summary>
    private static JsonObject BuildRecordFormat() => new()
    {
        ["reads"] = new JsonArray(RecordFormats.Reads.Select(f => (JsonNode)f).ToArray()),
        ["writes"] = RecordFormats.Current,
    };

    private static string DescribeFormats(JsonObject format) =>
        $"reads record formats {(format["reads"] is JsonArray reads ? string.Join(", ", reads.Select(r => r?.ToJsonString())) : "none")} and writes {format["writes"]?.ToJsonString() ?? "none"}";

    private static void Compare(List<string> live, List<string> documented, string what, string file, Func<string, string> show, List<FindingDto> findings)
    {
        foreach (string item in live.Except(documented, StringComparer.Ordinal))
        {
            findings.Add(new FindingDto("error", "docs.stale", file, $"This build has the {what} {show(item)} and the set does not; regenerate the set from this build."));
        }

        foreach (string item in documented.Except(live, StringComparer.Ordinal))
        {
            findings.Add(new FindingDto("error", "docs.stale", file, $"The set documents the {what} {show(item)}, which this build does not have."));
        }
    }

    private static List<CommandDoc> Walk(Command command, string path)
    {
        var result = new List<CommandDoc>();
        foreach (Command child in command.Subcommands.Where(c => !c.Hidden))
        {
            string childPath = string.IsNullOrEmpty(path) ? child.Name : path + " " + child.Name;
            var arguments = child.Arguments.Where(a => !a.Hidden).Select(a => (Display(a), a.Description ?? string.Empty)).ToList();
            // Built-ins are documented at the root. A command's own --version (or any other name) stays visible.
            var options = child.Options.Where(o => !o.Hidden && o is not HelpOption && o is not VersionOption).Select(o => (Display(o), Describe(o))).ToList();
            var verbs = child.Subcommands.Where(c => !c.Hidden).Select(c => c.Name).ToList();
            string usage = "vericairn " + childPath + string.Concat(child.Arguments.Where(a => !a.Hidden).Select(a => " " + UsageOf(a))) + (verbs.Count > 0 ? " [command]" : string.Empty) + " [options]";
            result.Add(new CommandDoc(childPath, child.Description ?? string.Empty, usage, arguments, options, verbs));
            result.AddRange(Walk(child, childPath));
        }

        return result;
    }

    private static string UsageOf(Argument argument)
    {
        string name = "<" + argument.Name + ">";
        if (argument.Arity.MaximumNumberOfValues > 1)
        {
            name += "...";
        }

        return argument.Arity.MinimumNumberOfValues == 0 ? "[" + name + "]" : name;
    }

    private static string Display(Argument argument) => "<" + argument.Name + ">" + (argument.Arity.MaximumNumberOfValues > 1 ? "..." : string.Empty);

    private static string Display(Option option)
    {
        IEnumerable<string> names = option.Aliases.OrderBy(a => a.Length).ThenBy(a => a, StringComparer.Ordinal).Append(option.Name);
        string text = string.Join(", ", names);
        if (option.ValueType != typeof(bool) && option is not HelpOption && option is not VersionOption)
        {
            text += " <" + (option.HelpName ?? option.Name.TrimStart('-')) + ">";
        }

        return text;
    }

    private static string Describe(Option option)
    {
        string text = option.Description ?? string.Empty;
        if (option.Recursive)
        {
            text = Sentence(text) + " Applies to subcommands.";
        }
        if (option.Required)
        {
            text = Sentence(text) + " Required.";
        }
        else if (option.HasDefaultValue)
        {
            object? value = option.GetDefaultValue();
            string? shown = value switch
            {
                null => null,
                string s when Path.IsPathRooted(s) => "the current directory",
                string s => s,
                bool => null,
                IEnumerable<string> many => string.Join(", ", many),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
            if (!string.IsNullOrEmpty(shown))
            {
                text = Sentence(text) + $" Default: {shown}.";
            }
        }

        return text.Trim();
    }

    // A note follows a whole sentence: a description without closing punctuation, as a built-in option's is, gains a full stop.
    private static string Sentence(string text)
    {
        string trimmed = text.TrimEnd();
        return trimmed.Length == 0 || trimmed[^1] is '.' or '!' or '?' ? trimmed : trimmed + ".";
    }

    private static List<string> Operations(JsonElement api)
    {
        var operations = new List<string>();
        if (api.TryGetProperty("paths", out JsonElement paths))
        {
            foreach (JsonProperty path in paths.EnumerateObject())
            {
                foreach (JsonProperty method in path.Value.EnumerateObject())
                {
                    if (method.Name is "get" or "head" or "post" or "put" or "delete" or "options" or "trace" or "patch")
                        operations.Add(method.Name.ToUpperInvariant() + " " + path.Name);
                }
            }
        }

        return operations.Order(StringComparer.Ordinal).ToList();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    private static void Write(string directory, string name, string text) => WriteBytes(directory, name, Vericairn.Records.Estate.DocumentMarkdown.Bytes(text));
}
