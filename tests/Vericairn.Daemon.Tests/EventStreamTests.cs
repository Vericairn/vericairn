// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// WATCH-05's exit question: a client hears every change it may read within seconds, resumes after a gap without losing one, and
/// learns nothing about records it may not read. Waits are bounded by the hang ceiling; only the delivery test measures time.
/// </summary>
[Collection("daemon")]
public sealed class EventStreamTests
{
    private const string Resource = """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"private"}""";

    public EventStreamTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task ARecordWrittenThroughTheRuntimeReachesAStreamWithinSeconds()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        await using var follower = new EventFollower(token => client.StreamEventsAsync(filter: new EventFilter(Kinds: ["record"]), cancellationToken: token));
        EventStreamItem opening = await follower.NextAsync();
        Assert.Null(opening.Event);
        Assert.False(opening.Reset);

        var watch = Stopwatch.StartNew();
        RecordWritten written = Write(client, estate.OperatorId);
        EventEnvelope delivered = await follower.NextEventAsync(e => e.Subject.Id == written.Id);
        TimeSpan latency = watch.Elapsed;
        Assert.True(latency < TimeSpan.FromSeconds(15), $"The event arrived {latency.TotalSeconds:F1} seconds after its write.");
        Assert.Equal(EventVocabulary.Format, delivered.Format);
        Assert.Equal("record", delivered.Kind);
        Assert.Equal("written", delivered.Change);
        Assert.Equal(new EventReference("resource", written.Id, written.Hash), delivered.Subject);
        Assert.StartsWith("sha256:", delivered.Key, StringComparison.Ordinal);

        // The envelope carries identities and states only: no body, title, path or command of the record.
        string json = JsonSerializer.Serialize(delivered, ProtocolJson.Options);
        foreach (string content in new[] { "Widget", "Example/widget", "records/", "vericairn " })
            Assert.DoesNotContain(content, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheClockTickFindsWhatChangedWithoutPassingThroughTheRuntime()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, options: new DaemonOptions { EventTick = TimeSpan.FromMilliseconds(200) }, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        await using var follower = new EventFollower(token => client.StreamEventsAsync(filter: new EventFilter(Kinds: ["record"]), cancellationToken: token));
        Assert.Null((await follower.NextAsync()).Event);

        // Written straight into the working tree, as a pull brings records: no write through this runtime asks for a pass.
        string pulled = estate.Add("resource", estate.OperatorId, Resource).Id;
        EventEnvelope found = await follower.NextEventAsync(e => e.Subject.Id == pulled);
        Assert.Equal("found", found.Change);
    }

    [Fact]
    public async Task AReaderResumesAfterAGapWithoutLosingAnEvent()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        string cursor = client.Status().EventsCursor!;
        Assert.NotNull(cursor);

        // Nobody listens while three records are written: the gap.
        string[] written = [Write(client, estate.OperatorId).Id, Write(client, estate.OperatorId).Id, Write(client, estate.OperatorId).Id];
        var records = new EventFilter(Kinds: ["record"]);
        EventsResponse polled = client.Events(after: cursor, filter: records, wait: 0);
        Assert.False(polled.Reset);
        Assert.Null(polled.Problem);
        Assert.Equal(written, polled.Events.Select(e => e.Subject.Id));
        Assert.All(polled.Events, e => Assert.Equal("written", e.Change));

        // A stream resumed from the second event's cursor delivers the third and nothing before it.
        await using (var follower = new EventFollower(token => client.StreamEventsAsync(after: polled.Events[1].Cursor, filter: records, cancellationToken: token)))
        {
            Assert.Null((await follower.NextAsync()).Event);
            Assert.Equal(written[2], (await follower.NextEventAsync()).Subject.Id);
        }

        // The long poll's own cursor resumes after everything it read; the next record is all that follows.
        string fourth = Write(client, estate.OperatorId).Id;
        EventsResponse next = await client.EventsAsync(after: polled.Cursor, filter: records, wait: EventVocabulary.MaxWaitSeconds, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([fourth], next.Events.Select(e => e.Subject.Id));

        // A record found in the working tree without passing through this runtime, as after a pull, is found on the next pass.
        string pulled = estate.Add("resource", estate.OperatorId, Resource).Id;
        EventsResponse found = client.Events(after: next.Cursor, filter: records, wait: EventVocabulary.MaxWaitSeconds);
        EventEnvelope foundEvent = Assert.Single(found.Events);
        Assert.Equal(pulled, foundEvent.Subject.Id);
        Assert.Equal("found", foundEvent.Change);
    }

