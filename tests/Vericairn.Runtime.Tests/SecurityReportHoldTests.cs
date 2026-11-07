// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>Ways around the hold that a record writer or a client might try: none lifts it, and none sees what the operator sees.</summary>
[Collection("policy-sources")]
public sealed class SecurityReportHoldTests
{
    private static JsonObject Repository(string kind, string github, string name = "Site") => new()
    {
        ["kind"] = kind, ["name"] = name, ["role"] = "Website source.", ["location"] = new JsonObject { ["github"] = github }, ["visibility"] = "public",
    };

    private static string[] Errors(SecurityReportFixture f, RecordEnvelope record) =>
        [.. f.Records.Estate.Load().Findings.Where(x => x.Severity == Severity.Error && x.Location.Contains(record.Id, StringComparison.Ordinal)).Select(x => x.Code)];

    // A second declared repository, Example/site, with an untriaged draft that holds the release.
    private static (RecordEnvelope Site, ReleaseSubmitRequest Request) DeclareSite(SecurityReportFixture f, MemorySecurityAdvisoryReader reader)
    {
        RecordEnvelope site = f.Records.Add("resource", Repository("repository", "Example/site"));
        JsonObject body = f.ProductBody(reader.Provider);
        body["securityReports"]!["repositories"] = new JsonArray(f.Resource.Id, site.Id);
        RecordEnvelope product = f.Records.Add("product", body, supersedes: f.Product.Id);
        reader.Set("Example/site", new(FakeGh.Drafted, "draft", "critical", "sha256:" + new string('c', 64)));
        ReleaseSubmitRequest request = f.Request with { Product = product.Id, Expected = product.ComputedHash };
        Assert.Contains(f.Api.ReleaseCheck(request).Check!.Objections, o => o.Code == "release.security-report");
        return (site, request);
    }

    [Fact]
    public void ABuilderCannotTurnADeclaredRepositoryIntoAServiceOrMoveItInTwoSteps()
    {
        var reader = new MemorySecurityAdvisoryReader();
        using var f = new SecurityReportFixture(reader: reader, provider: reader.Provider);
        (RecordEnvelope site, ReleaseSubmitRequest request) = DeclareSite(f, reader);

        RecordEnvelope service = f.Records.Add("resource", Repository("service", "Example/site"), builder: true, supersedes: site.Id);
        Assert.Contains("resource.security-reports-author", Errors(f, service));
        RecordEnvelope back = f.Records.Add("resource", Repository("repository", "Example/empty"), builder: true, supersedes: service.Id);
        Assert.Contains("resource.security-reports-author", Errors(f, back));
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        ReleaseResponse after = f.Api.ReleaseCheck(request);
        Assert.False(after.Ok);
        Assert.Contains(after.Check!.Objections, o => o.Code == "resource.security-reports-author");
    }

    [Fact]
    public void KindAndLocationChangesOfAnUndeclaredRepositoryNeedTheOperatorOrAdminister()
    {
        using var f = new SecurityReportFixture(declare: false);
        RecordEnvelope site = f.Records.Add("resource", Repository("repository", "Example/site"));
        RecordEnvelope service = f.Records.Add("resource", Repository("service", "Example/site"), builder: true, supersedes: site.Id);
        Assert.Contains("resource.location-author", Errors(f, service));
        RecordEnvelope other = f.Records.Add("resource", Repository("service", "Example/other"));
        RecordEnvelope promoted = f.Records.Add("resource", Repository("repository", "Example/other"), builder: true, supersedes: other.Id);
        Assert.Contains("resource.location-author", Errors(f, promoted));
        RecordEnvelope renamed = f.Records.Add("resource", Repository("repository", FakeGh.Repository, name: "Renamed"), builder: true, supersedes: f.Resource.Id);
        Assert.Empty(Errors(f, renamed));
        RecordEnvelope byOperator = f.Records.Add("resource", Repository("service", "Example/third"), supersedes: renamed.Id);
        Assert.Empty(Errors(f, byOperator));
    }

