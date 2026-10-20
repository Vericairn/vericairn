// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;
using Vericairn.Mcp;

namespace Vericairn.Runtime.Tests;

public sealed class PolicyReadTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ParallelWorkAndProspectiveConsistencyUseTheRecordedPolicyBasis(int assignments) =>
        PolicyWorkJourney.Run(api => api.PolicyEvaluate("2026-11-01T00:00:00Z"), assignments);

    [Fact]
    public void ReadsKeepRecordedTimeAndExposeCrossedDeadlinesWithoutWriting()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope policy = fixture.Policy();
        fixture.AssertValid();
        using var service = EstateService.Open(fixture.Estate.Root);
        int records = service.Session.Load().Records.Count;
        PolicyResponse first = service.Policies();
        Assert.Equal("recorded", first.Basis.Origin);
        Assert.Equal(JsonSerializer.Serialize(first, ProtocolJson.Options), JsonSerializer.Serialize(service.Policies(), ProtocolJson.Options));
        Assert.Equal(policy.Id, Assert.Single(service.Policy("maintenance")!.Policies).Id);
        Assert.Equal(policy.Id, Assert.Single(service.Policy(policy.Id)!.Policies).Id);
        Assert.Null(service.Policy("absent"));
        PolicyResponse before = service.PolicyEvaluate("2026-10-16T12:59:59Z");
        PolicyResponse deadline = service.PolicyEvaluate("2026-10-16T13:00:00Z");
        Assert.Equal("preview", deadline.Basis.Origin);
        Assert.Equal(0, before.Holds);
        Assert.Equal(1, deadline.Holds);
        DutyFindingDto grace = Assert.Single(before.Findings, f => f.Disposition == "due-within-grace").Duty!;
        Assert.False(grace.Completed);
        Assert.Equal("2026-10-16T13:00:00Z", grace.Deadline);
        Assert.True(Assert.Single(deadline.Findings, f => f.ReleaseHold).Duty!.Missed);
        Assert.Contains(deadline.Basis.Digest, deadline.Text, StringComparison.Ordinal);
        Assert.Contains("deadline 2026-10-16T13:00:00Z", deadline.Text, StringComparison.Ordinal);
        Assert.Equal(records, service.Session.Load().Records.Count);
        Assert.Equal(first.Basis.Digest, service.Policies().Basis.Digest);
    }

    [Fact]
    public void MissingKindsAndInvalidPreviewInputsNeverBecomeSatisfied()
    {
        using var fixture = new PolicyFixture();
        fixture.Policy(fixture.PolicyBody(duty: false));
        using var service = EstateService.Open(fixture.Estate.Root);
        PolicyResponse response = service.PolicyEvaluate("2026-10-16T13:00:00Z");
        Assert.False(response.Complete);
        Assert.Equal("unavailable", Assert.Single(response.Findings).Verdict);
        Assert.Equal(1, response.Holds);
        Assert.False(service.PolicyEvaluate("yesterday").Complete);
        Assert.Equal("not-found", service.PolicyEvaluate(release: "absent").Problem!.Code);
        Assert.Contains(service.PolicyKinds(), k => k.Kind == "vericairn.estate/recurring-duty" && k.Version == 1 && k.MaximumFindings > 0);
    }

    [Fact]
    [Trait("Journey", "D19-H")]
    public void WritingEntriesKeepCountsExactLocationsAndRemovedHistory()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope entry = fixture.Add("writing-exception", PolicyFixture.WritingBody());
        string directory = Path.Combine(fixture.Estate.Root, "docs");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "prose.md");
        File.WriteAllText(file, "Open311 and Open311. `Open311`\n");
        using var service = EstateService.Open(fixture.Estate.Root);
        int count = service.Session.Load().Records.Count;
        WritingExceptionsResponse response = service.WritingExceptions();
        Assert.True(response.Complete);
        Assert.Equal(2, Assert.Single(response.Entries).Matches);
        Assert.Equal(2, response.ExceptedOccurrences.Count);
        Assert.All(response.ExceptedOccurrences, occurrence =>
        {
            Assert.Equal("docs/prose.md", occurrence.Location.Member);
            Assert.Equal(entry.ComputedHash, Assert.Single(occurrence.Entries).Hash);
        });
        Assert.Equal(response.Basis.Digest, service.WritingException(entry.Id)!.Basis.Digest);
        Assert.Null(service.WritingException("absent"));
        Assert.Equal(count, service.Session.Load().Records.Count);
        File.WriteAllText(file, "Open311\n");
        Assert.NotEqual(response.Basis.Digest, service.WritingExceptions().Basis.Digest);
        var removed = PolicyFixture.Body(entry);
        removed["state"] = "removed";
        removed["removalReason"] = "The spelling no longer needs an exception.";
        fixture.Add("writing-exception", removed, supersedes: entry.Id);
        WritingExceptionsResponse history = service.WritingException(entry.Id)!;
        Assert.Equal(2, history.Records.Count);
        Assert.Empty(history.Entries);
        Assert.Empty(history.ExceptedOccurrences);
        Assert.Single(history.Warnings);
    }

    [Fact]
    public async Task ReadTransportsUseTheSameRoutesAndPreserveTheCompleteTypedResponse()
    {
        using var fixture = new PolicyFixture();
        fixture.Policy();
        using var service = EstateService.Open(fixture.Estate.Root);
        PolicyResponse expected = service.PolicyEvaluate("2026-10-16T13:00:00Z");
        List<string> requests = [];
        using var client = new VericairnClient(new HttpClient(new ReadHandler(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return JsonContent.Create(expected, options: ProtocolJson.Options);
        })) { BaseAddress = new Uri("http://fixture.invalid") });
        PolicyResponse sync = client.PolicyEvaluate("2026-10-16T13:00:00Z", "candidate");
        PolicyResponse asyncRead = await client.PolicyEvaluateAsync("2026-10-16T13:00:00Z", "candidate", TestContext.Current.CancellationToken);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        PolicyResponse bridged = bound.PolicyEvaluate("2026-10-16T13:00:00Z", "candidate");
        string canonical = JsonSerializer.Serialize(expected, ProtocolJson.Options);
        Assert.Equal(canonical, JsonSerializer.Serialize(sync, ProtocolJson.Options));
        Assert.Equal(canonical, JsonSerializer.Serialize(asyncRead, ProtocolJson.Options));
        Assert.Equal(canonical, JsonSerializer.Serialize(bridged, ProtocolJson.Options));
        Assert.All(requests, path => Assert.Equal("/v1/estate-policies/evaluate?asOf=2026-10-16T13%3A00%3A00Z&release=candidate", path));
    }

    [Fact]
    public async Task McpReadsReturnTheSameBasisAndCannotAcceptARelease()
    {
        using var fixture = new PolicyFixture();
        fixture.Policy();
        using var service = EstateService.Open(fixture.Estate.Root);
        var tools = new ToolRegistry(service, null);
        int before = service.Session.Load().Records.Count;
        ToolCallResult response = await tools.CallAsync("policy_evaluate", new System.Text.Json.Nodes.JsonObject { ["asOf"] = "2026-10-16T13:00:00Z" }, TestContext.Current.CancellationToken);
        Assert.False(response.IsError);
        PolicyResponse policy = JsonSerializer.Deserialize<PolicyResponse>(response.Text, ProtocolJson.Options)!;
        Assert.Equal(service.PolicyEvaluate("2026-10-16T13:00:00Z").Basis.Digest, policy.Basis.Digest);
        Assert.Equal(1, policy.Holds);
        Assert.DoesNotContain(tools.All, t => t.Name.Contains("release", StringComparison.Ordinal) && t.Writes);
        Assert.Equal(before, service.Session.Load().Records.Count);
    }

    private sealed class ReadHandler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request) });
    }
}
