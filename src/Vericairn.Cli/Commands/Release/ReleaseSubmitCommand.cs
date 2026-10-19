// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ReleaseSubmitCommand : Command
{
    public ReleaseSubmitCommand(CommandContext context)
        : base("submit", "Record the intent, then dispatch the build through the provider; the run id and outcome are recorded as they are observed. Never publishes.")
    {
        var byOption = new Option<string>("--by") { Description = "Principal id recording the operation (ULID).", Required = true };
        var versionOption = new Option<string>("--version") { Description = "The version to release, for example 0.4.0.", Required = true };
        var productOption = new Option<string>("--product") { Description = "Product record id the release belongs to; its declared release destination supplies the provider and settings the request does not name.", Required = true };
        var providerOption = new Option<string?>("--provider") { Description = "The provider id, for example github-releases; the product's declared provider when omitted." };
        var repositoryOption = new Option<string?>("--repository") { Description = "The setting repository (for GitHub, owner/name of the repository whose workflow builds the release)." };
        var refOption = new Option<string?>("--ref") { Description = "The setting ref (the branch or tag to build from); the provider's default when omitted." };
        var commitOption = new Option<string?>("--commit") { Description = "Prepared exact commit for every source check; required in format 3 or for release-repository document checks." };
        var expectedOption = new Option<string?>("--expected") { Description = "Expected current product hash; required in format 3." };
        var workflowOption = new Option<string?>("--workflow") { Description = "The setting workflow (the release workflow file); the provider's default when omitted." };
        var settingOption = new Option<string[]>("--setting") { Description = "Any declared setting as name=value; repeatable.", AllowMultipleArgumentsPerToken = true };
        var reconciliationOption = new Option<string?>("--reconciliation") { Description = "How an earlier failed, canceled or uncertain attempt under the same identity was settled; required for a retry." };
        Options.Add(productOption);
        Options.Add(versionOption);
        Options.Add(byOption);
        Options.Add(providerOption);
        Options.Add(repositoryOption);
        Options.Add(refOption);
        Options.Add(commitOption);
        Options.Add(expectedOption);
        Options.Add(workflowOption);
        Options.Add(settingOption);
        Options.Add(reconciliationOption);
        SetAction(parseResult => Execute(context,
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(productOption)!,
            parseResult.GetValue(versionOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(providerOption),
            parseResult.GetValue(repositoryOption),
            parseResult.GetValue(refOption),
            parseResult.GetValue(workflowOption),
            parseResult.GetValue(settingOption) ?? [],
            parseResult.GetValue(reconciliationOption),
            parseResult.GetValue(commitOption),
            parseResult.GetValue(expectedOption),
            parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string product, string version, string by, string? provider, string? repository, string? reference, string? workflow, string[] settings, string? reconciliation, string? commit, string? expected, bool json)
    {
        Dictionary<string, string>? parsed = CommandSettings.Parse(settings, out string? problem);
        return parsed is null
            ? context.Fail(problem!)
            : context.WithApi(root, api => ReleaseOutput.Write(context, api.ReleaseSubmit(new ReleaseSubmitRequest(product, version, repository, by, reference, workflow, reconciliation, provider, parsed, commit, expected)), json));
    }
}
