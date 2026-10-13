// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Cli;

/// <summary>
/// A release's documentation set: the references that cannot drift from the software because they are generated from it (the
/// command tree, the API document, the record types), the release notes generated from an estate's records, and the check that
/// refuses a set missing a document, a command, an operation or a record type. One set per release, written once and frozen.
/// </summary>
internal static class Docs
{
    /// <summary>The manifest format a set declares.</summary>
    public const string ManifestFormat = "vericairn.docs/1";

    /// <summary>Every document a set must hold: the authored ones are written by a person, the others by this family.</summary>
    public static readonly string[] RequiredDocuments =
    [
        "README.md", "install.md", "operator-guide.md", "cli-reference.md", "api-reference.md", "record-reference.md",
        "record-format.md", "api.md", "software.md", "release-notes.md", "limitations-and-platforms.md",
    ];

    private static readonly string[] GlobalOptionNames = ["--estate", "--json", "--version", "--help"];

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
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static string? Commit
    {
        get
        {
            string text = InformationalVersion;
            int plus = text.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? text[(plus + 1)..] : null;
        }
    }

    /// <summary>Writes the command-line, API and record references and the manifest of this build into a directory.</summary>
    public static int Generate(string output, bool json)
    {
        try
        {
            Directory.CreateDirectory(output);
            List<CommandDoc> commands = Walk(CliApp.Build(), string.Empty);
            using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
            List<string> operations = Operations(api.RootElement);
            List<string> types = RecordTypeRegistry.All.Select(t => t.Name).ToList();
            string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            Write(output, "cli-reference.md", CliReference(commands, date));
            Write(output, "api-reference.md", ApiReference(api.RootElement, date));
            Write(output, "record-reference.md", RecordReference(date));
            var manifest = new JsonObject
            {
                ["format"] = ManifestFormat,
                ["version"] = ReleaseVersion,
                ["commit"] = Commit,
                ["generatedAt"] = date,
                ["generator"] = "vericairn docs generate " + InformationalVersion,
                ["documents"] = new JsonArray(RequiredDocuments.Select(d => (JsonNode)d).ToArray()),
                ["commands"] = new JsonArray(commands.Select(c => (JsonNode)c.Path).ToArray()),
                ["operations"] = new JsonArray(operations.Select(o => (JsonNode)o).ToArray()),
                ["recordTypes"] = new JsonArray(types.Select(t => (JsonNode)t).ToArray()),
            };
            Write(output, "manifest.json", manifest.ToJsonString(Indented));

            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { output, version = ReleaseVersion, commands = commands.Count, operations = operations.Count, recordTypes = types.Count }));
            }
            else
            {
                Console.Out.WriteLine($"{output}: cli-reference.md ({commands.Count} commands), api-reference.md ({operations.Count} operations), record-reference.md ({types.Count} types), manifest.json for {ReleaseVersion}");
            }

            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>Writes release notes from the estate's records between two published releases.</summary>
    public static int Notes(string root, string fromVersion, string toVersion, string output, bool json)
    {
        try
        {
            EstateModel model = Commands.LoadCopy(root);
            RecordEnvelope? from = Published(model, fromVersion);
            RecordEnvelope? to = Published(model, toVersion);
            if (from is null || to is null)
            {
                return Commands.Fail($"No published release record for {(from is null ? fromVersion : toVersion)} in this estate.");
            }

            List<RecordEnvelope> window = model.Records
                .Where(r => string.CompareOrdinal(r.At, from.At) > 0 && string.CompareOrdinal(r.At, to.At) <= 0)
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
                .ToList();
            Directory.CreateDirectory(output);
            Write(output, "release-notes.md", ReleaseNotes(model, from, to, window));
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { output, from = fromVersion, to = toVersion, records = window.Count }));
            }
            else
            {
                Console.Out.WriteLine($"{output}/release-notes.md: {window.Count} records between {fromVersion} and {toVersion}");
            }

            return Commands.Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Commands.Fail(exception.Message);
        }
    }

    /// <summary>Refuses a set that lacks a document, a command, an operation or a record type, naming every gap at once.</summary>
    public static int Check(string directory, bool json)
    {
        var findings = new List<FindingDto>();
        if (!Directory.Exists(directory))
        {
            return Commands.Fail($"{directory}: no such directory.");
        }

        string manifestPath = Path.Combine(directory, "manifest.json");
        JsonElement manifest = default;
        bool haveManifest = false;
        if (!File.Exists(manifestPath))
        {
            findings.Add(new FindingDto("error", "docs.manifest", "manifest.json", "Missing; 'vericairn docs generate --out <directory>' writes it with the references."));
        }
        else
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                manifest = document.RootElement.Clone();
                haveManifest = true;
                if (Str(manifest, "format") != ManifestFormat)
                {
                    findings.Add(new FindingDto("error", "docs.manifest", "manifest.json", $"Format '{Str(manifest, "format")}' is not {ManifestFormat}."));
                }
            }
            catch (JsonException exception)
            {
                findings.Add(new FindingDto("error", "docs.manifest", "manifest.json", "Not valid JSON: " + exception.Message));
            }
        }

        foreach (string name in RequiredDocuments)
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                findings.Add(new FindingDto("error", "docs.document", name, "Missing or empty; every set holds this document."));
            }
        }

        if (haveManifest)
        {
            string version = Str(manifest, "version") ?? string.Empty;
            List<string> commands = Strings(manifest, "commands");
            List<string> operations = Strings(manifest, "operations");
            List<string> types = Strings(manifest, "recordTypes");
            Covered(directory, "cli-reference.md", commands, c => $"`vericairn {c}`", "docs.command", findings);
            Covered(directory, "api-reference.md", operations, o => $"`{o}`", "docs.operation", findings);
            Covered(directory, "record-reference.md", types, t => $"`{t}`", "docs.record-type", findings);
            string readme = Path.Combine(directory, "README.md");
            if (File.Exists(readme) && !File.ReadAllText(readme).Contains(version, StringComparison.Ordinal))
            {
                findings.Add(new FindingDto("error", "docs.version", "README.md", $"Does not name the version {version} the set documents."));
            }

            if (version == ReleaseVersion)
            {
                Compare(Walk(CliApp.Build(), string.Empty).Select(c => c.Path).ToList(), commands, "command", "cli-reference.md", c => $"vericairn {c}", findings);
                using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
                Compare(Operations(api.RootElement), operations, "operation", "api-reference.md", o => o, findings);
                Compare(RecordTypeRegistry.All.Select(t => t.Name).ToList(), types, "record type", "record-reference.md", t => t, findings);
            }
            else
            {
                findings.Add(new FindingDto("info", "docs.version", "manifest.json", $"The set documents {version}; this build is {ReleaseVersion}, so the set is checked against its own manifest, not this build."));
            }
        }

        int errors = findings.Count(f => f.Severity == "error");
        return Commands.ReportFindings(new FindingsResponse(errors == 0 ? $"{directory}: complete" : $"{directory}: incomplete", errors, 0, findings), json);
    }

    private static void Covered(string directory, string file, List<string> expected, Func<string, string> heading, string code, List<FindingDto> findings)
    {
        string path = Path.Combine(directory, file);
        if (!File.Exists(path))
        {
            return;
        }

        HashSet<string> headings = File.ReadLines(path).Where(l => l.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        foreach (string item in expected)
        {
            string wanted = heading(item);
            if (!headings.Any(h => h.Contains(wanted, StringComparison.Ordinal)))
            {
                findings.Add(new FindingDto("error", code, file, $"No heading for {wanted}."));
            }
        }
    }

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
            var options = child.Options.Where(o => !o.Hidden && !GlobalOptionNames.Contains(o.Name, StringComparer.Ordinal)).Select(o => (Display(o), Describe(o))).ToList();
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
        IEnumerable<string> names = option.Aliases.OrderBy(a => a.Length).Append(option.Name);
        string text = string.Join(", ", names);
        if (option.ValueType != typeof(bool))
        {
            text += " <" + (option.HelpName ?? option.Name.TrimStart('-')) + ">";
        }

        return text;
    }

    private static string Describe(Option option)
    {
        string text = option.Description ?? string.Empty;
        if (option.Required)
        {
            text += " Required.";
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
                text += $" Default: {shown}.";
            }
        }

        return text.Trim();
    }

    private static string CliReference(List<CommandDoc> commands, string date)
    {
        var lines = new List<string>
        {
            $"# Command-line reference, Vericairn {ReleaseVersion}",
            string.Empty,
            $"Generated by `vericairn docs generate` from the command tree of build {InformationalVersion} on {date}. Every command and verb is listed; nothing here is written by hand. Global options apply to every command: `--estate <path>` names the estate root (default: the current directory), `--json` switches to machine-readable output, `--version` prints the version, `--help` prints the usage of any command.",
            string.Empty,
        };
        foreach (CommandDoc command in commands)
        {
            int depth = command.Path.Count(c => c == ' ') + 2;
            lines.Add(new string('#', Math.Min(depth, 4)) + " `vericairn " + command.Path + "`");
            lines.Add(string.Empty);
            lines.Add(command.Description);
            lines.Add(string.Empty);
            lines.Add("Usage: `" + command.Usage + "`");
            if (command.Arguments.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Arguments:");
                lines.AddRange(command.Arguments.Select(a => "- `" + a.Name + "`: " + a.Description));
            }

            if (command.Options.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Options:");
                lines.AddRange(command.Options.Select(o => "- `" + o.Name + "`: " + o.Description));
            }

            if (command.Verbs.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Verbs: " + string.Join(", ", command.Verbs.Select(v => "`" + v + "`")));
            }

            lines.Add(string.Empty);
        }

        return string.Join("\n", lines);
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
                    operations.Add(method.Name.ToUpperInvariant() + " " + path.Name);
                }
            }
        }

        return operations;
    }

    private static string ApiReference(JsonElement api, string date)
    {
        string openapi = Str(api, "openapi") ?? string.Empty;
        string apiVersion = api.TryGetProperty("info", out JsonElement info) ? Str(info, "version") ?? string.Empty : string.Empty;
        var lines = new List<string>
        {
            $"# API reference, Vericairn {ReleaseVersion}",
            string.Empty,
            $"Generated by `vericairn docs generate` from the OpenAPI document the daemon of build {InformationalVersion} serves at `{OpenApiDocument.Path}` (OpenAPI {openapi}, API version {apiVersion}) on {date}. The daemon is the one owner of an estate's live state on a machine and every surface is its client; how to reach it and what the outcomes mean are in the API document (api.md). This reference lists every operation.",
            string.Empty,
        };
        if (api.TryGetProperty("paths", out JsonElement paths))
        {
            foreach (JsonProperty path in paths.EnumerateObject())
            {
                foreach (JsonProperty method in path.Value.EnumerateObject())
                {
                    JsonElement operation = method.Value;
                    lines.Add("## `" + method.Name.ToUpperInvariant() + " " + path.Name + "`");
                    lines.Add(string.Empty);
                    lines.Add(Str(operation, "summary") ?? string.Empty);
                    string? description = Str(operation, "description");
                    if (!string.IsNullOrEmpty(description))
                    {
                        lines.Add(string.Empty);
                        lines.Add(description);
                    }

                    lines.Add(string.Empty);
                    if (operation.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0)
                    {
                        lines.Add("Parameters:");
                        foreach (JsonElement parameter in parameters.EnumerateArray())
                        {
                            bool required = parameter.TryGetProperty("required", out JsonElement r) && r.ValueKind == JsonValueKind.True;
                            lines.Add("- `" + Str(parameter, "name") + "` (" + Str(parameter, "in") + (required ? ", required" : string.Empty) + "): " + Str(parameter, "description"));
                        }

                        lines.Add(string.Empty);
                    }

                    if (operation.TryGetProperty("requestBody", out JsonElement body) && body.TryGetProperty("content", out JsonElement content))
                    {
                        foreach (JsonProperty mediaType in content.EnumerateObject())
                        {
                            lines.Add("Request body (`" + mediaType.Name + "`): `" + SchemaName(mediaType.Value.TryGetProperty("schema", out JsonElement schema) ? schema : default) + "`");
                        }

                        lines.Add(string.Empty);
                    }

                    if (operation.TryGetProperty("responses", out JsonElement responses))
                    {
                        lines.Add("Responses:");
                        foreach (JsonProperty response in responses.EnumerateObject())
                        {
                            string shape = string.Empty;
                            if (response.Value.TryGetProperty("content", out JsonElement responseContent))
                            {
                                foreach (JsonProperty mediaType in responseContent.EnumerateObject())
                                {
                                    shape = " (`" + SchemaName(mediaType.Value.TryGetProperty("schema", out JsonElement schema) ? schema : default) + "`)";
                                }
                            }

                            lines.Add("- `" + response.Name + "`: " + Str(response.Value, "description") + shape);
                        }

                        lines.Add(string.Empty);
                    }
                }
            }
        }

        if (api.TryGetProperty("components", out JsonElement components) && components.TryGetProperty("schemas", out JsonElement schemas))
        {
            lines.Add("## Schemas");
            lines.Add(string.Empty);
            lines.Add("The request and response types named above, as the document defines them.");
            lines.Add(string.Empty);
            foreach (JsonProperty schema in schemas.EnumerateObject())
            {
                lines.Add("### `" + schema.Name + "`");
                lines.Add(string.Empty);
                string? description = Str(schema.Value, "description");
                if (!string.IsNullOrEmpty(description))
                {
                    lines.Add(description);
                    lines.Add(string.Empty);
                }

                if (schema.Value.TryGetProperty("properties", out JsonElement properties))
                {
                    HashSet<string> required = Strings(schema.Value, "required").ToHashSet(StringComparer.Ordinal);
                    lines.Add("| Property | Type | Required | Meaning |");
                    lines.Add("|---|---|---|---|");
                    foreach (JsonProperty property in properties.EnumerateObject())
                    {
                        lines.Add("| `" + property.Name + "` | `" + SchemaName(property.Value) + "` | " + (required.Contains(property.Name) ? "yes" : "no") + " | " + (Str(property.Value, "description") ?? string.Empty) + " |");
                    }

                    lines.Add(string.Empty);
                }
                else if (schema.Value.TryGetProperty("enum", out JsonElement values))
                {
                    lines.Add("One of: " + string.Join(", ", values.EnumerateArray().Select(v => "`" + v.GetString() + "`")));
                    lines.Add(string.Empty);
                }
            }
        }

        return string.Join("\n", lines);
    }

    private static string SchemaName(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string text = reference.GetString() ?? string.Empty;
            return text[(text.LastIndexOf('/') + 1)..];
        }

        string type = Str(schema, "type") ?? string.Empty;
        if (type == "array" && schema.TryGetProperty("items", out JsonElement items))
        {
            return "array of " + SchemaName(items);
        }

        return type;
    }

    private static string RecordReference(string date)
    {
        var lines = new List<string>
        {
            $"# Record reference, Vericairn {ReleaseVersion}",
            string.Empty,
            $"Generated by `vericairn docs generate` from the record type registry of build {InformationalVersion} on {date}. Each type lists its subject rule and its body fields with the field's kind, whether it is required and what it means. The envelope every record shares (format, type, id, at, by, body, hash and optionally subject, supersedes and signatures) is described in the record format document (record-format.md).",
            string.Empty,
        };
        foreach (IRecordType type in RecordTypeRegistry.All)
        {
            lines.Add("## `" + type.Name + "`");
            lines.Add(string.Empty);
            lines.Add(type.Summary);
            lines.Add(string.Empty);
            lines.Add(type.SubjectType is null ? (type.SubjectHashRequired ? "Subject: required, pinned to an exact hash." : "Subject: none.") : "Subject: " + type.SubjectType + (type.SubjectHashRequired ? ", pinned to an exact hash." : "."));
            lines.Add(string.Empty);
            lines.Add("| Field | Kind | Required | Meaning |");
            lines.Add("|---|---|---|---|");
            foreach (FieldDescription field in type.Fields)
            {
                lines.Add("| `" + field.Name + "` | `" + field.Kind + "` | " + (field.Required ? "required" : "optional") + " | " + field.Description + " |");
            }

            lines.Add(string.Empty);
        }

        lines.Add("Reserved for later releases: " + string.Join(", ", RecordTypeRegistry.Reserved) + ".");
        return string.Join("\n", lines);
    }

    private static RecordEnvelope? Published(EstateModel model, string version) =>
        model.Current("release").FirstOrDefault(r => Str(r.Body, "version") == version && Str(r.Body, "status") == "published");

    private static string ReleaseNotes(EstateModel model, RecordEnvelope from, RecordEnvelope to, List<RecordEnvelope> window)
    {
        string fromVersion = Str(from.Body, "version") ?? string.Empty;
        string toVersion = Str(to.Body, "version") ?? string.Empty;
        var lines = new List<string>
        {
            $"# Release notes, Vericairn {toVersion}",
            string.Empty,
            $"Generated by `vericairn docs notes` from the estate's records between the publication of {fromVersion} ({When(from.At)}) and the publication of {toVersion} ({When(to.At)}); the records are the source and this page is a rendering. Release {toVersion} was built from commit {Str(to.Body, "commit")}; signing: {Str(to.Body, "signing")}; qualified platforms: {string.Join(", ", Strings(to.Body, "qualified"))}.",
            string.Empty,
            "## What the release contains",
            string.Empty,
        };
        if (to.Body.TryGetProperty("evidence", out JsonElement evidence))
        {
            lines.AddRange(evidence.EnumerateArray().Select(e => "- " + Str(e, "summary")));
        }

        if (to.Body.TryGetProperty("recordFormat", out JsonElement format))
        {
            string reads = format.TryGetProperty("reads", out JsonElement readsElement) && readsElement.ValueKind == JsonValueKind.Array ? string.Join(", ", readsElement.EnumerateArray().Select(r => r.ToString())) : string.Empty;
            string writes = format.TryGetProperty("writes", out JsonElement writesElement) ? writesElement.ToString() : string.Empty;
            lines.Add($"- Record format: reads {reads}, writes {writes}.");
        }

        lines.Add(string.Empty);
        lines.Add("## Work accepted");
        lines.Add(string.Empty);
        foreach (RecordEnvelope result in window.Where(r => r.Type == "result"))
        {
            RecordEnvelope? order = OrderOf(model, result);
            lines.Add("### " + (order is null ? "Result " + result.Id : Str(order.Body, "key") + ": " + Str(order.Body, "title")));
            lines.Add(string.Empty);
            lines.Add($"Result {result.Id} ({Str(result.Body, "outcome")}), recorded {When(result.At)}. {Str(result.Body, "summary")}");
            List<string> limitations = Strings(result.Body, "limitations");
            if (limitations.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Limitations recorded:");
                lines.AddRange(limitations.Select(l => "- " + l));
            }

            lines.Add(string.Empty);
        }

        Section(lines, "Decisions", window.Where(r => r.Type == "decision").Select(r => $"**{Str(r.Body, "title")}** ({When(r.At)}, decision {r.Id}): {Str(r.Body, "summary")}"));
        Section(lines, "Work orders opened or revised", window.Where(r => r.Type == "work-order").Select(r => $"**{Str(r.Body, "key")}** ({Str(r.Body, "kind")}): {Str(r.Body, "title")}{(r.Supersedes is null ? string.Empty : " (revised)")}."));
        Section(lines, "Milestone and release acceptances", window.Where(r => r.Type == "acceptance" && !(r.Body.TryGetProperty("self", out JsonElement self) && self.ValueKind == JsonValueKind.True)).Select(r =>
        {
            RecordEnvelope? subject = r.Subject is null ? null : model.ById.GetValueOrDefault(r.Subject.Id);
            string what = subject is null ? r.Subject?.ToString() ?? string.Empty : subject.Type == "release" ? "release " + Str(subject.Body, "version") : Str(subject.Body, "key") ?? subject.Id;
            return $"{what}: \"{Str(r.Body, "statement")}\" (acceptance {r.Id}, {When(r.At)}{(r.HasSignatures ? ", signed" : string.Empty)})";
        }));
        Section(lines, "Identity and policy", window.Where(r => r.Type is "device" or "admission" or "principal" or "estate").Select(r => r.Type switch
        {
            "device" => $"Device \"{Str(r.Body, "name")}\" recorded ({r.Id}).",
            "admission" => $"Device admitted to author records, scope {Str(r.Body, "scope")} ({r.Id}).",
            "principal" => $"Principal {(r.Supersedes is null ? "recorded" : "revised")}: \"{Str(r.Body, "name")}\" ({r.Id}).",
            _ => $"Charter revised ({r.Id}).",
        }));
        Section(lines, "Design documents revised", window.Where(r => r.Type == "document-revision").Select(r => $"{Str(r.Body, "title")} revision {Str(r.Body, "revision") ?? Num(r.Body, "revision")} (`{Str(r.Body, "path")}`): {Str(r.Body, "summary")}"));
        Section(lines, "The release itself", window.Where(r => r.Type == "release").Select(r => $"{Str(r.Body, "version")} {Str(r.Body, "status")} ({r.Id}, {When(r.At)}); signing: {Str(r.Body, "signing")}; qualified: {string.Join(", ", Strings(r.Body, "qualified"))}."));
        Section(lines, "Upgrading", window.Where(r => r.Type == "migration").Select(r => $"Migration {r.Id}: from {Str(r.Body, "fromPin")} to {Str(r.Body, "toPin")}, record format {Num(r.Body, "fromFormat")} to {Num(r.Body, "toFormat")}; migrations applied: {(Strings(r.Body, "migrations") is { Count: > 0 } m ? string.Join(", ", m) : "none")}; changes: {string.Join("; ", Strings(r.Body, "changes"))}."));
        lines.Add($"An estate on {fromVersion} upgrades with `vericairn estate upgrade --to {toVersion} --by <principal>` run with the {toVersion} binary once the published release record is in the estate; `--check` shows the plan first.");
        return string.Join("\n", lines);
    }

    private static void Section(List<string> lines, string title, IEnumerable<string> items)
    {
        List<string> list = items.ToList();
        lines.Add("## " + title);
        lines.Add(string.Empty);
        if (list.Count == 0)
        {
            lines.Add("None in this window.");
        }
        else
        {
            lines.AddRange(list.Select(i => "- " + i));
        }

        lines.Add(string.Empty);
    }

    private static RecordEnvelope? OrderOf(EstateModel model, RecordEnvelope result)
    {
        RecordEnvelope? attempt = result.Subject is null ? null : model.ById.GetValueOrDefault(result.Subject.Id);
        RecordEnvelope? assignment = attempt?.Subject is null ? null : model.ById.GetValueOrDefault(attempt.Subject.Id);
        return assignment?.Subject is null ? null : model.ById.GetValueOrDefault(assignment.Subject.Id);
    }

    private static string When(string at) => at.Length >= 16 ? at[..10] + " " + at[11..16] + " UTC" : at;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Num(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.ToString() : string.Empty;

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    private static void Write(string directory, string name, string text) => File.WriteAllText(Path.Combine(directory, name), text.EndsWith('\n') ? text : text + "\n");
}
