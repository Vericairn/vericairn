// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>
/// ID-16 (0.9.4): a release build starts the daemon it launches with the .NET diagnostics channel off, the only configuration .NET reads
/// that switch from; a development build adds nothing to the daemon's environment.
/// </summary>
public sealed class DaemonStartTests
{
    [Fact]
    public void AReleaseBuildStartsItsDaemonWithTheDiagnosticsChannelOff()
    {
        string root = Path.Combine(Path.GetTempPath(), "estate");
        string[] launcher = ["/opt/vericairn/vericairn"];

        ProcessStartInfo release = DaemonLifecycle.DaemonStart(launcher, root, PresenceSessionBuild.Parse("ABCDE12345", null, "26"));
        ProcessStartInfo development = DaemonLifecycle.DaemonStart(launcher, root, PresenceSessionBuild.Parse(null, null, null));

        Assert.Equal("0", release.Environment[PresenceSessionBuild.DiagnosticsVariable]);
        Assert.Equal(Environment.GetEnvironmentVariable(PresenceSessionBuild.DiagnosticsVariable), development.Environment.TryGetValue(PresenceSessionBuild.DiagnosticsVariable, out string? value) ? value : null);
        Assert.Equal(["--estate", Path.GetFullPath(root), "runtime", "serve", "--detached"], release.ArgumentList);
        Assert.True(release.RedirectStandardInput && release.RedirectStandardOutput && release.RedirectStandardError);
    }
}
