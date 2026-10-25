// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class DocsAdoptCommand : Command
{
    internal DocsAdoptCommand(CommandContext context) : base("adopt", "Attach a pinned template to existing authored bytes, preserving revision history and existing checks.")
    {
        var template = new Argument<string>("template");
        var path = new Argument<string>("path");
        var version = new Option<string>("--version") { Required = true, Description = "Exact immutable template version." };
        var by = new Option<string>("--by") { Required = true, Description = "Author and instance owner." };
        var expected = new Option<string>("--expected-content") { Required = true, Description = "SHA-256 of the existing file's exact bytes." };
        var declaration = new Option<string?>("--expected-declaration") { Description = "Required exact current declaration hash when the document is already living." };
        var title = new Option<string?>("--title") { Description = "Keep the current title by default; an undeclared document uses its file name." };
        var configuration = new Option<string?>("--configuration") { Description = "JSON file containing options, scope and reasoned overrides; rename sections to match existing headings." };
        var level = new Option<string>("--level") { DefaultValueFactory = _ => "warn", Description = "Due level for a new declaration only; existing triggers and levels are retained." };
        var preview = new Option<bool>("--preview") { Description = "Return the exact proposal and any required migration evidence without signing or writing." };
        var regions = new Option<bool>("--with-regions") { Description = "Include template generators in declared regions; existing ownership changes still require their independent migration review." };
        Arguments.Add(template); Arguments.Add(path);
        Options.Add(version); Options.Add(by); Options.Add(expected); Options.Add(declaration); Options.Add(title);
        Options.Add(configuration); Options.Add(level); Options.Add(preview); Options.Add(regions);
        SetAction(p =>
        {
            int Adopt(JsonElement? config) => context.WithApi(p.GetValue(context.EstateOption)!, api =>
            {
                if (config is { } supplied && (supplied.ValueKind != JsonValueKind.Object
                    || supplied.EnumerateObject().Any(field => field.Name is not ("options" or "scope" or "overrides"))))
                    return context.Fail("Configuration must be an object containing only options, scope and overrides.");
                TemplatesResponse catalogue = api.Templates();
                if (catalogue.Problem is not null) return context.Fail(catalogue.Problem.Message);
                TemplateDto? selected = catalogue.Templates.SingleOrDefault(t => t.Pin.Id == p.GetValue(template) && t.Pin.Version == p.GetValue(version));
                if (selected is null) return context.Fail("The exact template version is unavailable.");
                JsonElement? Field(string key) => config is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(key, out JsonElement field) ? field : null;
                DocumentAdoptResponse response = api.DocumentAdopt(new(p.GetValue(by)!, selected.Pin, p.GetValue(path)!, p.GetValue(expected)!,
                    p.GetValue(declaration), p.GetValue(title), Field("options"), Field("scope"), Field("overrides"), p.GetValue(level)!, p.GetValue(preview), p.GetValue(regions)));
                if (p.GetValue(context.JsonOption) || p.GetValue(preview)) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                else
                {
                    if (response.Problem is not null) context.Error.WriteLine(response.Problem.Message);
                    if (response.Declaration is { } written) context.Output.WriteLine($"Stored declaration {written.Id} at {written.Hash}.");
                    if (response.Pin is { } pin) context.Output.WriteLine($"Stored document revision {pin.Id} at {pin.Hash}.");
                    if (response.Ok) context.Output.WriteLine($"Adopted {p.GetValue(path)}; retained original content {response.ContentHash}. Inspect its sections before review.");
                    else if (response.Declaration is not null || response.Pin is not null) context.Output.WriteLine("Reconcile the original batch before retrying.");
                }
                return response.Problem is null && (response.Ok || p.GetValue(preview)) ? Clean : Failed;
            });
            return p.GetValue(configuration) is { } file ? context.WithBody(file, body => Adopt(body)) : Adopt(null);
        });
    }
}
