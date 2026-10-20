// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Daemon.Tests;

/// <summary>Live stores, certificates and keys of test estates never touch the developer's real per-user state.</summary>
internal static class DaemonTestHome
{
    static DaemonTestHome()
    {
        string home = Path.Combine(Path.GetTempPath(), "vericairn-daemon-tests-home-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
    }

    internal static Task<DaemonHost> StartAsync(string root, string? endpoint = null, bool loopback = true,
        DaemonOptions? options = null, CancellationToken cancellationToken = default)
    {
        Ensure();
        TestPrerequisites.RequireLocalIpc();
        if (loopback || options?.LocalNetwork is not null)
        {
            TestPrerequisites.RequireLoopback();
            TestPrerequisites.RequireTlsCertificates();
        }
        return DaemonHost.StartAsync(root, endpoint, loopback, options, cancellationToken);
    }

    /// <summary>Forces the static constructor.</summary>
    public static void Ensure()
    {
        // The work is in the static constructor.
    }

}
