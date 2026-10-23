// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateInitCommand : Command
{
    public EstateInitCommand(CommandContext context)
        : this(context, CommandContext.VersionText())
    {
    }

    internal EstateInitCommand(CommandContext context, string versionText)
        : base("init", "Create a new estate: operator principal, charter and manifest, validated together.")
    {
        var operations = new EstateOperations(context);
        var nameOption = new Option<string>("--name") { Description = "Estate display name.", Required = true };
        var slugOption = new Option<string>("--slug") { Description = "Estate slug (lower-case kebab-case).", Required = true };
        var purposeOption = new Option<string>("--purpose") { Description = "Why the estate exists.", Required = true };
        var operatorNameOption = new Option<string>("--operator-name") { Description = "Display name of the root operator (a human).", Required = true };
        var organizationOption = new Option<string>("--organization") { Description = "Organization display name.", Required = true };
        var githubOption = new Option<string?>("--github") { Description = "GitHub organization login, if any." };
        var repositoryOption = new Option<string>("--repository") { Description = "GitHub owner/name of the Vericairn source to pin.", DefaultValueFactory = _ => "Vericairn/vericairn" };
        var commitOption = new Option<string?>("--commit") { Description = "Commit of the Vericairn source to pin (40 hex digits). Defaults to the running build's recorded commit; required when the build records none.", Arity = ArgumentArity.ExactlyOne };
        var releaseOption = new Option<bool>("--release") { Description = "Verify and pin the running published release, including its platform archive." };
        var fromOption = new Option<string?>("--from") { Description = "With --release, verify downloaded release responses, SHA256SUMS and archive in this directory without network access." };
        var profileOption = new Option<string>("--profile") { Description = "Storage profile: repository-synced (records as files in a git repository) or local-only (records in this machine's live store).", DefaultValueFactory = _ => "repository-synced" };
        Options.Add(profileOption);
        Options.Add(nameOption);
        Options.Add(slugOption);
        Options.Add(purposeOption);
        Options.Add(operatorNameOption);
        Options.Add(organizationOption);
        Options.Add(githubOption);
        Options.Add(repositoryOption);
        Options.Add(commitOption);
        Options.Add(releaseOption);
        Options.Add(fromOption);
        SetAction(parseResult => operations.Init(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(nameOption)!,
            parseResult.GetValue(slugOption)!,
            parseResult.GetValue(purposeOption)!,
            parseResult.GetValue(operatorNameOption)!,
            parseResult.GetValue(organizationOption)!,
            parseResult.GetValue(githubOption),
            parseResult.GetValue(repositoryOption)!,
            parseResult.GetValue(commitOption) ?? RecordedCommit(versionText),
            parseResult.GetValue(profileOption)!,
            parseResult.GetValue(context.JsonOption),
            parseResult.GetValue(releaseOption),
            parseResult.GetValue(fromOption), versionText));
    }

    private static string? RecordedCommit(string versionText)
    {
        int separator = versionText.IndexOf('+');
        if (separator < 0) return null;
        string commit = versionText[(separator + 1)..];
        return commit.Length == 40 && commit.All(char.IsAsciiHexDigit) ? commit.ToLowerInvariant() : null;
    }
}
