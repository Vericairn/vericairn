// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Cli;

internal sealed class GuidesCommand : Command
{
    public GuidesCommand(CommandContext context)
        : base("guides", "Deterministic community build guides from explicit local sources; private previews only, with no estate, network or publication operation.")
    {
        Subcommands.Add(new GuidesBuildCommand(context, check: false));
        Subcommands.Add(new GuidesBuildCommand(context, check: true));
        Subcommands.Add(new GuidesCompareCommand(context));
    }
}

internal sealed class GuidesBuildCommand : Command
{
    public GuidesBuildCommand(CommandContext context, bool check)
        : base(check ? "check" : "generate", check
            ? "Compare a private preview's complete file membership and bytes with fresh generation; writes nothing."
            : "Generate a private guide set, Markdown/HTML index, source manifest and coverage manifest in a caller-selected directory; existing files are checked, never overwritten.")
    {
        var catalog = Required("--catalog", "Local catalog root; only the index's allowlisted Markdown sources are read.");
        var contracts = Required("--contracts", "Local contracts root; only explicitly pinned role contracts are read.");
        var index = Required("--index", "Reviewed source-index JSON with exact file/block pins, stable row keys, status, boundaries and references.");
        var output = Required("--out", "Local private preview directory, separate from input trees. No public destination is supported.");
        var selection = new Option<string[]>("--select") { Description = "Select planned keys or aliases; repeat for several. Omitted means all planned rows, including planned remainders.", AllowMultipleArgumentsPerToken = true };
        Options.Add(selection);
        SetAction(p =>
        {
            try
            {
                if (!GuidePreview.SeparateOutput(p.GetValue(output)!, p.GetValue(index)!, p.GetValue(catalog)!, p.GetValue(contracts)!))
                    return context.Fail("The preview directory must be separate from the source trees and index.");
                GuideBundle bundle = GuidePreview.Load(p.GetValue(index)!, p.GetValue(catalog)!, p.GetValue(contracts)!, p.GetValue(selection));
                IReadOnlyList<GuideFinding> findings = check ? GuidePreview.Check(bundle, p.GetValue(output)!) : GuidePreview.Write(bundle, p.GetValue(output)!);
                if (p.GetValue(context.JsonOption))
                    context.Output.WriteLine(JsonSerializer.Serialize(new { ok = findings.Count == 0, publicationState = "private-preview", files = bundle.Files.Count, findings }, GuideJson.Options));
                else
                {
                    context.Output.WriteLine(findings.Count == 0 ? "Private guide preview " + (check ? "matches fresh generation." : "prepared. Nothing was published.") : "Private guide preview failed its checks.");
                    foreach (GuideFinding finding in findings) context.Error.WriteLine(finding.Entry + ": " + finding.Code + ": " + finding.Message);
                }
                return findings.Count == 0 ? CommandContext.Clean : CommandContext.HasErrors;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
            { return context.Fail("A guide input or output path is invalid or unavailable."); }
        });

        Option<string> Required(string name, string description)
        {
            var option = new Option<string>(name) { Description = description, Required = true };
            Options.Add(option);
            return option;
        }
    }
}

internal sealed class GuidesCompareCommand : Command
{
    public GuidesCompareCommand(CommandContext context)
        : base("compare", "Compare two local source manifests and list every guide whose inputs are due for review; writes nothing.")
    {
        var before = new Option<string>("--before") { Description = "Earlier private source-manifest JSON.", Required = true };
        var after = new Option<string>("--after") { Description = "Current private source-manifest JSON.", Required = true };
        Options.Add(before);
        Options.Add(after);
        SetAction(p =>
        {
            try
            {
                GuideInputComparison comparison = GuidePreview.Compare(p.GetValue(before)!, p.GetValue(after)!);
                if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(comparison, GuideJson.Options));
                else
                {
                    foreach (string key in comparison.Due) context.Output.WriteLine(key + ": due");
                    foreach (GuideFinding finding in comparison.Findings) context.Error.WriteLine(finding.Code + ": " + finding.Message);
                }
                return comparison.Findings.Count == 0 ? CommandContext.Clean : CommandContext.HasErrors;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
            { return context.Fail("A source manifest is unavailable."); }
        });
    }
}
