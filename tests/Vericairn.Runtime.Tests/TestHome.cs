// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

[assembly: AssemblyFixture(typeof(Vericairn.Runtime.Tests.TestHome))]

namespace Vericairn.Runtime.Tests;

/// <summary>
/// One Vericairn home for the whole runtime test process, set before any test runs and never swapped. Test classes run in
/// parallel, and two classes that set the process-wide variable and deleted their home when they finished pulled live stores out
/// from under the others (SQLite Error 10, disk I/O error, on macOS in FIX-01's first run).
/// </summary>
public sealed class TestHome : IDisposable
{
    private readonly string? created;

    /// <summary>Sets the home unless the environment already names one.</summary>
    public TestHome()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            created = Path.Combine(Path.GetTempPath(), "vericairn-runtime-tests-home-" + Ulid.NewUlid().Value);
            Directory.CreateDirectory(created);
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, created);
        }
    }

    /// <summary>Removes the home this fixture created, once every test has finished.</summary>
    public void Dispose()
    {
        if (created is null)
        {
            return;
        }

        try
        {
            WorkspaceCleanup.Delete(created);
        }
        catch (IOException)
        {
            // Best effort: a store still closing keeps its files a moment longer.
        }
    }
}
