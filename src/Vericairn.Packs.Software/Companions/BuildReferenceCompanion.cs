// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using static Vericairn.Packs.Software.CompanionInput;

namespace Vericairn.Packs.Software;

internal sealed class BuildReferenceCompanion(string name) : SoftwareCompanion(name)
{
    protected override string Render(CompanionInput input)
    {
        JsonElement source = input.Json();
        string kind = Id[(Id.LastIndexOf('/') + 1)..];
        if (kind == "api-reference") return Api(source);
        input.Build(source, "vericairn.build-references/1");
        if (String(source, "kind") != (kind == "cli-client-outcomes" ? "cli-reference" : kind)) throw new CompanionUnavailableException();
        if (kind == "cli-client-outcomes") return "# Generated CLI reference: client outcomes\n\n" + ClientOutcomes(source.GetProperty("clientOutcomes"));
        string content = kind == "cli-reference" ? Commands(source) : Records(source);
        return content + "\nBuild version: " + DocumentMarkdown.Text(Recorded(source, "version")) + "; provenance instant: " + DocumentMarkdown.Text(Recorded(source, "provenanceAt")) + ".\n";
    }

    private static string Commands(JsonElement source)
    {
        var text = new StringBuilder("# Command-line reference\n\n");
        if (source.TryGetProperty("clientOutcomes", out JsonElement outcomes)) text.Append(ClientOutcomes(outcomes)).Append('\n');
        text.Append("## Root options\n\n");
        Options(text, Array(source, "rootOptions"));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement command in Array(source, "commands").OrderBy(c => String(c, "name"), StringComparer.Ordinal))
        {
            string name = String(command, "name");
            if (!ReferenceInventory.ValidItem(new("command", name)) || !seen.Add(name)) throw new CompanionUnavailableException();
            text.Append('\n').Append('#', Math.Min(4, name.Count(c => c == ' ') + 1)).Append(" `").Append(name).Append("`\n\n").Append(DocumentMarkdown.Text(String(command, "description")))
                .Append("\n\nUsage: ").Append(Code(String(command, "usage"))).Append('\n');
            JsonElement[] arguments = Array(command, "arguments");
            if (arguments.Length > 0) { text.Append("\nArguments:\n\n"); Options(text, arguments); }
            JsonElement[] options = Array(command, "options");
            if (options.Length > 0) { text.Append("\nOptions:\n\n"); Options(text, options); }
            JsonElement[] verbs = Array(command, "verbs");
            if (verbs.Length > 0) text.Append("\nVerbs: ").AppendJoin(", ", verbs.Select(v => Code(v.GetString()!))).Append('\n');
        }
        return text.ToString();
    }

    private static string ClientOutcomes(JsonElement outcomes)
    {
        var text = new StringBuilder("## Interrupted client requests\n\nAn interrupted client request writes its diagnostic to standard error. It does not emit a replacement JSON result on standard output. Existing command names, options, help and successful JSON responses are unchanged.\n\n| Outcome | Exit code | Diagnostic |\n|---|---|---|\n");
        foreach (JsonElement row in Array(outcomes, "rows")) text.Append("| ").Append(Code(String(row, "code"))).Append(" | ").Append(Code(Recorded(row, "exitCode"))).Append(" | ").Append(Code(String(row, "diagnostic"))).Append(" |\n");
        text.Append("\nWhen an effect may have happened, the diagnostic also says: ").Append(Code(String(outcomes, "uncertain"))).Append("\n\n")
            .Append("A definite refusal keeps its diagnostic and exit code `2`; it is never called unavailable or of unknown outcome. Existing structured refusal responses keep their representation.\n\n")
            .Append("Caller cancellation (including Ctrl+C for ordinary requests) and a per-operation deadline are separate outcomes. Cancellation does not undo an effect, and these clients do not retry it. Foreground worker following still interprets Ctrl+C as a request to stop the original run; runtime serving still interprets it as a request to stop the service. Their requests remain subject to deadlines.\n");
        return text.ToString();
    }

    private static void Options(StringBuilder text, JsonElement[] options)
    {
        foreach (JsonElement option in options)
            text.Append("- ").Append(Code(String(option, "name"))).Append(": ").Append(DocumentMarkdown.Text(String(option, "description"))).Append('\n');
    }

    private static string Records(JsonElement source)
    {
        var text = new StringBuilder("# Record reference\n\n");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement type in Array(source, "types").OrderBy(t => String(t, "name"), StringComparer.Ordinal))
        {
            string name = String(type, "name");
            if (!ReferenceInventory.ValidItem(new("record-type", name)) || !seen.Add(name)) throw new CompanionUnavailableException();
            text.Append("## `").Append(name).Append("`\n\n").Append(DocumentMarkdown.Text(String(type, "summary")))
                .Append("\n\nSubject type: ").Append(DocumentMarkdown.Text(Recorded(type, "subjectType")))
                .Append("; subject hash required: ").Append(DocumentMarkdown.Text(Recorded(type, "subjectHashRequired")))
                .Append("; introduced in record format: ").Append(DocumentMarkdown.Text(Recorded(type, "sinceFormat"))).Append(".\n\n")
                .Append("| Field | Kind | Required | Introduced in format | Meaning |\n|---|---|---|---|---|\n");
            foreach (JsonElement field in Array(type, "fields")) Row(text, String(field, "name"), String(field, "kind"), Recorded(field, "required"), Recorded(field, "sinceFormat"), String(field, "description"));
            text.Append('\n');
        }
        text.Append("Reserved for later releases: ").Append(DocumentMarkdown.Text(Recorded(source, "reserved"))).Append(".\n");
        return text.ToString();
    }

    private static string Api(JsonElement source)
    {
        if (!String(source, "openapi").StartsWith("3.", StringComparison.Ordinal)) throw new CompanionUnavailableException();
        var text = new StringBuilder("# API reference\n\nThe retained OpenAPI document supplies every field below. Field paths are JSON Pointers; absent values are not inferred.\n\n");
        foreach (JsonProperty path in source.GetProperty("paths").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            foreach (JsonProperty method in path.Value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (method.Name is not ("get" or "head" or "post" or "put" or "delete" or "options" or "trace" or "patch")) continue;
                string operation = method.Name.ToUpperInvariant() + " " + path.Name;
                if (!ReferenceInventory.ValidItem(new("api-operation", operation))) throw new CompanionUnavailableException();
                text.Append("## `").Append(operation).Append("`\n\n");
                text.Append(Table(method.Value)).Append('\n');
            }
        }
        // Include path-level parameters, references, schemas, security, extensions and all remaining fields as well.
        text.Append("## Complete OpenAPI fields\n\n").Append(Table(source));
        return text.ToString();
    }

    internal static string Code(string value)
    {
        int longest = 0, run = 0;
        foreach (char c in value) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        string fence = new('`', longest + 1);
        string padding = value.StartsWith('`') || value.EndsWith('`') ? " " : "";
        return fence + padding + value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("|", "&#124;", StringComparison.Ordinal) + padding + fence;
    }
}
