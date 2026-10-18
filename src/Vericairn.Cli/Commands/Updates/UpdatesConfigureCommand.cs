// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Updates;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class UpdatesConfigureCommand : Command
{
    public UpdatesConfigureCommand(CommandContext context)
        : base("configure", "Show this machine's release check setting, or replace it with --enabled true or false. Enabled by default; the check is unauthenticated and carries no identifiers or usage telemetry. Settings and the timestamped cache stay under VericairnHome/updates, outside repositories and estates. Disabling makes no request and retains the cached answer. This command itself never checks the network.")
    {
        var enabled = new Option<bool?>("--enabled") { Description = "Whether to check for published releases at most once per 24 hours, including failed attempts. False prevents requests; true enables future checks. Omit to show the setting.", Arity = ArgumentArity.ExactlyOne };
        Options.Add(enabled);
        SetAction(result => Execute(context, result.GetValue(enabled), result.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, bool? enabled, bool json)
    {
        try
        {
            var store = new ReleaseUpdateStore();
            if (enabled is bool choice) store.Configure(choice);
            context.Output.WriteLine(json ? JsonSerializer.Serialize(new { enabled = store.Enabled }, ProtocolJson.Options) : ReleaseUpdates.Disclosure(store.Enabled));
            return Clean;
        }
        catch (Exception exception) when (ReleaseUpdateStore.Unavailable(exception))
        {
            return context.Fail("Could not save the machine-local release check setting: " + exception.Message);
        }
    }
}
