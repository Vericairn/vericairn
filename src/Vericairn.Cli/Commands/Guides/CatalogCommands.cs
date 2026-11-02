// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Cli;

internal sealed class CatalogExportCommand : Command
{
    public CatalogExportCommand(CommandContext context, bool compare)
        : base(compare ? "publication-check" : "export", compare
            ? "Compare a freshly generated catalog bundle with an explicit local destination checkout; never publishes or writes the destination."
            : "Prepare a cleared catalog repository bundle in a new or empty local directory; never creates a repository or publishes.")
    {
        var catalog = Required("--catalog", "Explicit local catalog source root.");
        var contracts = Required("--contracts", "Explicit local role-contract source root.");
        var index = Required("--index", "Private exact source-index JSON.");
        var plan = Required("--plan", "Private reviewed export selection and source allowlist JSON.");
        var destination = Required(compare ? "--destination" : "--out", compare ? "Existing local checkout or empty comparison directory." : "New or empty output directory, separate from every input.");
        var issues = new Option<string?>("--issues") { Description = "Explicit public entry-to-issue-number JSON array; omitted means an empty map." };
        Options.Add(issues);
        SetAction(p =>
        {
            try
            {
                string catalogPath = p.GetValue(catalog)!, contractsPath = p.GetValue(contracts)!, indexPath = p.GetValue(index)!,
                    planPath = p.GetValue(plan)!, output = p.GetValue(destination)!;
                string? issuesPath = p.GetValue(issues);
                if (!CatalogWorkspace.SeparateOutput(output, catalogPath, contractsPath, issuesPath is null ? [indexPath, planPath] : [indexPath, planPath, issuesPath]))
                    return context.Fail("The destination must be separate from every input tree and input file.");
                CatalogBundle bundle = CatalogWorkspace.Load(indexPath, planPath, catalogPath, contractsPath, issuesPath);
                CatalogComparison result = compare ? CatalogWorkspace.Compare(bundle, output) : new([], CatalogWorkspace.Write(bundle, output));
                if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(new
                {
                    ok = result.Findings.Count == 0, publicationState = "local-preparation", files = bundle.Files.Count,
                    result.Changes, result.Findings,
                }, GuideJson.Options));
                else
                {
                    context.Output.WriteLine(result.Findings.Count == 0 ? compare ? "Local publication comparison prepared. Publication requires the operator's decision." : "Catalog bundle prepared locally. Nothing was published." : "Catalog preparation failed its checks.");
                    foreach (CatalogChange change in result.Changes) context.Output.WriteLine(change.Action + ": " + change.Path);
                    foreach (GuideFinding finding in result.Findings) context.Error.WriteLine(finding.Code + ": " + finding.Message);
                }
                return result.Findings.Count == 0 ? CommandContext.Clean : CommandContext.HasErrors;
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
            { return context.Fail("An explicit catalog input or destination is unavailable."); }
        });

        Option<string> Required(string name, string description)
        {
            var option = new Option<string>(name) { Description = description, Required = true };
            Options.Add(option);
            return option;
        }
    }
}

internal sealed class CatalogRequestCommand : Command
{
    public CatalogRequestCommand(CommandContext context, bool reconcile)
        : base(reconcile ? "requests-reconcile" : "request-validate", reconcile
            ? "Reconcile supplied attributable provider observations locally; output is private and no provider write occurs."
            : "Validate shared request fields against an exact public bundle inventory; never submits a request.")
    {
        var inventory = new Option<string>("--inventory") { Description = "The exact bundle's indexes/entries.json.", Required = true };
        var input = new Option<string>(reconcile ? "--observations" : "--request")
        {
            Description = reconcile ? "Private complete observation snapshot JSON, including revision history." : "Request fields as JSON; unknown properties are refused. Optional fields may be omitted.",
            Required = true,
        };
        Options.Add(inventory);
        Options.Add(input);
        SetAction(p =>
        {
            try
            {
                CatalogInventory entries = CatalogWorkspace.Read<CatalogInventory>(p.GetValue(inventory)!);
                if (reconcile)
                {
                    CatalogRequestSnapshot snapshot = CatalogWorkspace.Read<CatalogRequestSnapshot>(p.GetValue(input)!);
                    CatalogReconciliation result = CatalogRequestReconciler.Reconcile(entries, snapshot);
                    context.Output.WriteLine(JsonSerializer.Serialize(new { snapshot.Repository, snapshot.WindowStart, snapshot.WindowEnd, snapshot.AsOf,
                        observationMode = "supplied", publicationState = "private-local-result", result }, GuideJson.Options));
                    return result.Complete ? CommandContext.Clean : CommandContext.HasErrors;
                }
                CatalogRequestValidation validation = CatalogRequests.Validate(entries, CatalogWorkspace.Read<CatalogRequest>(p.GetValue(input)!));
                context.Output.WriteLine(JsonSerializer.Serialize(validation, GuideJson.Options));
                return validation.Accepted ? CommandContext.Clean : CommandContext.HasErrors;
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or JsonException or NotSupportedException)
            { return context.Fail("A local inventory or request input is malformed, unavailable, linked or oversized."); }
        });
    }
}
