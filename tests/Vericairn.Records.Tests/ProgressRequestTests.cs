// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class ProgressRequestTests
{
    public const string DefaultRequest = """{"cadence":{"perStep":true}}""";
    public const string FullRequest = """{"cadence":{"minutes":10,"perStep":true},"statusText":true,"types":["accurate","best-effort","not-measurable"]}""";

    [Theory]
    [InlineData("estate")]
    [InlineData("roster")]
    [InlineData("goal")]
    [InlineData("brief")]
    public void OptionalRequestRoundTripsAtEveryScopeWithAStableGoldenBody(string scope)
    {
        using var fixture = new ProgressFixture();
        RecordEnvelope record = fixture.Set(scope, FullRequest);
        EstateModel reopened = fixture.Estate.Load();
        Assert.DoesNotContain(reopened.Findings, f => f.Severity == Severity.Error);
        Assert.Equal(3, record.Format);
        ProgressRequestSelection selected = ProgressRequests.Resolve(reopened, fixture.Assignment);
        Assert.Null(selected.Problem);
        Assert.Equal(scope, selected.Scope);
        Assert.Equal(record.Id, selected.Record);
        Assert.Equal(record.ComputedHash, selected.Hash);
        Assert.Equal(FullRequest, CanonicalJson.Serialize(selected.Request!.Value));
        Assert.Contains("### Requested progress", WorkView.RenderBrief(reopened, fixture.Order), StringComparison.Ordinal);
        using JsonDocument brief = JsonDocument.Parse(WorkView.RenderBriefJson(reopened, fixture.Order));
        Assert.Equal(FullRequest, CanonicalJson.Serialize(brief.RootElement.GetProperty("assignments")[0].GetProperty("progressRequest")));
    }

    [Fact]
    public void MostSpecificRequestReplacesTheWholeBroaderRequestAndOmissionInherits()
    {
        using var fixture = new ProgressFixture();
        Assert.Null(ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment).Request);
        fixture.Set("estate", FullRequest);
        fixture.Set("roster", """{"types":["best-effort"],"statusText":true,"cadence":{"minutes":10}}""");
        fixture.Set("goal", """{"types":["not-measurable"],"cadence":{"perStep":true}}""");
        fixture.Set("brief", DefaultRequest);
        ProgressRequestSelection selected = ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment);
        Assert.Equal("brief", selected.Scope);
        Assert.Equal(DefaultRequest, CanonicalJson.Serialize(selected.Request!.Value));
        Assert.False(selected.Request.Value.TryGetProperty("statusText", out _));
        Assert.False(selected.Request.Value.TryGetProperty("types", out _));
        foreach ((string removed, string winner) in new[] { ("brief", "goal"), ("goal", "roster"), ("roster", "estate") })
        {
            fixture.Set(removed, null);
            Assert.Equal(winner, ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment).Scope);
        }
        fixture.Set("estate", null);
        Assert.Null(ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment).Request);
    }

    [Fact]
    public void RosterRequestsFollowTheStepsRoleAndTheNamedRostersCurrentRevision()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("estate", DefaultRequest);
        JsonObject goal = JsonNode.Parse(fixture.Goal.Body.GetRawText())!.AsObject();
        goal["roster"] = fixture.Roster.Id;
        fixture.Estate.Add("goal", fixture.Estate.OperatorId, goal.ToJsonString(), supersedes: fixture.Goal.Id);
        JsonObject roster = JsonNode.Parse(fixture.Roster.Body.GetRawText())!.AsObject();
        roster["roles"]![1]!["progressRequest"] = JsonNode.Parse(FullRequest);
        RecordEnvelope unrelated = fixture.Estate.Add("roster", fixture.Estate.OperatorId, roster.ToJsonString(), supersedes: fixture.Roster.Id);
        Assert.Equal("estate", ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment).Scope);
        roster["roles"]![0]!["progressRequest"] = JsonNode.Parse(FullRequest);
        RecordEnvelope revised = fixture.Estate.Add("roster", fixture.Estate.OperatorId, roster.ToJsonString(), supersedes: unrelated.Id);
        ProgressRequestSelection selected = ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment);
        Assert.Equal("roster", selected.Scope);
        Assert.Equal(revised.Id, selected.Record);
        Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("estate")]
    [InlineData("roster")]
    [InlineData("goal")]
    [InlineData("brief")]
    public void FormatTwoRefusesNewRequestsWhileAnUnconfiguredEstateStaysValid(string scope)
    {
        using var fixture = new ProgressFixture(2);
        Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
        fixture.Set(scope, FullRequest);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "body.format" && f.Location.EndsWith(".progressRequest", StringComparison.Ordinal));
    }

    [Fact]
    public void CompletionGoldenAddsOnlyOptionalFieldsInFormatThreeAndPreservesTheOldBody()
    {
        const string legacy = """{"completedAt":"2026-10-13T12:01:00Z","note":"Read the widget.","phase":"review"}""";
        const string typed = """{"completedAt":"2026-10-13T12:01:00Z","note":"Read the widget.","phase":"review","progress":{"done":7,"estimatePercent":60,"total":20,"type":"accurate","unit":"files"},"statusText":"Read seven of twenty files."}""";
        foreach (long format in new[] { 2L, 3L })
        {
            using var fixture = new ProgressFixture(format);
            RecordEnvelope original = fixture.Estate.Add("work-progress", fixture.Estate.BuilderId, legacy, fixture.Attempt.Reference);
            Assert.Equal(legacy, CanonicalJson.Serialize(fixture.Estate.Load().ById[original.Id].Body));
            Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
            RecordEnvelope anotherAttempt = fixture.Estate.Add("attempt", fixture.Estate.BuilderId, fixture.Attempt.Body.GetRawText(), fixture.Assignment.Reference);
            RecordEnvelope added = fixture.Estate.Add("work-progress", fixture.Estate.BuilderId, typed, anotherAttempt.Reference);
            Assert.Equal(typed, CanonicalJson.Serialize(fixture.Estate.Load().ById[added.Id].Body));
            if (format == 2)
            {
                Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "body.format" && f.Location.EndsWith(".progress", StringComparison.Ordinal));
                Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "body.format" && f.Location.EndsWith(".statusText", StringComparison.Ordinal));
            }
            else Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
        }
    }

    [Fact]
    public void ConflictingGoalsNeedASelectionWhileAnExplicitBriefWinsAndEqualRequestsRemainUsable()
    {
        using var fixture = new ProgressFixture();
        fixture.Set("goal", DefaultRequest);
        JsonObject body = JsonNode.Parse(fixture.Goal.Body.GetRawText())!.AsObject();
        RecordEnvelope another = fixture.Estate.Add("goal", fixture.Estate.OperatorId, body.ToJsonString());
        Assert.Null(ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment).Problem);
        body["progressRequest"] = JsonNode.Parse(FullRequest);
        RecordEnvelope changed = fixture.Estate.Add("goal", fixture.Estate.OperatorId, body.ToJsonString(), supersedes: another.Id);
        ProgressRequestSelection ambiguous = ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment);
        Assert.NotNull(ambiguous.Problem);
        Assert.Null(ambiguous.Request);
        ProgressRequestSelection selected = ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment, changed);
        Assert.Equal(changed.Id, selected.Record);
        Assert.Equal(FullRequest, CanonicalJson.Serialize(selected.Request!.Value));
        EstateModel model = fixture.Estate.Load();
        string text = WorkView.RenderBrief(model, fixture.Order, includeDocumentCatalogue: false, changed);
        Assert.Contains("Source: goal record " + changed.Id, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unavailable:", text, StringComparison.Ordinal);
        using JsonDocument brief = JsonDocument.Parse(WorkView.RenderBriefJson(model, fixture.Order, changed));
        JsonElement assignment = brief.RootElement.GetProperty("assignments")[0];
        Assert.Equal(FullRequest, CanonicalJson.Serialize(assignment.GetProperty("progressRequest")));
        Assert.Equal(changed.Id, assignment.GetProperty("progressRequestSource").GetProperty("record").GetString());
        Assert.False(assignment.TryGetProperty("progressRequestProblem", out _));
        fixture.Set("brief", DefaultRequest);
        Assert.Equal("brief", ProgressRequests.Resolve(fixture.Estate.Load(), fixture.Assignment).Scope);
        Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("{\"cadence\":{\"minutes\":1}}")]
    [InlineData("{\"cadence\":{\"minutes\":2147483647,\"perStep\":false},\"statusText\":false}")]
    [InlineData("{\"cadence\":{\"perStep\":true},\"types\":[\"not-measurable\"]}")]
    public void CadenceAndOptionalRequestDefaultsRoundTripWithoutAddingFields(string request)
    {
        using var fixture = new ProgressFixture();
        fixture.Set("estate", request);
        EstateModel model = fixture.Estate.Load();
        Assert.DoesNotContain(model.Findings, f => f.Severity == Severity.Error);
        using JsonDocument original = JsonDocument.Parse(request);
        Assert.Equal(CanonicalJson.Serialize(original.RootElement), CanonicalJson.Serialize(ProgressRequests.Resolve(model, fixture.Assignment).Request!.Value));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"cadence\":{}}")]
    [InlineData("{\"cadence\":{\"perStep\":false}}")]
    [InlineData("{\"cadence\":{\"minutes\":0}}")]
    [InlineData("{\"cadence\":{\"minutes\":-1}}")]
    [InlineData("{\"cadence\":{\"minutes\":2147483648}}")]
    [InlineData("{\"cadence\":{\"minutes\":\"10\"}}")]
    [InlineData("{\"cadence\":{\"perStep\":\"yes\"}}")]
    [InlineData("{\"cadence\":{\"perStep\":true,\"seconds\":1}}")]
    [InlineData("{\"cadence\":null}")]
    [InlineData("{\"statusText\":true}")]
    [InlineData("{\"cadence\":{\"minutes\":10},\"types\":[]}")]
    [InlineData("{\"cadence\":{\"minutes\":10},\"types\":null}")]
    [InlineData("{\"cadence\":{\"minutes\":10},\"types\":[\"other\"]}")]
    [InlineData("{\"cadence\":{\"minutes\":10},\"types\":[\"accurate\",\"accurate\"]}")]
    [InlineData("{\"cadence\":{\"minutes\":10},\"statusText\":\"required\"}")]
    [InlineData("{\"cadence\":{\"minutes\":10},\"stop\":true}")]
    public void MalformedRequestsAreRefusedAtEveryScope(string request)
    {
        foreach (string scope in new[] { "estate", "roster", "goal", "brief" })
        {
            using var fixture = new ProgressFixture();
            RecordEnvelope record = fixture.Set(scope, request);
            Assert.Contains(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.StartsWith(record.ExpectedPath + "#body", StringComparison.Ordinal));
        }
    }
}
