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

    // From 0.9.4 (FIX-165): a harness command that is a batch program, as a package manager installs one, answers its probe
    // through the resolver's start and runs with a launcher's kind of arguments through the containment primitive, which hands them on
    // with %* to the program it wraps; the binding hashes the script.
    [Fact]
    public async Task OnWindowsAHarnessCommandThatIsABatchProgramStartsAndItsBindingHashesTheScript()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A batch program runs on Windows only; the command it is given is checked on every platform.");
        string folder = Directory.CreateDirectory(Path.Combine(root, "npm dir")).FullName;
        string report = Path.Combine(root, "harness.jsonl");
        string script = BatchScript.Write(folder, "harness.cmd",
            "@echo off",
            "if \"%~1\"==\"--version\" (echo harness 1.2.3& exit /b 0)",
            BatchScript.Line(FakeHarness.Command("generic", report)) + " %*");
        List<string> command = HarnessProcess.Command(new Dictionary<string, string> { ["command"] = script }, "harness");
        Assert.Equal([script], command);

        (int code, string output, _) = HarnessProcess.Run([.. command, "--version"], WorkerEnvironment.Current(), TestTiming.HangTimeout, root);
        Assert.Equal(0, code);
        Assert.Equal("harness 1.2.3", output.Trim());

        string[] arguments = ["exec", "--json", "-c", "projects={\"C:\\\\work dir\"={trust_level=\"untrusted\"}}", "--cd", @"C:\work dir\", "/goal Make it so & more!"];
        var info = new System.Diagnostics.ProcessStartInfo(command[0]) { WorkingDirectory = root };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        ContainedProcess process = ContainedProcess.Start(info);
        try
        {
            Task drained = Task.WhenAll(process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken), process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
            Assert.True(process.WaitForExit(TestTiming.HangTimeout));
            Assert.Equal(0, process.ExitCode);
            await drained.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            process.Close();
        }

        Assert.Equal(arguments, Assert.Single(FakeHarness.Reports(report))["arguments"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Equal(script, HarnessInventory.Executable(command));
        Assert.Equal(HarnessInventory.Digest(new { executable = ContentHash.Sha256File(script), entries = Array.Empty<string>() }),
            HarnessInventory.Binary(new LaunchContext(root, "condition", "brief", Path.Combine(root, "mcp.json"), [script],
                new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>()), command));
    }
}
