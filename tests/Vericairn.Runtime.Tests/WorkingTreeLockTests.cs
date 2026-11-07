// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>Two runtimes with their own Vericairn homes on one working tree write one at a time (FIX-99, issue #312).</summary>
public sealed class WorkingTreeLockTests : IDisposable
{
    private readonly string homeA = Directory.CreateTempSubdirectory("vericairn-tree-lock-a-").FullName;
    private readonly string homeB = Directory.CreateTempSubdirectory("vericairn-tree-lock-b-").FullName;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoWritersOfOneIdentityAtOnceLeaveOneRecordAndRefuseTheOther(bool sameHome)
    {
        using var estate = new TestEstate(3);
        using EstateService a = EstateService.Open(estate.Root, null, homeA);
        using EstateService b = EstateService.Open(estate.Root, null, sameHome ? homeA : homeB);

        for (int round = 0; round < 4; round++)
        {
            string identity = $"synthetic-collision:vault/once-{round}";
            WriteOutcome[] outcomes = AtOnce(() => a.NewRecord(Operation(estate, identity)), () => b.NewRecord(Operation(estate, identity)));

            WriteOutcome written = Assert.Single(outcomes, o => o.Ok);
            WriteOutcome refused = Assert.Single(outcomes, o => !o.Ok);
            Assert.NotNull(written.Written);
            Assert.Null(refused.Written);
            Assert.Equal("rejected", refused.Problem!.Code);
            Assert.Contains(refused.Problem.Findings, f => f.Code == "operation.duplicate-identity" && f.Severity == "error");
        }

        foreach (EstateService service in new[] { a, b })
        {
            Assert.Equal(0, service.Validate().Errors);
            Assert.Equal(4, service.Session.Load().Current("operation").Count());
        }
    }

    [Fact]
    public void TwoWritersOfDifferentIdentitiesAtOnceBothWrite()
    {
        using var estate = new TestEstate(3);
        using EstateService a = EstateService.Open(estate.Root, null, homeA);
        using EstateService b = EstateService.Open(estate.Root, null, homeB);

        for (int round = 0; round < 4; round++)
        {
            WriteOutcome[] outcomes = AtOnce(() => a.NewRecord(Operation(estate, $"synthetic:a-{round}")), () => b.NewRecord(Operation(estate, $"synthetic:b-{round}")));
            Assert.All(outcomes, o => Assert.True(o.Ok, o.Problem?.Message));
        }

        Assert.Equal(8, a.Session.Load().Current("operation").Count());
        Assert.Equal(0, a.Validate().Errors);
        Assert.Equal(0, b.Validate().Errors);
    }

    [Fact]
    public async Task ASecondWriterWaitsUnderTheLockAndJudgesTheFirstWritersRecord()
    {
        using var estate = new TestEstate(3);
        using EstateService a = EstateService.Open(estate.Root, null, homeA);
        using EstateService b = EstateService.Open(estate.Root, null, homeB);
        using var inside = new ManualResetEventSlim();
        using var waiting = new ManualResetEventSlim();
        a.BeforeValidatingRecords = _ =>
        {
            Assert.True(a.Session.WriteLock!.Held);
            inside.Set();
            Assert.True(waiting.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken), "The second writer never waited for the lock.");
        };
        b.Session.WriteLock!.Waiting = waiting.Set;

        Task<WriteOutcome> first = Task.Run(() => a.NewRecord(Operation(estate, "synthetic:once")), TestContext.Current.CancellationToken);
        Assert.True(inside.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
        WriteOutcome second = b.NewRecord(Operation(estate, "synthetic:once"));

        WriteOutcome written = await first.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.False(second.Ok);
        Assert.Contains(second.Problem!.Findings, f => f.Code == "operation.duplicate-identity");
        Assert.Single(b.Session.Load().Current("operation"));
        Assert.False(a.Session.WriteLock!.Held);
        Assert.False(File.Exists(a.Session.WriteLock.HolderPath));
    }

