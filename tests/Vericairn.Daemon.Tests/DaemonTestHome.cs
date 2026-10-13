// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Daemon.Tests;

/// <summary>Live stores, certificates and keys of test estates never touch the developer's real per-user state.</summary>
internal static class DaemonTestHome
{
    static DaemonTestHome()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            string home = Path.Combine(Path.GetTempPath(), "vericairn-daemon-tests-home-" + Ulid.NewUlid().Value);
            Directory.CreateDirectory(home);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
        }
    }

    /// <summary>Forces the static constructor.</summary>
    public static void Ensure()
    {
        // The work is in the static constructor.
    }

    /// <summary>Waits until no daemon answers at the endpoint, or fails after ten seconds.</summary>
    public static async Task WaitUntilGoneAsync(Protocol.DaemonEndpoint endpoint)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline && Sdk.VericairnClient.IsAlive(endpoint))
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.False(Sdk.VericairnClient.IsAlive(endpoint), "The daemon kept answering after shutdown.");
    }
}
