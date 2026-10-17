// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class BatchJournalRecoveryTests
{
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
        Assert.Contains("batch.unreadable", service.Status().Text, StringComparison.Ordinal);
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
