// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

/// <summary>
/// OPS-05: after an upgrade no runtime of an older release keeps serving the estate on this machine. The older runtime is a daemon
/// in this test process that reports an older release; the command line under test is the built one, run as an operator runs it,
/// so the daemon it starts in the older one's place is the pinned release's.
/// </summary>
[Collection("cli")]
public sealed class UpgradeRuntimeJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Product = """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";
    private const string Pinned = "50.0.0";

    internal static string ClientIdentity => typeof(VericairnClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    internal static string ClientRelease => ClientIdentity.Split('+')[0];

    // The source commit the build under test embeds; a build without one is pinned by its version alone.
    internal static string ClientCommit => ClientIdentity.Split('+') is [_, string commit] && commit.Length == 40 ? commit : Commit;

    private static string StartWord => OperatingSystem.IsWindows() ? @".\bootstrap.ps1 runtime start" : "./bootstrap runtime start";

    [Fact]
    public async Task AnUpgradeWithAnOlderRuntimeServingLeavesNoneServingIt()
    {
        CliHarness.RequireDaemonTransport();
        string root = CliHarness.TempDirectory("upgrade-runtime");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-scratch");
        try
        {
            string release = ClientRelease;
            (string op, string builder) = Publish(root, scratch, release, ClientCommit);
            await using DaemonHost older = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken);
            older.Service.ReportedVersion = "0.1.0";
            Assert.Equal("0.1.0", CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json.GetProperty("servingRelease").GetString());

            (int code, string output, string error) = await RunBuiltAsync([.. Upgrade(root, release, builder), "--json"]);
            try
            {
                Assert.True(code == 0, output + error);
                JsonElement upgrade = JsonDocument.Parse(output).RootElement;
                Assert.True(upgrade.GetProperty("applied").GetBoolean());
                JsonElement runtime = upgrade.GetProperty("runtime");
                Assert.True(runtime.GetProperty("outcome").GetString() == "replaced", output + error);
                Assert.Equal(Environment.ProcessId, runtime.GetProperty("served").GetProperty("processId").GetInt32());
                Assert.Equal("0.1.0", runtime.GetProperty("served").GetProperty("release").GetString());
                Assert.Equal(release, runtime.GetProperty("started").GetProperty("release").GetString());
                int started = runtime.GetProperty("started").GetProperty("processId").GetInt32();
                Assert.NotEqual(Environment.ProcessId, started);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("warning").ValueKind);

                // No runtime of the older release serves the estate: the pinned release's own does, from the command line that
                // upgraded it, with no mismatch to name.
                JsonElement status = CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json;
                Assert.Equal("daemon", status.GetProperty("mode").GetString());
                Assert.Equal(started, status.GetProperty("daemon").GetProperty("processId").GetInt32());
                Assert.Equal(release, status.GetProperty("servingRelease").GetString());
                Assert.Equal(release, status.GetProperty("pinnedRelease").GetString());
                Assert.Equal(JsonValueKind.Null, status.GetProperty("mismatch").ValueKind);
                string human = CliHarness.Ok("--estate", root, "runtime", "status").Out;
                Assert.Contains($"  release  Vericairn {release}, the release the estate pins", human, StringComparison.Ordinal);
                Assert.DoesNotContain("mismatch", human, StringComparison.Ordinal);

                // The upgrade's records and pin are its own, and the new daemon writes at once: nothing kept the working tree's
                // write lock across the stop and the start.
                Assert.Contains($"\"version\": \"{release}\"", File.ReadAllText(Path.Combine(root, "estate", "manifest.json")), StringComparison.Ordinal);
                Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "records", "migration"), "*.json", SearchOption.AllDirectories));
                CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "after.json", """{"kind":"agent","name":"After","roles":["contributor"],"agent":{"vendor":"Example","product":"Example Agent"}}"""));
                Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
            }
            finally
            {
                CliHarness.Run("--estate", root, "runtime", "stop");
            }

            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public async Task ACommandLineOfAnotherReleaseStopsTheOlderRuntimeAndNamesThePinnedStart()
    {
        CliHarness.RequireDaemonTransport();
        string root = CliHarness.TempDirectory("upgrade-runtime-other");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-other-scratch");
        try
        {
            (_, string builder) = Publish(root, scratch, Pinned);
            await using DaemonHost older = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken);
            older.Service.ReportedVersion = "0.1.0";

            CliHarness.Outcome upgraded = CliHarness.Run(Upgrade(root, Pinned, builder));
            Assert.True(upgraded.Code == 0, upgraded.Out + upgraded.Err);
            Assert.Contains("upgraded source", upgraded.Out, StringComparison.Ordinal);
            Assert.Contains($"runtime: stopped daemon {Environment.ProcessId} of Vericairn 0.1.0, older than the estate's pin, release {Pinned}", upgraded.Out, StringComparison.Ordinal);
            Assert.Contains($"warning: this command line is Vericairn {ClientRelease}, not the pinned release {Pinned}, so it started no runtime", upgraded.Err, StringComparison.Ordinal);
            Assert.Contains($"'{StartWord}' in the estate's directory, which runs release {Pinned}", upgraded.Err, StringComparison.Ordinal);
            Assert.Contains($"runtime start' with Vericairn {Pinned}'s command line", upgraded.Err, StringComparison.Ordinal);

            // None serves the estate, and none was started in the older one's place.
            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
            Assert.Contains($"\"version\": \"{Pinned}\"", File.ReadAllText(Path.Combine(root, "estate", "manifest.json")), StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public async Task ABuildOfThePinnedVersionFromAnotherCommitStartsNoRuntime()
    {
        CliHarness.RequireDaemonTransport();
        string root = CliHarness.TempDirectory("upgrade-runtime-commit");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-commit-scratch");
        try
        {
            string other = ClientCommit == Commit ? new string('f', 40) : Commit;
            (_, string builder) = Publish(root, scratch, ClientRelease, other);
            await using DaemonHost older = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken);
            older.Service.ReportedVersion = "0.1.0";

            CliHarness.Outcome upgraded = CliHarness.Run([.. Upgrade(root, ClientRelease, builder), "--json"]);
            Assert.True(upgraded.Code == 0, upgraded.Out + upgraded.Err);
            JsonElement runtime = upgraded.Json.GetProperty("runtime");
            Assert.Equal("stopped", runtime.GetProperty("outcome").GetString());
            Assert.Equal($"stopped daemon {Environment.ProcessId} of Vericairn 0.1.0, older than the estate's pin, release {ClientRelease}", runtime.GetProperty("done").GetString());
            Assert.Contains($"this command line is a build of Vericairn {ClientRelease} from commit ", runtime.GetProperty("warning").GetString(), StringComparison.Ordinal);
            Assert.Contains($"not the pinned release's commit {other}, so it started no runtime", runtime.GetProperty("warning").GetString(), StringComparison.Ordinal);
            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Theory]
    [InlineData("refuses")]
    [InlineData("ignores")]
    public async Task AnOlderRuntimeThatCannotBeStoppedIsNamedWithWhatToRunAndTheUpgradeStands(string daemon)
    {
        CliHarness.RequireDaemonTransport();
        string root = CliHarness.TempDirectory("upgrade-runtime-stuck");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-stuck-scratch");
        Func<VericairnClient, CancellationToken, Task> ask = DaemonLifecycle.AskToStop;
        TimeSpan wait = DaemonLifecycle.StopWait;
        try
        {
            (_, string builder) = Publish(root, scratch, Pinned);
            await using DaemonHost older = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken);
            older.Service.ReportedVersion = "0.1.0";

            // Another user's daemon refuses the request; a stuck one accepts it and keeps answering.
            DaemonLifecycle.AskToStop = daemon == "refuses"
                ? (_, _) => throw new ClientRefusedException("Response status code does not indicate success: 403 (Forbidden).", statusCode: HttpStatusCode.Forbidden)
                : (_, _) => Task.CompletedTask;
            DaemonLifecycle.StopWait = TimeSpan.FromSeconds(1);
            CliHarness.Outcome upgraded = CliHarness.Run([.. Upgrade(root, Pinned, builder), "--json"]);
            DaemonLifecycle.AskToStop = ask;
            DaemonLifecycle.StopWait = wait;

            Assert.True(upgraded.Code == 0, upgraded.Out + upgraded.Err);
            JsonElement runtime = upgraded.Json.GetProperty("runtime");
            Assert.True(upgraded.Json.GetProperty("applied").GetBoolean());
            Assert.Equal("not-stopped", runtime.GetProperty("outcome").GetString());
            string warning = runtime.GetProperty("warning").GetString()!;
            Assert.Contains($"daemon {Environment.ProcessId} of Vericairn 0.1.0, older than the estate's pin, release {Pinned}, still serves this estate", warning, StringComparison.Ordinal);
            Assert.Contains(daemon == "refuses" ? "403 (Forbidden)" : "it still answered a second after it was asked to stop", warning, StringComparison.Ordinal);
            Assert.Contains("The upgrade is applied.", warning, StringComparison.Ordinal);
            Assert.Contains($"run 'vericairn --estate {ServingRuntime.Quote(Path.GetFullPath(root))} runtime stop' with Vericairn 0.1.0's command line or this one", warning, StringComparison.Ordinal);
            Assert.Contains($"end process {Environment.ProcessId}", warning, StringComparison.Ordinal);
            Assert.Contains(StartWord, warning, StringComparison.Ordinal);
            Assert.Contains("warning: " + warning, upgraded.Err, StringComparison.Ordinal);
            Assert.Contains($"\"version\": \"{Pinned}\"", File.ReadAllText(Path.Combine(root, "estate", "manifest.json")), StringComparison.Ordinal);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "records", "migration"), "*.json", SearchOption.AllDirectories));

            // Status and runtime status name the mismatch while it stands.
            JsonElement status = CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json;
            Assert.Equal("0.1.0", status.GetProperty("servingRelease").GetString());
            Assert.Equal($"release {Pinned}", status.GetProperty("pin").GetString());
            Assert.Equal(Pinned, status.GetProperty("pinnedRelease").GetString());
            Assert.StartsWith($"the serving runtime is Vericairn 0.1.0, older than the estate's pin, release {Pinned}", status.GetProperty("mismatch").GetString(), StringComparison.Ordinal);
            string human = CliHarness.Ok("--estate", root, "runtime", "status").Out;
            Assert.Contains($"  release  Vericairn 0.1.0; the estate pins release {Pinned}", human, StringComparison.Ordinal);
            Assert.Contains($"  mismatch: the serving runtime is Vericairn 0.1.0, older than the estate's pin, release {Pinned}", human, StringComparison.Ordinal);
            Assert.Contains("Stop it with 'vericairn --estate ", human, StringComparison.Ordinal);

            CliHarness.Outcome estate = CliHarness.Ok("--estate", root, "status");
            Assert.Contains($"Serving runtime (this machine): daemon {Environment.ProcessId}, Vericairn 0.1.0; the estate pins release {Pinned}.", estate.Out, StringComparison.Ordinal);
            Assert.Contains($"Mismatch: the serving runtime is Vericairn 0.1.0, older than the estate's pin, release {Pinned}", estate.Out, StringComparison.Ordinal);
            CliHarness.Outcome canonical = CliHarness.Ok("--estate", root, "--json", "status");
            Assert.Equal("release", canonical.Json.GetProperty("vericairn").GetProperty("mode").GetString());
            Assert.DoesNotContain("Serving runtime", canonical.Out, StringComparison.Ordinal);
            Assert.Contains($"warning: the serving runtime is Vericairn 0.1.0, older than the estate's pin, release {Pinned}", canonical.Err, StringComparison.Ordinal);

            Assert.Equal(0, CliHarness.Run("--estate", root, "runtime", "stop").Code);
            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            DaemonLifecycle.AskToStop = ask;
            DaemonLifecycle.StopWait = wait;
            CliHarness.Delete(root, scratch);
        }
    }

    [Theory]
    [InlineData("99.0.0", "kept")]
    [InlineData(Pinned, "pinned")]
    public async Task ARuntimeOfThePinnedOrANewerReleaseIsNeverTouched(string serving, string outcome)
    {
        CliHarness.RequireDaemonTransport();
        string root = CliHarness.TempDirectory("upgrade-runtime-newer");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-newer-scratch");
        try
        {
            (_, string builder) = Publish(root, scratch, Pinned);
            await using DaemonHost daemon = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken);
            daemon.Service.ReportedVersion = serving;

            CliHarness.Outcome upgraded = CliHarness.Run([.. Upgrade(root, Pinned, builder), "--json"]);
            Assert.True(upgraded.Code == 0, upgraded.Out + upgraded.Err);
            JsonElement runtime = upgraded.Json.GetProperty("runtime");
            Assert.Equal(outcome, runtime.GetProperty("outcome").GetString());
            Assert.Equal(serving, runtime.GetProperty("served").GetProperty("release").GetString());
            Assert.Equal(JsonValueKind.Null, runtime.GetProperty("started").ValueKind);
            if (outcome == "kept")
            {
                Assert.StartsWith($"the serving runtime is Vericairn 99.0.0, newer than the estate's pin, release {Pinned}", runtime.GetProperty("warning").GetString(), StringComparison.Ordinal);
                Assert.Contains("warning: the serving runtime is Vericairn 99.0.0, newer than the estate's pin", upgraded.Err, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal($"daemon {Environment.ProcessId} serves this estate with Vericairn {Pinned}, the release it pins", runtime.GetProperty("done").GetString());
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("warning").ValueKind);
            }

            // The daemon still serves, untouched, and status shows its release against the pin.
            JsonElement status = CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json;
            Assert.Equal(Environment.ProcessId, status.GetProperty("daemon").GetProperty("processId").GetInt32());
            Assert.Equal(serving, status.GetProperty("servingRelease").GetString());
            Assert.Equal(outcome == "pinned", status.GetProperty("mismatch").ValueKind == JsonValueKind.Null);
            string estate = CliHarness.Ok("--estate", root, "status").Out;
            Assert.Contains(outcome == "pinned"
                ? $"Serving runtime (this machine): daemon {Environment.ProcessId}, Vericairn {Pinned}, the release the estate pins."
                : $"Serving runtime (this machine): daemon {Environment.ProcessId}, Vericairn 99.0.0; the estate pins release {Pinned}.", estate, StringComparison.Ordinal);
            Assert.Equal(outcome == "kept", estate.Contains("Mismatch: ", StringComparison.Ordinal));

            CliHarness.Outcome text = CliHarness.Run(Upgrade(root, Pinned, builder));
            Assert.Equal(0, text.Code);
            if (outcome == "pinned") Assert.Contains($"runtime: daemon {Environment.ProcessId} serves this estate with Vericairn {Pinned}, the release it pins", text.Out, StringComparison.Ordinal);
            else Assert.DoesNotContain("runtime:", text.Out, StringComparison.Ordinal);
            Assert.Equal(Environment.ProcessId, CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json.GetProperty("daemon").GetProperty("processId").GetInt32());
            Assert.Equal(0, CliHarness.Run("--estate", root, "runtime", "stop").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AnUpgradeWithNoRuntimeServingStartsNoneAndACheckLeavesTheRuntimeAlone()
    {
        string root = CliHarness.TempDirectory("upgrade-runtime-none");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-none-scratch");
        try
        {
            (_, string builder) = Publish(root, scratch, Pinned);
            JsonElement check = CliHarness.Ok("--estate", root, "--json", "estate", "upgrade", "--to", Pinned, "--check").Json;
            Assert.False(check.TryGetProperty("runtime", out _));

            CliHarness.Outcome upgraded = CliHarness.Run([.. Upgrade(root, Pinned, builder), "--json"]);
            Assert.True(upgraded.Code == 0, upgraded.Out + upgraded.Err);
            JsonElement runtime = upgraded.Json.GetProperty("runtime");
            Assert.Equal("none", runtime.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, runtime.GetProperty("served").ValueKind);
            Assert.Equal(JsonValueKind.Null, runtime.GetProperty("started").ValueKind);
            Assert.DoesNotContain("warning:", upgraded.Err, StringComparison.Ordinal);
            Assert.Contains("no daemon", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);
            JsonElement status = CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json;
            Assert.Equal(JsonValueKind.Null, status.GetProperty("servingRelease").ValueKind);
            Assert.Equal(Pinned, status.GetProperty("pinnedRelease").GetString());
            Assert.DoesNotContain("Serving runtime", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void TheServingRuntimeIsComparedWithThePinOnlyWhenItServesThisEstate()
    {
        string root = CliHarness.TempDirectory("upgrade-runtime-relation");
        string scratch = CliHarness.TempDirectory("upgrade-runtime-relation-scratch");
        try
        {
            (_, string builder) = Publish(root, scratch, Pinned);
            Assert.Equal(0, CliHarness.Run(Upgrade(root, Pinned, builder)).Code);
            string estate = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "estate", "manifest.json"))).RootElement.GetProperty("estate").GetString()!;
            var endpoint = new DaemonEndpoint("unix", "socket", 4242, "now", root, ApiVersion.Current);
            ServingRuntime Serving(string release, string? estateId = null, string? at = null) =>
                ServingRuntime.Of(root, endpoint, new HealthResponse(ApiVersion.Current, release, estateId ?? estate, at ?? Path.GetFullPath(root), "repository-synced", 3, "now", 4242));

            Assert.Equal("pinned", Serving(Pinned + "+build.7").Relation);
            Assert.Null(Serving(Pinned).Mismatch());
            Assert.Equal("older", Serving("49.9.9").Relation);
            Assert.Equal("older", Serving(Pinned + "-rc.1").Relation);
            Assert.Equal("newer", Serving("50.0.1").Relation);
            Assert.Equal("unordered", Serving("unknown").Relation);
            Assert.StartsWith("the serving runtime reports Vericairn unknown, which cannot be ordered against the estate's pin", Serving("unknown").Mismatch(), StringComparison.Ordinal);

            // Another estate's daemon, or this estate's at another root, is never compared and never stopped.
            ServingRuntime other = Serving("0.1.0", estateId: "01M00000000000000000000000");
            Assert.Equal("other-estate", other.Relation);
            Assert.Contains("serves estate 01M00000000000000000000000", other.Mismatch(), StringComparison.Ordinal);
            Assert.Contains("it was left running", other.Mismatch(), StringComparison.Ordinal);
            Assert.Equal("other-estate", Serving("0.1.0", at: scratch).Relation);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    internal static string[] Upgrade(string root, string release, string builder) =>
        ["--estate", root, "estate", "upgrade", "--to", release, .. release.Contains('-', StringComparison.Ordinal) ? new[] { "--channel", "prerelease" } : [], "--by", builder];

    /// <summary>An estate with a published release of the version, ready to upgrade to it, as the self-hosting loop records one.</summary>
    internal static (string Operator, string Builder) Publish(string root, string scratch, string version, string commit = Commit)
    {
        string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Upgrade runtime.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
        string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
        string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
        CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
        string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;
        string channel = version.Contains('-', StringComparison.Ordinal) ? "\"channel\":\"prerelease\"," : string.Empty;
        string candidateBody = $$"""{"version":"{{version}}",{{channel}}"repository":"Example/example","commit":"{{commit}}","status":"candidate","artifacts":[{"name":"vericairn-{{version}}-linux-x64.tar.gz","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"},{"name":"vericairn-{{version}}-osx-arm64.tar.gz","hash":"sha256:{{new string('2', 64)}}","bytes":10,"platform":"osx-arm64"},{"name":"vericairn-{{version}}-win-x64.zip","hash":"sha256:{{new string('3', 64)}}","bytes":10,"platform":"win-x64"},{"name":"SHA256SUMS","hash":"sha256:{{new string('4', 64)}}","bytes":3,"platform":"any"}],"recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[{"summary":"Tests passed on three operating systems."}],"qualified":["linux-x64","osx-arm64","win-x64"],"signing":"unsigned developer build","limitations":["No signing material yet."]}""";
        CliHarness.Outcome candidate = CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", builder, "--subject", $"product:{product}", "--body", CliHarness.WriteJson(scratch, "candidate.json", candidateBody));
        string publishedAt = Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddMinutes(5));
        string publishedBody = candidateBody.Replace("\"status\":\"candidate\"", $"\"status\":\"published\",\"publishedAt\":\"{publishedAt}\",\"url\":\"https://example.invalid/releases/{version}\"", StringComparison.Ordinal);
        CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate.Id}:{candidate.Hash}", "--body", CliHarness.WriteJson(scratch, "accept.json", $$"""{"statement":"Accepted release {{version}} from the recorded evidence.","basis":[],"self":false}"""));
        CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", builder, "--subject", $"product:{product}", "--supersedes", candidate.Id, "--body", CliHarness.WriteJson(scratch, "published.json", publishedBody));
        return (op, builder);
    }

    /// <summary>The built command line as a separate process, as an operator runs it; its output ends when it exits, whatever daemon it starts.</summary>
    private static async Task<(int Code, string Out, string Err)> RunBuiltAsync(string[] arguments)
    {
        var info = new ProcessStartInfo(CliHarness.BuiltExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(await TestTiming.CompletesAsync(Task.WhenAll(output, error), TestContext.Current.CancellationToken), "The upgrade's output did not end before the hang guard.");
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }
}
