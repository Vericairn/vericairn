// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Cli;

internal static partial class CliApp
{
    private const string UpdateHelp = "Discloses and checks GitHub for a newer published release at most once per 24 hours, shared with Workbench; no identifiers or usage telemetry. Disclosure and upgrade notices go to stderr, leaving stdout and JSON unchanged. Disable with vericairn updates configure --enabled false. Offline failures keep the cached answer and never fail this command.";

    private static Command UpdatesFamily(Option<bool> json)
    {
        var updates = new Command("updates", "Machine-local release check settings shared by the CLI and Workbench; nothing installs or upgrades automatically.");
        var configure = new Command("configure", "Show this machine's release check setting, or replace it with --enabled true or false. Enabled by default; the check is unauthenticated and carries no identifiers or usage telemetry. Settings and the timestamped cache stay under VericairnHome/updates, outside repositories and estates. Disabling makes no request and retains the cached answer. This command itself never checks the network.");
        var enabled = new Option<bool?>("--enabled") { Description = "Whether to check for published releases at most once per 24 hours, including failed attempts. False prevents requests; true enables future checks. Omit to show the setting.", Arity = ArgumentArity.ExactlyOne };
        configure.Options.Add(enabled);
        configure.SetAction(result => Commands.UpdatesConfigure(result.GetValue(enabled), result.GetValue(json)));
        updates.Subcommands.Add(configure);
        return updates;
    }
}

internal static partial class Commands
{
    internal static int WithUpdates(ReleaseUpdates updates, Func<int> action)
    {
        Console.Error.WriteLine(ReleaseUpdates.Disclosure(updates.Enabled));
        string notice = ReleaseUpdates.Notice(updates.CheckAsync().GetAwaiter().GetResult(), VersionText());
        if (notice.Length > 0) Console.Error.WriteLine(notice);
        return action();
    }

    internal static int UpdatesConfigure(bool? enabled, bool json)
    {
        try
        {
            var store = new ReleaseUpdateStore();
            if (enabled is bool choice) store.Configure(choice);
            Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { enabled = store.Enabled }, ProtocolJson.Options) : ReleaseUpdates.Disclosure(store.Enabled));
            return Clean;
        }
        catch (Exception exception) when (ReleaseUpdateStore.Unavailable(exception))
        {
            return Fail("Could not save the machine-local release check setting: " + exception.Message);
        }
    }
}
