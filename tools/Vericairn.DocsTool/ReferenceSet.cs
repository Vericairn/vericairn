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
using Vericairn.Records.Types;

namespace Vericairn.DocsTool;

/// <summary>
/// Vericairn's own reference generation, part of its build tooling and not of the shipped command line: the command-line, API
/// and record references written from the build they document, the manifest listing the documents the product declares, and the
/// comparison of a set with the build of its version.
/// </summary>
internal static class ReferenceSet
{
    /// <summary>The manifest format a set declares.</summary>
    public const string ManifestFormat = "vericairn.docs/1";

    // The options every command takes from the root are documented once, not per command. The built-in help and version options
    // are recognized by type, never by name: 'release submit' and 'release observe' have a --version of their own (the version to
    // release), which the 0.3.1 reference left out because it was filtered by name.
    private static readonly string[] GlobalOptionNames = ["--estate", "--json"];

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
    public static int Generate(string output, string declaration, bool json)
    {
        try
        {
            List<string> documents;
            using (JsonDocument declared = JsonDocument.Parse(File.ReadAllBytes(declaration)))
            {
                documents = declared.RootElement.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!).ToList();
            }

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
                ["generator"] = "vericairn-docs generate " + InformationalVersion,
                ["documents"] = new JsonArray(documents.Select(d => (JsonNode)d).ToArray()),
                ["commands"] = new JsonArray(commands.Select(c => (JsonNode)c.Path).ToArray()),
                ["operations"] = new JsonArray(operations.Select(o => (JsonNode)o).ToArray()),
                ["recordTypes"] = new JsonArray(types.Select(t => (JsonNode)t).ToArray()),
            };
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
        }

        int errors = findings.Count(f => f.Severity == "error");
        return Commands.ReportFindings(new FindingsResponse(errors == 0 ? $"{directory}: matches this build" : $"{directory}: differs from this build", errors, 0, findings), json);
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
            var options = child.Options.Where(o => !o.Hidden && o is not HelpOption && o is not VersionOption && !GlobalOptionNames.Contains(o.Name, StringComparer.Ordinal)).Select(o => (Display(o), Describe(o))).ToList();
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
            $"Generated by the build tooling (`vericairn-docs generate`) from the command tree of build {InformationalVersion} on {date}. Every command and verb is listed; nothing here is written by hand. Global options apply to every command: `--estate <path>` names the estate root (default: the current directory), `--json` switches to machine-readable output, `--version` prints the version, `--help` prints the usage of any command.",
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
            $"Generated by the build tooling (`vericairn-docs generate`) from the OpenAPI document the daemon of build {InformationalVersion} serves at `{OpenApiDocument.Path}` (OpenAPI {openapi}, API version {apiVersion}) on {date}. The daemon is the one owner of an estate's live state on a machine and every surface is its client; how to reach it and what the outcomes mean are in the API document (api.md). This reference lists every operation.",
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
            $"Generated by the build tooling (`vericairn-docs generate`) from the record type registry of build {InformationalVersion} on {date}. Each type lists its subject rule and its body fields with the field's kind, whether it is required and what it means. The envelope every record shares (format, type, id, at, by, body, hash and optionally subject, supersedes and signatures) is described in the record format document (record-format.md).",
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

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    private static void Write(string directory, string name, string text) => File.WriteAllText(Path.Combine(directory, name), text.EndsWith('\n') ? text : text + "\n");
}
