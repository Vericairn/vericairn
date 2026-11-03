// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;
using static Vericairn.Records.Tests.DecisionTimestampHistoryTests;
using static Vericairn.Runtime.Tests.DecisionTimestampWriteTests;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DecisionChronologyApiTests
{
    public DecisionChronologyApiTests() => DaemonTestHome.Ensure();

    [Theory]
    [InlineData("http")]
    [InlineData("sync")]
    [InlineData("async")]
    [InlineData("bound")]
    public async Task RecordsRouteAndClientsRefuseLaterTimesAndAllowEquality(string surface)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        using var http = new HttpClient(new DecisionNewApiTests.PinRouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        var request = new NewRecordRequest("decision", estate.OperatorId, Body(estate.OperatorId, Later), At: RecordedAt);

        WriteOutcome refused = await Submit(request);
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        AssertTimeError(refused.Problem!, Later, RecordedAt);
        Assert.Empty(service.Records("decision"));

        WriteOutcome accepted = await Submit(request with { Body = Body(estate.OperatorId, RecordedAt) });
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        Assert.Single(service.Records("decision"));

        async Task<WriteOutcome> Submit(NewRecordRequest input)
        {
            if (surface == "sync") return client.NewRecord(input);
            if (surface == "async") return await client.NewRecordAsync(input, TestContext.Current.CancellationToken);
            if (surface == "bound") return await bound.NewRecordAsync(input, TestContext.Current.CancellationToken);
            using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/records", UriKind.Relative), input,
                ProtocolJson.Options, TestContext.Current.CancellationToken);
            Assert.Equal(input.Body.GetProperty("decidedAt").GetString() == Later ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<WriteOutcome>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuidedRouteNamesBothTimesAndLeavesAllRecordsUnchanged(bool defaultDecisionTime)
    {
        using var fixture = new DecisionFixture();
        using var http = new HttpClient(new DecisionNewApiTests.PinRouteHandler(fixture.Service)) { BaseAddress = new Uri("http://fixture.invalid") };
        string later = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1));
        DecisionNewRequest request = fixture.Request;
        request = defaultDecisionTime ? request with { AnsweredAt = later } : request with { DecidedAt = later };
        var before = fixture.Records();

        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/decisions/new", UriKind.Relative), request,
            ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        DecisionNewResponse refused = (await response.Content.ReadFromJsonAsync<DecisionNewResponse>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Empty(refused.Pins);
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == "decision.record-time");
        Assert.Equal("error", finding.Severity);
        Assert.Contains(later, finding.Message, StringComparison.Ordinal);
        Assert.Matches("record's at [0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", finding.Message);
        Assert.Equal(before, fixture.Records());
    }

    [Fact]
    public async Task PreparationRefusesLaterTimesAndAnEqualTimeSurvivesDelayedSigning()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome created = service.DeviceCreate(new(estate.OperatorId, "Test signing device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the test device.")).Ok);
        string publicKey = EstateModel.BodyString(service.Session.Load().ById[device], "publicKey")!;
        var clock = new FixedTime(DateTimeOffset.UtcNow.AddSeconds(1));
        service.PreparedClock = clock;
        string at = RecordParser.FormatTimestamp(clock.Now);
        string later = RecordParser.FormatTimestamp(clock.Now.AddSeconds(1));
        using EstateService.RequestDeviceScope scope = service.ForDeviceRequest(device, publicKey);
        using var http = new HttpClient(new DecisionNewApiTests.PinRouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var client = new VericairnClient(http);
        var request = new NewRecordRequest("decision", estate.OperatorId, Body(estate.OperatorId, later));

        using HttpResponseMessage refusedResponse = await http.PostAsJsonAsync(new Uri("/v1/records/prepare", UriKind.Relative), request,
            ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedResponse.StatusCode);
        RecordPrepareResponse refused = (await refusedResponse.Content.ReadFromJsonAsync<RecordPrepareResponse>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        Assert.False(refused.Ok);
        Assert.Null(refused.Prepared);
        AssertTimeError(refused.Problem!, later, at);
        Assert.Empty(service.Records("decision"));

        RecordPrepareResponse prepared = await client.PrepareRecordAsync(request with { Body = Body(estate.OperatorId, at) }, TestContext.Current.CancellationToken);
        Assert.True(prepared.Ok, prepared.Problem?.Message);
        RecordPrepared plan = prepared.Prepared!;
        Assert.Equal(at, plan.At);
        clock.Now += TimeSpan.FromMinutes(1);
        var keys = new FileKeyStore(VericairnHome.Path());
        string signature = new StoreSigner(keys, device).Sign(RecordSigning.Digest(plan.Hash)).Signature;
        WriteOutcome written = await client.NewSignedRecordAsync(new(plan.Plan, signature), TestContext.Current.CancellationToken);
        Assert.True(written.Ok, written.Problem?.Message);
        RecordEnvelope decision = service.Session.Load().ById[written.Written!.Id];
        Assert.Equal(at, decision.At);
        Assert.Equal(at, EstateModel.BodyString(decision, "decidedAt"));
        Assert.Equal(plan.Hash, decision.ComputedHash);
        Assert.Equal(0, service.Validate().Errors);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
