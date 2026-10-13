// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Verification, observation and operation records: evidence and intent, never permission; intent before effect; one operation per identity.</summary>
public class SoftwareRecordsTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Root = "fedcba9876543210fedcba9876543210fedcba98";

    [Fact]
    public void ARepositoryIsIdentifiedByItsRootCommitWhenItHasNoRemote()
    {
        using var estate = new TestEstate();
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId, $$"""{"kind":"repository","name":"Widget","role":"source of the widget","location":{"rootCommit":"{{Root}}"},"visibility":"private"}""");
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
        estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Nowhere","role":"x","location":{},"visibility":"private"}""");
        Assert.Contains(estate.Load().Findings, f => f.Code == "body.missing" && f.Location.Contains("location", StringComparison.Ordinal));
        Assert.NotNull(resource);
    }

    [Fact]
    public void AVerificationNamesTheCommitTheMethodTheEnvironmentAndTheObservedResult()
    {
        using var estate = new TestEstate();
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId, $$"""{"kind":"repository","name":"Widget","role":"source","location":{"rootCommit":"{{Root}}"},"visibility":"private"}""");
        var subject = new RecordReference("resource", resource.Id, resource.ComputedHash);
        estate.Add("verification", estate.BuilderId, $$"""{"method":"command","commit":"{{Commit}}","command":"dotnet test","environment":{"platform":"linux","os":"Ubuntu"},"role":"self","outcome":"passed","summary":"All tests pass.","exitCode":0,"durationMs":1200,"startedAt":"2026-10-13T12:00:00Z","finishedAt":"2026-10-13T12:00:02Z","evidence":[{"summary":"Output","hash":"sha256:{{new string('a', 64)}}"}],"limitations":[]}""", subject);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        estate.Add("verification", estate.BuilderId, $$"""{"method":"manual","commit":"{{Commit}}","environment":{"platform":"linux"},"role":"operator","outcome":"passed","summary":"Looked.","startedAt":"2026-10-13T12:00:02Z","finishedAt":"2026-10-13T12:00:00Z","evidence":[],"limitations":[]}""", subject);
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "verification.role");
        Assert.Contains(model.Findings, f => f.Code == "verification.time");

        estate.Add("verification", estate.BuilderId, $$"""{"method":"command","commit":"{{Commit}}","environment":{"path":42},"role":"self","outcome":"maybe","summary":"x","evidence":[],"limitations":[]}""", subject);
        model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "body.type" && f.Location.EndsWith("environment.path", StringComparison.Ordinal));
        Assert.Contains(model.Findings, f => f.Code == "body.enum" && f.Location.EndsWith("outcome", StringComparison.Ordinal));
    }

    [Fact]
    public void AnObservationIsFlatFactsWithAnAsOfTime()
    {
        using var estate = new TestEstate();
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId, $$"""{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget","rootCommit":"{{Root}}"},"visibility":"public"}""");
        var subject = new RecordReference("resource", resource.Id, resource.ComputedHash);
        estate.Add("observation", estate.BuilderId, $$"""{"kind":"repository","asOf":"2026-10-13T12:00:00Z","summary":"At {{Commit}} on main; clean.","facts":{"commit":"{{Commit}}","branch":"main","clean":true,"modified":0,"tags":["v0.1.0"]},"limitations":["One machine."]}""", subject);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);
        estate.Add("observation", estate.BuilderId, """{"kind":"repository","asOf":"2026-10-13T12:00:00Z","summary":"x","facts":{"nested":{"a":1}},"limitations":[]}""", subject);
        Assert.Contains(estate.Load().Findings, f => f.Code == "body.type" && f.Location.EndsWith("facts.nested", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOperationRecordsIntentBeforeEffectAndEveryChangeOfStateAsARevision()
    {
        using var estate = new TestEstate();
        string op = estate.OperatorId;
        string intent = """{"provider":"github-releases","action":"dispatch-release-workflow","identity":"github-releases:Example/widget:v0.2.0","intent":"Build 0.2.0.","target":{"repository":"Example/widget","workflow":"release.yml","ref":"main","version":"0.2.0"},"status":"STATUS","evidence":[]}""";

        // Intent first: a record that starts dispatched is refused; one that starts not-started is accepted.
        estate.Add("operation", op, intent.Replace("STATUS", "dispatched", StringComparison.Ordinal).Replace("\"evidence\"", "\"dispatchedAt\":\"2026-10-13T13:00:00Z\",\"evidence\"", StringComparison.Ordinal));
        Assert.Contains(estate.Load().Findings, f => f.Code == "operation.intent-first");

        using var fresh = new TestEstate();
        op = fresh.OperatorId;
        RecordEnvelope notStarted = fresh.Add("operation", op, intent.Replace("STATUS", "not-started", StringComparison.Ordinal));
        Assert.DoesNotContain(fresh.Load().Findings, f => f.Severity == Severity.Error);

        // A second current operation under the same identity is a duplicate, never a parallel attempt.
        RecordEnvelope duplicate = fresh.Add("operation", op, intent.Replace("STATUS", "not-started", StringComparison.Ordinal));
        Assert.Contains(fresh.Load().Findings, f => f.Code == "operation.duplicate-identity");
        File.Delete(Path.Combine(fresh.Root, duplicate.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));

        // Transitions: not-started to observed-success skips the dispatch and is refused; to dispatched is accepted.
        RecordEnvelope skipped = fresh.Add("operation", op, intent.Replace("STATUS", "observed-success", StringComparison.Ordinal).Replace("\"evidence\"", "\"dispatchedAt\":\"2026-10-13T13:00:00Z\",\"observed\":{\"summary\":\"done\",\"at\":\"2026-10-13T13:00:00Z\"},\"evidence\"", StringComparison.Ordinal), supersedes: notStarted.Id);
        Assert.Contains(fresh.Load().Findings, f => f.Code == "operation.transition");
        File.Delete(Path.Combine(fresh.Root, skipped.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
        RecordEnvelope dispatched = fresh.Add("operation", op, intent.Replace("STATUS", "dispatched", StringComparison.Ordinal).Replace("\"evidence\"", "\"dispatchedAt\":\"2026-10-13T13:00:00Z\",\"evidence\"", StringComparison.Ordinal), supersedes: notStarted.Id);
        Assert.DoesNotContain(fresh.Load().Findings, f => f.Severity == Severity.Error);

        // A revision keeps its identity; a known failure needs its observation; a retry after a failure names the reconciliation.
        RecordEnvelope renamed = fresh.Add("operation", op, intent.Replace("STATUS", "known-failure", StringComparison.Ordinal).Replace("v0.2.0", "v0.3.0", StringComparison.Ordinal).Replace("\"evidence\"", "\"observed\":{\"summary\":\"failed\",\"at\":\"2026-10-13T13:01:00Z\"},\"evidence\"", StringComparison.Ordinal), supersedes: dispatched.Id);
        Assert.Contains(fresh.Load().Findings, f => f.Code == "operation.identity");
        File.Delete(Path.Combine(fresh.Root, renamed.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
        RecordEnvelope failed = fresh.Add("operation", op, intent.Replace("STATUS", "known-failure", StringComparison.Ordinal).Replace("\"evidence\"", "\"dispatchedAt\":\"2026-10-13T13:00:00Z\",\"observed\":{\"summary\":\"The run failed.\",\"at\":\"2026-10-13T13:01:00Z\",\"reference\":\"1000\"},\"evidence\"", StringComparison.Ordinal), supersedes: dispatched.Id);
        Assert.DoesNotContain(fresh.Load().Findings, f => f.Severity == Severity.Error);
        RecordEnvelope retry = fresh.Add("operation", op, intent.Replace("STATUS", "dispatched", StringComparison.Ordinal).Replace("\"evidence\"", "\"dispatchedAt\":\"2026-10-13T13:02:00Z\",\"evidence\"", StringComparison.Ordinal), supersedes: failed.Id);
        Assert.Contains(fresh.Load().Findings, f => f.Code == "operation.reconciliation");
        File.Delete(Path.Combine(fresh.Root, retry.ExpectedPath.Replace('/', Path.DirectorySeparatorChar)));
        fresh.Add("operation", op, intent.Replace("STATUS", "dispatched", StringComparison.Ordinal).Replace("\"evidence\"", "\"dispatchedAt\":\"2026-10-13T13:02:00Z\",\"reconciliation\":\"The failed run produced nothing; retried under the same identity.\",\"evidence\"", StringComparison.Ordinal), supersedes: failed.Id);
        Assert.DoesNotContain(fresh.Load().Findings, f => f.Severity == Severity.Error);

        // Only the operator or a principal under local-engineering records external operations.
        fresh.Add("operation", fresh.BuilderId, intent.Replace("STATUS", "not-started", StringComparison.Ordinal).Replace("v0.2.0", "v0.9.0", StringComparison.Ordinal));
        Assert.Contains(fresh.Load().Findings, f => f.Code == "operation.author");
    }

    [Fact]
    public void StatusListsCurrentOperationsAndCountsVerifications()
    {
        using var estate = new TestEstate();
        estate.Add("operation", estate.OperatorId, """{"provider":"github-releases","action":"dispatch-release-workflow","identity":"github-releases:Example/widget:v0.2.0","intent":"Build.","target":{"repository":"Example/widget"},"status":"not-started","evidence":[]}""");
        EstateModel model = estate.Load();
        IReadOnlyList<EstateStatus.OperationItem> operations = EstateStatus.OperationItems(model);
        Assert.Single(operations);
        Assert.Equal("not-started", operations[0].Status);
        string text = EstateStatus.Render(model, new FindingList());
        Assert.Contains("Operations:\n  github-releases:Example/widget:v0.2.0  not-started  github-releases/dispatch-release-workflow", text, StringComparison.Ordinal);
        Assert.Contains("Verifications: 0", text, StringComparison.Ordinal);
        Assert.Contains("\"operations\"", EstateStatus.RenderJson(model, new FindingList()), StringComparison.Ordinal);
    }
}