    [Fact]
    public async Task ACursorBeyondTheBoundForeignOrMalformedIsResetAndStateIsReadAgain()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, options: new DaemonOptions { EventCapacity = 2 }, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        string old = client.Status().EventsCursor!;
        for (int i = 0; i < 3; i++) Write(client, estate.OperatorId);

        // Three events into a journal that keeps two: the events after the old cursor are no longer all kept.
        EventsResponse reset = client.Events(after: old, wait: 0);
        Assert.True(reset.Reset);
        Assert.Empty(reset.Events);
        Assert.NotEqual(old, reset.Cursor);
        await using (var follower = new EventFollower(token => client.StreamEventsAsync(after: old, cancellationToken: token)))
        {
            EventStreamItem item = await follower.NextAsync();
            Assert.True(item.Reset);
        }

        // Re-reading state gives a cursor that holds; from it nothing is reset and nothing is missing.
        string fresh = client.Status().EventsCursor!;
        Assert.False(client.Events(after: fresh, wait: 0).Reset);
        string next = Write(client, estate.OperatorId).Id;
        Assert.Equal([next], client.Events(after: fresh, filter: new EventFilter(Kinds: ["record"]), wait: 0).Events.Select(e => e.Subject.Id));

        // A cursor sealed for another principal, or one that is not a cursor at all, is reset alike.
        string builders = client.Events(principal: estate.BuilderId, wait: 0).Cursor;
        Assert.True(client.Events(after: builders, wait: 0).Reset);
        Assert.False(client.Events(after: builders, principal: estate.BuilderId, wait: 0).Reset);
        Assert.True(client.Events(after: "not-a-cursor", wait: 0).Reset);
        Assert.True(client.Events(after: fresh[..^4] + "AAAA", wait: 0).Reset);
    }

    [Fact]
    public async Task ACursorFromBeforeACleanRestartHoldsAndOneFromAnotherEpochIsReset()
    {
        using var estate = new TestEstate(3);
        string cursor;
        string journalPath;
        await using (DaemonHost first = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken))
        {
            using var client = new VericairnClient(first.Endpoint);
            cursor = client.Status().EventsCursor!;
            journalPath = Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!, "events.db");
        }

        // Written while no daemon runs: the next one finds it, after the cursor kept across the clean stop.
        string offline = estate.Add("resource", estate.OperatorId, Resource).Id;
        await using (DaemonHost second = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken))
        {
            using var client = new VericairnClient(second.Endpoint);
            EventsResponse resumed = client.Events(after: cursor, filter: new EventFilter(Kinds: ["record"]), wait: 0);
            Assert.False(resumed.Reset);
            Assert.Equal("found", Assert.Single(resumed.Events, e => e.Subject.Id == offline).Change);
            if (!OperatingSystem.IsWindows())
            {
                // Owner-only from creation: the database, the -wal and -shm files SQLite made beside it, and the naming key.
                foreach (string file in new[] { journalPath, journalPath + "-wal", journalPath + "-shm", Path.Combine(Path.GetDirectoryName(journalPath)!, "events.key") })
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
        }

        // A lost journal starts a new epoch, in which every older cursor answers reset.
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(journalPath)!, "events.db*")) File.Delete(file);
        await using DaemonHost third = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var reader = new VericairnClient(third.Endpoint);
        Assert.True(reader.Events(after: cursor, wait: 0).Reset);
    }

    [Fact]
    public async Task FiltersNarrowByKindTypeAndSubjectAndRefuseWhatTheyCannotName()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        string cursor = client.Status().EventsCursor!;
        RecordWritten widget = Write(client, estate.OperatorId);
        RecordWritten other = Write(client, estate.OperatorId);
        WriteOutcome check = client.NewRecord(new NewRecordRequest("verification", estate.BuilderId, Json("""{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"passed","summary":"A check passed.","evidence":[],"limitations":[]}"""),
            Subject: "resource:" + widget.Id + ":" + widget.Hash));
        Assert.True(check.Ok, check.Problem?.Message);

        Assert.Equal([check.Written!.Id], client.Events(after: cursor, filter: new EventFilter(Kinds: ["record"], Types: ["verification"]), wait: 0).Events.Select(e => e.Subject.Id));
        EventsResponse about = client.Events(after: cursor, filter: new EventFilter(Kinds: ["record"], Subjects: [widget.Id]), wait: 0);
        Assert.Equal([widget.Id, check.Written.Id], about.Events.Select(e => e.Subject.Id));
        Assert.DoesNotContain(about.Events, e => e.Subject.Id == other.Id);
        Assert.Equal(new EventReference("resource", widget.Id, widget.Hash), Assert.Single(about.Events[1].Sources));
        Assert.Empty(client.Events(after: cursor, filter: new EventFilter(Kinds: ["ask"]), wait: 0).Events);

        foreach (EventFilter invalid in new[] { new EventFilter(Kinds: ["everything"]), new EventFilter(Categories: ["urgent"]), new EventFilter(Subjects: ["a b"]), new EventFilter(Types: [.. Enumerable.Range(0, 33).Select(i => "t" + i)]) })
            Assert.Equal(HttpStatusCode.BadRequest, Assert.Throws<HttpRequestException>(() => client.Events(filter: invalid, wait: 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, Assert.Throws<HttpRequestException>(() => client.Events(wait: 26)).StatusCode);
    }

    [Fact]
    public async Task PerPrincipalAttentionReachesOnlyItsPrincipal()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        string source = AttentionApiTests.SeedFailure(harness.Estate);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient agent = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        var attention = new EventFilter(Kinds: ["attention"]);
        AttentionResponse agentView = agent.AttentionGet();
        AttentionResponse operatorView = harness.Socket.AttentionGet(harness.Estate.OperatorId);
        Assert.NotNull(agentView.EventsCursor);
        Assert.NotNull(operatorView.EventsCursor);

        // The operator acknowledges: presentation state of the operator's alone.
        AttentionNotice notice = Assert.Single(operatorView.Items, i => i.SubjectId == source);
        Assert.True(harness.Socket.AttentionAcknowledge(new(harness.Estate.OperatorId, notice.Kind, notice.SubjectType, notice.SubjectId, notice.Revision)).Ok);
        EventsResponse operatorEvents = await harness.Socket.EventsAsync(after: operatorView.EventsCursor, filter: attention, wait: EventVocabulary.MaxWaitSeconds, cancellationToken: TestContext.Current.CancellationToken);
        EventEnvelope acknowledged = Assert.Single(operatorEvents.Events);
        Assert.Equal(("acknowledged", harness.Estate.OperatorId, "failed-check"), (acknowledged.Change, acknowledged.To, acknowledged.Category));
        Assert.Equal(("verification", source), (acknowledged.Subject.Type, acknowledged.Subject.Id));

        // The device of another principal hears nothing of it: its long poll waits out its time, then answers with no event.
        var waited = Stopwatch.StartNew();
        EventsResponse agentEvents = agent.Events(after: agentView.EventsCursor, filter: attention, wait: 2);
        Assert.Empty(agentEvents.Events);
        Assert.True(waited.Elapsed >= TimeSpan.FromSeconds(1.5), "A withheld event must not end a long poll early.");
        Assert.Null(agent.EventGet(acknowledged.Key));
        Assert.Null(agent.EventGet("sha256:" + new string('0', 64)));
        Assert.Equal(acknowledged.Key, harness.Socket.EventGet(acknowledged.Key)!.Key);

        // Its own acknowledgment reaches it, and still nothing of the operator's.
        AttentionNotice own = Assert.Single(agentView.Items, i => i.SubjectId == source);
        Assert.True(agent.AttentionAcknowledge(new(harness.Estate.BuilderId, own.Kind, own.SubjectType, own.SubjectId, own.Revision)).Ok);
        EventsResponse ownEvents = await agent.EventsAsync(after: agentEvents.Cursor, filter: attention, wait: EventVocabulary.MaxWaitSeconds, cancellationToken: TestContext.Current.CancellationToken);
        EventEnvelope mine = Assert.Single(ownEvents.Events);
        Assert.Equal(("acknowledged", harness.Estate.BuilderId), (mine.Change, mine.To));
        Assert.NotEqual(acknowledged.Key, mine.Key);
    }

    [Fact]
    public async Task AnEventAboutARecordTheReaderMayNotReadIsNeitherDeliveredNorCounted()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        string builder = estate.BuilderId;
        string readerCursor = client.Status().EventsCursor!;
        string builderCursor = client.Events(principal: builder, wait: 0).Cursor;
        RecordWritten hidden = Write(client, estate.OperatorId);
        // A narrower audience than today's: the builder may not read this one record.
        host.Service.EventReadRule = (principal, reference) => !(principal == builder && reference.Id == hidden.Id);
        RecordWritten visible = Write(client, estate.OperatorId);
        WriteOutcome resting = client.NewRecord(new NewRecordRequest("verification", estate.BuilderId, Json("""{"method":"test","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"fixture"},"role":"automation","outcome":"passed","summary":"Checked.","evidence":[],"limitations":[]}"""),
            Subject: "resource:" + hidden.Id + ":" + hidden.Hash));
        Assert.True(resting.Ok, resting.Problem?.Message);

        var records = new EventFilter(Kinds: ["record"]);
        EventsResponse operatorView = client.Events(after: readerCursor, filter: records, wait: 0);
        Assert.Equal([hidden.Id, visible.Id, resting.Written!.Id], operatorView.Events.Select(e => e.Subject.Id));

        // The builder hears only the visible record: neither the hidden one nor the record resting on it, and its subjects filter
        // cannot reach the hidden record through what relates to it.
        EventsResponse builderView = client.Events(after: builderCursor, filter: records, principal: builder, wait: 0);
        Assert.Equal([visible.Id], builderView.Events.Select(e => e.Subject.Id));
        Assert.Empty(client.Events(after: builderCursor, filter: new EventFilter(Subjects: [hidden.Id]), principal: builder, wait: 0).Events);
        using (JsonDocument raw = JsonDocument.Parse(JsonSerializer.Serialize(builderView, ProtocolJson.Options)))
            Assert.Equal(["events", "cursor", "reset", "problem"], raw.RootElement.EnumerateObject().Select(p => p.Name));

        // Unknown and unreadable keys are answered alike; the operator reads the same key.
        string hiddenKey = operatorView.Events[0].Key;
        Assert.Null(client.EventGet(hiddenKey, builder));
        Assert.Null(client.EventGet("sha256:" + new string('f', 64), builder));
        Assert.Equal(hidden.Id, client.EventGet(hiddenKey)!.Subject.Id);

        // Cursors reveal no position: two sealings of one position differ, and each resumes the same way.
        string first = builderView.Cursor;
        string second = client.Events(after: first, filter: records, principal: builder, wait: 0).Cursor;
        Assert.NotEqual(first, second);
        Assert.Empty(client.Events(after: second, filter: records, principal: builder, wait: 0).Events);

        // An SSE stream from the same cursor delivers the visible record only, then stays silent.
        await using var follower = new EventFollower(token => client.StreamEventsAsync(after: builderCursor, filter: records, principal: builder, cancellationToken: token));
        Assert.Null((await follower.NextAsync()).Event);
        Assert.Equal(visible.Id, (await follower.NextEventAsync()).Subject.Id);
        RecordWritten later = Write(client, estate.OperatorId);
        Assert.Equal(later.Id, (await follower.NextEventAsync()).Subject.Id);
    }

    [Fact]
    public async Task StreamsAuthenticateAsTheirProfileAndCloseWhenTheDeviceIsRevoked()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient agent = VericairnClient.LocalNetwork(paired, harness.Signer(paired));

        using (VericairnClient unsigned = VericairnClient.Loopback(harness.Host.Endpoint, null))
            Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => unsigned.Events(wait: 0)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => agent.Events(principal: harness.Estate.OperatorId, wait: 0)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => agent.EventGet("sha256:" + new string('0', 64), harness.Estate.OperatorId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => harness.Socket.Events(principal: "01NOTAPRINCIPAL0000000000", wait: 0)).StatusCode);
        Assert.False(agent.Events(wait: 0).Reset);

        await using var follower = new EventFollower(token => agent.StreamEventsAsync(cancellationToken: token));
        Assert.Null((await follower.NextAsync()).Event);
        WriteOutcome revoked = harness.Socket.DeviceRevoke(new RevokeRequest(paired.Device, harness.Estate.OperatorId, "The phone was lost."));
        Assert.True(revoked.Ok, revoked.Problem?.Message);

        // The stream closes before the next event, which is the revocation itself.
        Assert.True(await TestTiming.CompletesAsync(follower.Completion, TestContext.Current.CancellationToken), "The revoked device's stream stayed open.");
        Assert.DoesNotContain(follower.Received, item => item.Event is not null);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => agent.Events(wait: 0)).StatusCode);
    }

    [Fact]
    public async Task ASignedStreamEndsAfterSixtyMinutesAndTheWatchResumesFromItsCursor()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId);
        using VericairnClient agent = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        await using var follower = new EventFollower(token => agent.StreamEventsAsync(cancellationToken: token));
        Assert.Null((await follower.NextAsync()).Event);
        harness.Time.Advance(EventRoutes.SignedStreamLimit + TimeSpan.FromMinutes(1));
        Assert.True(await TestTiming.CompletesAsync(follower.Completion, TestContext.Current.CancellationToken), "The signed stream outlived its 60 minutes.");
    }

    [Theory]
    [InlineData("rotate")]
    [InlineData("withdraw-profile")]
    public async Task AStreamClosesWhenItsDeviceRotatesItsKeyOrLosesItsProfile(string change)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        WriteOutcome created = harness.Socket.DeviceCreate(new DeviceCreateRequest(harness.Estate.BuilderId, "Stream device", "file", ["local-network"]));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(device, harness.Estate.OperatorId, "A stream test device.")).Ok);
        DeviceRequestSigner signer = DeviceRequestSigner.Find(device)!;
        string publicKey = harness.Host.Service.Standing(device)!.PublicKey;

        // A key rotation is judged on loopback; a withdrawn profile on the local network, the profile it takes away.
        using VericairnClient client = change == "rotate"
            ? VericairnClient.Loopback(harness.Host.Endpoint, signer)
            : VericairnClient.LocalNetwork(new RuntimeConnection(Records.Identity.Ulid.NewUlid().Value, harness.Address, harness.Estate.Estate.Id, null,
                harness.Host.IdentityPublicKey!, device, publicKey, harness.Estate.BuilderId, null, ["local-network"], "2026-11-07T00:00:00Z"), signer);
        await using var follower = new EventFollower(token => client.StreamEventsAsync(cancellationToken: token));
        Assert.Null((await follower.NextAsync()).Event);

        WriteOutcome changed = change == "rotate" ? harness.Socket.DeviceRotate(new DeviceRotateRequest(device)) : WithdrawProfile(harness, device);
        Assert.True(changed.Ok, changed.Problem?.Message);
        Assert.True(await TestTiming.CompletesAsync(follower.Completion, TestContext.Current.CancellationToken), "The stream outlived its device's " + change + ".");
        Assert.DoesNotContain(follower.Received, item => item.Event is not null);
    }

    [Fact]
    public async Task AFaultingPassNeitherStopsThePumpNorFailsAReadNorTheDaemonsStop()
    {
        using var estate = new TestEstate(3);
        var log = new ConcurrentQueue<string>();
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, options: new DaemonOptions { Log = log.Enqueue }, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        await using var follower = new EventFollower(token => client.StreamEventsAsync(filter: new EventFilter(Kinds: ["record"]), cancellationToken: token));
        Assert.Null((await follower.NextAsync()).Event);

        // One pass throws what no narrower catch would name; the pump logs it and the next pass derives what that one missed.
        var faulted = TestTiming.Signal();
        int faults = 0;
        host.Service.EventPassStarting = () =>
        {
            if (Interlocked.Increment(ref faults) == 1)
            {
                faulted.TrySetResult();
                throw new ArithmeticException("Injected fault.");
            }
        };
        RecordWritten first = Write(client, estate.OperatorId);
        await faulted.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        RecordWritten second = Write(client, estate.OperatorId);
        Assert.Equal(first.Id, (await follower.NextEventAsync()).Subject.Id);
        Assert.Equal(second.Id, (await follower.NextEventAsync()).Subject.Id);
        Assert.Contains(log, line => line.Contains("could not derive", StringComparison.Ordinal) && line.Contains(nameof(ArithmeticException), StringComparison.Ordinal));

        // While every pass throws, a state read still answers, without a cursor, and a new reader is told the journal is unavailable.
        host.Service.EventPassStarting = () => throw new ArithmeticException("Injected fault.");
        StatusResponse status = client.Status();
        Assert.Null(status.EventsCursor);
        Assert.False(string.IsNullOrEmpty(status.Text));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, Assert.Throws<HttpRequestException>(() => client.Events(wait: 0)).StatusCode);
        host.Service.EventPassStarting = null;
        Assert.NotNull(client.Status().EventsCursor);
        await host.StopAsync();
    }

    [Fact]
    public void TheRuntimeCapHoldsDevicesToTheirPoolAndKeepsTheLocalSocketsReserve()
    {
        var slots = new EventStreamSlots();
        var held = new List<IDisposable?>();
        // Two principals with two devices each fill the devices' pool, each device at its own most.
        for (int principal = 0; principal < 2; principal++)
            for (int device = 0; device < 2; device++)
                for (int stream = 0; stream < EventRoutes.StreamsPerDevice; stream++)
                    held.Add(slots.TryAcquire($"device-{principal}-{device}", $"principal-{principal}"));
        Assert.All(held, Assert.NotNull);
        Assert.Equal(EventRoutes.StreamsPerRuntime, slots.Total);
        Assert.Null(slots.TryAcquire("device-new", "principal-new"));

        // The local socket's user still has its reserve, and no more while the pool is full.
        for (int i = 0; i < EventRoutes.LocalReserve; i++) held.Add(Assert.IsAssignableFrom<IDisposable>(slots.TryAcquire(null, "operator")));
        Assert.Null(slots.TryAcquire(null, "operator"));
        foreach (IDisposable? slot in held) slot!.Dispose();
        held.Clear();
        Assert.Equal(0, slots.Total);

        // One principal's devices hold at most half the pool, whichever device asks.
        for (int device = 0; device < 3; device++)
            for (int stream = 0; stream < EventRoutes.StreamsPerDevice; stream++)
                held.Add(slots.TryAcquire($"device-{device}", "principal"));
        Assert.Equal(EventRoutes.StreamsPerPrincipal, held.Count(h => h is not null));
        Assert.Null(slots.TryAcquire("device-9", "principal"));

        // With the pool free the local socket takes up to a device's most, from the pool.
        for (int i = 0; i < EventRoutes.StreamsPerDevice; i++) held.Add(Assert.IsAssignableFrom<IDisposable>(slots.TryAcquire(null, "operator")));
        Assert.Null(slots.TryAcquire(null, "operator"));
        foreach (IDisposable? slot in held) slot?.Dispose();
        Assert.Equal(0, slots.Total);
    }

    [Fact]
    public async Task ADeviceHoldsAtMostFourStreams()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        var followers = new List<EventFollower>();
        try
        {
            for (int i = 0; i < EventRoutes.StreamsPerDevice; i++)
            {
                var follower = new EventFollower(token => client.StreamEventsAsync(cancellationToken: token));
                followers.Add(follower);
                Assert.Null((await follower.NextAsync()).Event);
            }

            Assert.Equal(EventRoutes.StreamsPerDevice, host.EventStreams.Total);
            Assert.Equal(HttpStatusCode.TooManyRequests, Assert.Throws<HttpRequestException>(() => client.Events(wait: 0)).StatusCode);
            await followers[0].DisposeAsync();
            followers.RemoveAt(0);
            await WaitUntil(() => host.EventStreams.Total < EventRoutes.StreamsPerDevice, "The closed stream kept its slot.");
            Assert.NotNull(client.Events(wait: 0).Cursor);
        }
        finally
        {
            foreach (EventFollower follower in followers) await follower.DisposeAsync();
        }
    }

    [Fact]
    public async Task StateReadsReturnACursorFromWhichNothingIsMissed()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        string?[] cursors = [client.Status().EventsCursor, client.Progress().EventsCursor, client.AttentionGet(estate.OperatorId).EventsCursor, client.Asks().EventsCursor];
        Assert.All(cursors, Assert.NotNull);
        foreach (string? cursor in cursors)
        {
            Assert.False(client.Events(after: cursor, wait: 0).Reset);
        }

        // An asks read for a principal on the local socket seals its cursor for that principal, whose stream then continues from it.
        string asksForBuilder = client.Asks(estate.BuilderId).EventsCursor!;
        Assert.False(client.Events(after: asksForBuilder, principal: estate.BuilderId, wait: 0).Reset);
        Assert.True(client.Events(after: asksForBuilder, wait: 0).Reset);

        RecordWritten written = Write(client, estate.OperatorId);
        foreach (string? cursor in cursors)
        {
            Assert.Contains(client.Events(after: cursor, filter: new EventFilter(Kinds: ["record"]), wait: 0).Events, e => e.Subject.Id == written.Id);
        }

        // The embedded runtime keeps no journal: its reads carry no cursor, and its event read says so.
        using var other = new TestEstate(3);
        using EstateService embedded = EstateService.Open(other.Root);
        Assert.Null(embedded.Status().EventsCursor);
        Assert.Equal("unavailable", embedded.Events().Problem?.Code);
        using JsonDocument status = JsonDocument.Parse(JsonSerializer.Serialize(embedded.Status(), ProtocolJson.Options));
        Assert.False(status.RootElement.TryGetProperty("eventsCursor", out _));
    }

    [Fact]
    public void TheContractCoversTheStreamWithAScopeASchemaAndBothRoutes()
    {
        Assert.True(RequestScope.Covers("GET /v1/events"));
        Assert.True(RequestScope.Covers("GET /v1/events/{key}"));
        Assert.Contains(("GET", "/v1/events"), OpenApiDocument.Routes);
        Assert.Contains(("GET", "/v1/events/{key}"), OpenApiDocument.Routes);
        using JsonDocument api = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement get = api.RootElement.GetProperty("paths").GetProperty("/v1/events").GetProperty("get");
        Assert.Contains("text/event-stream", get.GetProperty("responses").GetProperty("200").GetProperty("content").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("never authority", get.GetProperty("responses").GetProperty("200").GetProperty("description").GetString(), StringComparison.Ordinal);
        string[] parameters = [.. get.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()!)];
        Assert.Equal(["after", "Last-Event-ID", "principal", "wait", "kind", "type", "subject", "to", "category"], parameters);
        JsonElement schemas = api.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(EventVocabulary.Format, schemas.GetProperty("EventEnvelope").GetProperty("properties").GetProperty("format").GetProperty("enum")[0].GetString());
        Assert.True(schemas.GetProperty("AttentionResponse").GetProperty("properties").TryGetProperty("eventsCursor", out _));
        foreach (string member in new[] { "Events", "EventsAsync", "EventGet", "EventGetAsync" })
            Assert.False(typeof(IEstateApi).GetMethod(member)!.IsAbstract);
    }

    private static RecordWritten Write(VericairnClient client, string by)
    {
        WriteOutcome outcome = client.NewRecord(new NewRecordRequest("resource", by, Json(Resource)));
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!;
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    /// <summary>A device revision that keeps only the embedded profile, written on the runtime's machine.</summary>
    private static WriteOutcome WithdrawProfile(LocalNetworkHarness harness, string device)
    {
        RecordResponse record = harness.Socket.Record(device)!;
        JsonObject body = JsonNode.Parse(record.Envelope.GetProperty("body").GetRawText())!.AsObject();
        body["profiles"] = new JsonArray("embedded");
        using JsonDocument changed = JsonDocument.Parse(body.ToJsonString());
        return harness.Socket.NewRecord(new NewRecordRequest("device", harness.Estate.BuilderId, changed.RootElement.Clone(), Supersedes: device));
    }

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        while (!condition())
        {
            try { await Task.Delay(20, guard.Token); }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { Assert.Fail(because); }
        }
    }
}

