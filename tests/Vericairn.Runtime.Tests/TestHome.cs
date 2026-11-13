// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

[assembly: AssemblyFixture(typeof(Vericairn.Runtime.Tests.TestHome))]

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The Vericairn homes of the runtime test process: one for the process, set before any test runs and never swapped, and inside it one
/// for each test class, which every test of that class, its constructor and disposal and everything they start reach through
/// <see cref="VericairnHome.Path"/>, so no class reads or removes another's keys, clone registry, live stores or worker settings
/// (FIX-118). Test classes run in parallel and the environment variable is one per process, so a class's home is the scope of the test
/// context it runs in, never the variable; two classes that set the variable and deleted their home when they finished pulled live stores
/// out from under the others (SQLite Error 10, disk I/O error, on macOS in FIX-01's first run, and FIX-123). Code that runs outside
/// every test class, such as a static initializer, keeps the process's home.
/// </summary>
public sealed class TestHome : IDisposable
{
    private static int instances;
    private static readonly ConcurrentDictionary<string, string> Classes = new(StringComparer.Ordinal);
    private static string? root;
    private readonly string created;

    /// <summary>Always creates an isolated home before tests can use runtime storage, once per test process: a second home would
    /// point the process-wide variable elsewhere and delete it under the tests still running.</summary>
    public TestHome()
    {
        if (Interlocked.Increment(ref instances) > 1)
            throw new InvalidOperationException("The runtime tests' home is created once, by the assembly fixture; use VericairnHome.Path() instead.");
        created = Path.Combine(Path.GetTempPath(), "vericairn-runtime-tests-home-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(created);
        root = created;
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, created);
        VericairnHome.Scope = ClassHome;
        TestPrerequisites.RequireGit();
    }

    /// <summary>
    /// The home of the test class the calling code runs for, created on first use; null outside every test class. It is named by a short
    /// hash of the class's full name, so Windows paths under it stay short.
    /// </summary>
    internal static string? ClassHome()
    {
        string? name = TestContext.Current.TestClass?.TestClassName;
        return name is null || root is null ? null : Classes.GetOrAdd(name, Create);
    }

    private static string Create(string name)
    {
        string home = Path.Combine(root!, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12]);
        Directory.CreateDirectory(home);
        return home;
    }

    /// <summary>Removes every home this fixture created, once every test has finished.</summary>
    public void Dispose()
    {
        VericairnHome.Scope = null;
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
