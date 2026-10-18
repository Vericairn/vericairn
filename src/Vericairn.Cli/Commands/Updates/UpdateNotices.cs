// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Sdk;

namespace Vericairn.Cli;

internal static class UpdateNotices
{
    public const string Help = "Discloses and checks GitHub for a newer published release at most once per 24 hours, shared with Workbench; no identifiers or usage telemetry. Disclosure and upgrade notices go to stderr, leaving stdout and JSON unchanged. Disable with vericairn updates configure --enabled false. Offline failures keep the cached answer and never fail this command.";

    public static int Run(CommandContext context, ReleaseUpdates updates, Func<int> action)
    {
        context.Error.WriteLine(ReleaseUpdates.Disclosure(updates.Enabled));
        string notice = ReleaseUpdates.Notice(updates.CheckAsync().GetAwaiter().GetResult(), CommandContext.VersionText());
        if (notice.Length > 0) context.Error.WriteLine(notice);
        return action();
    }
}