    [Fact]
    public async Task AWriterThatCannotTakeTheLockInTimeIsRefusedNamingTheHolder()
    {
        using var estate = new TestEstate(3);
        using EstateService a = EstateService.Open(estate.Root, null, homeA);
        using EstateService b = EstateService.Open(estate.Root, null, homeB);
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        string? holder = null;
        a.BeforeValidatingRecords = _ =>
        {
            holder = File.ReadAllText(a.Session.WriteLock!.HolderPath);
            inside.Set();
            Assert.True(release.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
        };
        b.Session.WriteLock!.Wait = TimeSpan.FromMilliseconds(300);

        Task<WriteOutcome> first = Task.Run(() => a.NewRecord(Operation(estate, "synthetic:held")), TestContext.Current.CancellationToken);
        Assert.True(inside.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken));
        WriteOutcome busy = b.NewRecord(Operation(estate, "synthetic:other"));
        // A session's own store waits for the same lock.
        AuthoredRecord direct = RecordAuthor.Create(new RecordDraft("resource", estate.OperatorId,
            JsonDocument.Parse("""{"kind":"service","name":"Site","role":"the public site","location":{"url":"https://example.invalid/site"},"visibility":"public"}""").RootElement, Format: 3), []);
        IOException storeBusy = Assert.ThrowsAny<IOException>(() => b.Session.Write(direct));
        release.Set();

        Assert.False(busy.Ok);
        Assert.Null(busy.Written);
        Assert.Equal("conflict", busy.Problem!.Code);
        string id = WorkingTreeLock.HomeId(homeA);
        foreach (string message in new[] { busy.Problem.Message, storeBusy.Message })
        {
            Assert.Contains($"process {Environment.ProcessId}", message, StringComparison.Ordinal);
            Assert.Contains(Environment.MachineName, message, StringComparison.Ordinal);
            Assert.Contains(id, message, StringComparison.Ordinal);
            Assert.DoesNotContain(homeA, message, StringComparison.Ordinal);
        }

        using (JsonDocument recorded = JsonDocument.Parse(holder!))
        {
            Assert.Equal(Environment.ProcessId, recorded.RootElement.GetProperty("pid").GetInt32());
            Assert.Equal(id, recorded.RootElement.GetProperty("home").GetString());
            Assert.DoesNotContain(homeA, holder!, StringComparison.Ordinal);
        }

        WriteOutcome held = await first.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.True(held.Ok, held.Problem?.Message);
        Assert.False(File.Exists(Path.Combine(estate.Root, direct.RelativePath)));
        // Once the holder is done, the refused writer writes.
        WriteOutcome retried = b.NewRecord(Operation(estate, "synthetic:other"));
        Assert.True(retried.Ok, retried.Problem?.Message);
        Assert.Equal(2, b.Session.Load().Current("operation").Count());
    }

    [Fact]
    public void TheLockLivesInTheWorkingTreeIgnoresItselfAndIsReleasedAfterARefusal()
    {
        using var estate = new TestEstate(3);
        using EstateService a = EstateService.Open(estate.Root, null, homeA);
        WriteOutcome refused = a.NewRecord(new("resource", estate.OperatorId, JsonDocument.Parse("""{"kind":"service"}""").RootElement));
        Assert.False(refused.Ok);
        WorkingTreeLock tree = a.Session.WriteLock!;
        Assert.False(tree.Held);
        Assert.Equal(Path.Combine(Path.GetFullPath(estate.Root), ".vericairn", "write-lock", "write.lock"), tree.LockPath);
        Assert.Equal("*\n", File.ReadAllText(Path.Combine(Path.GetDirectoryName(tree.LockPath)!, ".gitignore")));

        using EstateService b = EstateService.Open(estate.Root, null, homeB);
        b.Session.WriteLock!.Wait = TimeSpan.Zero;
        Assert.True(b.NewRecord(Operation(estate, "synthetic:after-refusal")).Ok);
        Assert.Equal(0, b.Check().Errors);
        using EstateService reader = EstateService.OpenReadOnly(estate.Root);
        Assert.Null(reader.Session.WriteLock);
    }

