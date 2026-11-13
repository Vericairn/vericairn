// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>Each test class reaches its own Vericairn home, and so does every process it starts (FIX-118).</summary>
public sealed class TestHomeTests
{
    [Fact]
    public void ATestClassHasItsOwnHomeInsideTheProcessHome()
    {
        string process = Path.GetFullPath(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)!);
        string home = VericairnHome.Path();
        Assert.NotEqual(process, home);
        Assert.Equal(process, Path.GetDirectoryName(home));
        Assert.True(Directory.Exists(home));
        Assert.Equal(home, VericairnHome.Path());
        Assert.Equal(home, TestHome.ClassHome());
        // Named by the class's full name, so every other class reaches another directory.
        string name = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(typeof(TestHomeTests).FullName!)))[..12];
        Assert.Equal(name, Path.GetFileName(home));
    }

    [Fact]
    public async Task CodeOutsideEveryTestClassKeepsTheProcessHome()
    {
        string process = Path.GetFullPath(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)!);
        Task<string> outside;
        using (ExecutionContext.SuppressFlow())
        {
            outside = Task.Run(VericairnHome.Path, TestContext.Current.CancellationToken);
        }

        Assert.Equal(process, await outside);
        Assert.NotEqual(process, VericairnHome.Path());
    }

    [Fact]
    public void AProcessStartedFromATestIsGivenItsClassHome()
    {
        Assert.Equal(VericairnHome.Path(), WorkerEnvironment.Current()[VericairnHome.EnvironmentVariable]);
        Assert.Equal(VericairnHome.Path(), WorkerEnvironment.Build(WorkerEnvironment.Current())[VericairnHome.EnvironmentVariable]);
    }
}
