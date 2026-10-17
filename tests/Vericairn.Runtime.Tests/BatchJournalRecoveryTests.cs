// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class BatchJournalRecoveryTests
{
    [Fact]
    public void ReadableJournalsAndDiagnosticsAreReturnedSeparatelyWithoutRemovingEitherFile()
    {
        using var estate = new TestEstate();
        using EstateService service = EstateService.Open(estate.Root);
        using JsonDocument body = JsonDocument.Parse("""{"kind":"human","name":"Another person","roles":[]}""");
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft("principal", estate.OperatorId, body.RootElement));
        BatchJournal journal = BatchJournal.Begin(service.Session, "A readable journal", null, [record]);
        string damaged = Journal(service, "untrusted name", "{broken");

        BatchJournal.ReadResult read = BatchJournal.All(service.Session);
        Assert.Equal(journal.Id, Assert.Single(read.Journals).Id);
        Assert.Single(read.Unreadable);
        Assert.True(File.Exists(Path.Combine(BatchJournal.Directory(service.Session), journal.Id + ".json")));
        Assert.Equal("{broken", File.ReadAllText(damaged));
    }

    [Theory]
    [InlineData("records")]
    [InlineData("entry")]
    [InlineData("path")]
    [InlineData("id")]
    [InlineData("startedAt")]
    public void StructurallyIncompleteJournalsAreKeptInsteadOfBeingJudgedEmpty(string damage)
    {
        using var estate = new TestEstate();
        string file;
        ReconcileTask first;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            using JsonDocument body = JsonDocument.Parse("""{"kind":"human","name":"Another person","roles":[]}""");
            AuthoredRecord record = RecordAuthor.Create(new RecordDraft("principal", estate.OperatorId, body.RootElement));
            BatchJournal journal = BatchJournal.Begin(service.Session, "Incomplete entries", null, [record]);
            file = Path.Combine(BatchJournal.Directory(service.Session), journal.Id + ".json");
            JsonObject json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            switch (damage)
            {
                case "records": json["records"] = new JsonArray(); break;
                case "entry": json["records"]![0] = 42; break;
                case "path": json["records"]![0]!["path"] = "../outside.json"; break;
                case "id": json["records"]![0]!.AsObject().Remove("id"); break;
                case "startedAt": json.Remove("startedAt"); break;
            }

            File.WriteAllText(file, json.ToJsonString());
            first = AssertRecovery(service, estate.OperatorId);
        }

        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Equal(first, AssertRecovery(reopened, estate.OperatorId));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void AnUnreadableJournalHoldsGroupedWritesUntilLocalRecovery()
    {
        using var run = new RunEstate(keepOperatorKey: true);
        RoutingServiceTests.Routed(run);
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"W-0","kind":"ordinary","title":"A person step","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[{"type":"goal","id":"{{run.Goal}}"}],"deliverables":["work"],"role":"publisher"}"""))));
        int assignments = run.Service.Records("assignment").Count;
        string file = Journal(run.Service, Ulid.NewUlid().Value, "{truncated");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            GoalStepResponse refused = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId));
            Assert.False(refused.Ok);
            Assert.Equal("reconcile-required", refused.Problem!.Code);
            Assert.Null(refused.Assignment);
            Assert.Null(refused.Ask);
            Assert.Equal(assignments, run.Service.Records("assignment").Count);
            Assert.Empty(run.Service.Records("ask"));
            Assert.Equal("{truncated", File.ReadAllText(file));
            run.Reopen();
        }

        // Simulate the operator preserving the damaged file after inspecting the stored records.
        File.Move(file, Path.Combine(run.Scratch, "preserved-journal.json"));
        Assert.Empty(run.Service.Reconcile());
        GoalStepResponse resumed = run.Service.GoalStep(run.Goal, new GoalStepRequest(run.Estate.OperatorId));
        Assert.True(resumed.Ok, resumed.Message);
        Assert.NotNull(resumed.Assignment);
        Assert.NotNull(resumed.Ask);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"batch\":\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void DamagedJournalsRemainVisibleAndBlockWritesAcrossReopen(string content)
    {
        using var estate = new TestEstate();
        string file;
        ReconcileTask first;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            file = Journal(service, "untrusted name", content);
            first = AssertRecovery(service, estate.OperatorId);
            Assert.DoesNotContain("untrusted name", first.Location, StringComparison.Ordinal);
            Assert.DoesNotContain(estate.Root, first.Message, StringComparison.Ordinal);
            Assert.Equal(content, File.ReadAllText(file));
        }

        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Equal(first, AssertRecovery(reopened, estate.OperatorId));
        Assert.Equal(content, File.ReadAllText(file));
    }

    [Fact]
    public void AReadDeniedJournalRemainsVisibleAcrossReopenAndCanBeReadAfterRelease()
    {
        using var estate = new TestEstate();
        string file;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            file = Journal(service, Ulid.NewUlid().Value, "{truncated");
        }

        ReconcileTask first;
        using (var denied = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using (EstateService service = EstateService.Open(estate.Root))
            {
                first = AssertRecovery(service, estate.OperatorId);
                Assert.Contains("read", first.Message, StringComparison.OrdinalIgnoreCase);
            }

            using EstateService reopened = EstateService.Open(estate.Root);
            Assert.Equal(first, AssertRecovery(reopened, estate.OperatorId));
            Assert.True(File.Exists(file));
        }

        using EstateService readable = EstateService.Open(estate.Root);
        Assert.Equal(first.Location, AssertRecovery(readable, estate.OperatorId).Location);
        Assert.Equal("{truncated", File.ReadAllText(file));
    }

    [Fact]
    public void AnUnreadableJournalDirectoryIsNotTreatedAsAbsent()
    {
        using var estate = new TestEstate();
        ReconcileTask first;
        string directory;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            directory = BatchJournal.Directory(service.Session);
            File.WriteAllText(directory, "Not a readable journal directory.");
            first = AssertRecovery(service, estate.OperatorId);
            Assert.Equal("batch-journal:directory", first.Location);
        }

        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Equal(first, AssertRecovery(reopened, estate.OperatorId));
        Assert.True(File.Exists(directory));
    }

    private static string Journal(EstateService service, string name, string content)
    {
        string directory = BatchJournal.Directory(service.Session);
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name + ".json");
        File.WriteAllText(file, content);
        return file;
    }

    private static ReconcileTask AssertRecovery(EstateService service, string by)
    {
        ReconcileTask task = Assert.Single(service.Reconcile());
        Assert.Equal("batch.unreadable", task.Code);
        Assert.StartsWith("batch-journal:", task.Location, StringComparison.Ordinal);
        Assert.DoesNotContain(service.Root, task.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(BatchJournal.Directory(service.Session), task.Message, StringComparison.Ordinal);
        StatusResponse status = service.Status();
        Assert.Contains("batch.unreadable", status.Text, StringComparison.Ordinal);
        Assert.Contains("batch.unreadable", status.Json.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(task.Location, status.Json.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(service.Check().Findings, finding => finding.Code == task.Code && finding.Location == task.Location);

        using JsonDocument body = JsonDocument.Parse("""{"kind":"human","name":"Another person","roles":[]}""");
        WriteOutcome refused = service.NewRecord(new NewRecordRequest("principal", by, body.RootElement));
        Assert.False(refused.Ok);
        Assert.Equal("reconcile-required", refused.Problem!.Code);
        Assert.Contains(refused.Problem.Findings, finding => finding.Code == task.Code && finding.Location == task.Location);
        Assert.Contains("record writes", task.Message, StringComparison.Ordinal);
        return task;
    }
}
