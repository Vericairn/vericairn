// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class WorkNewTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FieldsCreateTheSameBodyAsRecordsNewAndTheActingPrincipalsDeviceSigns(long format)
    {
        using var estate = new TestEstate(format);
        using EstateService service = EstateService.Open(estate.Root);
        string device = WorkNewCases.AdmitBuilder(estate, service);
        RecordWritten receipt = WorkNewCases.Written(service.WorkNew(WorkNewCases.Valid(estate.BuilderId)));
        RecordEnvelope record = service.Session.Load().ById[receipt.Id];
        Assert.Equal("work-order", record.Type);
        Assert.Equal(estate.BuilderId, record.By);
        Assert.Equal(device, Assert.Single(record.Signatures).Device);
        Assert.Equal(receipt.Hash, record.ComputedHash);
        Assert.Equal(receipt.Path, record.ExpectedPath);
        Assert.Equal(CanonicalJson.SerializePretty(WorkNewCases.Body()), CanonicalJson.SerializePretty(record.Body));
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void RepeatedFieldsKeepTheirOrderAndReferencesRemainRecordIds()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string device = WorkNewCases.AdmitBuilder(estate, service);
        RecordWritten first = WorkNewCases.Written(service.WorkNew(WorkNewCases.Valid(estate.BuilderId) with { Key = "W-1" }));
        RecordWritten second = WorkNewCases.Written(service.WorkNew(WorkNewCases.Valid(estate.BuilderId) with { Key = "W-2" }));
        var request = WorkNewCases.Valid(estate.BuilderId) with
        {
            Key = "M-1", Kind = "milestone", Title = new string('x', 200),
            Scope = ["widget sources", "widget tests"], Deliverables = ["widget.txt", "test results"],
            After = [first.Id, second.Id], Inputs = ["work-order:" + first.Id, "principal:" + estate.OperatorId],
        };
        RecordWritten receipt = WorkNewCases.Written(service.WorkNew(request));
        JsonElement envelope = service.Record(receipt.Id)!.Envelope;
        JsonElement body = envelope.GetProperty("body");
        Assert.Equal("milestone", body.GetProperty("kind").GetString());
        Assert.Equal(request.Title, body.GetProperty("title").GetString());
        Assert.Equal(request.Scope, body.GetProperty("scope").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(request.Deliverables, body.GetProperty("deliverables").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(request.After, body.GetProperty("after").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal([("work-order", first.Id), ("principal", estate.OperatorId)], body.GetProperty("inputs").EnumerateArray()
            .Select(v => (v.GetProperty("type").GetString(), v.GetProperty("id").GetString())));
        Assert.Equal(device, Assert.Single(envelope.GetProperty("signatures").EnumerateArray()).GetProperty("device").GetString());
        Assert.Equal(0, service.Validate().Errors);
    }

    [Theory]
    [MemberData(nameof(WorkNewCases.Refusals), MemberType = typeof(WorkNewCases))]
    public void InvalidFieldsHaveTheSameRefusalAndPublishNothing(string fault, string finding)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        (WorkNewRequest request, JsonElement body) = WorkNewCases.Invalid(fault, estate.OperatorId);
        int before = service.Session.Load().Records.Count;
        WriteOutcome fields = service.WorkNew(request);
        WriteOutcome generic = service.NewRecord(new NewRecordRequest("work-order", request.By, body));
        WorkNewCases.SameRefusal(generic, fields);
        Assert.Contains(fields.Problem!.Findings, f => f.Code == finding);
        Assert.Equal(before, service.Session.Load().Records.Count);
    }

    [Fact]
    public void DuplicateKeysAndAnInvalidAuthorUseTheExistingRefusals()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        var request = WorkNewCases.Valid(estate.OperatorId);
        WorkNewCases.Written(service.WorkNew(request));
        WorkNewCases.SameRefusal(service.NewRecord(new NewRecordRequest("work-order", request.By, WorkNewCases.Body())), service.WorkNew(request));
        WorkNewCases.SameRefusal(service.NewRecord(new NewRecordRequest("work-order", "invalid", WorkNewCases.Body())), service.WorkNew(request with { By = "invalid" }));
        Assert.Single(service.Session.Load().OfType("work-order"));
    }

    [Fact]
    public void APrincipalWithoutAnAdmittedKeyHasTheSameReadOnlyRefusal()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string device = WorkNewCases.Written(service.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator device", "file"))).Id;
        WorkNewCases.Written(service.DeviceAdmit(new AdmitRequest(device, estate.OperatorId, "First authority.")));
        JsonNode charter = JsonNode.Parse(estate.Estate.Body.GetRawText())!;
        charter["policies"]!["signatures"] = "required";
        WorkNewCases.Written(service.NewRecord(new NewRecordRequest("estate", estate.OperatorId, JsonSerializer.SerializeToElement(charter), Supersedes: estate.Estate.Id)));
        int before = service.Session.Load().Records.Count;
        WriteOutcome refused = service.WorkNew(WorkNewCases.Valid(estate.BuilderId));
        WorkNewCases.SameRefusal(service.NewRecord(new NewRecordRequest("work-order", estate.BuilderId, WorkNewCases.Body())), refused);
        Assert.Contains("read-only", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, service.Session.Load().Records.Count);
    }

    [Fact]
    public async Task CancellationBeforeDispatchPublishesNothing()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        IEstateApi api = service;
        await Assert.ThrowsAsync<ClientCancelledException>(() => api.WorkNewAsync(WorkNewCases.Valid(estate.OperatorId), stop.Token));
        Assert.Empty(service.Session.Load().OfType("work-order"));
    }
}

