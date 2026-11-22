// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class RepositoryChangeRecordsTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Basis = new('b', 40);

    [Theory]
    [InlineData("missing")]
    [InlineData("pending")]
    [InlineData("failed")]
    [InlineData("other-head")]
    [InlineData("none")]
    public void GenericOperationWritesCannotAuthorizeAMergeWithoutItsExactChecks(string invalid)
    {
        using var fixture = new Fixture(invalid == "none");
        JsonObject body = fixture.Body("change-merge");
        body["checks"] = invalid == "missing" ? new JsonArray() : new JsonArray(new JsonObject
        {
            ["name"] = "tests", ["head"] = invalid == "other-head" ? Basis : Head,
            ["status"] = invalid == "pending" ? "in_progress" : "completed", ["conclusion"] = invalid == "failed" ? "failure" : "success",
        });
        fixture.Dispatch(body);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "operation.merge-checks");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("failed")]
    [InlineData("other-range")]
    [InlineData("findings")]
    public void GenericOperationWritesCannotAuthorizeAnUnscannedPush(string invalid)
    {
        using var fixture = new Fixture();
        JsonObject body = fixture.Body("change-push");
        body["target"]!["base"] = Basis;
        if (invalid != "missing") body["scan"] = new JsonObject { ["tool"] = "scanner", ["version"] = "1.0.0",
            ["range"] = Basis + ".." + (invalid == "other-range" ? Basis : Head), ["verdict"] = invalid == "failed" ? "incomplete" : "passed",
            ["findingCount"] = invalid == "findings" ? 1 : 0 };
        fixture.Dispatch(body);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "operation.push-scan");
    }

    // FIX-154: a push records where its scan began: the merge base, or the observed push of the same repository, branch and target that it
    // continued, whose head is its base where the revision records one (a retry's revision before its dispatch keeps the basis without
    // the base it finds again). Nothing else records a basis.
    [Theory]
    [InlineData("merge-base", true)]
    [InlineData("continued", true)]
    [InlineData("other-branch", false)]
    [InlineData("other-head", false)]
    [InlineData("other-target", false)]
    [InlineData("not-observed", false)]
    [InlineData("unknown", false)]
    [InlineData("on-a-merge", false)]
    [InlineData("no-base", true)]
    public void APushsBasisIsTheMergeBaseOrTheObservedPushOfTheBranchItContinued(string basis, bool valid)
    {
        using var fixture = new Fixture();
        string earlier = new('d', 40);
        JsonObject first = fixture.Body("change-push");
        first["identity"] = "example-first";
        first["target"]!["head"] = basis == "other-head" ? Basis : earlier;
        first["target"]!["branch"] = basis == "other-branch" ? "work/other" : "work/change";
        if (basis == "other-target") first["target"]!["target"] = "dev";
        first["target"]!["base"] = Basis;
        first["scan"] = new JsonObject { ["tool"] = "scanner", ["version"] = "1.0.0", ["range"] = Basis + ".." + first["target"]!["head"]!.GetValue<string>(), ["verdict"] = "passed", ["findingCount"] = 0 };
        RecordEnvelope continued = fixture.Dispatch(first);
        if (basis != "not-observed")
        {
            first["status"] = "observed-success";
            continued = fixture.Estate.Add("operation", fixture.Estate.OperatorId, first.ToJsonString(), supersedes: continued.Id);
        }
        JsonObject body = fixture.Body(basis == "on-a-merge" ? "change-merge" : "change-push");
        body["target"]!["branch"] = "work/change";
        if (basis != "no-base") body["target"]!["base"] = earlier;
        body["target"]!["basis"] = basis switch { "merge-base" => "merge-base", "unknown" => new string('0', 26), _ => continued.Id };
        fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString());
        Assert.Equal(valid, !fixture.Estate.Load().Findings.Any(f => f.Code == "operation.change-basis"));
    }

    // A retry after a known failure may find its base and heads again but keeps a recorded basis, as a reader before 0.9.4 requires.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARetryFindsItsBaseAgainButKeepsTheBasisItRecorded(bool keepsBasis)
    {
        using var fixture = new Fixture();
        JsonObject body = fixture.Body("change-push");
        body["target"]!["branch"] = "work/change";
        body["target"]!["base"] = Basis;
        body["target"]!["basis"] = "merge-base";
        RecordEnvelope intent = fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString());
        body["status"] = "known-failure";
        body["observed"] = new JsonObject { ["summary"] = "The push was refused.", ["at"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(1)) };
        RecordEnvelope failed = fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString(), supersedes: intent.Id);
        body["status"] = "uncertain";
        body["dispatchedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock.AddSeconds(1));
        body["reconciliation"] = "The remote branch was observed; nothing was pushed.";
        body["target"]!["base"] = Head;
        if (!keepsBasis) body["target"]!.AsObject().Remove("basis");
        fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString(), supersedes: failed.Id);
        Assert.Equal(keepsBasis, !fixture.Estate.Load().Findings.Any(f => f.Code == "operation.change-target"));
    }

    [Fact]
    public void ScanRecordsRejectFindingContentsAndProcessOutput()
    {
        using var fixture = new Fixture();
        JsonObject body = fixture.Body("change-push");
        body["scan"] = new JsonObject { ["tool"] = "scanner", ["version"] = "1.0.0", ["range"] = Basis + ".." + Head,
            ["verdict"] = "findings", ["findingCount"] = 1, ["matchedText"] = "A report field that must never be admitted.", ["standardError"] = "Diagnostics are not evidence." };
        fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString());
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Location.Contains("scan", StringComparison.Ordinal) && f.Severity == Severity.Error);
    }

    [Fact]
    public void ADispatchedMergeKeepsItsHeadAndTheChecksOnWhichItRelied()
    {
        using var fixture = new Fixture();
        JsonObject body = fixture.Body("change-merge");
        body["checks"] = new JsonArray(new JsonObject { ["name"] = "tests", ["head"] = Head, ["status"] = "completed", ["conclusion"] = "success" });
        RecordEnvelope dispatched = fixture.Dispatch(body);
        Assert.DoesNotContain(fixture.Estate.Load().Findings, f => f.Severity == Severity.Error);
        body["status"] = "uncertain";
        body["target"]!["head"] = Basis;
        body["checks"]![0]!["head"] = Basis;
        fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString(), supersedes: dispatched.Id);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "operation.change-target");
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "operation.change-evidence-kept");
    }

    [Fact]
    public void AMissingOrMalformedTargetProducesFindingsWithoutCrashingValidation()
    {
        using var fixture = new Fixture();
        JsonObject body = fixture.Body("change-push");
        body.Remove("target");
        fixture.Estate.Add("operation", fixture.Estate.OperatorId, body.ToJsonString());
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "operation.change-head");
    }

    [Fact]
    public void AProductCannotDeclareAServiceAsItsRepository()
    {
        using var fixture = new Fixture();
        RecordEnvelope service = fixture.Estate.Add("resource", fixture.Estate.OperatorId, """{"kind":"service","name":"Site","role":"Website","location":{"url":"https://example.invalid"},"visibility":"public"}""");
        JsonObject product = JsonNode.Parse(fixture.Product.Body.GetRawText())!.AsObject();
        product["changes"]!["repository"] = service.Id;
        fixture.Estate.Add("product", fixture.Estate.OperatorId, product.ToJsonString(), supersedes: fixture.Product.Id);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "product.changes");
    }

    [Fact]
    public void RepositoryChangesBelongToRecordFormatThree()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""");
        RecordEnvelope product = estate.Add("product", estate.OperatorId, $$$"""
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"example","repository":"{{{repository.Id}}}","target":"main","requiredChecks":["tests"]}}
            """);
        estate.Add("operation", estate.OperatorId, $$$"""
            {"provider":"example","action":"change-merge","identity":"example-change","intent":"A checked repository change.","status":"not-started",
             "target":{"head":"{{{Head}}}","product":"{{{product.Id}}}","productHash":"{{{product.ComputedHash}}}","destination":"Example/widget","target":"main"},
             "requiredChecks":["tests"],"evidence":[]}
            """);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "body.format" && f.Location.EndsWith("#body.changes", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.format" && f.Location.EndsWith("#body.requiredChecks", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "operation.format");
    }

    private sealed class Fixture : IDisposable
    {
        internal TestEstate Estate { get; } = new(recordFormat: 3);
        internal RecordEnvelope Product { get; }
        private readonly RecordEnvelope repository;
        private readonly bool none;

        internal Fixture(bool none = false)
        {
            this.none = none;
            repository = Estate.Add("resource", Estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""");
            Product = Estate.Add("product", Estate.OperatorId, $$"""
                {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
                 "changes":{"provider":"example","repository":"{{repository.Id}}","target":"main","requiredChecks":{{(none ? "[]" : "[\"tests\"]")}} } }
                """);
        }

        internal JsonObject Body(string action) => new()
        {
            ["provider"] = "example", ["action"] = action, ["identity"] = "example-change", ["intent"] = "A checked repository change.", ["status"] = "not-started",
            ["target"] = new JsonObject { ["head"] = Head, ["product"] = Product.Id, ["productHash"] = Product.ComputedHash,
                ["repository"] = repository.Id, ["repositoryHash"] = repository.ComputedHash, ["destination"] = "Example/widget", ["target"] = "main" },
            ["requiredChecks"] = none ? new JsonArray() : new JsonArray("tests"), ["evidence"] = new JsonArray(),
        };

        internal RecordEnvelope Dispatch(JsonObject body)
        {
            RecordEnvelope intent = Estate.Add("operation", Estate.OperatorId, body.ToJsonString());
            body["status"] = "dispatched";
            body["dispatchedAt"] = RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1));
            return Estate.Add("operation", Estate.OperatorId, body.ToJsonString(), supersedes: intent.Id);
        }

        public void Dispose() => Estate.Dispose();
    }
}
