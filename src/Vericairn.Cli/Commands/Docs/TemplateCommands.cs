// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class TemplatesCommand : Command
{
    internal TemplatesCommand(CommandContext context) : base("templates", "Inspect declarative template versions; a template grants no authority.")
    {
        Subcommands.Add(new TemplatesListCommand(context));
        Subcommands.Add(new TemplatesShowCommand(context));
    }
}

internal sealed class TemplatesListCommand : Command
{
    internal TemplatesListCommand(CommandContext context) : base("list", "List available exact template versions.") =>
        SetAction(p => TemplatesShowCommand.Show(context, p.GetValue(context.EstateOption)!, null, null, p.GetValue(context.JsonOption)));
}

internal sealed class TemplatesShowCommand : Command
{
    internal TemplatesShowCommand(CommandContext context) : base("show", "Show the complete contract of a template version.")
    {
        var id = new Argument<string>("template");
        var version = new Option<string?>("--version") { Description = "Select one exact version; omitted lists every available version of this template." };
        Arguments.Add(id); Options.Add(version);
        SetAction(p => Show(context, p.GetValue(context.EstateOption)!, p.GetValue(id), p.GetValue(version), p.GetValue(context.JsonOption)));
    }

    internal static int Show(CommandContext context, string root, string? id, string? version, bool json) => context.WithApi(root, api =>
    {
        TemplatesResponse response = api.Templates();
        TemplateDto[] selected = response.Templates.Where(t => (id is null || t.Pin.Id == id) && (version is null || t.Pin.Version == version)).ToArray();
        if (json) context.Output.WriteLine(JsonSerializer.Serialize(response with { Templates = selected }, ProtocolJson.Options));
        else if (response.Problem is not null) context.Error.WriteLine(response.Problem.Message);
        else foreach (TemplateDto template in selected)
        {
            context.Output.WriteLine($"{template.Pin.Id} {template.Pin.Version} at {template.Pin.Hash}");
            if (id is not null) context.Output.WriteLine(template.Definition.GetRawText());
        }
        return response.Problem is not null || (id is not null && selected.Length == 0) ? Failed : Clean;
    });
}

internal sealed class DocsNewCommand : Command
{
    internal DocsNewCommand(CommandContext context) : base("new", "Create a living document from an exact template version and lay out its sections.")
    {
        var template = new Argument<string>("template");
        var path = new Argument<string>("path");
        var version = new Option<string>("--version") { Required = true, Description = "Exact template version; instances never follow latest." };
        var by = new Option<string>("--by") { Required = true, Description = "Author and instance owner." };
        var title = new Option<string>("--title") { Required = true, Description = "Document title." };
        var options = new Option<string?>("--configuration") { Description = "JSON file with optional options, scope and overrides." };
        var level = new Option<string>("--level") { DefaultValueFactory = _ => "warn", Description = "remind, warn or refuse." };
        Arguments.Add(template); Arguments.Add(path);
        Options.Add(version); Options.Add(by); Options.Add(title); Options.Add(options); Options.Add(level);
        SetAction(p =>
        {
            int Create(JsonElement? configuration) => context.WithApi(p.GetValue(context.EstateOption)!, api =>
            {
                if (configuration is { } supplied && (supplied.ValueKind != JsonValueKind.Object
                    || supplied.EnumerateObject().Any(field => field.Name is not ("options" or "scope" or "overrides"))))
                    return context.Fail("Configuration must be an object containing only options, scope and overrides.");
                TemplatesResponse catalogue = api.Templates();
                if (catalogue.Problem is not null) return context.Fail(catalogue.Problem.Message);
                TemplateDto? selected = catalogue.Templates.SingleOrDefault(t => t.Pin.Id == p.GetValue(template) && t.Pin.Version == p.GetValue(version));
                if (selected is null) return context.Fail("The exact template version is unavailable.");
                JsonElement? Field(string key) => configuration is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(key, out JsonElement value) ? value : null;
                DocumentNewResponse response = api.DocumentNew(new(p.GetValue(by)!, selected.Pin, p.GetValue(path)!, p.GetValue(title)!, Field("options"), Field("scope"), Field("overrides"), p.GetValue(level)!));
                if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                else if (response.Ok) context.Output.WriteLine($"Created {response.Path}; declaration {response.Declaration!.Id}; content {response.ContentHash}.");
                else
                {
                    context.Error.WriteLine(response.Problem?.Message);
                    if (response.Declaration is { } stored) context.Output.WriteLine($"Stored declaration {stored.Id} at {stored.Hash}; reconcile its original batch before retrying.");
                }
                return response.Ok ? Clean : Failed;
            });
            return p.GetValue(options) is { } file ? context.WithBody(file, body => Create(body)) : Create(null);
        });
    }
}

internal sealed class DocsSectionsCommand : Command
{
    internal DocsSectionsCommand(CommandContext context) : base("sections", "Show each section's derived state, required fidelity and evidence.")
    {
        var path = new Argument<string>("path");
        var commit = new Option<string?>("--commit") { Description = "Exact commit for a document in another repository." };
        Arguments.Add(path); Options.Add(commit);
        SetAction(p => context.WithApi(p.GetValue(context.EstateOption)!, api =>
        {
            DocumentSectionsResponse response = api.DocumentSections(p.GetValue(path)!, p.GetValue(commit));
            context.Output.WriteLine(p.GetValue(context.JsonOption) ? JsonSerializer.Serialize(response, ProtocolJson.Options) : TemplatePresentation.Sections(response));
            return response.Problem is null ? Clean : Failed;
        }));
    }
}

internal sealed class DocsBriefCommand : Command
{
    internal DocsBriefCommand(CommandContext context) : base("brief", "Export a section's complete measured context and review criteria.")
    {
        var path = new Argument<string>("path");
        var section = new Option<string>("--section") { Required = true, Description = "The section or field identifier." };
        var maxBytes = new Option<long?>("--max-bytes") { Description = "Positive allocation for the complete context; overflow refuses without truncation." };
        var commit = new Option<string?>("--commit") { Description = "Exact commit for a document in another repository." };
        Arguments.Add(path); Options.Add(section); Options.Add(maxBytes); Options.Add(commit);
        SetAction(p => context.WithApi(p.GetValue(context.EstateOption)!, api =>
        {
            SectionBriefResponse response = api.SectionBrief(p.GetValue(path)!, p.GetValue(section)!, p.GetValue(maxBytes), p.GetValue(commit));
            context.Output.WriteLine(p.GetValue(context.JsonOption) ? JsonSerializer.Serialize(response, ProtocolJson.Options) : TemplatePresentation.Brief(response));
            return response.Problem is null ? Clean : Failed;
        }));
    }
}