/// <summary>Reads an event stream on its own task into a channel, so a test waits for each item with a bounded wait.</summary>
internal sealed class EventFollower : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly Channel<EventStreamItem> items = Channel.CreateUnbounded<EventStreamItem>();
    private readonly List<EventStreamItem> received = [];

    public EventFollower(Func<CancellationToken, IAsyncEnumerable<EventStreamItem>> open)
    {
        Completion = Task.Run(async () =>
        {
            try
            {
                await foreach (EventStreamItem item in open(stop.Token))
                {
                    lock (received) received.Add(item);
                    await items.Writer.WriteAsync(item);
                }

                items.Writer.TryComplete();
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                // Stopped by the test: a platform may surface the cancelled read as an I/O failure.
                items.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                items.Writer.TryComplete(exception);
                throw;
            }
        });
    }

    /// <summary>Completes when the server ends the stream.</summary>
    public Task Completion { get; }

    public IReadOnlyList<EventStreamItem> Received
    {
        get { lock (received) return [.. received]; }
    }

    public async Task<EventStreamItem> NextAsync()
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        return await items.Reader.ReadAsync(guard.Token);
    }

    public async Task<EventEnvelope> NextEventAsync(Func<EventEnvelope, bool>? match = null)
    {
        while (true)
        {
            EventStreamItem item = await NextAsync();
            if (item.Event is EventEnvelope envelope && (match is null || match(envelope)))
            {
                return envelope;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try { await Completion; }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException or ObjectDisposedException) { }
        stop.Dispose();
    }
}