    [Fact]
    public void ALocalOnlyEstateLocksBesideTheLiveStoreThatHoldsItsRecords()
    {
        string root = Directory.CreateTempSubdirectory("vericairn-tree-lock-local-").FullName;
        try
        {
            var initialization = new EstateInitialization("Local", "local", "Local-only profile.", "Operator", "Example", null, "Example/vericairn", new string('b', 40), new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), "local-only");
            PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
            string manifest = Path.Combine(root, EstateLayout.ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.WriteAllBytes(manifest, prepared.ManifestBytes);
            using EstateSession session = EstateSession.Open(root, false, homeA);
            session.Write(prepared.Operator);
            session.Write(prepared.Estate);
            Assert.Equal(Path.Combine(Path.GetDirectoryName(session.Live.Path)!, "write-lock", "write.lock"), session.WriteLock!.LockPath);
            Assert.True(File.Exists(session.WriteLock.LockPath));
            Assert.Equal(["estate"], Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            try { WorkspaceCleanup.Delete(root); }
            catch (IOException) { /* Best effort. */ }
        }
    }

    [Fact]
    public void TheWriteLockAndTheCacheRebuildLockDoNotWaitOnEachOther()
    {
        using var estate = new TestEstate(3);
        using (EstateService a = EstateService.Open(estate.Root))
        {
            using WorkingTreeLock.Hold held = a.Session.HoldWriteLock();
            // A rebuild is refused at once while a session has the estate open, and it never takes the working tree's lock.
            IOException present = Assert.Throws<IOException>(() => LiveStoreRecovery.Rebuild(estate.Root));
            Assert.Equal(LiveStoreRecovery.WriterPresent, present.Message);
            // Entering again on the holding thread does not wait.
            using WorkingTreeLock.Hold again = a.Session.HoldWriteLock();
            Assert.True(a.Session.WriteLock!.Held);
        }

        LiveStoreRebuilt rebuilt = LiveStoreRecovery.Rebuild(estate.Root);
        Assert.Equal(estate.Load().Records.Count, rebuilt.Records);
    }

    public void Dispose()
    {
        foreach (string home in new[] { homeA, homeB })
        {
            try { WorkspaceCleanup.Delete(home); }
            catch (IOException) { /* Best effort: a store still closing keeps its files a moment longer. */ }
        }
    }

    /// <summary>Starts both writes behind one barrier, as the issue's reproduction did, and returns both outcomes.</summary>
    private static WriteOutcome[] AtOnce(Func<WriteOutcome> first, Func<WriteOutcome> second)
    {
        using var barrier = new Barrier(2);
        WriteOutcome Run(Func<WriteOutcome> write)
        {
            barrier.SignalAndWait(TestTiming.HangTimeout);
            return write();
        }

        Task<WriteOutcome>[] writers = [Task.Factory.StartNew(() => Run(first), TaskCreationOptions.LongRunning), Task.Factory.StartNew(() => Run(second), TaskCreationOptions.LongRunning)];
        Assert.True(Task.WaitAll(writers, TestTiming.HangTimeout));
        return [writers[0].Result, writers[1].Result];
    }

    /// <summary>A recorded-only operation under an identity, never dispatched.</summary>
    public static NewRecordRequest Operation(TestEstate estate, string identity) => new("operation", estate.OperatorId, JsonSerializer.SerializeToElement(new
    {
        provider = "github-releases", action = "dispatch-release-workflow", identity,
        intent = "Recorded only, never dispatched.",
        target = new { repository = "Example/widget", workflow = "release.yml", @ref = "main", version = "0.2.0" },
        status = "not-started", evidence = Array.Empty<object>(),
    }));
}
