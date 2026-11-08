// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Writing and adopting history checkpoints at the runtime's boundary (design/signed-history.md §§ 7, 9 and 10; OPS-07): adoption at birth
/// and as the operator's step, each trigger writing exactly one checkpoint signed by the device then writing, a trigger that cannot write
/// never blocking its act, and the machine's memory of the newest checkpoint it verified.
/// </summary>
public class HistoryTriggerTests
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void ANewEstateAdoptsAtBirthWhenItsRootOperatorsFirstDeviceAdmitsItself()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-history-birth-" + Ulid.NewUlid().Value);
        try
        {
            (RecordEnvelope op, RecordEnvelope charter) = EstateInitializer.Initialize(root, new EstateInitialization("Born", "born", "Adopt at birth.", "Operator", "Org", null, "Vericairn/vericairn", new string('a', 40)));
            using EstateService api = EstateService.Open(root);
            RecordWritten device = Ok(api.DeviceCreate(new(op.Id, "Operator's device", "file")));
            WriteOutcome admitted = api.DeviceAdmit(new AdmitRequest(device.Id, op.Id, "The first authority."));
            Assert.True(admitted.Ok, JsonSerializer.Serialize(admitted.Problem, ProtocolJson.Options));
            Assert.Equal("adoption", admitted.Checkpoint!.Trigger);
            Assert.Null(admitted.Checkpoint.Skipped);

            EstateModel model = api.Session.Load();
            Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
            RecordEnvelope adoption = Assert.Single(model.OfType("history-checkpoint"));
            Assert.Equal(admitted.Checkpoint.Written!.Id, adoption.Id);
            Assert.True(SignedHistory.Adopted(model));
            Assert.Equal(op.Id, adoption.By);
            Assert.Equal(device.Id, Assert.Single(adoption.Signatures).Device);
            // It witnesses everything estate init wrote and the first authority, and anchors what init wrote without a signature.
            Assert.Equal(model.Records.Count - 1, SignedHistory.Of(model).Witnessed);
            Assert.Equal(new[] { op.Id, charter.Id }.Order(StringComparer.Ordinal), adoption.Body.GetProperty("anchored").EnumerateArray().Select(a => a.GetProperty("id").GetString()));

            (HistoryCheckpointResponse? again, Problem? refused) = api.HistoryCheckpoint(new HistoryCheckpointRequest(op.Id, Adopt: true));
            Assert.Null(again);
            Assert.Contains("adopted history checkpoints already", refused!.Message, StringComparison.Ordinal);
        }
        finally { Delete(root); }
    }

    [Fact]
    public void AnEstateHoldingMoreThanItsBirthAdoptsOnlyAsTheOperatorsStep()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-history-grown-" + Ulid.NewUlid().Value);
        try
        {
            (RecordEnvelope op, _) = EstateInitializer.Initialize(root, new EstateInitialization("Grown", "grown", "Adopt later.", "Operator", "Org", null, "Vericairn/vericairn", new string('a', 40)));
            using EstateService api = EstateService.Open(root);
            Ok(api.NewRecord(new NewRecordRequest("work-order", op.Id, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("GROWN-1"))!))));
            RecordWritten device = Ok(api.DeviceCreate(new(op.Id, "Operator's device", "file")));
            WriteOutcome admitted = api.DeviceAdmit(new AdmitRequest(device.Id, op.Id, "The first authority."));
            Assert.True(admitted.Ok);
            Assert.Null(admitted.Checkpoint);
            Assert.False(SignedHistory.Adopted(api.Session.Load()));
            Assert.DoesNotContain(api.Session.Load().Records, r => r.Type == "history-checkpoint");
        }
        finally { Delete(root); }
    }

    [Fact]
    public void AdoptionIsTheRootOperatorsStepNeverAnAgentsAndItsCheckListsWhatItWouldAnchor()
    {
        using var f = new Fixture(adopt: false);
        EstateModel before = f.Api.Session.Load();
        (HistoryCheckpointResponse? manual, Problem? beforeAdoption) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.BuilderId));
        Assert.Null(manual);
        Assert.Contains("only its adoption writes one", beforeAdoption!.Message, StringComparison.Ordinal);

        (HistoryCheckpointResponse? plan, Problem? planProblem) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(null, Adopt: true, Check: true));
        Assert.Null(planProblem);
        Assert.False(plan!.Applied);
        Assert.Equal("adoption", plan.Trigger);
        Assert.Equal(before.Records.Count, plan.Witnessed);
        Assert.Equal(before.Records.Where(r => r.Signatures.Count == 0).Select(r => r.Id).Order(StringComparer.Ordinal), plan.Anchored.Select(a => a.Id));
        Assert.All(plan.Anchored, a => Assert.Equal(before.ById[a.Id].ExpectedPath, a.Path));
        Assert.Empty(plan.ByRevokedDevices);
        Assert.Contains("git adapter", plan.Unavailable, StringComparison.Ordinal);
        Assert.Equal(before.Records.Count, f.Api.Session.Load().Records.Count);

        // An agent is refused before anything is signed, and nothing is written.
        (HistoryCheckpointResponse? agents, Problem? refused) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.BuilderId, Adopt: true));
        Assert.Null(agents);
        Assert.Contains("never an agent", refused!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Api.Session.Load().Records, r => r.Type == "history-checkpoint");

        (HistoryCheckpointResponse? adopted, Problem? problem) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.OperatorId, Adopt: true));
        Assert.Null(problem);
        Assert.True(adopted!.Applied);
        Assert.True(adopted.Adopted);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        RecordEnvelope adoption = Assert.Single(model.OfType("history-checkpoint"));
        Assert.Equal(adopted.Written!.Id, adoption.Id);
        Assert.Equal("adoption", adoption.Body.GetProperty("trigger").GetString());
        Assert.Equal(f.OperatorDevice, Assert.Single(adoption.Signatures).Device);
        Assert.Equal(plan.Anchored.Select(a => a.Id), adoption.Body.GetProperty("anchored").EnumerateArray().Select(a => a.GetProperty("id").GetString()));

        // Afterwards any admitted device, an agent's included, may write a checkpoint; witnessing grants nothing.
        (HistoryCheckpointResponse? witnessed, Problem? witnessProblem) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.BuilderId));
        Assert.Null(witnessProblem);
        RecordEnvelope written = f.Api.Session.Load().ById[witnessed!.Written!.Id];
        Assert.Equal("manual", written.Body.GetProperty("trigger").GetString());
        Assert.False(written.Body.TryGetProperty("anchored", out _));
        Assert.Equal(f.BuilderDevice, Assert.Single(written.Signatures).Device);
    }

    [Fact]
    public void ACharterRevisionWritesOneCheckpointInItsBatchSignedByTheDeviceThenWriting()
    {
        using var f = new Fixture();
        int checkpoints = Checkpoints(f).Count;
        WriteOutcome revised = f.Api.NewRecord(new NewRecordRequest("estate", f.Records.Estate.OperatorId, PolicyFixture.Element(Charter(f, "Exercise the record format, revised.")), Supersedes: f.Records.Estate.Estate.Id));
        Assert.True(revised.Ok, JsonSerializer.Serialize(revised.Problem, ProtocolJson.Options));
        Assert.Equal("charter", revised.Checkpoint!.Trigger);

        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(checkpoints + 1, Checkpoints(f).Count);
        RecordEnvelope checkpoint = model.ById[revised.Checkpoint.Written!.Id];
        Assert.Equal(checkpoint.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);
        Assert.Equal(f.OperatorDevice, Assert.Single(checkpoint.Signatures).Device);
        Assert.True(SignedHistory.Of(model).IsWitnessed(revised.Written!.Id));
        Assert.Equal(1, SignedHistory.Of(model).Tail);
        Assert.False(checkpoint.Body.TryGetProperty("anchored", out _));
    }

    [Fact]
    public void ARevisionToRequiredSignaturesAnchorsTheUnsignedRecordsThatCountInTheSameBatch()
    {
        using var f = new Fixture();
        // Recorded without a signature under the optional charter, after adoption: it counts, and no anchor lists it yet.
        RecordEnvelope unsigned = f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("REQ-1"))!.AsObject(), sign: false);
        Assert.True(f.Api.Session.Load().Counts(f.Api.Session.Load().ById[unsigned.Id]));
        JsonObject required = Charter(f, "Exercise the record format.");
        required["policies"]!["signatures"] = "required";
        WriteOutcome revised = f.Api.NewRecord(new NewRecordRequest("estate", f.Records.Estate.OperatorId, PolicyFixture.Element(required), Supersedes: f.Records.Estate.Estate.Id));
        Assert.True(revised.Ok, JsonSerializer.Serialize(revised.Problem, ProtocolJson.Options));
        Assert.Equal("charter", revised.Checkpoint!.Trigger);

        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        RecordEnvelope checkpoint = model.ById[revised.Checkpoint.Written!.Id];
        Assert.Equal([unsigned.Id], checkpoint.Body.GetProperty("anchored").EnumerateArray().Select(a => a.GetProperty("id").GetString()));
        Assert.True(model.Counts(model.ById[unsigned.Id]));
        Assert.Equal(f.OperatorDevice, Assert.Single(checkpoint.Signatures).Device);
    }

    [Fact]
    public void AnIntervalCheckpointIsWrittenOnceWhenDueAndAPersonsDeviceWithPresenceIsNeverAsked()
    {
        using var f = new Fixture();
        // A record dated more than a day ago puts the tail's oldest record past the interval.
        f.Records.Estate.SetClock(DateTimeOffset.UtcNow.AddDays(-2));
        f.Records.Add("work-order", JsonNode.Parse(HistoryFixture.Order("OLD-1"))!.AsObject());
        Assert.True(SignedHistory.IntervalDue(f.Api.Session.Load(), DateTimeOffset.UtcNow));

        // A person whose only device here needs presence writes; nobody is asked for an interval checkpoint.
        string person = f.Records.Add("principal", new JsonObject { ["kind"] = "human", ["name"] = "Second person", ["roles"] = new JsonArray("operator") }).Id;
        string presence = f.PresenceDevice(person);
        int checkpoints = Checkpoints(f).Count;
        WriteOutcome byPerson = f.Api.NewRecord(new NewRecordRequest("work-order", person, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("INT-1"))!)));
        Assert.True(byPerson.Ok, JsonSerializer.Serialize(byPerson.Problem, ProtocolJson.Options));
        Assert.Equal(presence, Assert.Single(f.Api.Session.Load().ById[byPerson.Written!.Id].Signatures).Device);
        Assert.Null(byPerson.Checkpoint);
        Assert.Equal(checkpoints, Checkpoints(f).Count);

        // The agent's next write carries it, signed by the agent's device, and the one after it carries none.
        WriteOutcome byAgent = f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.BuilderId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("INT-2"))!)));
        Assert.True(byAgent.Ok, JsonSerializer.Serialize(byAgent.Problem, ProtocolJson.Options));
        Assert.Equal("interval", byAgent.Checkpoint!.Trigger);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        RecordEnvelope interval = model.ById[byAgent.Checkpoint.Written!.Id];
        Assert.Equal(f.BuilderDevice, Assert.Single(interval.Signatures).Device);
        Assert.False(interval.Body.TryGetProperty("anchored", out _));
        Assert.True(SignedHistory.Of(model).IsWitnessed(byAgent.Written!.Id));
        Assert.Equal(checkpoints + 1, Checkpoints(f).Count);

        WriteOutcome next = f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.BuilderId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("INT-3"))!)));
        Assert.True(next.Ok);
        Assert.Null(next.Checkpoint);
        Assert.Equal(checkpoints + 1, Checkpoints(f).Count);
    }

    [Fact]
    public void ATriggerThatCannotWriteItsCheckpointNeverBlocksItsActAndSaysWhy()
    {
        using var f = new Fixture();
        int checkpoints = Checkpoints(f).Count;
        f.ForgetOperatorDevice();
        WriteOutcome revised = f.Api.NewRecord(new NewRecordRequest("estate", f.Records.Estate.OperatorId, PolicyFixture.Element(Charter(f, "Exercise the record format, without a device here.")), Supersedes: f.Records.Estate.Estate.Id));
        Assert.True(revised.Ok, JsonSerializer.Serialize(revised.Problem, ProtocolJson.Options));
        Assert.Equal("charter", revised.Checkpoint!.Trigger);
        Assert.Null(revised.Checkpoint.Written);
        Assert.Contains("no admitted device whose key this machine holds", revised.Checkpoint.Skipped, StringComparison.Ordinal);
        Assert.Equal(checkpoints, Checkpoints(f).Count);
        Assert.True(f.Api.Session.Load().IsCurrent(f.Api.Session.Load().ById[revised.Written!.Id]));
    }

    [Fact]
    public void TheReleaseGateWritesACheckpointBeforeTheSubmissionCheckAndTheCheckWritesNothing()
    {
        using var f = new Fixture(adopt: false);
        RecordEnvelope product = f.Records.Add("product", ReleaseLineFixture.ProductBody());
        var provider = new FakeReleaseProvider();
        f.Api.Providers = new ProviderRegistry();
        f.Api.Providers.Register(provider);
        ReleaseGateSource.Prepare(f.Api, "Example/widget", provider.HeadSha);
        ReleaseSubmitRequest Request() => new(product.Id, "1.1.0", "Example/widget", f.Records.Estate.OperatorId,
            Provider: provider.Provider, Commit: provider.HeadSha, Expected: product.ComputedHash) { ClientBuild = EstateService.Version };

        // Before adoption no release checkpoint is asked.
        Assert.Null(f.Api.ReleaseSubmit(Request() with { Version = "1.0.9" }).Checkpoint);
        Assert.Empty(Checkpoints(f));

        f.Adopt();
        int checkpoints = Checkpoints(f).Count;
        ReleaseResponse checkedRelease = f.Api.ReleaseCheck(Request());
        Assert.Null(checkedRelease.Checkpoint);
        Assert.Equal(checkpoints, Checkpoints(f).Count);

        ReleaseResponse submitted = f.Api.ReleaseSubmit(Request());
        Assert.Equal("release", submitted.Checkpoint!.Trigger);
        EstateModel model = f.Api.Session.Load();
        RecordEnvelope gate = model.ById[submitted.Checkpoint.Written!.Id];
        Assert.Equal(checkpoints + 1, Checkpoints(f).Count);
        Assert.Equal("release", gate.Body.GetProperty("trigger").GetString());
        Assert.Equal(f.OperatorDevice, Assert.Single(gate.Signatures).Device);
        // Written before the check, so the intent the submission recorded follows it.
        if (submitted.Operation is { } intent) Assert.False(SignedHistory.Of(model).IsWitnessed(intent.Id));

        // Where the checkpoint cannot be written, the submission is refused and says so.
        f.ForgetOperatorDevice();
        ReleaseResponse refused = f.Api.ReleaseSubmit(Request() with { Version = "1.2.0" });
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, x => x.Code == "history.checkpoint");
        Assert.Null(refused.Checkpoint!.Written);
    }

    [Fact]
    public void AnUpgradeWritesOneCheckpointInTheSameBatchAsItsMigration()
    {
        using var f = new Fixture();
        Publish(f.Records, "0.9.2");
        int checkpoints = Checkpoints(f).Count;
        (UpgradeResponse? applied, Problem? problem) = f.Api.Upgrade(new UpgradeRequest("0.9.2", f.Records.Estate.OperatorId, false));
        Assert.True(problem is null, JsonSerializer.Serialize(problem, ProtocolJson.Options));
        Assert.True(applied!.Applied);
        Assert.Equal("upgrade", applied.Checkpoint!.Trigger);
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(checkpoints + 1, Checkpoints(f).Count);
        RecordEnvelope checkpoint = model.ById[applied.Checkpoint.Written!.Id];
        Assert.Equal(f.OperatorDevice, Assert.Single(checkpoint.Signatures).Device);
        Assert.True(SignedHistory.Of(model).IsWitnessed(applied.Migration!.Id));
        Assert.Equal(checkpoint.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);
    }

    [Fact]
    public void AnUpgradeOfAnEstateThatHasNotAdoptedWritesNoCheckpoint()
    {
        using var f = new Fixture(adopt: false);
        Publish(f.Records, "0.9.2");
        (UpgradeResponse? applied, Problem? problem) = f.Api.Upgrade(new UpgradeRequest("0.9.2", f.Records.Estate.OperatorId, false));
        Assert.True(problem is null, JsonSerializer.Serialize(problem, ProtocolJson.Options));
        Assert.Null(applied!.Checkpoint);
        Assert.Empty(Checkpoints(f));
        // Pinned to a release that writes checkpoints, adoption now waits on the operator.
        JsonElement items = f.Api.OperatorActions().Json.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), i => i.GetProperty("kind").GetString() == "history-adoption");
        Assert.True(f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.OperatorId, Adopt: true)).Checkpoint!.Applied);
        Assert.DoesNotContain(f.Api.OperatorActions().Json.GetProperty("items").EnumerateArray(), i => i.GetProperty("kind").GetString() == "history-adoption");
    }

    [Fact]
    public void TheMachineWarnsRolledBackAfterAHistoryRewriteButNotAfterABranchCheckout()
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        f.Api.Providers = new ProviderRegistry();
        f.Api.Providers.Register(new GitCommandAdapter());
        string root = f.Records.Estate.Root;
        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".vericairn/\n");
        Git(root, "add", "-A");
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Adopted"]);
        Assert.DoesNotContain(f.Api.Check().Findings, x => x.Code == "history.rolled-back");
        HistoryMemory.Remembered first = f.Memory().Read()!;
        Assert.Equal(Assert.Single(SignedHistory.Of(f.Api.Session.Load()).Heads).Id, first.Checkpoint!.Id);

        (HistoryCheckpointResponse? next, _) = f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.BuilderId));
        Git(root, "add", "-A");
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "A checkpoint"]);
        Assert.DoesNotContain(f.Api.Check().Findings, x => x.Code == "history.rolled-back");
        Assert.Equal(next!.Written!.Id, f.Memory().Read()!.Checkpoint!.Id);

        // A branch checkout is judged through the main line, which still holds the newest checkpoint verified.
        Git(root, "checkout", "-q", "-b", "older", "HEAD~1");
        Assert.DoesNotContain(f.Api.Check().Findings, x => x.Code == "history.rolled-back");
        Git(root, "checkout", "-q", "main");

        // A rewrite that drops it from the main line is warned, outside the canonical status.
        Git(root, "reset", "-q", "--hard", "HEAD~1");
        FindingDto warning = Assert.Single(f.Api.Check().Findings, x => x.Code == "history.rolled-back");
        Assert.Equal("warning", warning.Severity);
        Assert.Contains(next.Written.Id, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("history.rolled-back", f.Api.Status().Json.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(next.Written.Id, f.Memory().Read()!.Checkpoint!.Id);

        // An expectation checked against a head known elsewhere replaces what the machine remembers.
        Assert.Null(HistoryMemory.RememberExpected(root, f.Records.Estate.Estate.Id, "repository-synced", first.Checkpoint.Id, (first.Checkpoint.Id, first.Checkpoint.Hash, first.Checkpoint.Path),
            new GitCommandAdapter(), DateTimeOffset.UtcNow, f.MemoryHome));
        HistoryMemory.Remembered replaced = f.Memory().Read()!;
        Assert.Equal(first.Checkpoint.Id, replaced.Checkpoint!.Id);
        Assert.Equal(("held", first.Checkpoint.Id), (replaced.Expect!.Outcome, replaced.Expect.Checkpoint));
        Assert.DoesNotContain(f.Api.Check().Findings, x => x.Code == "history.rolled-back");
    }

    [Fact]
    public void ColdStartStatusIsIdenticalOnEveryCopy()
    {
        using var f = new Fixture();
        Ok(f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.BuilderId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("COPY-1"))!))));
        f.Api.HistoryCheckpoint(new HistoryCheckpointRequest(f.Records.Estate.BuilderId));
        string copy = Path.Combine(Path.GetTempPath(), "vericairn-history-copy-" + Ulid.NewUlid().Value);
        try
        {
            Copy(f.Records.Estate.Root, copy);
            using EstateService other = EstateService.Open(copy);
            StatusResponse original = f.Api.Status(), copied = other.Status();
            Assert.Equal(original.Json.GetRawText(), copied.Json.GetRawText());
            Assert.Equal(HistoryLine.FromStatus(original.Json), HistoryLine.FromStatus(copied.Json));
            Assert.Contains(HistoryLine.FromStatus(copied.Json)!, copied.Text, StringComparison.Ordinal);
            Assert.StartsWith("History: adopted · head ", HistoryLine.FromStatus(copied.Json), StringComparison.Ordinal);
        }
        finally { Delete(copy); }
    }

    private static List<RecordEnvelope> Checkpoints(Fixture f) => [.. f.Api.Session.Load().Records.Where(r => r.Type == "history-checkpoint")];

    private static JsonObject Charter(Fixture f, string purpose) => new()
    {
        ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = purpose, ["operator"] = f.Records.Estate.OperatorId, ["recordFormat"] = 3,
        ["policies"] = new JsonObject { ["signatures"] = "optional", ["releaseAcceptance"] = new JsonArray("operator") },
    };

    private static void Publish(PolicyFixture f, string version)
    {
        RecordEnvelope product = f.Add("product", new JsonObject
        {
            ["name"] = "Example", ["slug"] = "example", ["summary"] = "Test upgrade product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Test edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Test surface." }),
        });
        JsonObject body = new()
        {
            ["version"] = version, ["repository"] = "Example/example", ["commit"] = new string('1', 40), ["status"] = "candidate",
            ["artifacts"] = new JsonArray(new JsonObject { ["name"] = EstateUpgrade.DistributableName(version, "linux-x64"), ["hash"] = "sha256:" + new string('1', 64), ["bytes"] = 10, ["platform"] = "linux-x64" }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(),
            ["signing"] = "unsigned test artifact", ["limitations"] = new JsonArray(),
        };
        RecordEnvelope candidate = f.Add("release", body, subject: new RecordReference("product", product.Id, null));
        f.Add("acceptance", new JsonObject { ["statement"] = "Test release accepted.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        body["status"] = "published";
        body["publishedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        body["url"] = "https://example.invalid/releases/" + version;
        f.Add("release", body, subject: candidate.Subject, supersedes: candidate.Id);
    }

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome.Problem, ProtocolJson.Options));
        return outcome.Written!;
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output.Trim();
    }

    private static void Copy(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith(".git", StringComparison.Ordinal)) continue;
            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void Delete(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// A format-3 estate whose operator and builder each have a device held by the runtime, admitted by the operator's first authority, so the
    /// runtime signs as either; adopted by the root operator unless asked not to. The machine's memory of history is kept in a home of its own.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly List<string> keys = [];

        public Fixture(bool adopt = true)
        {
            Api = EstateService.Open(Records.Estate.Root);
            Api.HistoryMemoryHome = MemoryHome;
            OperatorDevice = Held(Records.Estate.OperatorId);
            BuilderDevice = Held(Records.Estate.BuilderId);
            if (adopt) Adopt();
        }

        public PolicyFixture Records { get; } = new();

        public EstateService Api { get; }

        public string OperatorDevice { get; }

        public string BuilderDevice { get; }

        public string MemoryHome { get; } = Path.Combine(Path.GetTempPath(), "vericairn-history-memory-" + Ulid.NewUlid().Value);

        public void Adopt()
        {
            (HistoryCheckpointResponse? adopted, Problem? problem) = Api.HistoryCheckpoint(new HistoryCheckpointRequest(Records.Estate.OperatorId, Adopt: true));
            Assert.True(adopted?.Applied == true, JsonSerializer.Serialize(problem, ProtocolJson.Options));
        }

        public HistoryMemory Memory() => new(Records.Estate.Root, Records.Estate.Estate.Id, "repository-synced", MemoryHome);

        /// <summary>A device this machine holds for a person, recorded as needing its owner's presence at every signature, admitted by the operator.</summary>
        public string PresenceDevice(string principal)
        {
            string id = Ulid.NewUlid().Value;
            var store = new FileKeyStore(VericairnHome.Path());
            string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
            keys.Add(id);
            Records.Estate.SetClock(DateTimeOffset.UtcNow.AddMinutes(-5));
            RecordEnvelope device = Records.Estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"A person's device","platform":"macos","custody":"macos-secure-enclave","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"required","purpose":"signing"}""",
                id: id, signer: new StoreSigner(store, id));
            Records.Add("admission", new JsonObject { ["principal"] = principal, ["status"] = "admitted", ["note"] = "A person's device." }, subject: device.Reference);
            return id;
        }

        /// <summary>Removes the operator's runtime-held key, so no device of the operator signs on this machine.</summary>
        public void ForgetOperatorDevice() => new FileKeyStore(VericairnHome.Path()).Remove(OperatorDevice);

        private string Held(string principal)
        {
            WriteOutcome device = Api.DeviceCreate(new(principal, "Runtime's device", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            keys.Add(device.Written!.Id);
            RecordEnvelope held = Api.Session.Load().ById[device.Written!.Id];
            Records.Estate.SetClock(held.AtTime.AddSeconds(-1));
            Records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "The runtime's device." }, subject: held.Reference);
            return device.Written.Id;
        }

        public void Dispose()
        {
            Api.Dispose();
            Records.Dispose();
            var store = new FileKeyStore(VericairnHome.Path());
            foreach (string key in keys.Where(store.Has)) store.Remove(key);
            if (Directory.Exists(MemoryHome)) Directory.Delete(MemoryHome, recursive: true);
        }
    }
}
