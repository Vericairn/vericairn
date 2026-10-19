// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Sdk;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class DutyTransportTests
{
    [Theory]
    [InlineData("embedded")]
    [InlineData("sdk")]
    [InlineData("sdk-async")]
    [InlineData("bound")]
    [InlineData("mcp")]
    public async Task EveryTransportPreservesExactDutyBindingsAndTheReviewReadClosure(string transport)
    {
        using var f = new Fixture();
        JsonObject definition = f.Records.PolicyBody();
        definition["rules"]![0]!["parameters"]!["discharge"]!["records"] = new JsonArray(DutyProofEvaluationTests.VerificationClause());
        RecordEnvelope policy = f.Records.Policy(definition);
        RecordEnvelope attempt = f.Records.BeginAttempt();
        var duty = new ResultDutyDto(Ref(policy), "monthly", new("interval", Number: 0), Ref(f.Records.Estate.Estate),
            new([], [], [Ref(policy)]), "The declared inventory and exact proof were checked.", [new("restore", Kind: "result-verification")]);
        JsonObject body = ResultBody(duty);
        Assert.False(body["duties"]![0]!["occurrence"]!.AsObject().ContainsKey("at"));
        Assert.False(body["duties"]![0]!["proofs"]![0]!.AsObject().ContainsKey("records"));
        using var client = Client(f.Api);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        var resultRequest = new ResultRequest(attempt.Id, attempt.By, PolicyFixture.Element(body), attempt.ComputedHash);
        f.Advance();
        WriteOutcome submitted = transport switch
        {
            "sdk" => client.Result(resultRequest),
            "sdk-async" => await client.ResultAsync(resultRequest, TestContext.Current.CancellationToken),
            "bound" => bound.Result(resultRequest),
            "mcp" => await Mcp(f.Api, attempt.By, "work_result", new() { ["attempt"] = attempt.Id, ["expected"] = attempt.ComputedHash, ["body"] = body.DeepClone() }),
            _ => await ((IEstateApi)f.Api).ResultAsync(resultRequest, TestContext.Current.CancellationToken),
        };
        RecordEnvelope result = f.Model.ById[Ok(submitted).Id];
        Assert.Equal(CanonicalJson.Serialize(PolicyFixture.Element(body)), CanonicalJson.Serialize(result.Body));
        Assert.True(SignatureChecks.IsAuthentic(f.Model, result));
        Assert.False(Duty(f.Api).Completed);
        f.Records.Estate.SetClock(result.AtTime.AddSeconds(1));
        RecordEnvelope verification = f.Records.Add("verification", DutyProofEvaluationTests.VerificationBody(), subject: result.Reference);
        f.Advance();
        var review = new ResultReviewBody("operator", "pass", "Inspected the exact result and proof.", [], []);
        // A passing review which did not read the exact result verification does not discharge the occurrence.
        Ok(f.Api.ReviewResult(new(result.Id, f.Operator, JsonSerializer.SerializeToElement(review, ProtocolJson.Options), result.ComputedHash)));
        Assert.False(Duty(f.Api).Completed);
        f.Advance();
        JsonElement reviewed = JsonSerializer.SerializeToElement(review with { Read = [Ref(verification)] }, ProtocolJson.Options);
        var reviewRequest = new ResultReviewRequest(result.Id, f.Operator, reviewed, result.ComputedHash);
        WriteOutcome passed = transport switch
        {
            "sdk" => client.ReviewResult(reviewRequest),
            "sdk-async" => await client.ReviewResultAsync(reviewRequest, TestContext.Current.CancellationToken),
            "bound" => bound.ReviewResult(reviewRequest),
            "mcp" => await Mcp(f.Api, f.Operator, "work_review", new() { ["result"] = result.Id, ["expected"] = result.ComputedHash, ["body"] = JsonNode.Parse(reviewed.GetRawText()) }),
            _ => await ((IEstateApi)f.Api).ReviewResultAsync(reviewRequest, TestContext.Current.CancellationToken),
        };
        RecordEnvelope writtenReview = f.Model.ById[Ok(passed).Id];
        Assert.True(SignatureChecks.IsAuthentic(f.Model, writtenReview));
        Assert.Equal(CanonicalJson.Serialize(reviewed), CanonicalJson.Serialize(writtenReview.Body));
        DutyFindingDto complete = Duty(f.Api);
        Assert.True(complete.Completed);
        Assert.Contains(complete.Completions, c => c.Result.Hash == result.ComputedHash && c.Evidence.Any(e => e.Hash == verification.ComputedHash));
        Assert.Empty(f.Model.OfType("acceptance"));
        Assert.Empty(f.Model.OfType("release"));
    }

    [Fact]
    public void StaleHashesMalformedBindingsAndBackdatedGenericWritesCreateNoEvidence()
    {
        using var f = new Fixture();
        RecordEnvelope policy = f.Records.Policy();
        RecordEnvelope attempt = f.Records.BeginAttempt();
        JsonObject body = ResultBody(JsonSerializer.Deserialize<ResultDutyDto>(f.Records.DutyLink(policy).ToJsonString(), ProtocolJson.Options)!);
        JsonElement result = PolicyFixture.Element(body);
        f.Advance();
        int before = f.Model.Records.Count;
        Assert.Equal("conflict", f.Api.Result(new(attempt.Id, attempt.By, result)).Problem!.Code);
        Assert.Equal("conflict", f.Api.Result(new(attempt.Id, attempt.By, result, "stale")).Problem!.Code);
        string subject = "attempt:" + attempt.Id + ":" + attempt.ComputedHash;
        Assert.Equal("conflict", f.Api.NewRecord(new("result", attempt.By, result, subject)).Problem!.Code);
        Assert.False(f.Api.NewRecord(new("result", attempt.By, result, subject, At: attempt.At, Expected: attempt.ComputedHash)).Ok);
        body["duties"]![0]!["proofs"] = new JsonArray(new JsonObject { ["clause"] = "invented", ["records"] = new JsonArray() });
        Assert.False(f.Api.Result(new(attempt.Id, attempt.By, PolicyFixture.Element(body), attempt.ComputedHash)).Ok);
        Assert.Equal(before, f.Model.Records.Count);
        RecordWritten evidence = Ok(f.Api.NewRecord(new("result", attempt.By, result, subject, Expected: attempt.ComputedHash)));
        RecordEnvelope written = f.Model.ById[evidence.Id];
        before = f.Model.Records.Count;
        JsonElement review = JsonSerializer.SerializeToElement(new ResultReviewBody("operator", "pass", "Inspected the result.", [], []), ProtocolJson.Options);
        Assert.Equal("conflict", f.Api.ReviewResult(new(written.Id, f.Operator, review, "stale")).Problem!.Code);
        Assert.False(f.Api.NewRecord(new("review", f.Operator, review, "result:" + written.Id + ":" + written.ComputedHash, At: written.At, Expected: written.ComputedHash)).Ok);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public async Task ARevokedActingDeviceCannotReviewThroughMcp()
    {
        using var f = new Fixture();
        RecordEnvelope result = f.Records.Result();
        var registry = new ToolRegistry(f.Api, f.Operator);
        Assert.Contains(await registry.VisibleAsync(TestContext.Current.CancellationToken), t => t.Name == "work_review");
        RecordEnvelope device = f.Model.ById[f.OperatorDevice];
        f.Records.Add("admission", new() { ["principal"] = f.Operator, ["status"] = "revoked", ["note"] = "Fixture revocation." }, subject: device.Reference);
        int before = f.Model.Records.Count;
        ToolCallResult refused = await registry.CallAsync("work_review", new() { ["result"] = result.Id, ["expected"] = result.ComputedHash,
            ["body"] = JsonSerializer.SerializeToNode(new ResultReviewBody("operator", "pass", "Reviewed.", [], []), ProtocolJson.Options) }, TestContext.Current.CancellationToken);
        Assert.True(refused.IsError);
        Assert.Equal(before, f.Model.Records.Count);
    }

    [Fact]
    public async Task AWorkerCannotListOrCallTheResultJudgmentTool()
    {
        using var f = new Fixture();
        RecordEnvelope result = f.Records.Result();
        var worker = new ToolRegistry(f.Api, f.Records.Estate.BuilderId);
        Assert.True(await worker.WritesVisibleAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(await worker.VisibleAsync(TestContext.Current.CancellationToken), t => t.Name == "work_review");
        int before = f.Model.Records.Count;
        ToolCallResult refused = await worker.CallAsync("work_review", new()
        {
            ["result"] = result.Id, ["expected"] = result.ComputedHash,
            ["body"] = JsonSerializer.SerializeToNode(new ResultReviewBody("self", "pass", "A worker claim cannot become a judgment through MCP.", [], []), ProtocolJson.Options),
        }, TestContext.Current.CancellationToken);
        Assert.True(refused.Unknown);
        Assert.True(refused.IsError);
        Assert.Equal(before, f.Model.Records.Count);
    }

    private static JsonObject ResultBody(ResultDutyDto duty) => JsonSerializer.SerializeToNode(new
    {
        outcome = "completed", summary = "Checked the exact inventory.", outputs = Array.Empty<object>(), evidence = Array.Empty<object>(),
        checks = new[] { new { name = "inventory", outcome = "passed", summary = "Inventory checked." } }, limitations = Array.Empty<string>(),
        unresolvedEffects = Array.Empty<string>(), nextStep = "Review the exact result.", duties = new[] { duty },
    }, ProtocolJson.Options)!.AsObject();

    private static DutyFindingDto Duty(EstateService api) => Assert.Single(api.Policy("maintenance")!.Findings, f => f.Duty?.Occurrence.GetProperty("number").GetInt64() == 0).Duty!;
    private static PolicyRecordReference Ref(RecordEnvelope record) => new(record.Type, record.Id, record.ComputedHash);
    private static RecordWritten Ok(WriteOutcome outcome) { Assert.True(outcome.Ok, JsonSerializer.Serialize(outcome, ProtocolJson.Options)); return outcome.Written!; }
    private static async Task<WriteOutcome> Mcp(EstateService api, string by, string tool, JsonObject arguments)
    {
        ToolCallResult call = await new ToolRegistry(api, by).CallAsync(tool, arguments, TestContext.Current.CancellationToken);
        Assert.False(call.IsError, call.Text);
        return JsonSerializer.Deserialize<WriteOutcome>(call.Text, ProtocolJson.Options)!;
    }

    private static VericairnClient Client(EstateService api) => new(new HttpClient(new Handler(api)) { BaseAddress = new Uri("http://fixture.invalid") });
    private sealed class Handler(EstateService api) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WriteOutcome outcome = request.RequestUri!.AbsolutePath switch
            {
                "/v1/work/result" => api.Result((await request.Content!.ReadFromJsonAsync<ResultRequest>(ProtocolJson.Options, cancellationToken))!),
                "/v1/work/review" => api.ReviewResult((await request.Content!.ReadFromJsonAsync<ResultReviewRequest>(ProtocolJson.Options, cancellationToken))!),
                _ => throw new InvalidOperationException("Unexpected transport route."),
            };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(outcome, options: ProtocolJson.Options) };
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        internal PolicyFixture Records { get; } = new();
        internal EstateService Api { get; }
        private Clock Time { get; } = new();
        internal string Operator => Records.Estate.OperatorId;
        internal string OperatorDevice { get; } = "";
        internal EstateModel Model => Api.Session.Load();
        internal Fixture()
        {
            DateTimeOffset setup = Records.Estate.Clock;
            Api = EstateService.Open(Records.Estate.Root);
            foreach (string principal in new[] { Operator, Records.Estate.BuilderId })
            {
                RecordWritten device = Ok(Api.DeviceCreate(new(principal, "Fixture evidence signer", "file")));
                if (principal == Operator) OperatorDevice = device.Id;
                Records.Estate.SetClock(Model.ById[device.Id].AtTime.AddSeconds(1));
                Records.Add("admission", new() { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Fixture evidence signer admission." }, subject: Model.ById[device.Id].Reference);
            }
            Api.PolicyClock = Time;
            Advance();
            Records.Estate.SetClock(setup);
        }
        internal void Advance() { Time.Now = Model.Records.Max(r => r.AtTime).AddSeconds(1); Records.Estate.SetClock(Time.Now); }
        public void Dispose() { Api.Dispose(); Records.Dispose(); }
    }
}