/// <summary>Independent record bodies and field requests shared by the command and API parity tests.</summary>
public static class WorkNewCases
{
    public const string UnknownId = "01ARZ3NDEKTSV4RRFFQ69G5FAV";

    public static WorkNewRequest Valid(string by) => new(by, "W-1", "Build the widget", "Make a useful widget.", ["widget"], "Does it work?", ["widget.txt"]);

    public static JsonElement Body() => JsonSerializer.Deserialize<JsonElement>("""{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Make a useful widget.","scope":["widget"],"exitQuestion":"Does it work?","inputs":[],"deliverables":["widget.txt"]}""");

    public static TheoryData<string, string> Refusals => new()
    {
        { "missing-key", "body.missing" }, { "long-title", "body.length" }, { "unknown-after", "body.reference" },
        { "malformed-input", "record.reference" }, { "input-without-separator", "record.reference" },
        { "input-with-extra-separator", "record.reference" }, { "input-without-type", "record.reference" },
        { "invalid-key", "body.pattern" }, { "invalid-kind", "body.enum" }, { "empty-scope", "body.empty" },
        { "empty-deliverables", "body.empty" }, { "unknown-input", "body.reference" },
        { "multiple-errors", "body.missing" },
    };

    public static (WorkNewRequest Request, JsonElement Body) Invalid(string fault, string by)
    {
        WorkNewRequest request = Valid(by);
        JsonObject body = JsonNode.Parse(Body().GetRawText())!.AsObject();
        switch (fault)
        {
            case "missing-key":
                request = request with { Key = null }; body.Remove("key"); break;
            case "long-title":
                request = request with { Title = new string('x', 201) }; body["title"] = new string('x', 201); break;
            case "unknown-after":
                request = request with { After = [UnknownId] }; body["after"] = new JsonArray(UnknownId); break;
            case "malformed-input":
                request = request with { Inputs = ["work-order:not-an-id"] };
                body["inputs"] = JsonNode.Parse("""[{"type":"work-order","id":"not-an-id"}]"""); break;
            case "input-without-separator":
                request = request with { Inputs = ["work-order"] };
                body["inputs"] = JsonNode.Parse("""[{"type":"work-order","id":""}]"""); break;
            case "input-with-extra-separator":
                request = request with { Inputs = ["work-order:" + UnknownId + ":extra"] };
                body["inputs"] = JsonNode.Parse($$"""[{"type":"work-order","id":"{{UnknownId}}:extra"}]"""); break;
            case "input-without-type":
                request = request with { Inputs = [":" + UnknownId] };
                body["inputs"] = JsonNode.Parse($$"""[{"type":"","id":"{{UnknownId}}"}]"""); break;
            case "invalid-key":
                request = request with { Key = "lower-case" }; body["key"] = "lower-case"; break;
            case "invalid-kind":
                request = request with { Kind = "unknown" }; body["kind"] = "unknown"; break;
            case "empty-scope":
                request = request with { Scope = [] }; body["scope"] = new JsonArray(); break;
            case "empty-deliverables":
                request = request with { Deliverables = [] }; body["deliverables"] = new JsonArray(); break;
            case "unknown-input":
                request = request with { Inputs = ["work-order:" + UnknownId] };
                body["inputs"] = JsonNode.Parse($$"""[{"type":"work-order","id":"{{UnknownId}}"}]"""); break;
            case "multiple-errors":
                request = request with { Key = null, Title = new string('x', 201), Inputs = ["work-order:not-an-id"] };
                body.Remove("key"); body["title"] = new string('x', 201);
                body["inputs"] = JsonNode.Parse("""[{"type":"work-order","id":"not-an-id"}]"""); break;
            default: throw new ArgumentException("Unknown refusal case.", nameof(fault));
        }
        return (request, JsonSerializer.SerializeToElement(body));
    }

    public static RecordWritten Written(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message + " " + JsonSerializer.Serialize(outcome.Problem?.Findings));
        return Assert.IsType<RecordWritten>(outcome.Written);
    }

    public static string AdmitBuilder(TestEstate estate, EstateService service)
    {
        string op = Written(service.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator device", "file"))).Id;
        Written(service.DeviceAdmit(new AdmitRequest(op, estate.OperatorId, "First authority.")));
        string builder = Written(service.DeviceCreate(new DeviceCreateRequest(estate.BuilderId, "Builder device", "file"))).Id;
        Written(service.DeviceAdmit(new AdmitRequest(builder, estate.OperatorId, "Admit the builder.")));
        return builder;
    }

    public static void SameRefusal(WriteOutcome expected, WriteOutcome actual)
    {
        Assert.False(expected.Ok);
        Assert.False(actual.Ok);
        Assert.Null(expected.Written);
        Assert.Null(actual.Written);
        Assert.Equal(expected.Problem!.Code, actual.Problem!.Code);
        Assert.Equal(expected.Problem.Message, actual.Problem.Message);
        Assert.Equal(Findings(expected.Problem), Findings(actual.Problem));
    }

    private static IEnumerable<(string Severity, string Code, string Field, string Message)> Findings(Problem problem) =>
        problem.Findings.Select(f => (f.Severity, f.Code, f.Location.StartsWith("records/work-order/", StringComparison.Ordinal)
            ? (f.Location.Contains('#', StringComparison.Ordinal) ? f.Location[f.Location.IndexOf('#', StringComparison.Ordinal)..] : "work-order") : f.Location, f.Message));
}
