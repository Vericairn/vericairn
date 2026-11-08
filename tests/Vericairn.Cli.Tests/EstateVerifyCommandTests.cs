// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Daemon;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Cli.Tests;

/// <summary>
/// estate verify (the estate's design/release-procedure.md § 7, the upgrade flow's runtime and verify steps): what serves the estate on
/// this machine against its pinned release, and the estate check with that runtime, each recorded as a verification for the pinned
/// release that the upgrade flow's steps and traps read; a runtime of an older release serving the estate fails the runtime check.
/// </summary>
[Collection("cli")]
public sealed class EstateVerifyCommandTests
{
    private static readonly string[] GitOptions = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public async Task AnOlderRuntimeServingTheEstateFailsTheRuntimeCheckAndThePinnedOnePassesIt()
    {
        CliHarness.RequireDaemonTransport();
        string root = CliHarness.TempDirectory("estate-verify");
        string scratch = CliHarness.TempDirectory("estate-verify-scratch");
        try
        {
            string release = UpgradeRuntimeJourneyTests.ClientRelease;
            (_, string builder) = UpgradeRuntimeJourneyTests.Publish(root, scratch, release, UpgradeRuntimeJourneyTests.ClientCommit);
            CliHarness.Ok(UpgradeRuntimeJourneyTests.Upgrade(root, release, builder));
            using (Vericairn.Runtime.Service.EstateService api = Vericairn.Runtime.Service.EstateService.Open(root))
            {
                string op = Records.Estate.EstateModel.BodyString(api.Session.Load().CurrentEstate!, "operator")!;
                foreach (string principal in new[] { op, builder })
                {
                    Vericairn.Protocol.WriteOutcome created = api.DeviceCreate(new(principal, "Verifying device", "file"));
                    Assert.True(created.Ok, created.Problem?.Message);
                    Vericairn.Protocol.WriteOutcome admitted = api.DeviceAdmit(new(created.Written!.Id, op, "Signs on this machine."));
                    Assert.True(admitted.Ok, admitted.Problem?.Message);
                }
            }

            string commit = Commit(root);

            // No daemon: this command line's own runtime, the pinned release's build, serves the estate here.
            CliHarness.Outcome verified = CliHarness.Run("--estate", root, "--json", "estate", "verify", "--by", builder);
            Assert.True(verified.Code == 0, verified.Out + verified.Err);
            JsonElement json = JsonDocument.Parse(verified.Out).RootElement;
            Assert.Equal((release, release, false, commit), (json.GetProperty("pinned").GetString(), json.GetProperty("served").GetString(), json.GetProperty("daemon").GetBoolean(), json.GetProperty("commit").GetString()));
            EstateModel model = EstateLoader.Load(root);
            RecordEnvelope runtime = Verification(model, EstateVerifyCommand.ServingRuntimeMethod);
            Assert.Equal(("passed", release, model.CurrentEstate!.Id), (EstateModel.BodyString(runtime, "outcome"), GoalPredicates.FieldString(runtime, "environment.release"), runtime.Subject!.Id));
            Assert.Equal("passed", EstateModel.BodyString(Verification(model, EstateVerifyCommand.EstateCheckMethod), "outcome"));

            // The machine is named by the signing device it holds, so a pass here supersedes only this machine's own verification.
            string device = GoalPredicates.FieldString(runtime, "environment.device")!;
            Assert.Equal(EstateModel.BodyString(model.ById[device], "principal"), builder);

            // A daemon of an older release serves the estate: the runtime check fails, naming it and the way to replace it, and the
            // estate is not checked with it.
            await using (DaemonHost older = await DaemonHost.StartAsync(root, cancellationToken: TestContext.Current.CancellationToken))
            {
                older.Service.ReportedVersion = "0.1.0";
                CliHarness.Outcome stale = CliHarness.Run("--estate", root, "estate", "verify", "--by", builder);
                Assert.Equal(1, stale.Code);
                Assert.Contains($"{EstateVerifyCommand.ServingRuntimeMethod}: failed: Vericairn 0.1.0 serves the estate on this machine, through the daemon serving it here", stale.Out, StringComparison.Ordinal);
                Assert.Contains($"the estate pins release {release}", stale.Out, StringComparison.Ordinal);
                Assert.Contains("runtime stop", stale.Out, StringComparison.Ordinal);
                Assert.Contains($"{EstateVerifyCommand.EstateCheckMethod}: failed: Not checked with the pinned release", stale.Out, StringComparison.Ordinal);
            }

            model = EstateLoader.Load(root);
            RecordEnvelope failed = Verification(model, EstateVerifyCommand.ServingRuntimeMethod);

            // No verification names a path of this machine: the estate's root, the daemon's address or a temporary directory.
            foreach (RecordEnvelope recorded in model.OfType("verification"))
            {
                string text = File.ReadAllText(Path.Combine(root, recorded.ExpectedPath));
                Assert.DoesNotContain(text.Split('\n'), line => SecretShapes.AbsolutePath().IsMatch(line));
                Assert.DoesNotContain(Path.GetFullPath(root), text, StringComparison.OrdinalIgnoreCase);
                Assert.False(PortableText.HasMachinePath(EstateModel.BodyString(recorded, "summary") + "\n" + string.Join("\n", recorded.Body.GetProperty("limitations").EnumerateArray().Select(l => l.GetString()))), text);
            }

            Assert.Equal(("failed", runtime.Id, "0.1.0"), (EstateModel.BodyString(failed, "outcome"), failed.Supersedes, GoalPredicates.FieldString(failed, "environment.served")));

            // What the upgrade flow's runtime trap reads: a failed check for the pinned release now stands, and the passed one does not.
            var trap = JsonDocument.Parse($$"""{"type":"verification","fields":{"method":"{{EstateVerifyCommand.ServingRuntimeMethod}}","environment.release":"{{release}}","outcome":"failed"},"max":0}""").RootElement;
            Assert.Equal("not-met", GoalPredicates.EvaluatePredicate(model, trap).Value);
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        }
        finally
        {
            CliHarness.Run("--estate", root, "runtime", "stop");
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public void AnEstateWithoutAReleasePinIsRefusedAndNothingIsRecorded()
    {
        string root = CliHarness.TempDirectory("estate-verify-source");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Verify.", "--operator-name", "Operator", "--organization", "Example", "--commit", new string('a', 40)).Json.GetProperty("operator").GetString()!;
            int before = EstateLoader.Load(root).Records.Count;
            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "estate", "verify", "--by", op);
            Assert.NotEqual(0, refused.Code);
            Assert.Contains("pinned to a published release", refused.Err, StringComparison.Ordinal);
            Assert.Equal(before, EstateLoader.Load(root).Records.Count);
            Assert.Contains("Stops and starts nothing", string.Join(' ', CliHarness.Run("estate", "verify", "--help").Out.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    private static RecordEnvelope Verification(EstateModel model, string method) =>
        Assert.Single(model.Current("verification"), v => EstateModel.BodyString(v, "method") == method);

    private static string Commit(string root)
    {
        Git(root, "init", "--initial-branch=main");
        Git(root, "add", "--all");
        Git(root, "commit", "-m", "Estate after its upgrade");
        return Git(root, "rev-parse", "HEAD").Trim();
    }

    private static string Git(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (string argument in GitOptions.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
