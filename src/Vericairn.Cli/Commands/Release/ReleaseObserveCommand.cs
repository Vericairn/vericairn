// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ReleaseObserveCommand : Command
{
    public ReleaseObserveCommand(CommandContext context)
        : base("observe", "Observe the run and the draft through the provider the operation records; record the outcome, the candidate release, and later the operator's manual publication.")
    {
        var byOption = new Option<string>("--by") { Description = "Principal id recording the operation (ULID).", Required = true };
        var versionOption = new Option<string>("--version") { Description = "The version to release, for example 0.4.0.", Required = true };
        var providerOption = new Option<string?>("--provider") { Description = "The provider id, for example github-releases; the product's declared provider when omitted." };
        var repositoryOption = new Option<string?>("--repository") { Description = "The setting repository (for GitHub, owner/name of the repository whose workflow builds the release)." };
        var observeProductOption = new Option<string?>("--product") { Description = "Product record id, to find the operation by product and version." };
        var qualifiedOption = new Option<string[]>("--qualified") { Description = "Platform whose qualification is recorded for the candidate; repeatable.", AllowMultipleArgumentsPerToken = true };
        var limitationOption = new Option<string[]>("--limitation") { Description = "A known limitation of the candidate; repeatable.", AllowMultipleArgumentsPerToken = true };
        var signingOption = new Option<string>("--signing") { Description = "Signing state recorded on the candidate.", DefaultValueFactory = _ => "unsigned developer build" };
        var cancelOption = new Option<string?>("--cancel") { Description = "Settle an intent that was never dispatched, or an outcome reconciled as having had no effect, with this reason." };
        Options.Add(versionOption);
        Options.Add(byOption);
        Options.Add(observeProductOption);
        Options.Add(providerOption);
        Options.Add(repositoryOption);
        Options.Add(qualifiedOption);
        Options.Add(limitationOption);
        Options.Add(signingOption);
        Options.Add(cancelOption);
        SetAction(parseResult => Execute(context,
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(versionOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(observeProductOption),
            parseResult.GetValue(providerOption),
            parseResult.GetValue(repositoryOption),
            parseResult.GetValue(qualifiedOption) ?? [],
            parseResult.GetValue(limitationOption) ?? [],
            parseResult.GetValue(signingOption)!,
            parseResult.GetValue(cancelOption),
            parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string version, string by, string? product, string? provider, string? repository, string[] qualified, string[] limitations, string signing, string? cancel, bool json) =>
        context.WithApi(root, api => ReleaseOutput.Write(context, api.ReleaseObserve(new ReleaseObserveRequest(version, repository, by, qualified, limitations, signing, cancel, product, provider)), json));
}
