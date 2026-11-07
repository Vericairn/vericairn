// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>History checkpoints at the runtime's boundary: writing them, refusing what they forbid, and the check-time git warning.</summary>
public class HistoryCheckpointServiceTests
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void TheRuntimeWritesACheckpointOnlyWhenItFollowsEveryHeadAndListsTheTail()
    {
        using var f = new Fixture();
        RecordWritten adoption = Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        EstateModel model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(adoption.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);

        // A checkpoint prepared before another record was written no longer lists the tail, and one naming no head skips one.
        CheckpointDraft stale = f.Prepare("interval");
        Ok(f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-20"))!))));
        WriteOutcome refused = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.BuilderId, stale.Body));
        Assert.False(refused.Ok);
        Assert.Contains("lists every record no head witnesses", refused.Problem!.Message, StringComparison.Ordinal);
        JsonObject skipping = JsonNode.Parse(f.Prepare("interval").Body.GetRawText())!.AsObject();
        skipping["previous"] = new JsonArray();
        skipping["trigger"] = "adoption";
        refused = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.OperatorId, PolicyFixture.Element(skipping)));
        Assert.False(refused.Ok);
        Assert.Contains("follows every head", refused.Problem!.Message, StringComparison.Ordinal);

        // The agent's interval checkpoint witnesses; witnessing grants nothing.
        RecordWritten interval = Ok(f.Checkpoint(f.Records.Estate.BuilderId, "interval"));
        model = f.Api.Session.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(interval.Id, Assert.Single(SignedHistory.Of(model).Heads).Id);
        Assert.True(SignatureChecks.IsAuthentic(model, model.ById[interval.Id]));
    }

    [Fact]
    public void AnAgentsAnchorsAnEarlierTimeAndAnExplicitIdAreRefused()
    {
        using var f = new Fixture();
        f.Require();
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        RecordEnvelope late = f.Records.Estate.Add("work-order", f.Records.Estate.OperatorId, HistoryFixture.Order("HIS-21", "Late"));
        WriteOutcome agents = f.Checkpoint(f.Records.Estate.BuilderId, "manual", [late]);
        Assert.False(agents.Ok);
        Assert.Contains(agents.Problem!.Findings, x => x.Code == "history.anchor-author");
        File.Delete(Path.Combine(f.Records.Estate.Root, late.ExpectedPath));

        CheckpointDraft draft = f.Prepare("manual");
        WriteOutcome dated = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.OperatorId, draft.Body, At: "2026-10-13T12:00:00Z"));
        Assert.False(dated.Ok);
        Assert.Contains("states no earlier time", dated.Problem!.Message, StringComparison.Ordinal);
        WriteOutcome named = f.Api.NewRecord(new NewRecordRequest("history-checkpoint", f.Records.Estate.OperatorId, draft.Body, Id: Ulid.NewUlid().Value));
        Assert.False(named.Ok);
    }

    [Fact]
    public void WhileAWitnessedRecordIsMissingWritesAndReleaseSubmissionAreRefused()
    {
        using var f = new Fixture();
        RecordEnvelope product = f.Records.Add("product", ReleaseLineFixture.ProductBody());
        var provider = new FakeReleaseProvider();
        f.Api.Providers = new ProviderRegistry();
        f.Api.Providers.Register(provider);
        ReleaseGateSource.Prepare(f.Api, "Example/widget", provider.HeadSha);
        var request = new ReleaseSubmitRequest(product.Id, "1.1.0", "Example/widget", f.Records.Estate.OperatorId,
            Provider: provider.Provider, Commit: provider.HeadSha, Expected: product.ComputedHash) { ClientBuild = EstateService.Version };
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        Assert.DoesNotContain(f.Api.ReleaseCheck(request).Check?.Objections ?? [], o => o.Code.StartsWith("history.", StringComparison.Ordinal));

        string path = Path.Combine(f.Records.Estate.Root, f.Records.Decision.ExpectedPath);
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        ReleaseResponse checkedRelease = f.Api.ReleaseCheck(request);
        Assert.False(checkedRelease.Ok);
        Assert.Contains(checkedRelease.Check!.Objections, o => o.Code == "history.removed" && o.Location == f.Records.Decision.ExpectedPath);
        WriteOutcome write = f.Api.NewRecord(new NewRecordRequest("work-order", f.Records.Estate.OperatorId, PolicyFixture.Element(JsonNode.Parse(HistoryFixture.Order("HIS-23"))!)));
        Assert.False(write.Ok);
        Assert.Contains(write.Problem!.Findings, x => x.Code == "history.removed");
        string status = f.Api.Status().Text;
        Assert.True(status.IndexOf("history.removed", StringComparison.Ordinal) < status.IndexOf("Principals:", StringComparison.Ordinal));

        File.WriteAllBytes(path, bytes);
        Assert.DoesNotContain(f.Api.ReleaseCheck(request).Check?.Objections ?? [], o => o.Code.StartsWith("history.", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordFilesDeletedInGitAreWarnedAtCheckTimeAndUnavailableInAShallowClone()
    {
        TestPrerequisites.RequireGit();
        using var f = new Fixture();
        RecordEnvelope order = f.Records.Estate.Add("work-order", f.Records.Estate.OperatorId, HistoryFixture.Order("HIS-24"), signer: f.Records.OperatorSigner);
        string root = f.Records.Estate.Root;
        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".vericairn/\n");
        Git(root, "add", "-A");
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "The estate"]);
        Git(root, "rm", "-q", order.ExpectedPath);
        Git(root, [.. GitIdentity, "commit", "-q", "-m", "Remove a record no checkpoint witnessed"]);
        string deleting = Git(root, "rev-parse", "HEAD");
        f.Api.Providers.Register(new GitCommandAdapter());

        FindingsResponse check = f.Api.Check();
        FindingDto warning = Assert.Single(check.Findings, x => x.Code == "history.deleted-in-git");
        Assert.Equal("warning", warning.Severity);
        Assert.Equal(order.ExpectedPath, warning.Location);
        Assert.Contains(deleting, warning.Message, StringComparison.Ordinal);
        // The warning depends on the clone, so the canonical status does not carry it.
        Assert.DoesNotContain("history.deleted-in-git", f.Api.Status().Json.GetRawText(), StringComparison.Ordinal);

        string shallow = Path.Combine(Path.GetTempPath(), "vericairn-history-shallow-" + Ulid.NewUlid().Value);
        try
        {
            Git(Path.GetTempPath(), "clone", "-q", "--depth", "1", new Uri(root).AbsoluteUri, shallow);
            RecordDeletions? deletions = new GitCommandAdapter().DeletedRecordFiles(shallow);
            Assert.NotNull(deletions);
            Assert.True(deletions.Shallow);
            Assert.Empty(deletions.Deleted);
            RecordDeletions? full = new GitCommandAdapter().DeletedRecordFiles(root);
            Assert.Equal(order.ExpectedPath, Assert.Single(full!.Deleted).Path);
        }
        finally
        {
            foreach (string file in Directory.Exists(shallow) ? Directory.EnumerateFiles(shallow, "*", SearchOption.AllDirectories) : [])
                File.SetAttributes(file, FileAttributes.Normal);
            if (Directory.Exists(shallow)) Directory.Delete(shallow, recursive: true);
        }
    }

    [Fact]
    public void InventoryObjectsAreReadWithinTheObjectLimitAndNeverThroughALink()
    {
        using var f = new Fixture();
        Ok(f.Checkpoint(f.Records.Estate.OperatorId, "adoption", f.Unsigned()));
        RecordEnvelope adoption = f.Api.Session.Load().OfType("history-checkpoint").Single();
        string hash = adoption.Body.GetProperty("inventory")[0].GetString()!;
        string objects = Path.Combine(f.Records.Estate.Root, EstateLayout.ObjectsDirectory);
        string path = Path.Combine(objects, hash["sha256:".Length..]);
        string outside = Path.Combine(Path.GetTempPath(), "vericairn-history-outside-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(outside);
        try
        {
            // An object reached through a link is not read, even when the bytes it leads to are the right ones.
            string target = Path.Combine(outside, Path.GetFileName(path));
            File.Move(path, target);
            bool linked = true;
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { linked = false; }
            if (linked)
                Assert.Contains(f.Api.Session.Load().Findings, x => x.Code == "history.checkpoint-missing" && x.Message.Contains(hash, StringComparison.Ordinal));
            File.Delete(path);
            File.Move(target, path);
            Assert.DoesNotContain(f.Api.Session.Load().Findings, x => x.Code == "history.checkpoint-missing");

            // An object over the limit is not read either.
            byte[] large = new byte[EstateLayout.MaxObjectBytes + 1];
            Array.Fill(large, (byte)'a');
            string largeHash = Records.Hashing.ContentHash.Sha256(large);
            File.WriteAllBytes(Path.Combine(objects, largeHash["sha256:".Length..]), large);
            JsonObject body = JsonNode.Parse(f.Prepare("interval").Body.GetRawText())!.AsObject();
            body["inventory"] = new JsonArray(largeHash);
            f.Records.Add("history-checkpoint", body);
            Assert.Contains(f.Api.Session.Load().Findings, x => x.Code == "history.checkpoint-missing" && x.Message.Contains(largeHash, StringComparison.Ordinal));
        }
        finally { Directory.Delete(outside, recursive: true); }
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

    /// <summary>
    /// A format-3 estate with its operator's first authority and builder held in a disposable key store, and a device of each held by
    /// the runtime, admitted by the operator's first authority, so the runtime signs as either.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Api = EstateService.Open(Records.Estate.Root);
            foreach (string principal in new[] { Records.Estate.OperatorId, Records.Estate.BuilderId })
            {
                WriteOutcome device = Api.DeviceCreate(new(principal, "Runtime's device", "file"));
                Assert.True(device.Ok, device.Problem?.Message);
                RecordEnvelope held = Api.Session.Load().ById[device.Written!.Id];
                Records.Estate.SetClock(held.AtTime.AddSeconds(-1));
                Records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "The runtime's device." }, subject: held.Reference);
            }
        }

        public PolicyFixture Records { get; } = new();

        public EstateService Api { get; }

        public RecordEnvelope Require() => Records.Add("estate", new JsonObject
        {
            ["name"] = "Test Estate", ["slug"] = "test-estate", ["purpose"] = "Exercise the record format.", ["operator"] = Records.Estate.OperatorId, ["recordFormat"] = 3,
            ["policies"] = new JsonObject { ["signatures"] = "required", ["releaseAcceptance"] = new JsonArray("operator") },
        }, supersedes: Records.Estate.Estate.Id);

        public IReadOnlyList<RecordEnvelope> Unsigned() => [.. Api.Session.Load().Records.Where(r => r.Signatures.Count == 0)];

        public CheckpointDraft Prepare(string trigger, IEnumerable<RecordEnvelope>? anchored = null)
        {
            CheckpointDraft draft = SignedHistory.Prepare(Api.Session.Load(), trigger, anchored);
            foreach (byte[] bytes in draft.Objects) Api.Session.Objects.Put(bytes);
            return draft;
        }

        public WriteOutcome Checkpoint(string by, string trigger, IEnumerable<RecordEnvelope>? anchored = null) =>
            Api.NewRecord(new NewRecordRequest("history-checkpoint", by, Prepare(trigger, anchored).Body));

        public void Dispose()
        {
            Api.Dispose();
            Records.Dispose();
        }
    }
}
