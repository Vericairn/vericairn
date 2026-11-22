// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Records.Hashing;
using Vericairn.Records.Processes;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>From 0.9.4 (FIX-161): a launch binding hashes the file the start runs, found by the same program resolver.</summary>
public sealed class LaunchInventoryProgramTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("vericairn-inventory-program-").FullName;

    public void Dispose() => WorkspaceCleanup.Delete(root);

    [Fact]
    public void TheBindingHashesTheFileTheStartRunsWhateverTheHarnessEnvironmentsPathNames()
    {
        string name = OperatingSystem.IsWindows() ? "cmd" : "sh";
        string started = ProgramPath.Resolve(name);
        // A same-named program, and on Windows a script of that name, in a directory only the harness environment's PATH names.
        string decoys = Directory.CreateDirectory(Path.Combine(root, "decoys")).FullName;
        File.WriteAllText(Path.Combine(decoys, name), "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(decoys, name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else File.WriteAllText(Path.Combine(decoys, name + ".cmd"), "@exit /b 0\r\n");
        var environment = new Dictionary<string, string>(WorkerEnvironment.Names) { ["PATH"] = decoys, ["PATHEXT"] = ".CMD;.EXE" };
        var context = new LaunchContext(root, "condition", "brief", Path.Combine(root, "mcp.json"), [started],
            new Dictionary<string, string>(), new Dictionary<string, string>(), environment);
        Assert.NotEqual(ContentHash.Sha256File(started), ContentHash.Sha256File(Path.Combine(decoys, name)));

        Assert.Equal(started, HarnessInventory.Executable([name, "--version"]));
        Assert.Equal(HarnessInventory.Digest(new { executable = ContentHash.Sha256File(started), entries = Array.Empty<string>() }), HarnessInventory.Binary(context, [name]));
    }

    [Fact]
    public void ARelativeMissingOrUnresolvableExecutableIsRefused()
    {
        string name = OperatingSystem.IsWindows() ? "cmd" : "sh";
        Assert.Equal(ProgramPath.Resolve(name), HarnessInventory.Executable([ProgramPath.Resolve(name)]));
        ProviderRefusedException relative = Assert.Throws<ProviderRefusedException>(() => HarnessInventory.Executable([Path.Combine(".", name)]));
        Assert.Contains("must be absolute", relative.Message, StringComparison.Ordinal);
        Assert.Throws<ProviderRefusedException>(() => HarnessInventory.Executable([Path.Combine(root, "absent")]));
        Assert.Throws<ProviderRefusedException>(() => HarnessInventory.Executable(["vericairn-absent-" + Guid.NewGuid().ToString("N")]));
    }
}