    [Fact]
    public void ADeclaredResourceThatNoLongerResolvesToAGitHubRepositoryHoldsAndIsNeverSkipped()
    {
        var reader = new MemorySecurityAdvisoryReader();
        using var f = new SecurityReportFixture(reader: reader, provider: reader.Provider);
        (RecordEnvelope site, ReleaseSubmitRequest request) = DeclareSite(f, reader);
        // Even the operator's own change leaves the declaration naming a resource that nothing reads: it holds until corrected.
        f.Records.Add("resource", Repository("service", "Example/site"), supersedes: site.Id);
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        ReleaseObjection objection = Assert.Single(f.Api.ReleaseCheck(request).Check!.Objections, o => o.Code == "release.security-reports-unavailable");
        Assert.Contains(site.Id, objection.Message, StringComparison.Ordinal);
        Assert.Contains("no repository on GitHub", objection.Message, StringComparison.Ordinal);
        f.Api.SecurityReportsScheduled();
        AttentionNotice notice = Assert.Single(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-reports");
        Assert.Equal(("failed check", site.Id), (notice.Kind, notice.SubjectId));
        Assert.True(f.Api.SecurityReportsPoll(new(f.Operator)).Held);
    }

    [Fact]
    public void AReadOfAnOldLocationNeverCountsForANewOne()
    {
        var reader = new MemorySecurityAdvisoryReader();
        using var f = new SecurityReportFixture(reader: reader, provider: reader.Provider);
        reader.Set(FakeGh.Repository, new(FakeGh.Drafted, "draft", "critical", "sha256:" + new string('d', 64)));
        f.Api.SecurityReportsScheduled();
        int reads = reader.Reads;
        reader.Refusal = "The new location cannot be read.";
        // The operator moves the declared repository; the read of the old location, a second old, no longer counts.
        f.Records.Add("resource", Repository("repository", "Example/moved", name: "Widget"), supersedes: f.Resource.Id);
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        Assert.True(reader.Reads > reads);
        ReleaseObjection objection = Assert.Single(check.Check!.Objections, o => o.Code == "release.security-reports-unavailable");
        Assert.Contains("Example/moved", objection.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(check.Check.Objections, o => o.Message.Contains(FakeGh.Drafted, StringComparison.Ordinal));
        Assert.Contains(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "security-reports" && n.SubjectId == "Example/moved");
    }

    [Fact]
    public void ABuilderRemovingTheDeclarationLeavesTheReleaseRefused()
    {
        using var f = new SecurityReportFixture();
        Assert.False(f.Api.ReleaseCheck(f.Request).Ok);
        RecordEnvelope removed = f.Records.Add("product", f.ProductBody(null), builder: true, supersedes: f.Product.Id);
        Assert.Contains("product.security-reports-author", Errors(f, removed));
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        ReleaseResponse after = f.Api.ReleaseCheck(f.Request with { Product = removed.Id, Expected = removed.ComputedHash });
        Assert.False(after.Ok);
        Assert.Contains(after.Check!.Objections, o => o.Code == "product.security-reports-author");
    }

    [Fact]
    public void ARevisionOfTheResourceThatKeepsItsKindAndLocationKeepsItsTriage()
    {
        using var f = new SecurityReportFixture();
        f.TriageAll();
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
        RecordEnvelope same = f.Records.Add("resource", Repository("repository", FakeGh.Repository, name: "Widget 2"), builder: true, supersedes: f.Resource.Id);
        Assert.Empty(Errors(f, same));
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        ReleaseResponse after = f.Api.ReleaseCheck(f.Request);
        Assert.True(after.Ok, string.Join(",", after.Check!.Objections.Select(o => o.Code)));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("builder")]
    [InlineData("operator")]
    public void TheMcpCheckSpeaksForTheActingPrincipalAndReadsReportsOnlyForTheOperator(string acting)
    {
        using var f = new SecurityReportFixture();
        string? principal = acting switch { "builder" => f.Builder, "operator" => f.Operator, _ => null };
        var registry = new ToolRegistry(f.Api, principal);
        ReleaseSubmitRequest r = f.Request;
        var arguments = new JsonObject
        {
            ["product"] = r.Product, ["version"] = r.Version, ["by"] = f.Operator, ["commit"] = r.Commit, ["expected"] = r.Expected,
            ["clientBuild"] = r.ClientBuild, ["repository"] = r.Repository,
        };
        ToolCallResult result = registry.Call("release_submit_check", arguments);
        Assert.True(result.IsError);
        if (acting == "operator")
        {
            Assert.Contains(FakeGh.Drafted, result.Text, StringComparison.Ordinal);
            Assert.NotEmpty(f.Gh.Calls);
            return;
        }
        Assert.Contains("release.security-reports", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("GHSA", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("release.security-report\"", result.Text, StringComparison.Ordinal);
        Assert.Empty(f.Gh.Calls);
    }
}
