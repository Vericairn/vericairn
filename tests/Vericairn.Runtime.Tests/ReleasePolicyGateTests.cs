// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

[Collection("policy-sources")]
public sealed class ReleasePolicyGateTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void ElapsedTimeAloneKeepsTheSameOperationHistory(int preparationSeconds, int dispatchSeconds)
    {
        using var f = new Fixture();
        f.Duty();
        string preparedAt = RecordParser.FormatTimestamp(f.Clock.Now);
        f.Api.ReleaseBoundary = stage =>
        {
            f.Clock.Now = f.Clock.Now.AddSeconds(stage == "prepared" ? preparationSeconds : dispatchSeconds);
        };
        f.Provider.OnDispatch = () =>
        {
            Assert.Equal(0, f.Provider.Calls);
            RecordEnvelope intent = Assert.Single(f.Api.Session.Load().OfType("operation"));
            Assert.Equal("not-started", EstateModel.BodyString(intent, "status"));
            Assert.Single(intent.Body.GetProperty("evidence").EnumerateArray());
        };

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        Assert.True(response.Ok, response.Message);
        Assert.Equal(1, f.Provider.Calls);
        string hash = Assert.Single(response.BasisEvidence!);
        ReleaseBasisEvidence retained = f.Api.ReleaseBasis(hash)!.Evidence;
        Assert.Equal(preparedAt, retained.Basis.AsOf);
        Assert.Equal(RecordParser.FormatTimestamp(f.Clock.Now), response.Policies!.Basis.AsOf);
        Assert.Equal(retained.Basis.RecordDigest, response.Policies.Basis.RecordDigest);
        if (preparationSeconds + dispatchSeconds > 0)
            Assert.NotEqual(retained.Basis.Digest, response.Policies.Basis.Digest);
        RecordEnvelope receipt = Assert.Single(f.Api.Session.Load().Current("operation"));
        Assert.Equal("dispatched", EstateModel.BodyString(receipt, "status"));
        Assert.Equal(2, f.Api.Session.Load().OfType("operation").Count());
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData("prepared", false)]
    [InlineData("before-dispatch", false)]
    [InlineData("prepared", true)]
    [InlineData("before-dispatch", true)]
    public void StalenessAgeAloneKeepsTheSameOperationHistory(string boundary, bool overdue)
    {
        using var f = new Fixture();
        RecordEnvelope anchor = f.Staleness(overdue ? "warn" : "refuse");
        f.Clock.Now = anchor.AtTime.AddDays(1).AddSeconds(overdue ? 1 : -2);
        string preparedAt = RecordParser.FormatTimestamp(f.Clock.Now);
        f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = f.Clock.Now.AddSeconds(1); };
        f.Provider.OnDispatch = () =>
        {
            Assert.Equal(0, f.Provider.Calls);
            RecordEnvelope intent = Assert.Single(f.Api.Session.Load().OfType("operation"));
            Assert.Equal("not-started", EstateModel.BodyString(intent, "status"));
            Assert.Single(intent.Body.GetProperty("evidence").EnumerateArray());
        };

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        Assert.True(response.Ok, response.Message);
        Assert.Equal(1, f.Provider.Calls);
        ReleaseBasisEvidence retained = f.Api.ReleaseBasis(Assert.Single(response.BasisEvidence!))!.Evidence;
        PolicyFindingDto original = Assert.Single(retained.Policies.Findings);
        PolicyFindingDto fresh = Assert.Single(response.Policies!.Findings);
        Assert.Equal(overdue ? "86401 elapsed seconds." : "86398 elapsed seconds.", original.Actual);
        Assert.Equal(overdue ? "86402 elapsed seconds." : "86399 elapsed seconds.", fresh.Actual);
        Assert.Equal(overdue ? "unmet" : "satisfied", fresh.Verdict);
        Assert.Equal(original.Verdict, fresh.Verdict);
        Assert.Equal(original.Anchor, fresh.Anchor);
        Assert.Equal(original.AnchorAt, fresh.AnchorAt);
        Assert.False(fresh.ReleaseHold);
        Assert.Equal(preparedAt, retained.Basis.AsOf);
        Assert.Equal(RecordParser.FormatTimestamp(f.Clock.Now), response.Policies.Basis.AsOf);
        Assert.Equal(retained.Basis.RecordDigest, response.Policies.Basis.RecordDigest);
        Assert.NotEqual(retained.Basis.Digest, response.Policies.Basis.Digest);
        Assert.Equal(2, f.Api.Session.Load().OfType("operation").Count());
        Assert.Equal("dispatched", EstateModel.BodyString(Assert.Single(f.Api.Session.Load().Current("operation")), "status"));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData("prepared", "warn")]
    [InlineData("before-dispatch", "warn")]
    [InlineData("prepared", "refuse")]
    [InlineData("before-dispatch", "refuse")]
    public void CrossedStalenessThresholdIsCaughtBeforeAnyProviderCall(string boundary, string level)
    {
        using var f = new Fixture();
        RecordEnvelope anchor = f.Staleness(level);
        DateTimeOffset threshold = anchor.AtTime.AddDays(1);
        f.Clock.Now = threshold.AddSeconds(-1);
        f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = threshold; };
        f.Provider.OnDispatch = () =>
        {
            Assert.Equal("warn", level);
            Assert.Equal(0, f.Provider.Calls);
            EstateModel model = f.Api.Session.Load();
            RecordEnvelope current = Assert.Single(model.Current("operation"));
            Assert.Equal(boundary == "before-dispatch" ? 2 : 1, model.OfType("operation").Count());
            Assert.Equal(boundary == "before-dispatch" ? "uncertain" : "not-started", EstateModel.BodyString(current, "status"));
            AssertChangedEvidence(current);
        };

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        if (level == "warn") Assert.True(response.Ok, response.Message);
        else Assert.False(response.Ok);
        Assert.Equal(level == "warn" ? 1 : 0, f.Provider.Calls);
        PolicyFindingDto fresh = Assert.Single(response.Policies!.Findings);
        Assert.Equal("unmet", fresh.Verdict);
        Assert.Equal("86400 elapsed seconds.", fresh.Actual);
        Assert.Equal(level == "refuse", fresh.ReleaseHold);
        EstateModel final = f.Api.Session.Load();
        if (level == "warn")
        {
            Assert.Equal(boundary == "before-dispatch" ? 3 : 2, final.OfType("operation").Count());
            Assert.Equal(2, response.BasisEvidence!.Count);
        }
        else if (boundary == "before-dispatch")
        {
            Assert.Equal("canceled", response.Status);
            Assert.Equal(2, final.OfType("operation").Count());
            RecordEnvelope canceled = Assert.Single(final.Current("operation"));
            Assert.Equal("canceled", EstateModel.BodyString(canceled, "status"));
            AssertChangedEvidence(canceled);
        }
        else Assert.Empty(final.OfType("operation"));
        Assert.Equal(0, f.Api.Validate().Errors);

        void AssertChangedEvidence(RecordEnvelope operation)
        {
            ReleaseBasisEvidence[] retained = operation.Body.GetProperty("evidence").EnumerateArray()
                .Select(item => f.Api.ReleaseBasis(item.GetProperty("hash").GetString()!)!.Evidence).ToArray();
            Assert.Equal(2, retained.Length);
            PolicyFindingDto original = Assert.Single(retained[0].Policies.Findings);
            PolicyFindingDto updated = Assert.Single(retained[1].Policies.Findings);
            Assert.Equal("satisfied", original.Verdict);
            Assert.Equal("86399 elapsed seconds.", original.Actual);
            Assert.Equal("unmet", updated.Verdict);
            Assert.Equal("86400 elapsed seconds.", updated.Actual);
            Assert.Equal(original.Anchor, updated.Anchor);
            Assert.Equal(level == "refuse", updated.ReleaseHold);
            Assert.Equal(retained[0].Basis.RecordDigest, retained[1].Basis.RecordDigest);
            Assert.Equal(RecordParser.FormatTimestamp(threshold), retained[1].Basis.AsOf);
            if (boundary == "before-dispatch")
                Assert.Equal(operation.Supersedes, Assert.Single(retained[1].ExcludedOperationRevisions).Id);
        }
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void AnAlreadyMissedDutyKeepsTheSameOperationHistoryAsTimePasses(string boundary)
    {
        using var f = new Fixture();
        f.Duty(level: "warn");
        f.Clock.Now = f.Due.AddSeconds(1);
        f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = f.Clock.Now.AddSeconds(1); };
        f.Provider.OnDispatch = () =>
        {
            Assert.Equal(0, f.Provider.Calls);
            RecordEnvelope intent = Assert.Single(f.Api.Session.Load().OfType("operation"));
            Assert.Equal("not-started", EstateModel.BodyString(intent, "status"));
        };

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        Assert.True(response.Ok, response.Message);
        ReleaseBasisEvidence retained = f.Api.ReleaseBasis(Assert.Single(response.BasisEvidence!))!.Evidence;
        PolicyFindingDto original = Assert.Single(retained.Policies.Findings, finding => finding.Duty!.Missed);
        PolicyFindingDto fresh = Assert.Single(response.Policies!.Findings, finding => finding.Duty!.Missed);
        Assert.Equal("unmet", fresh.Verdict);
        Assert.False(fresh.ReleaseHold);
        Assert.False(fresh.Duty!.Completed);
        Assert.Equal(original.Duty!.DueAt, fresh.Duty.DueAt);
        Assert.Equal(original.Duty.Deadline, fresh.Duty.Deadline);
        Assert.Equal(original.Duty.Occurrence.GetRawText(), fresh.Duty.Occurrence.GetRawText());
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(2, f.Api.Session.Load().OfType("operation").Count());
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData("record", "prepared")]
    [InlineData("record", "before-dispatch")]
    [InlineData("source", "prepared")]
    [InlineData("source", "before-dispatch")]
    [InlineData("verdict", "prepared")]
    [InlineData("verdict", "before-dispatch")]
    [InlineData("finding", "prepared")]
    [InlineData("finding", "before-dispatch")]
    public void RealChangesAreRetainedBeforeAnyProviderCall(string change, string boundary)
    {
        using var f = new Fixture();
        f.Duty(level: "warn", graceDays: change == "finding" ? 1 : 0);
        f.Records.Estate.WriteFile("docs/evidence.md", "Original source.\n");
        RecordEnvelope? changedRecord = null;
        f.Api.ReleaseBoundary = stage =>
        {
            if (stage != boundary) return;
            Assert.Equal(0, f.Provider.Calls);
            switch (change)
            {
                case "record":
                    JsonObject revised = PolicyFixture.Body(f.Records.Decision);
                    revised["summary"] = "A revised fixture decision.";
                    changedRecord = f.Records.Add("decision", revised, supersedes: f.Records.Decision.Id);
                    break;
                case "source":
                    f.Records.Estate.WriteFile("docs/evidence.md", "Changed source.\n");
                    break;
                default:
                    f.Clock.Now = f.Due;
                    break;
            }
        };
        f.Provider.OnDispatch = () =>
        {
            Assert.Equal(0, f.Provider.Calls);
            EstateModel model = f.Api.Session.Load();
            RecordEnvelope current = Assert.Single(model.Current("operation"));
            Assert.Equal(boundary == "before-dispatch" ? 2 : 1, model.OfType("operation").Count());
            Assert.Equal(boundary == "before-dispatch" ? "uncertain" : "not-started", EstateModel.BodyString(current, "status"));
            ReleaseBasisEvidence[] retained = current.Body.GetProperty("evidence").EnumerateArray()
                .Select(item => f.Api.ReleaseBasis(item.GetProperty("hash").GetString()!)!.Evidence).ToArray();
            Assert.Equal(2, retained.Length);
            ReleaseBasisEvidence first = retained[0], fresh = retained[1];
            Assert.NotEqual(first.Basis.Digest, fresh.Basis.Digest);
            if (boundary == "before-dispatch")
            {
                RecordEnvelope intent = model.ById[current.Supersedes!];
                PolicyRecordReference excluded = Assert.Single(fresh.ExcludedOperationRevisions);
                Assert.Equal(intent.Id, excluded.Id);
                Assert.Equal(intent.ComputedHash, excluded.Hash);
                Assert.DoesNotContain(fresh.Records, record => record.GetProperty("type").GetString() == "operation");
            }
            else Assert.Empty(fresh.ExcludedOperationRevisions);

            if (change == "record")
            {
                Assert.NotEqual(first.Basis.RecordDigest, fresh.Basis.RecordDigest);
                Assert.DoesNotContain(first.Records, record => record.GetProperty("id").GetString() == changedRecord!.Id);
                Assert.Contains(fresh.Records, record => record.GetProperty("id").GetString() == changedRecord!.Id &&
                    record.GetProperty("body").GetProperty("summary").GetString() == "A revised fixture decision.");
            }
            else
            {
                Assert.Equal(first.Basis.RecordDigest, fresh.Basis.RecordDigest);
                if (change == "source")
                {
                    Assert.Equal("Original source.\n", SourceText(first));
                    Assert.Equal("Changed source.\n", SourceText(fresh));
                }
                else
                {
                    Assert.Equal(JsonSerializer.Serialize(first.Repositories), JsonSerializer.Serialize(fresh.Repositories));
                    PolicyFindingDto original = Assert.Single(first.Policies.Findings);
                    PolicyFindingDto updated = Assert.Single(fresh.Policies.Findings, finding => finding.Duty!.DueAt == RecordParser.FormatTimestamp(f.Due));
                    Assert.Equal("satisfied", original.Verdict);
                    Assert.Equal("scheduled", original.Disposition);
                    Assert.Equal(change == "verdict" ? "unmet" : "satisfied", updated.Verdict);
                    Assert.Equal(change == "verdict" ? "none" : "due-within-grace", updated.Disposition);
                    Assert.False(updated.ReleaseHold);
                    Assert.Equal(RecordParser.FormatTimestamp(f.Due), fresh.Basis.AsOf);
                }
            }
        };

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        Assert.True(response.Ok, response.Message);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(2, response.BasisEvidence!.Count);
        Assert.Equal(boundary == "before-dispatch" ? 3 : 2, f.Api.Session.Load().OfType("operation").Count());
        Assert.Equal(0, f.Api.Validate().Errors);

        static string SourceText(ReleaseBasisEvidence evidence) => System.Text.Encoding.UTF8.GetString(
            Assert.Single(evidence.Repositories.SelectMany(repository => repository.Files), file => file.Path == "docs/evidence.md").Bytes.ToArray());
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void RevokedSigningAuthorityIsRecheckedBeforeIntentAndDispatch(string boundary)
    {
        using var f = new Fixture();
        f.Api.ReleaseBoundary = stage =>
        {
            if (stage != boundary) return;
            var device = f.Api.Session.Load().ById[Assert.Single(f.Api.Devices(), d => d.HeldHere && d.Principal == f.Records.Estate.OperatorId).Id];
            f.Records.Estate.SetClock(f.Clock.Now);
            f.Records.Add("admission", new() { ["principal"] = f.Records.Estate.OperatorId, ["status"] = "revoked", ["note"] = "Withdraw the prepared signing device." }, subject: device.Reference);
            f.Clock.Now = f.Records.Estate.Clock.AddSeconds(1);
        };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Contains(response.Problem!.Findings, finding => finding.Code is "release.signature" or "release.author");
        if (boundary == "prepared") Assert.Empty(f.Api.Records("operation"));
        else
        {
            Assert.Equal("not-started", EstateModel.BodyString(Assert.Single(f.Api.Session.Load().Current("operation")), "status"));
            Assert.Contains("reconcile", response.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void DeadlineCrossedWhileAcquiringSourcesUsesTheFreshInstant(string boundary)
    {
        using var f = new Fixture();
        f.Duty();
        var repository = f.Records.Add("resource", new() { ["kind"] = "repository", ["name"] = "Source inventory", ["role"] = "Read exact sources.", ["location"] = new JsonObject { ["github"] = "Example/widget" }, ["visibility"] = "public" });
        JsonObject definition = f.Records.PolicyBody("source");
        definition["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-10));
        definition["level"] = "remind";
        definition["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository.Id, ["selectors"] = new JsonArray("docs/**") };
        definition["rules"] = new JsonArray(new JsonObject { ["key"] = "revision", ["kind"] = "vericairn.estate/revision-line", ["version"] = 1, ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        f.Records.Policy(definition);
        bool cross = boundary == "intent";
        int prepared = 0;
        f.Api.ReleaseBoundary = stage => { if (stage == "prepared") prepared++; if (stage == boundary) cross = true; };
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new CrossingSource(() => { if (cross) f.Clock.Now = f.Due; });
            ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
            Assert.False(response.Ok);
            Assert.Equal(0, f.Provider.Calls);
            Assert.Contains(response.Policies!.Findings, finding => finding.ReleaseHold && finding.Duty is not null);
            Assert.Equal(RecordParser.FormatTimestamp(f.Due), response.Policies.Basis.AsOf);
            if (boundary == "intent") Assert.Equal(0, prepared);
            if (boundary != "before-dispatch") Assert.Empty(f.Api.Records("operation"));
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    private sealed class CrossingSource(Action read) : IPolicyRepositorySource
    {
        public PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit)
        {
            read();
            return new(repository.Id, commit ?? "fixture-source", PolicyBasis.RepositoryContract, true, []);
        }
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void CrossedDeadlineRefusesAtEveryAdmissionBoundaryWithoutProviderCalls(string boundary)
    {
        using var f = new Fixture();
        f.Duty();
        Assert.Equal(0, f.Api.Policies().Holds);
        if (boundary == "intent") f.Clock.Now = f.Due;
        else f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = f.Due; };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Contains(response.Problem!.Findings, finding => finding.Code == "policy.unmet" && finding.Message.Contains("deadline", StringComparison.Ordinal));
        Assert.Equal("submission", response.Policies!.Basis.Origin);
        Assert.Equal(1, response.Policies.Holds);
        Assert.Equal(0, f.Provider.Calls);
        if (boundary == "before-dispatch")
        {
            Assert.Equal("canceled", response.Status);
            EstateModel model = f.Api.Session.Load();
            RecordEnvelope canceled = Assert.Single(model.Current("operation"));
            Assert.Equal("canceled", EstateModel.BodyString(canceled, "status"));
            Assert.Equal(2, model.OfType("operation").Count());
            Assert.Equal(2, response.BasisEvidence!.Count);
            ReleaseBasisEvidence original = f.Api.ReleaseBasis(response.BasisEvidence[0])!.Evidence;
            ReleaseBasisEvidence rechecked = f.Api.ReleaseBasis(response.BasisEvidence[1])!.Evidence;
            Assert.Equal(original.Basis.RecordDigest, rechecked.Basis.RecordDigest);
            Assert.Equal("satisfied", Assert.Single(original.Policies.Findings).Verdict);
            Assert.Contains(rechecked.Policies.Findings, finding => finding.Verdict == "unmet" && finding.ReleaseHold);
            Assert.Equal(RecordParser.FormatTimestamp(f.Due), rechecked.Basis.AsOf);
            Assert.Equal(canceled.Supersedes, Assert.Single(rechecked.ExcludedOperationRevisions).Id);
            Assert.Contains(canceled.Body.GetProperty("evidence").EnumerateArray(),
                evidence => evidence.GetProperty("hash").GetString() == response.BasisEvidence[1]);
        }
        else Assert.Empty(f.Api.Records("operation"));
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void APolicyAddedAfterPreparationIsReevaluatedBeforeAnyProviderCall(string boundary)
    {
        using var f = new Fixture();
        f.Api.ReleaseBoundary = stage =>
        {
            if (stage != boundary) return;
            JsonObject policy = f.Records.PolicyBody("new-hold", duty: false);
            policy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
            f.Records.Policy(policy);
        };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Contains(response.Problem!.Findings, finding => finding.Code == "policy.unavailable");
        Assert.Equal(0, f.Provider.Calls);
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void AnExactSubmissionExceptionCannotOutliveItsExpiry(string boundary)
    {
        using var f = new Fixture();
        RecordEnvelope policy = f.Duty();
        f.Records.Estate.SetClock(f.Due.AddSeconds(1));
        RecordEnvelope candidate = f.Candidate();
        JsonObject exception = f.Records.ExceptionBody(policy);
        DateTimeOffset expiry = f.Due.AddMinutes(10);
        exception["expiresAt"] = RecordParser.FormatTimestamp(expiry);
        exception["targets"]![0]!["submission"] = new JsonObject { ["release"] = PolicyFixture.Ref(candidate), ["product"] = f.Product.Id, ["version"] = "1.0.0" };
        f.Records.Add("policy-exception", exception, subject: policy.Reference);
        f.Clock.Now = expiry.AddSeconds(-1);
        f.Records.AssertValid();
        f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = expiry; };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Contains(response.Policies!.Findings, finding => finding.ReleaseHold && finding.Disposition != "excepted");
    }

    [Fact]
    public void BuiltInDocumentPolicyAndPreparationObjectionsAreReportedTogether()
    {
        using var f = new Fixture();
        f.Duty();
        f.Clock.Now = f.Due;
        f.Records.Add("living-document", new JsonObject
        {
            ["pathPattern"] = "docs/generated/{month}/{date}-{slug}-{id}.md", ["title"] = "Generated decisions", ["kind"] = "generated",
            ["generator"] = new JsonObject { ["id"] = "example.documents/uninstalled", ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
            ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["checks"] = new JsonArray("matches-generator"), ["summary"] = "Mandatory fixture verification.",
        });
        Directory.CreateDirectory(Path.Combine(f.Records.Estate.Root, "docs"));
        File.WriteAllBytes(Path.Combine(f.Records.Estate.Root, "docs", "prose.md"), [0xff]);
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request with { Expected = "stale" });
        Assert.False(response.Ok);
        string[] codes = response.Problem!.Findings.Select(finding => finding.Code).ToArray();
        Assert.Contains("layout.encoding", codes);
        Assert.Contains("document.release-hold", codes);
        Assert.Contains("policy.unmet", codes);
        Assert.Contains("release.expected", codes);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Api.Records("operation"));
        ReleaseResponse unavailableProvider = f.Api.ReleaseSubmit(f.Request with { Provider = "uninstalled" });
        Assert.Contains(unavailableProvider.Problem!.Findings, finding => finding.Code == "release.setup");
        Assert.Contains(unavailableProvider.Problem.Findings, finding => finding.Code == "policy.unmet");
        Assert.Contains(unavailableProvider.Problem.Findings, finding => finding.Code == "document.release-hold");
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void TheRetainedBasisNamesRecordsAndSourcesByHashSoALargeEstateStaysWithinTheObjectLimit()
    {
        using var f = new Fixture();
        f.Duty();
        // A basis that copied these sources, as 0.8.0's did, would exceed the estate's object limit and refuse every submission.
        string docs = Path.Combine(f.Records.Estate.Root, "docs");
        Directory.CreateDirectory(docs);
        string page = new string('x', 512 * 1024) + "\n";
        for (int i = 0; i < 6; i++) File.WriteAllText(Path.Combine(docs, $"large-{i}.md"), i + page);

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        Assert.True(response.Ok, response.Message);
        string hash = Assert.Single(response.BasisEvidence!);
        byte[] stored = f.Api.Session.Objects.Read(hash)!;
        Assert.True(stored.Length < 1024 * 1024, $"The retained basis is {stored.Length} bytes.");
        using JsonDocument document = JsonDocument.Parse(stored);
        Assert.All(document.RootElement.GetProperty("records").EnumerateArray(),
            r => Assert.Equal(["hash", "id", "type"], r.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        Assert.Equal(0, f.Api.Validate().Errors);
        ReleaseBasisEvidence retained = f.Api.ReleaseBasis(hash)!.Evidence;
        Assert.All(retained.Records, r => Assert.True(r.TryGetProperty("body", out _)));
        ReleaseBasisFile large = Assert.Single(retained.Repositories.SelectMany(r => r.Files), file => file.Path == "docs/large-3.md");
        Assert.Equal("3" + page, System.Text.Encoding.UTF8.GetString(large.Bytes.ToArray()));
    }

    [Fact]
    public void ACopiedBasisFromAnEarlierReleaseStillReads()
    {
        using var f = new Fixture();
        f.Duty();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        ReleaseBasisEvidence copied = f.Api.ReleaseBasis(Assert.Single(response.BasisEvidence!))!.Evidence with { Format = "vericairn.release-basis/1" };

        string hash = f.Api.Session.Objects.Put(JsonSerializer.SerializeToUtf8Bytes(copied, ProtocolJson.Options));

        Assert.Equal(JsonSerializer.Serialize(copied, ProtocolJson.Options), JsonSerializer.Serialize(f.Api.ReleaseBasis(hash)!.Evidence, ProtocolJson.Options));
    }

    [Fact]
    public void AReferencedBasisReadsAsUnavailableWhenASourceItNamesIsGone()
    {
        using var f = new Fixture();
        f.Duty();
        Directory.CreateDirectory(Path.Combine(f.Records.Estate.Root, "docs"));
        File.WriteAllText(Path.Combine(f.Records.Estate.Root, "docs", "evidence.md"), "Read only by this basis.\n");
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        string hash = Assert.Single(response.BasisEvidence!);
        Assert.NotNull(f.Api.ReleaseBasis(hash));
        string source = Vericairn.Records.Hashing.ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes("Read only by this basis.\n"));
        string path = Path.Combine(f.Records.Estate.Root, EstateLayout.ObjectsDirectory, source[Vericairn.Records.Hashing.ContentHash.Prefix.Length..]);
        Assert.True(File.Exists(path), path);

        File.Delete(path);

        Assert.Null(f.Api.ReleaseBasis(hash));
    }

    [Fact]
    public void AReferencedBasisReadsAsUnavailableWhenARecordsSignaturesDifferFromThoseItJudged()
    {
        using var f = new Fixture();
        f.Duty();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        string hash = Assert.Single(response.BasisEvidence!);
        Assert.NotNull(f.Api.ReleaseBasis(hash));
        using JsonDocument stored = JsonDocument.Parse(f.Api.Session.Objects.Read(hash)!);
        HashSet<string> named = stored.RootElement.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        RecordEnvelope judged = f.Api.Session.Load().Records.First(r => named.Contains(r.Id) && r.Signatures.Count > 0);
        string path = Directory.GetFiles(Path.Combine(f.Records.Estate.Root, "records"), judged.Id + ".json", SearchOption.AllDirectories).Single();

        // The record keeps its content hash, which leaves out signatures; only the stored record digest covers them.
        byte[] signature = System.Buffers.Text.Base64Url.DecodeFromChars(judged.Signatures[0].Signature);
        signature[0] ^= 0xff;
        File.WriteAllText(path, File.ReadAllText(path).Replace(judged.Signatures[0].Signature, System.Buffers.Text.Base64Url.EncodeToString(signature), StringComparison.Ordinal));

        Assert.Contains(f.Api.Session.Load().Records, r => r.Id == judged.Id && r.ComputedHash == judged.ComputedHash);
        Assert.Null(f.Api.ReleaseBasis(hash));
    }

    [Fact]
    public async Task APassingSubmissionRetainsExactInputsAndRechecksWithoutHashingItsOwnIntent()
    {
        using var f = new Fixture();
        f.Duty();
        Directory.CreateDirectory(Path.Combine(f.Records.Estate.Root, "docs"));
        File.WriteAllText(Path.Combine(f.Records.Estate.Root, "docs", "evidence.md"), "Retained admission source.\n");
        f.Api.ReleaseBoundary = stage => { if (stage == "before-dispatch") f.Clock.Now = f.Clock.Now.AddSeconds(1); };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(Fixture.Commit, f.Provider.Dispatched!.ExpectedCommit);
        string hash = Assert.Single(response.BasisEvidence!);
        RecordEnvelope intent = f.Api.Session.Load().OfType("operation").Single(o => EstateModel.BodyString(o, "status") == "not-started");
        using JsonDocument evidence = JsonDocument.Parse(f.Api.Session.Objects.Read(hash)!);
        JsonElement root = evidence.RootElement;
        Assert.Equal("vericairn.release-basis/2", root.GetProperty("format").GetString());
        // Records and sources are named by hash; the evidence below resolves them to their exact bytes.
        Assert.All(root.GetProperty("records").EnumerateArray(), r => Assert.False(r.TryGetProperty("body", out _)));
        Assert.All(root.GetProperty("repositories").EnumerateArray().SelectMany(r => r.GetProperty("files").EnumerateArray()),
            file => Assert.False(file.TryGetProperty("bytes", out _)));
        JsonElement basis = root.GetProperty("basis");
        Assert.Equal(Fixture.Commit, basis.GetProperty("submission").GetProperty("commit").GetString());
        Assert.Equal("submission", basis.GetProperty("origin").GetString());
        Assert.DoesNotContain(root.GetProperty("records").EnumerateArray(), r => r.GetProperty("id").GetString() == intent.Id);
        string digest = basis.GetProperty("recordDigest").GetString()!;
        Assert.Equal(response.Policies!.Basis.RecordDigest, digest);
        int before = f.Api.Session.Load().Records.Count;
        ReleaseBasisResponse retained = f.Api.ReleaseBasis(hash)!;
        Assert.NotNull(retained);
        Assert.Equal(digest, retained.Evidence.Basis.RecordDigest);
        Assert.Equal(Fixture.Commit, retained.Evidence.Basis.Submission!.Commit);
        Assert.Equal("Retained admission source.\n", System.Text.Encoding.UTF8.GetString(Assert.Single(retained.Evidence.Repositories.SelectMany(r => r.Files), file => file.Path == "docs/evidence.md").Bytes.ToArray()));
        using var client = new VericairnClient(new HttpClient(new BasisHandler(retained)) { BaseAddress = new Uri("http://fixture.invalid") });
        Assert.Equal(JsonSerializer.Serialize(retained, ProtocolJson.Options), JsonSerializer.Serialize(client.ReleaseBasis(hash), ProtocolJson.Options));
        Assert.Equal(JsonSerializer.Serialize(retained, ProtocolJson.Options), JsonSerializer.Serialize(await client.ReleaseBasisAsync(hash, TestContext.Current.CancellationToken), ProtocolJson.Options));
        ToolCallResult read = await new ToolRegistry(f.Api, null).CallAsync("release_basis_get", new() { ["hash"] = hash }, TestContext.Current.CancellationToken);
        Assert.False(read.IsError, read.Text);
        Assert.Equal(JsonSerializer.Serialize(retained, ProtocolJson.Options), read.Text);
        Assert.Equal(before, f.Api.Session.Load().Records.Count);
        Assert.Null(f.Api.ReleaseBasis("unavailable"));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    private sealed class BasisHandler(ReleaseBasisResponse basis) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/release-bases/" + Uri.EscapeDataString(basis.Hash), request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(basis, options: ProtocolJson.Options) });
        }
    }

    [Fact]
    public void OtherProductAndRepositoryHoldsStayVisibleButDoNotHoldThisRelease()
    {
        using var f = new Fixture();
        RecordEnvelope other = f.Records.Add("product", Fixture.ProductBody("other"));
        JsonObject productPolicy = f.Records.PolicyBody("other-product", duty: false);
        productPolicy["appliesTo"] = new JsonObject { ["kind"] = "product", ["product"] = other.Id };
        productPolicy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        f.Records.Policy(productPolicy);
        RecordEnvelope repository = f.Records.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Other source", ["role"] = "Another product's source.", ["location"] = new JsonObject { ["github"] = "Example/other" }, ["visibility"] = "public" });
        JsonObject repositoryPolicy = f.Records.PolicyBody("other-repository", duty: false);
        repositoryPolicy["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository.Id, ["selectors"] = new JsonArray("docs/**") };
        repositoryPolicy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        f.Records.Policy(repositoryPolicy);
        f.Records.AssertValid();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        Assert.All(response.Policies!.Findings, finding => Assert.False(finding.Enforced));
        Assert.Equal(1, f.Provider.Calls);
    }

    [Fact]
    public void GenericHistoryKeepsItsAuthoringContractWhileSubmissionEnforcesLiveHolds()
    {
        using var f = new Fixture();
        f.Duty();
        f.Clock.Now = f.Due;
        JsonElement body = PolicyFixture.Element(ReleaseFlow.IntentBody(f.Provider.Provider, "Example/widget", new Dictionary<string, string> { ["repository"] = "Example/widget", ["ref"] = Fixture.Commit }, "1.0.0", f.Product.Id));
        string recordedAt = RecordParser.FormatTimestamp(f.Due.AddSeconds(-1));
        var request = new NewRecordRequest("operation", f.Records.Estate.OperatorId, body, "product:" + f.Product.Id, At: recordedAt);
        WriteOutcome history = f.Api.NewRecord(request);
        Assert.True(history.Ok, history.Problem?.Message);
        Assert.Equal(recordedAt, f.Api.Record(history.Written!.Id)!.Envelope.GetProperty("at").GetString());
        Assert.Equal(0, f.Provider.Calls);
        RecordEnvelope candidate = f.Candidate(withArtifact: true);
        JsonObject published = PolicyFixture.Body(candidate);
        published["status"] = "published";
        published["publishedAt"] = RecordParser.FormatTimestamp(f.Clock.Now);
        published["url"] = "https://example.invalid/release";
        var publicationRequest = new NewRecordRequest("release", f.Records.Estate.OperatorId, PolicyFixture.Element(published), "product:" + f.Product.Id, candidate.Id, At: RecordParser.FormatTimestamp(f.Clock.Now));
        WriteOutcome unaccepted = f.Api.NewRecord(publicationRequest);
        Assert.False(unaccepted.Ok);
        Assert.Contains(unaccepted.Problem!.Findings, finding => finding.Code == "release.unaccepted");
        f.Records.Add("acceptance", new JsonObject { ["statement"] = "Fixture acceptance.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        WriteOutcome publication = f.Api.NewRecord(publicationRequest);
        Assert.True(publication.Ok, JsonSerializer.Serialize(publication.Problem, ProtocolJson.Options));
        Assert.Equal(candidate.Id, f.Api.Record(publication.Written!.Id)!.Envelope.GetProperty("supersedes").GetString());
        Assert.Single(f.Api.Records("release"));
        int before = f.Api.Session.Load().Records.Count;
        ReleaseResponse refused = f.Api.ReleaseSubmit(f.Request with { Version = "2.0.0" });
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, finding => finding.Code == "policy.unmet");
        Assert.Equal(before, f.Api.Session.Load().Records.Count);
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void ARefuseRepositoryPolicyWithNoRegisteredCloneRemainsUnavailable()
    {
        using var f = new Fixture();
        RecordEnvelope repository = f.Records.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Release source", ["role"] = "The selected source.", ["location"] = new JsonObject { ["github"] = "Example/widget" }, ["visibility"] = "public" });
        JsonObject policy = f.Records.PolicyBody("source", duty: false);
        policy["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository.Id, ["selectors"] = new JsonArray("docs/**") };
        policy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        policy["rules"] = new JsonArray(new JsonObject { ["key"] = "revision", ["kind"] = "vericairn.estate/revision-line", ["version"] = 1, ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        f.Records.Policy(policy);
        f.Records.AssertValid();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Contains(response.Policies!.Findings, finding => finding.ReleaseHold && finding.Verdict == "unavailable");
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Api.Records("operation"));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", false)]
    public void ExplicitWorkPolicyAppliesOnlyToTheReleaseThatCarriesTheWork(string version, bool held)
    {
        using var f = new Fixture();
        RecordEnvelope carrier = f.Records.Add("work-order", new JsonObject
        {
            ["key"] = "REL-1", ["kind"] = "ordinary", ["title"] = "A fixture release", ["intent"] = "Carry the selected work.", ["scope"] = new JsonArray("fixture"),
            ["exitQuestion"] = "Is the release complete?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("release"),
            ["releasePlan"] = new JsonObject { ["product"] = f.Product.Id, ["version"] = version },
        });
        JsonObject policy = f.Records.PolicyBody("explicit-work", duty: false);
        policy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        policy["appliesTo"] = new JsonObject { ["kind"] = "work-orders", ["workOrders"] = new JsonArray(carrier.Id) };
        f.Records.Policy(policy);
        f.Records.AssertValid();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.Equal(!held, response.Ok);
        Assert.Equal(held ? 0 : 1, f.Provider.Calls);
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = at;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Commit = "0123456789abcdef0123456789abcdef01234567";
        internal PolicyFixture Records { get; } = new();
        internal EstateService Api { get; }
        internal CountingProvider Provider { get; }
        internal RecordEnvelope Product { get; }
        internal Clock Clock { get; }
        internal DateTimeOffset Due { get; }
        internal ReleaseSubmitRequest Request => new(Product.Id, "1.0.0", "Example/widget", Records.Estate.OperatorId, Commit: Commit, Expected: Product.ComputedHash);

        internal Fixture()
        {
            Product = Records.Add("product", ProductBody("widget"));
            Api = EstateService.Open(Records.Estate.Root);
            RecordWritten device = Api.DeviceCreate(new(Records.Estate.OperatorId, "Fixture release writer", "file")).Written!;
            RecordEnvelope held = Api.Session.Load().ById[device.Id];
            Records.Estate.SetClock(held.AtTime);
            Records.Add("admission", new JsonObject { ["principal"] = Records.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Admit the isolated fixture writer." }, subject: held.Reference);
            Clock = new(Records.Estate.Clock.AddMinutes(1));
            Due = Clock.Now.AddSeconds(30);
            Api.PolicyClock = Clock;
            Provider = new(() => Clock.Now);
            var registry = new ProviderRegistry();
            registry.Register(Provider);
            Api.Providers = registry;
        }

        internal RecordEnvelope Duty(string level = "refuse", int graceDays = 0)
        {
            JsonObject policy = Records.PolicyBody();
            policy["level"] = level;
            policy["startsAt"] = RecordParser.FormatTimestamp(Clock.Now.AddSeconds(-10));
            policy["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = RecordParser.FormatTimestamp(Due);
            policy["rules"]![0]!["parameters"]!["graceDays"] = graceDays;
            return Records.Policy(policy);
        }

        internal RecordEnvelope Staleness(string level)
        {
            RecordEnvelope order = Records.Add("work-order", new JsonObject
            {
                ["key"] = "WAIT-1", ["kind"] = "ordinary", ["title"] = "Waiting work", ["intent"] = "Await release placement.",
                ["scope"] = new JsonArray("fixture"), ["exitQuestion"] = "Is the work placed?",
                ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("placement"),
            });
            JsonObject policy = Records.PolicyBody("planned-age", duty: false);
            policy["level"] = level;
            policy["startsAt"] = RecordParser.FormatTimestamp(Clock.Now.AddSeconds(-10));
            policy["rules"] = new JsonArray(new JsonObject
            {
                ["key"] = "age", ["kind"] = "vericairn.estate/staleness", ["version"] = 1,
                ["parameters"] = new JsonObject { ["class"] = "planned-without-release", ["maxAgeDays"] = 1 },
            });
            Records.Policy(policy);
            Records.AssertValid();
            return order;
        }

        internal RecordEnvelope Candidate(bool withArtifact = false) => Records.Add("release", new JsonObject
        {
            ["version"] = "1.0.0", ["repository"] = "Example/widget", ["commit"] = Commit, ["status"] = "candidate",
            ["artifacts"] = withArtifact ? new JsonArray(new JsonObject { ["name"] = "fixture.zip", ["hash"] = PolicyBasis.Hash("fixture"), ["bytes"] = 7, ["platform"] = "any" }) : new JsonArray(),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(),
            ["signing"] = "fixture", ["limitations"] = new JsonArray(),
        }, subject: Product.Reference);

        internal static JsonObject ProductBody(string slug) => new()
        {
            ["name"] = slug, ["slug"] = slug, ["summary"] = "A fixture product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture interface." }),
            ["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = "github-releases", ["settings"] = new JsonObject { ["repository"] = "Example/" + slug } } },
        };

        public void Dispose() { Api.Dispose(); Records.Dispose(); }
    }

    private sealed class CountingProvider(Func<DateTimeOffset> now) : IReleaseProvider
    {
        internal int Calls { get; private set; }
        internal DispatchRequest? Dispatched { get; private set; }
        internal Action? OnDispatch { get; set; }
        public string Provider => "github-releases";
        public ProviderDescriptor Descriptor { get; } = new FakeReleaseProvider().Descriptor;
        public string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) { Calls++; throw new InvalidOperationException("Admission must not resolve through the provider."); }
        public DispatchReceipt Dispatch(DispatchRequest request)
        {
            OnDispatch?.Invoke();
            Calls++;
            Dispatched = request;
            return new("fixture-run", "https://example.invalid/fixture-run", "Fixture dispatch.", RecordParser.FormatTimestamp(now()));
        }
        public RunObservation? ObserveRun(string destination, string runId) { Calls++; return null; }
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) { Calls++; return null; }
        public string ManualPublicationCommand(string destination, string tag) { Calls++; return "Manual fixture publication."; }
    }
}
