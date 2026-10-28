// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RequestedProgressTests
{
    public static TheoryData<string> Values => new()
    {
        """{"type":"accurate","done":7,"total":20,"unit":"files"}""",
        """{"type":"best-effort","percent":60}""",
        """{"type":"not-measurable"}""",
        """{"type":"accurate","done":7,"total":20,"unit":"files","estimatePercent":60}""",
        """{"type":"accurate","done":7,"unit":"files"}""",
        """{"type":"accurate","done":0,"total":0,"unit":"files","estimatePercent":0}""",
        """{"type":"accurate","done":9007199254740991,"unit":"items","estimatePercent":100}""",
        """{"type":"best-effort","percent":0}""",
        """{"type":"best-effort","percent":100}""",
    };

    private static readonly string[] MalformedValues =
    [
        "null", "[]", "true", "3", "\"accurate\"", "{}", "{\"type\":null}", "{\"type\":3}", "{\"type\":\"unknown\"}",
        """{"type":"accurate","total":20,"unit":"files"}""",
        """{"type":"accurate","done":7,"total":20}""",
        """{"type":"accurate","done":7,"unit":" "}""",
        """{"type":"accurate","done":7,"unit":""}""",
        """{"type":"accurate","done":7,"unit":3}""",
        """{"type":"accurate","done":7,"unit":"files\nread"}""",
        """{"type":"accurate","done":7,"unit":null}""",
        """{"type":"accurate","done":-1,"total":20,"unit":"files"}""",
        """{"type":"accurate","done":21,"total":20,"unit":"files"}""",
        """{"type":"accurate","done":0,"total":-1,"unit":"files"}""",
        """{"type":"accurate","done":1.5,"unit":"files"}""",
        """{"type":"accurate","done":1,"total":1.5,"unit":"files"}""",
        """{"type":"accurate","done":1,"total":null,"unit":"files"}""",
        """{"type":"accurate","done":"1","unit":"files"}""",
        """{"type":"accurate","done":null,"unit":"files"}""",
        """{"type":"accurate","done":true,"unit":"files"}""",
        """{"type":"accurate","done":9007199254740992,"unit":"items"}""",
        """{"type":"accurate","done":1,"unit":"files","percent":50}""",
        """{"type":"accurate","done":1,"unit":"files","estimatePercent":101}""",
        """{"type":"accurate","done":1,"unit":"files","estimatePercent":-1}""",
        """{"type":"accurate","done":1,"unit":"files","estimatePercent":null}""",
        """{"type":"accurate","done":1,"unit":"files","estimatePercent":"60"}""",
        """{"type":"accurate","done":1,"unit":"files","estimatePercent":60.5}""",
        """{"type":"accurate","done":1,"unit":"files","extra":true}""",
        """{"type":"best-effort"}""",
        """{"type":"best-effort","percent":-1}""",
        """{"type":"best-effort","percent":101}""",
        """{"type":"best-effort","percent":60.5}""",
        """{"type":"best-effort","percent":1e1}""",
        """{"type":"best-effort","percent":"60"}""",
        """{"type":"best-effort","percent":null}""",
        """{"type":"best-effort","percent":false}""",
        """{"type":"best-effort","percent":50,"done":1}""",
        """{"type":"best-effort","percent":50,"total":10}""",
        """{"type":"best-effort","percent":50,"unit":"files"}""",
        """{"type":"best-effort","percent":50,"estimatePercent":60}""",
        """{"type":"not-measurable","percent":50}""",
        """{"type":"not-measurable","done":0}""",
        """{"type":"not-measurable","total":10}""",
        """{"type":"not-measurable","unit":"files"}""",
        """{"type":"not-measurable","estimatePercent":60}""",
        """{"type":"not-measurable","value":null}""",
        """{"type":"best-effort","percent":60,"percent":60}""",
    ];

    [Theory]
    [MemberData(nameof(Values))]
    public void EveryTypedClaimSurvivesLiveReopenAndExactlyOneFormatThreeCompletion(string value)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", ProgressRequestTests.FullRequest);
        var request = Request(fixture, value);
        int records;
        using (EstateService service = EstateService.Open(fixture.Estate.Root))
        {
            records = service.Session.Load().Records.Count;
            WorkProgressResponse active = service.WorkProgress(request);
            Assert.True(active.Ok, active.Problem?.Message);
            Assert.Null(active.Written);
            EqualValue(value, active.Claim!.Progress!.Value);
            Assert.Equal("Reading the widget.", active.Claim.StatusText);
            Assert.Equal(records, service.Session.Load().Records.Count);
            EqualValue(value, service.Progress().Json.GetProperty("agentClaims")[0].GetProperty("progress"));
        }
        using EstateService reopened = EstateService.Open(fixture.Estate.Root);
        EqualValue(value, reopened.Progress().Json.GetProperty("agentClaims")[0].GetProperty("progress"));
        WorkProgressRequest completed = request with { Completed = true };
        WorkProgressResponse first = reopened.WorkProgress(completed);
        Assert.True(first.Ok, first.Problem?.Message);
        Assert.Equal(first.Written, reopened.WorkProgress(completed).Written);
        Assert.Equal(records + 1, reopened.Session.Load().Records.Count);
        RecordResponse record = reopened.Record(first.Written!.Id)!;
        Assert.Equal("vericairn.record/3", record.Envelope.GetProperty("format").GetString());
        JsonElement body = record.Envelope.GetProperty("body");
        Assert.Equal(["completedAt", "note", "phase", "progress", "statusText"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        EqualValue(value, body.GetProperty("progress"));
        Assert.Equal(request.StatusText, body.GetProperty("statusText").GetString());
        Assert.False(reopened.WorkProgress(completed with { StatusText = "A changed claim." }).Ok);
        Assert.False(reopened.WorkProgress(completed with { Progress = null }).Ok);
        Assert.False(reopened.WorkProgress(completed with { Completed = false }).Ok);
        reopened.Session.Live.SetMeta("work-progress:" + fixture.Attempt.Id, "null");
        EqualValue(value, reopened.Progress().Json.GetProperty("agentClaims")[0].GetProperty("progress"));
        Assert.Equal(request.StatusText, reopened.Progress().Json.GetProperty("agentClaims")[0].GetProperty("statusText").GetString());
        Assert.DoesNotContain(reopened.Session.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void ToolRefusesEveryMalformedValueForBothStatesWithoutReplacingLiveStateOrWriting()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("brief", ProgressRequestTests.FullRequest);
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        ToolRegistry tools = Tools(fixture, service);
        ToolCallResult accepted = tools.Call("work_progress", Arguments(fixture, """{"type":"not-measurable"}"""));
        Assert.False(accepted.IsError, accepted.Text);
        int records = service.Session.Load().Records.Count;
        string? live = service.Session.Live.Meta("work-progress:" + fixture.Attempt.Id);
        foreach (string value in MalformedValues)
        {
            foreach (string state in new[] { "active", "completed" })
            {
                JsonObject arguments = Arguments(fixture, value);
                arguments["state"] = state;
                ToolCallResult refused = tools.Call("work_progress", arguments);
                Assert.True(refused.IsError, state + ": " + value + "\n" + refused.Text);
                Assert.Equal(records, service.Session.Load().Records.Count);
                Assert.Equal(live, service.Session.Live.Meta("work-progress:" + fixture.Attempt.Id));
            }
        }
        foreach (JsonNode? status in new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create(true), new JsonObject(),
            JsonValue.Create(""), JsonValue.Create("   "), JsonValue.Create("line\nbreak"), JsonValue.Create("line\rbreak"), JsonValue.Create(new string('x', 501)) })
        {
            JsonObject arguments = Arguments(fixture, """{"type":"not-measurable"}""");
            arguments["statusText"] = status;
            Assert.True(tools.Call("work_progress", arguments).IsError);
        }
        Assert.Equal(records, service.Session.Load().Records.Count);
        Assert.Equal(live, service.Session.Live.Meta("work-progress:" + fixture.Attempt.Id));
    }

    [Fact]
    public void ToolDoesNotSilentlyDropUnrecognizedClaimArgumentsOnAnUnrequestedAttempt()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        ToolRegistry tools = Tools(fixture, service);
        int records = service.Session.Load().Records.Count;
        JsonObject arguments = Arguments(fixture, """{"type":"not-measurable"}""");
        arguments.Remove("progress");
        arguments.Remove("statusText");
        arguments["percent"] = 60;
        Assert.True(tools.Call("work_progress", arguments).IsError);
        Assert.Null(service.Session.Live.Meta("work-progress:" + fixture.Attempt.Id));
        arguments.Remove("percent");
        Assert.False(tools.Call("work_progress", arguments).IsError);
        Assert.Equal(records, service.Session.Load().Records.Count);
    }

    [Theory]
    [InlineData("estate")]
    [InlineData("roster")]
    [InlineData("goal")]
    [InlineData("brief")]
    public void ToolEnforcesEveryScopeAndBothTypesOfAnAccompanyingEstimate(string scope)
    {
        using var fixture = new ProgressFixture();
        fixture.Set(scope, """{"types":["accurate"],"statusText":true,"cadence":{"minutes":10}}""");
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        ToolRegistry tools = Tools(fixture, service);
        Assert.True(tools.Call("work_progress", Arguments(fixture, """{"type":"best-effort","percent":60}""")).IsError);
        Assert.True(tools.Call("work_progress", Arguments(fixture, """{"type":"not-measurable"}""")).IsError);
        Assert.True(tools.Call("work_progress", Arguments(fixture, """{"type":"accurate","done":7,"unit":"files","estimatePercent":60}""")).IsError);
        JsonObject accurate = Arguments(fixture, """{"type":"accurate","done":7,"total":20,"unit":"files"}""");
        accurate.Remove("statusText");
        Assert.True(tools.Call("work_progress", accurate).IsError);
        accurate["statusText"] = "Reading the widget.";
        Assert.False(tools.Call("work_progress", accurate).IsError);
        accurate.Remove("progress");
        Assert.True(tools.Call("work_progress", accurate).IsError);
        fixture.Set("brief", ProgressRequestTests.DefaultRequest);
        JsonObject permissive = Arguments(fixture, """{"type":"accurate","done":7,"unit":"files","estimatePercent":60}""");
        permissive.Remove("statusText");
        ToolCallResult accepted = tools.Call("work_progress", permissive);
        Assert.False(accepted.IsError, accepted.Text);
        using JsonDocument response = JsonDocument.Parse(accepted.Text);
        Assert.Equal(60, response.RootElement.GetProperty("claim").GetProperty("progress").GetProperty("estimatePercent").GetInt32());
    }

    [Fact]
    public void CompletionRetryIgnoresLaterConfigurationButCannotChangeItsValueOrReopenThePhase()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        WorkProgressRequest request = Request(fixture, """{"type":"best-effort","percent":60}""") with { Completed = true };
        WorkProgressResponse first = service.WorkProgress(request);
        Assert.True(first.Ok, first.Problem?.Message);
        fixture.Set("estate", """{"types":["accurate"],"statusText":true,"cadence":{"minutes":1}}""");
        Assert.Equal(first.Written, service.WorkProgress(request).Written);
        Assert.False(service.WorkProgress(request with { Progress = RunEstate.Json("""{"type":"best-effort","percent":61}""") }).Ok);
        Assert.False(service.WorkProgress(request with { Phase = "another" }).Ok);
        Assert.DoesNotContain(service.Session.Load().Findings, f => f.Severity == Severity.Error);
        RunEstate.Ok(service.Result(new ResultRequest(fixture.Attempt.Id, fixture.Estate.BuilderId, RunEstate.Json("""{"outcome":"completed","summary":"Reviewed.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Read the result."}"""))));
        Assert.Equal(first.Written, service.WorkProgress(request).Written);
        Assert.False(service.WorkProgress(request with { Phase = "after-result", Completed = false }).Ok);
    }

    [Fact]
    public void CompletionRetriesRefuseMalformedNumbersEvenWhenTheyHaveTheSameNumericValue()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        ToolRegistry tools = Tools(fixture, service);
        JsonObject arguments = Arguments(fixture, """{"type":"best-effort","percent":60}""");
        arguments["state"] = "completed";
        ToolCallResult first = tools.Call("work_progress", arguments);
        Assert.False(first.IsError, first.Text);
        int records = service.Session.Load().Records.Count;
        foreach (string malformed in new[] { """{"type":"best-effort","percent":60.0}""", """{"type":"best-effort","percent":6e1}""", """{"type":"best-effort","percent":60,"percent":60}""" })
        {
            arguments["progress"] = JsonNode.Parse(malformed);
            Assert.True(tools.Call("work_progress", arguments).IsError, malformed);
        }
        arguments["progress"] = JsonNode.Parse("""{"percent":60,"type":"best-effort"}""");
        Assert.False(tools.Call("work_progress", arguments).IsError);
        Assert.Equal(records, service.Session.Load().Records.Count);
    }

    [Fact]
    public void NewStatusAndUnitFieldsPassTheSamePortableContentChecksAsNotes()
    {
        using var fixture = new ProgressFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        ToolRegistry tools = Tools(fixture, service);
        int records = service.Session.Load().Records.Count;
        string syntheticPath = string.Join('/', "", "Users", "example", Path.GetFileName(fixture.Estate.Root));
        JsonObject arguments = Arguments(fixture, """{"type":"not-measurable"}""");
        arguments["statusText"] = syntheticPath;
        Assert.True(tools.Call("work_progress", arguments).IsError);
        arguments["statusText"] = "Reading the widget.";
        arguments["progress"] = new JsonObject { ["type"] = "accurate", ["done"] = 7, ["unit"] = syntheticPath };
        Assert.True(tools.Call("work_progress", arguments).IsError);
        Assert.Equal(records, service.Session.Load().Records.Count);
        Assert.Null(service.Session.Live.Meta("work-progress:" + fixture.Attempt.Id));
    }

    [Fact]
    public void FormatTwoRejectsBothAdditionsAndUnconfiguredFormatThreeKeepsVoluntaryNotes()
    {
        foreach (long format in new[] { 2L, 3L })
        {
            using var fixture = new ProgressFixture(format);
            using EstateService service = EstateService.Open(fixture.Estate.Root);
            WorkProgressRequest request = Request(fixture, """{"type":"not-measurable"}""");
            Assert.Equal(format == 3, service.WorkProgress(request).Ok);
            Assert.Equal(format == 3, service.WorkProgress(request with { Progress = null }).Ok);
            WorkProgressResponse legacy = service.WorkProgress(request with { Progress = null, StatusText = null });
            Assert.True(legacy.Ok, legacy.Problem?.Message);
            JsonElement serialized = JsonSerializer.SerializeToElement(legacy.Claim, ProtocolJson.Options);
            Assert.False(serialized.TryGetProperty("progress", out _));
            Assert.False(serialized.TryGetProperty("statusText", out _));
            Assert.Empty(service.Records("work-progress"));
        }
    }

    internal static WorkProgressRequest Request(ProgressFixture fixture, string value) =>
        new(fixture.Attempt.Id, fixture.Estate.BuilderId, "review", "Reviewing the widget.")
            { Progress = RunEstate.Json(value), StatusText = "Reading the widget." };

    internal static JsonObject Arguments(ProgressFixture fixture, string value) => new()
    {
        ["attempt"] = fixture.Attempt.Id, ["phase"] = "review", ["note"] = "Reviewing the widget.", ["state"] = "active",
        ["progress"] = JsonNode.Parse(value), ["statusText"] = "Reading the widget.",
    };

    internal static ToolRegistry Tools(ProgressFixture fixture, EstateService service)
    {
        string authority = RunEstate.Ok(service.DeviceCreate(new DeviceCreateRequest(fixture.Estate.OperatorId, "Operator device", "file"))).Id;
        RunEstate.Ok(service.DeviceAdmit(new AdmitRequest(authority, fixture.Estate.OperatorId, "The first test authority.")));
        string device = RunEstate.Ok(service.DeviceCreate(new DeviceCreateRequest(fixture.Estate.BuilderId, "Contributor device", "file"))).Id;
        RunEstate.Ok(service.DeviceAdmit(new AdmitRequest(device, fixture.Estate.OperatorId, "Admit the test contributor.")));
        return new ToolRegistry(service, fixture.Estate.BuilderId);
    }

    internal static void EqualValue(string expected, JsonElement actual) => Assert.Equal(CanonicalJson.Serialize(RunEstate.Json(expected)), CanonicalJson.Serialize(actual));
}
