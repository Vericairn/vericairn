// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// What a worker run can reach (CODE-82): a planted marker outside the workspace and a loopback test host, read by a probe in the
/// harness's place; the secret-bearing paths the user can read, by path in the run's custody and by count and kind in its record.
/// </summary>
[Collection("home")]
public class HarnessReachTests
{
    [Fact]
    public void AProbeInTheHarnessPlaceReadsThePlantedMarkerAndReachesTheTestHost()
    {
        using var scratch = new Scratch();
        ReachMeasurement measured = HarnessReach.Measure(scratch.Plant, scratch.Workspace, scratch.Environment(), FakeHarness.ScopeCommand());
        Assert.Equal(new ReachMeasurement("readable", "reachable"), measured);
        // The marker is removed after the probe; nothing is left outside the workspace.
        Assert.Empty(Directory.EnumerateFileSystemEntries(scratch.Plant));
    }

    [Fact]
    public void ARefusedReadOrConnectionIsBlockedAndOnlyThenBlocked()
    {
        using var scratch = new Scratch();
        bool refusedRead = false;
        ReachMeasurement measured = HarnessReach.Measure(scratch.Plant, scratch.Workspace, scratch.Environment(), FakeHarness.ScopeCommand(), (marker, listener) =>
        {
            // The test host stops listening, as a sandbox's network denial would leave it unreachable.
            listener.Stop();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(marker, UnixFileMode.None);
                try { using FileStream opened = File.OpenRead(marker); }
                catch (UnauthorizedAccessException) { refusedRead = true; }
            }
        });
        Assert.Equal("blocked", measured.TestHost);
        // A user that the file's mode does not refuse (an administrator) still reads it: the probe reports what happened.
        Assert.Equal(refusedRead ? "blocked" : "readable", measured.OutsideFile);
    }

    [Fact]
    public void AProbeThatDoesNotRunOrReportIsUnknownNeverBlocked()
    {
        TestPrerequisites.RequireGit();
        using var scratch = new Scratch();
        // git takes no such argument and reports nothing the worker reads.
        Assert.Equal(ReachMeasurement.Unknown, HarnessReach.Measure(scratch.Plant, scratch.Workspace, scratch.Environment(), ["git"]));
        Assert.Equal(ReachMeasurement.Unknown, HarnessReach.Measure(scratch.Plant, scratch.Workspace, scratch.Environment(), [Path.Combine(scratch.Root, "no-such-probe")]));
        Assert.Empty(Directory.EnumerateFileSystemEntries(scratch.Plant));
    }

    [Theory]
    [InlineData("wrong", "unknown", "unknown")]
    [InlineData("refused", "blocked", "blocked")]
    [InlineData("extra", "unknown", "unknown")]
    [InlineData("garbled", "unknown", "unknown")]
    public void OnlyTheExactPlantedTokenIsReadableOrReachable(string report, string file, string host)
    {
        using var scratch = new Scratch();
        // The stand-in probe reports what the scenario says, never what it read: any other text than the planted token is unknown.
        ReachMeasurement measured = HarnessReach.Measure(scratch.Plant, scratch.Workspace, scratch.Environment(), [.. FakeHarness.ScopeCommand(), FakeHarness.ReachReport + report]);
        Assert.Equal(new ReachMeasurement(file, host), measured);
    }

    [Fact]
    public async Task ANamedPipeAtAConventionalPathDoesNotHoldTheInventory()
    {
        if (OperatingSystem.IsWindows()) return; // Named pipes live in their own namespace there, never at a home path.
        using var scratch = new Scratch();
        string pipe = Path.Combine(scratch.Home, ".netrc");
        using (var mkfifo = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", [pipe]) { UseShellExecute = false })!)
        {
            Assert.True(mkfifo.WaitForExit(TestTiming.HangTimeoutMilliseconds));
            Assert.Equal(0, mkfifo.ExitCode);
        }

        string key = scratch.File(".ssh/id_stand_in", "synthetic key material");
        // The inventory runs apart from the test, so a blocking open shows as the hang guard failing rather than a stuck test.
        Task<IReadOnlyList<SecretPath>> inventory = Task.Run(() => HarnessReach.SecretPaths(scratch.Environment(), out _), TestContext.Current.CancellationToken);
        IReadOnlyList<SecretPath> paths = await inventory.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.Contains(paths, p => p.Path == key);
    }

    [Fact]
    public void TheProbeReadsOnlyItsMarkerAndHostArguments()
    {
        Assert.Null(HarnessReach.TryRun(["status"]));
        Assert.Null(HarnessReach.TryRun([HarnessReach.Marker, "marker"]));
        Assert.Null(HarnessReach.TryRun([HarnessReach.Marker, "marker", "0"]));
        Assert.Null(HarnessReach.TryRun([HarnessReach.Marker, "marker", "65536"]));
        Assert.Null(HarnessReach.TryRun([HarnessReach.Marker, "marker", "-1"]));
    }

    [Fact]
    public void TheInventoryListsReadableSecretBearingPathsByPathAndKindAndReadsNoContent()
    {
        using var scratch = new Scratch();
        string ssh = scratch.File(".ssh/id_stand_in", "synthetic key material");
        scratch.File(".ssh/id_stand_in.pub", "public half");
        scratch.File(".ssh/known_hosts", "hosts");
        scratch.File(".ssh/config", "Host *");
        string aws = scratch.File(".aws/credentials", "synthetic cloud material");
        string npm = scratch.File(".npmrc", "synthetic registry material");
        string login = Path.Combine(scratch.Root, "harness-home", ".credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(login)!);
        File.WriteAllText(login, "synthetic login");
        string keys = Path.Combine(scratch.Root, "vericairn-home", "keys");
        Directory.CreateDirectory(keys);
        string key = Path.Combine(keys, "device.key");
        File.WriteAllText(key, "synthetic device key");
        string? unreadable = null;
        if (!OperatingSystem.IsWindows())
        {
            unreadable = scratch.File(".git-credentials", "synthetic forge material");
            File.SetUnixFileMode(unreadable, UnixFileMode.None);
            try { using FileStream opened = File.OpenRead(unreadable); unreadable = null; }
            catch (UnauthorizedAccessException) { }
        }

        Dictionary<string, string> environment = scratch.Environment();
        environment["CLAUDE_CONFIG_DIR"] = Path.GetDirectoryName(login)!;
        environment["VERICAIRN_HOME"] = Path.Combine(scratch.Root, "vericairn-home");
        IReadOnlyList<SecretPath> paths = HarnessReach.SecretPaths(environment, out bool complete);
        Assert.True(complete);
        Assert.Equal([new("ssh-key", ssh), new("cloud-credentials", aws), new("registry-credentials", npm), new("harness-login", login), new("device-key", key)],
            paths.OrderBy(p => Array.IndexOf([.. HarnessReach.Kinds], p.Kind)).ThenBy(p => p.Path, StringComparer.Ordinal).ToArray());
        if (unreadable is not null) Assert.DoesNotContain(paths, p => p.Path == unreadable);

        var record = HarnessReach.Record(new("readable", "reachable"), paths, complete);
        Assert.Equal(5, record["secretPaths"]!.GetValue<int>());
        Assert.Equal(["ssh-key", "cloud-credentials", "registry-credentials", "harness-login", "device-key"], record["secretKinds"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.DoesNotContain(scratch.Root, record.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInventoryWithoutAHomeSaysItIsIncomplete()
    {
        Assert.Empty(HarnessReach.SecretPaths(new Dictionary<string, string>(StringComparer.Ordinal) { ["VERICAIRN_HOME"] = Path.Combine(Path.GetTempPath(), "vr-" + Ulid.NewUlid().Value) }, out bool complete));
        Assert.False(complete);
    }

    [Fact]
    public void ARunRecordsWhatItCouldReachAndKeepsTheSecretPathsInItsCustodyByPathOnly()
    {
        using var fixture = new NativeRun("claude", recordFormat: 3);
        string home = fixture.Probe.Context.Environment["HOME"];
        string key = Path.Combine(home, ".ssh", "id_stand_in");
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        File.WriteAllText(key, "synthetic-reach-key-material");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);

        Records.Estate.AgentRuns.Run run = Records.Estate.AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!;
        JsonElement reach = run.Intent.Body.GetProperty("execution").GetProperty("reach");
        Assert.Equal("readable", reach.GetProperty("outsideFile").GetString());
        Assert.Equal("reachable", reach.GetProperty("testHost").GetString());
        Assert.True(reach.GetProperty("secretPaths").GetInt32() >= 2);
        string[] kinds = reach.GetProperty("secretKinds").EnumerateArray().Select(k => k.GetString()!).ToArray();
        Assert.Contains("ssh-key", kinds);
        // The run's own imported harness login is reachable by the harness too, and is counted.
        Assert.Contains("harness-login", kinds);
        Assert.DoesNotContain(home, run.Intent.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Run.Service.Check().Errors);

        var directory = new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run);
        // The list stays out of the run's directory, whose temporary directory and stop file the harness is told of.
        Assert.False(directory.ReachFile.StartsWith(directory.Path + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        string custody = File.ReadAllText(directory.ReachFile);
        using JsonDocument kept = JsonDocument.Parse(custody);
        Assert.Contains(kept.RootElement.GetProperty("secretPaths").EnumerateArray(), p => p.GetProperty("path").GetString() == key && p.GetProperty("kind").GetString() == "ssh-key");
        Assert.DoesNotContain("synthetic-reach-key-material", custody, StringComparison.Ordinal);
        Assert.Equal("readable", kept.RootElement.GetProperty("outsideFile").GetString());
    }

    [Fact]
    public void ARunBelowFormatThreeKeepsItsCustodyAndRecordsNoReach()
    {
        using var fixture = new NativeRun("claude");
        RunResponse started = fixture.Start();
        Assert.True(started.Ok, started.Message);
        Assert.Equal("observed-success", fixture.Run.Settled(started.Run).Status);
        Records.Estate.AgentRuns.Run run = Records.Estate.AgentRuns.Find(fixture.Run.Estate.Load(), started.Run)!;
        Assert.False(run.Intent.Body.GetProperty("execution").TryGetProperty("reach", out _));
        Assert.True(File.Exists(new RunDirectory(fixture.Run.Service.Session.Manifest.Estate, started.Run).ReachFile));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void QualificationRecordsWhetherTheMarkerAndTheTestHostWereBlocked(string harness)
    {
        using var probe = new NativeProbe(harness);
        using var run = new RunEstate(keepOperatorKey: true, recordFormat: 3);
        run.Service.Providers.Register(probe.Launcher);
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = probe.Context.Environment, Vericairn = probe.Context.McpServer };
        probe.Behavior("version", harness == "codex" ? "codex-cli 0.160.0" : "2.1.297 (Claude Code)");
        RecordWritten written = RunEstate.Ok(run.Service.QualifyHarness(new(probe.Launcher.Launcher, run.Estate.OperatorId, probe.Context.Settings)));
        RecordEnvelope record = run.Estate.Load().ById[written.Id];
        Assert.Equal(3, record.Format);
        JsonElement reach = record.Body.GetProperty("reach");
        Assert.Equal("readable", reach.GetProperty("file").GetString());
        Assert.Equal("reachable", reach.GetProperty("host").GetString());
        Assert.Equal(0, run.Service.Validate().Errors);
        // Qualifying a version the worker then runs still admits it: the reach is not an admission identity.
        Assert.False(File.Exists(probe.Report));
    }

    [Fact]
    public void AQualificationWhoseProbeCannotRunRecordsUnknownNotBlocked()
    {
        TestPrerequisites.RequireGit();
        using var probe = new NativeProbe("claude");
        using var run = new RunEstate(keepOperatorKey: true, recordFormat: 3);
        run.Service.Providers.Register(probe.Launcher);
        // git stands for a vericairn command that takes no probe argument: it runs and reports nothing the worker reads.
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = probe.Context.Environment, Vericairn = ["git"] };
        probe.Behavior("version", "2.1.297 (Claude Code)");
        RecordWritten written = RunEstate.Ok(run.Service.QualifyHarness(new(probe.Launcher.Launcher, run.Estate.OperatorId, probe.Context.Settings)));
        JsonElement reach = run.Estate.Load().ById[written.Id].Body.GetProperty("reach");
        Assert.Equal("unknown", reach.GetProperty("file").GetString());
        Assert.Equal("unknown", reach.GetProperty("host").GetString());
    }

    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Root = Path.Combine(Path.GetTempPath(), "vr-" + Ulid.NewUlid().Value);
            Plant = Directory.CreateDirectory(Path.Combine(Root, "run")).FullName;
            Workspace = Directory.CreateDirectory(Path.Combine(Root, "workspace")).FullName;
            Home = Directory.CreateDirectory(Path.Combine(Root, "user")).FullName;
        }

        public string Root { get; }
        public string Plant { get; }
        public string Workspace { get; }
        public string Home { get; }

        public Dictionary<string, string> Environment()
        {
            Dictionary<string, string> environment = WorkerEnvironment.Build(WorkerEnvironment.Current());
            environment["HOME"] = Home;
            environment["USERPROFILE"] = Home;
            environment["VERICAIRN_HOME"] = Path.Combine(Root, "no-vericairn-home");
            return environment;
        }

        /// <summary>Writes a file under the fixture home, the relative path's segments joined with this platform's separator.</summary>
        public string File(string relative, string content)
        {
            string path = Path.Combine([Home, .. relative.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            WorkspaceCleanup.Delete(Root);
        }
    }
}
