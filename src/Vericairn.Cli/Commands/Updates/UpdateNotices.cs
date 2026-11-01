// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Cli;

internal static class UpdateNotices
{
    public const string Help = "Discloses and checks GitHub for a newer published release at most once per 24 hours, shared with Workbench; no identifiers or usage telemetry. Disclosure and upgrade notices go to stderr, leaving stdout and JSON unchanged. Disable with vericairn updates configure --enabled false. Offline failures keep the cached answer and never fail this command.";

    public static int Run(CommandContext context, ReleaseUpdates updates, Func<int> action, string root)
    {
        PublishedRelease? release = updates.CheckAsync().GetAwaiter().GetResult();
        context.Error.WriteLine(ReleaseUpdates.Disclosure(updates.Enabled));
        context.ReleaseNoticePin = null;
        int result = action();
        // Status already reads the serving runtime: prefer that exact response without another estate load.
        // Version and doctor use the local manifest selection and need no new runtime request.
        string notice = context.ReleaseNoticePin is { } pin ? ReleaseUpdates.EstateNotice(release, CommandContext.VersionText(), pin) : Notice(release, root);
        if (notice.Length > 0) context.Error.WriteLine(notice);
        return result;
    }

    private static string Notice(PublishedRelease? release, string root)
    {
        try
        {
            if (!File.Exists(Path.Combine(root, EstateLayout.ManifestPath))) return ReleaseUpdates.Notice(release, CommandContext.VersionText());
            var findings = new FindingList();
            EstateManifest? manifest = EstateManifest.Load(root, findings);
            return manifest is null || findings.HasErrors ? string.Empty
                : ReleaseUpdates.Notice(release, CommandContext.VersionText(), manifest.Repository, manifest.Line, manifest.Channel,
                    manifest.Mode == "release" ? manifest.Version : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
