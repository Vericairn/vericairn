// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;

[assembly: AssemblyFixture(typeof(Vericairn.Records.Tests.IsolatedHome))]

namespace Vericairn.Records.Tests;

/// <summary>Record tests always create their own home before any store can be used.</summary>
public sealed class IsolatedHome : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "vericairn-record-tests-" + Guid.NewGuid().ToString("N"));

    public IsolatedHome()
    {
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* Best effort for test-owned files. */ }
    }
}
