// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

[assembly: AssemblyFixture(typeof(Vericairn.Records.Tests.TestHome))]

namespace Vericairn.Records.Tests;

/// <summary>Records tests never inherit a contributor's runtime storage.</summary>
public sealed class TestHome : IDisposable
{
    private readonly string created = Path.Combine(Path.GetTempPath(), "vericairn-records-tests-home-" + Ulid.NewUlid().Value);

    public TestHome()
    {
        Directory.CreateDirectory(created);
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, created);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(created, recursive: true);
        }
        catch (IOException)
        {
            // Best effort after all tests finish.
        }
    }
}
