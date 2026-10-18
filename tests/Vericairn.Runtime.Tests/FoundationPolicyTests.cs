// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class FoundationPolicyTests
{
    internal static readonly PolicyKindRegistry Kinds = PolicyKindRegistry.Foundation(PolicyBasis.Hash("foundation conformance implementation"));
    private static readonly string[] OrderIds = ["order"];
    private static readonly string[] MarkdownPaths = ["docs/*.md"];
    private static readonly string[] AllPaths = ["**"];
    private static readonly string[] OperatorRoles = ["operator"];
    private static readonly string[] AcceptedCase = ["accepted-still-open"];
    [Theory]
    [InlineData("2026-01-01T23:59:59Z", "satisfied")]
    [InlineData("2026-01-02T00:00:00Z", "unmet")]
    [InlineData("2026-01-02T00:00:01Z", "unmet")]
    public void PlannedAgeUsesElapsedSecondsAtTheExactBoundary(string at, string verdict)
    {
        PolicyEvaluation result = Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 1 }, [Order()], at);
        PolicyFinding finding = Assert.Single(result.Findings);
        Assert.Equal(verdict, finding.Verdict);
        Assert.Equal("order", finding.Anchor!.Id);
        Assert.Equal("2026-01-01T00:00:00Z", finding.AnchorAt);
        Assert.Equal(verdict == "unmet" ? 1 : 0, result.Holds);
    }

    [Fact]
    public void RepeatedPlannedStateAndExplanationEditsPreserveAgeWhileARealTransitionRestartsIt()
    {
        RecordEnvelope order = Order();
        RecordEnvelope edit = R("work-order", "edited", new { key = "TASK-01" }, "2026-01-01T12:00:00Z", supersedes: order.Id);
        RecordEnvelope planned = R("work-status", "planned", new { status = "planned" }, "2026-01-01T18:00:00Z", order.Reference);
        PolicyFinding same = Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 1 }, [order, edit, planned]).Findings);
        Assert.Equal("order", same.Anchor!.Id);
        RecordEnvelope active = R("work-status", "active", new { status = "active" }, "2026-01-01T10:00:00Z", order.Reference);
        PolicyFinding changed = Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 1 }, [order, edit, active, planned]).Findings);
        Assert.Equal(planned.Reference, changed.Anchor);
        Assert.Equal("satisfied", changed.Verdict);
        RecordEnvelope conflicting = R("work-status", "conflict", new { status = "paused" }, planned.At, order.Reference);
        Assert.Equal("unavailable", Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 1 }, [order, planned, conflicting]).Findings).Verdict);
    }

    [Fact]
    public void ABacklogPassesPlacementButDoesNotQuietTheIndependentReleaseAge()
    {
        RecordEnvelope order = Order(new { kind = "backlog", key = "later" });
        var backlogs = new[] { new { key = "later", title = "Later", owner = "operator", reason = "Approved future work." } };
        RecordEnvelope placement = P(FoundationPolicyKinds.Placement, new { backlogs }, "placement");
        RecordEnvelope age = P(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 0 }, "age");
        PolicyEvaluation result = Evaluate([order, placement, age]);
        Assert.Equal(["unmet", "satisfied"], result.Findings.Select(f => f.Verdict));
        Assert.Equal(1, result.Holds);
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.Placement, new { backlogs = System.Array.Empty<object>() }, [order]).Findings).Verdict);
        Assert.All(Evaluate([order, placement, P(FoundationPolicyKinds.Placement, new { backlogs }, "second")]).Findings, f => Assert.Equal("unavailable", f.Verdict));
    }

    [Theory]
    [InlineData("candidate", "1.0.0", "satisfied")]
    [InlineData("published", "1.0.0", "unmet")]
    [InlineData("withdrawn", "1.0.0", "unmet")]
    [InlineData("candidate", "2.0.0", "unmet")]
    public void ReleasePlacementRequiresTheExactLivePlan(string releaseState, string plannedVersion, string verdict)
    {
        RecordEnvelope carrier = R("work-order", "carrier", new { key = "REL-001", releasePlan = new { product = "product", version = plannedVersion } });
        RecordEnvelope order = Order(new { kind = "release", carrier = carrier.Id, product = "product", version = "1.0.0" });
        RecordEnvelope release = R("release", "publication", new { status = releaseState, version = "1.0.0" }, subject: new("product", "product", null));
        PolicyEvaluation result = Run(FoundationPolicyKinds.Placement, new { backlogs = System.Array.Empty<object>() }, [carrier, order, release], selection: new { kind = "work-orders", workOrders = OrderIds });
        Assert.Equal(verdict, Assert.Single(result.Findings).Verdict);
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.Placement, new { backlogs = System.Array.Empty<object>() }, [order], selection: new { kind = "work-orders", workOrders = OrderIds }).Findings).Verdict);
    }

    [Fact]
    public void ExactAssignmentAndResultRelationsControlWaiting()
    {
        RecordEnvelope order = Order();
        RecordEnvelope assignment = R("assignment", "assignment", new { contributor = "author" }, subject: order.Reference);
        RecordEnvelope attempt = R("attempt", "attempt", new { }, subject: assignment.Reference);
        RecordEnvelope result = R("result", "result", new { outcome = "completed" }, "2026-01-01T12:00:00Z", attempt.Reference, by: "author");
        RecordEnvelope self = R("review", "self", new { independence = "self", outcome = "pass" }, "2026-01-01T13:00:00Z", result.Reference, by: "author");
        RecordEnvelope independent = R("review", "review", new { independence = "independent-agent", outcome = "findings" }, "2026-01-01T14:00:00Z", result.Reference, by: "reviewer");
        var age = new { @class = "result-awaiting-review", maxAgeDays = 0, reviewRequirement = "independent" };
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "assignment-without-attempt", maxAgeDays = 0 }, [order, assignment]).Findings).Verdict);
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "assignment-without-attempt", maxAgeDays = 0 }, [order, assignment, attempt]).Rules).Subjects);
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.Staleness, age, [order, assignment, attempt, result, self]).Findings).Verdict);
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.Staleness, age, [order, assignment, attempt, result, self, independent]).Rules).Subjects);
        RecordEnvelope replaced = R("result", "replacement", new { outcome = "completed" }, "2026-01-01T15:00:00Z", attempt.Reference, result.Id, "author");
        PolicyFinding pending = Assert.Single(Run(FoundationPolicyKinds.Staleness, age, [order, assignment, attempt, result, independent, replaced]).Findings);
        Assert.Equal(replaced.Reference, pending.Anchor);
        RecordEnvelope newAssignment = R("assignment", "new-assignment", new { contributor = "author" }, "2026-01-01T16:00:00Z", order.Reference, assignment.Id);
        Assert.Equal(newAssignment.Reference, Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "assignment-without-attempt", maxAgeDays = 0 }, [order, assignment, attempt, newAssignment]).Findings).Anchor);
    }

    [Fact]
    public void AcceptanceOfAnEarlierAttemptsResultDoesNotAcceptANewAttemptOrResetItsAgeToCreation()
    {
        RecordEnvelope order = Order();
        RecordEnvelope assignment = R("assignment", "assignment", new { contributor = "author" }, subject: order.Reference);
        RecordEnvelope first = R("attempt", "first", new { }, "2026-01-01T01:00:00Z", assignment.Reference);
        RecordEnvelope result = R("result", "result", new { outcome = "completed" }, "2026-01-01T02:00:00Z", first.Reference);
        RecordEnvelope accepted = R("acceptance", "accepted", new { }, "2026-01-01T03:00:00Z", result.Reference);
        RecordEnvelope next = R("attempt", "next", new { }, "2026-01-02T12:00:00Z", assignment.Reference);
        RecordEnvelope status = R("work-status", "status", new { status = "planned" }, "2026-01-01T00:00:00Z", order.Reference);
        RecordEnvelope[] records = [order, assignment, first, result, accepted, next, status];
        PolicyFinding age = Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 1 }, records, "2026-01-03T00:00:00Z").Findings);
        Assert.Equal("satisfied", age.Verdict);
        Assert.Equal(next.Reference, age.Anchor);
        Assert.Equal("satisfied", Assert.Single(Run(FoundationPolicyKinds.Consistency, new { cases = AcceptedCase }, records, "2026-01-03T00:00:00Z").Findings).Verdict);
    }

    [Fact]
    public void AskNotificationPreservesItsAnchorAndOnlyAnswerOrWithdrawalEndsWaiting()
    {
        RecordEnvelope ask = R("ask", "question", new { state = "open" });
        RecordEnvelope notification = R("ask", "notification", new { state = "notified" }, "2026-01-01T20:00:00Z", supersedes: ask.Id);
        var parameters = new { @class = "open-ask", maxAgeDays = 1 };
        Assert.Equal(ask.Reference, Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [ask, notification]).Findings).Anchor);
        RecordEnvelope answer = R("decision", "answer", new { answers = new[] { ask.Id } }, "2026-01-01T21:00:00Z");
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [ask, notification, answer]).Rules).Subjects);
    }

    [Fact]
    public void GatesRequireAllEventsAndKeepSupersededObligationsWithoutInventingDatesFromProse()
    {
        RecordEnvelope decision = R("decision", "decision", new { obligations = new[] { new { key = "check", gates = "After the release." } } });
        RecordEnvelope edit = R("decision", "edit", new { }, "2026-01-01T12:00:00Z", supersedes: decision.Id);
        object[] conditions = [new { kind = "at", at = "2026-01-02T00:00:00Z" }, new { kind = "release-published", product = "product", version = "1.0.0" }];
        object[] gates = [new { subject = Reference(decision), obligation = "check", conditions }];
        var parameters = new { @class = "obligation-past-gate", maxAgeDays = 0, gates };
        Assert.Equal("satisfied", Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [decision, edit]).Findings).Verdict);
        RecordEnvelope release = R("release", "publication", new { status = "published", version = "1.0.0" }, "2026-01-02T00:00:00Z", new("product", "product", null));
        PolicyFinding due = Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [decision, edit, release]).Findings);
        Assert.Equal("unmet", due.Verdict);
        Assert.Equal("2026-01-02T00:00:00Z", due.AnchorAt);
        Assert.Equal("unavailable", Assert.Single(Run(FoundationPolicyKinds.Staleness, new { @class = "obligation-past-gate", maxAgeDays = 0, gates = System.Array.Empty<object>() }, [decision, edit]).Findings).Verdict);
        RecordEnvelope discharge = R("discharge", "discharge", new { obligation = "check" }, "2026-01-02T00:00:00Z", decision.Reference);
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [decision, edit, release, discharge]).Rules).Subjects);
    }

    [Fact]
    public void RetirementNeedsBothAnAttributableRemovalAndCompletePathInventory()
    {
        RecordEnvelope retirement = R("retirement", "retirement", new { path = "docs/old.md", condition = "When done." });
        var parameters = new { @class = "retirement-past-condition", maxAgeDays = 0, gates = new[] { new { subject = Reference(retirement), conditions = new[] { new { kind = "at", at = "2026-01-01T00:00:00Z" } } } } };
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [retirement], repositories: [Repository()]).Findings).Verdict);
        RecordEnvelope removed = R("retirement", "removed", new { path = "docs/old.md", condition = "When done.", retiredAt = "2026-01-01T12:00:00Z" }, "2026-01-01T12:00:00Z", supersedes: retirement.Id);
        Assert.Equal("unavailable", Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [retirement, removed]).Findings).Verdict);
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [retirement, removed], repositories: [Repository()]).Rules).Subjects);
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.Staleness, parameters, [retirement, removed], repositories: [Repository(PolicyBasisTests.File("docs/old.md", "still here"))]).Findings).Verdict);
    }

    [Theory]
    [InlineData("TASK-01", "satisfied")]
    [InlineData("TASK-01-DOCS", "satisfied")]
    [InlineData("task-01", "unmet")]
    [InlineData("TASK-1", "unmet")]
    [InlineData("TASK-001", "unmet")]
    [InlineData("TASK-01-DOCS-EXTRA", "unmet")]
    public void KeyFamiliesUseLiteralAsciiPartsAndExactWidth(string key, string verdict)
    {
        var parameters = new { families = new[] { new { prefix = "TASK", separator = "-", digits = 2, suffixes = new[] { "", "DOCS" } } }, createdFrom = "2026-01-01T00:00:00Z" };
        Assert.Equal(verdict, Assert.Single(Run(FoundationPolicyKinds.KeyFamily, parameters, [R("work-order", "order", new { key })]).Findings).Verdict);
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.KeyFamily, parameters with { createdFrom = "2026-01-01T00:00:01Z" }, [Order()]).Rules).Subjects);
        Assert.Equal("unmet", Assert.Single(Run(FoundationPolicyKinds.KeyFamily, parameters with { families = parameters.families.Concat(parameters.families).ToArray() }, [Order()]).Findings).Verdict);
    }

    [Theory]
    [InlineData("Revision 1, January 2, 2026. Initial explanation.", "satisfied")]
    [InlineData("Revision 1, January 3, 2026. Tomorrow.", "unmet")]
    [InlineData("Revision 0, January 2, 2026. Zero.", "unmet")]
    [InlineData("Revision 2, February 29, 2025. Impossible date.", "unmet")]
    [InlineData("Revision 2, January 2, 2026.", "unmet")]
    [InlineData("Revision 2, Janvier 2, 2026. Wrong language.", "unmet")]
    public void RevisionLinesCheckTheFirstProseLineAndUtcCalendar(string line, string verdict)
    {
        PolicyEvaluation result = Run(FoundationPolicyKinds.RevisionLine, new { selectors = MarkdownPaths, style = "estate-design/1" }, [],
            selection: new { kind = "repository", repository = "estate", selectors = AllPaths }, repositories: [Repository(PolicyBasisTests.File("docs/new.md", "# Title\n\n" + line))]);
        PolicyFinding finding = Assert.Single(result.Findings);
        Assert.Equal(verdict, finding.Verdict);
        Assert.Equal("docs/new.md", finding.Target.Path);
        Assert.NotNull(finding.Target.Hash);
    }

    [Theory]
    [InlineData("```\n# Example title\nRevision 1, January 1, 2026. Example.\n```", "unmet")]
    [InlineData("# Title\n~~~\nRevision 1, January 1, 2026. Example.\n~~~\nMissing revision.", "unmet")]
    [InlineData("# Title\n~~~\nExample text.\n~~~\nRevision 1, January 1, 2026. Actual revision.", "satisfied")]
    [InlineData("   # Title\n\nRevision 1, January 1, 2026. Actual revision.", "satisfied")]
    public void CodeExamplesCannotSupplyTheDocumentsTitleOrRevisionProse(string text, string verdict)
    {
        PolicyEvaluation result = Run(FoundationPolicyKinds.RevisionLine, new { selectors = MarkdownPaths, style = "estate-design/1" }, [],
            selection: new { kind = "repository", repository = "estate", selectors = AllPaths }, repositories: [Repository(PolicyBasisTests.File("docs/new.md", text))]);
        Assert.Equal(verdict, Assert.Single(result.Findings).Verdict);
    }

    [Fact]
    public void ConsistencyRequiresExplicitOpenStateAndExactAcceptanceOrPublication()
    {
        RecordEnvelope order = Order();
        RecordEnvelope accepted = R("acceptance", "acceptance", new { }, subject: order.Reference);
        var parameters = new { cases = new[] { "accepted-still-open", "published-plan-still-open" } };
        Assert.All(Run(FoundationPolicyKinds.Consistency, parameters, [order, accepted]).Findings, f => Assert.Equal("satisfied", f.Verdict));
        RecordEnvelope status = R("work-status", "status", new { status = "submitted" }, subject: order.Reference);
        Assert.Equal(["unmet", "satisfied"], Run(FoundationPolicyKinds.Consistency, parameters, [order, accepted, status]).Findings.Select(f => f.Verdict));
        RecordEnvelope edit = R("work-order", "edit", new { key = "TASK-01" }, "2026-01-01T01:00:00Z", supersedes: order.Id);
        Assert.All(Run(FoundationPolicyKinds.Consistency, parameters, [order, accepted, status, edit]).Findings, f => Assert.Equal("satisfied", f.Verdict));
    }

    [Theory]
    [InlineData("vericairn.estate/staleness", "{\"class\":\"planned-without-release\",\"maxAgeDays\":-1}")]
    [InlineData("vericairn.estate/staleness", "{\"class\":\"open-ask\",\"maxAgeDays\":0,\"guess\":true}")]
    [InlineData("vericairn.estate/work-state-consistency", "{\"cases\":[\"guess\"]}")]
    [InlineData("vericairn.estate/work-key-family", "{\"families\":[{\"prefix\":\".*\",\"separator\":\"-\",\"digits\":2,\"suffixes\":[\"\"]}],\"createdFrom\":\"2026-01-01T00:00:00Z\"}")]
    public void MalformedParametersRemainErrorsAndUnavailableEvenForAnEmptySelection(string kind, string json)
    {
        PolicyEvaluation result = Run(kind, JsonSerializer.Deserialize<JsonElement>(json), []);
        Assert.Equal(1, result.Errors);
        Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
    }

    [Theory]
    [InlineData("docs/*.md", "docs/a.md", true)]
    [InlineData("docs/*.md", "docs/nested/a.md", false)]
    [InlineData("docs/**/a.md", "docs/a.md", true)]
    [InlineData("docs/**/a.md", "docs/nested/a.md", true)]
    [InlineData("docs/*.md", "Docs/a.md", false)]
    public void SelectorsAreOrdinalAndUseOnlyTheDeclaredGrammar(string selector, string path, bool expected) => Assert.Equal(expected, PolicyInputs.Matches(selector, path));

    [Fact]
    public void MissingUnreadableAndConflictingSourcesNeverBecomeAnEmptyPass()
    {
        var parameters = new { selectors = MarkdownPaths, style = "estate-design/1" };
        var selection = new { kind = "repository", repository = "estate", selectors = AllPaths };
        PolicyFile file = PolicyBasisTests.File("docs/a.md", "# A\nRevision 1, January 1, 2026. Initial text.");
        PolicyRepository[] inventories = [Repository(file with { Available = false }), Repository(file, file with { Path = "docs/A.md" }), Repository(file with { IsLink = true }), Repository(file with { Path = "docs/../a.md" }), Repository(file with { Hash = new string('0', 64) })];
        foreach (PolicyRepository inventory in inventories)
        {
            PolicyEvaluation result = Run(FoundationPolicyKinds.RevisionLine, parameters, [], selection: selection, repositories: [inventory]);
            Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
            Assert.Equal(1, result.Holds);
        }
        Assert.Equal("unavailable", Assert.Single(Run(FoundationPolicyKinds.RevisionLine, parameters, [], selection: selection).Findings).Verdict);
        Assert.Equal(0, Assert.Single(Run(FoundationPolicyKinds.RevisionLine, parameters, [], selection: selection, repositories: [Repository()]).Rules).Subjects);
    }

    [Fact]
    public void SubmissionGateUsesTheExactRecordedIntentOrTheExplicitProposal()
    {
        RecordEnvelope decision = R("decision", "decision", new { obligations = new[] { new { key = "check", gates = "At submission." } } });
        var parameters = new { @class = "obligation-past-gate", maxAgeDays = 0, gates = new[] { new { subject = Reference(decision), obligation = "check", conditions = new[] { new { kind = "release-submission", product = "product", version = "1.0.0" } } } } };
        RecordEnvelope policy = P(FoundationPolicyKinds.Staleness, parameters);
        PolicyBasis basis = Basis([policy, decision]);
        Assert.Equal("satisfied", Assert.Single(PolicyEvaluator.Evaluate(basis, Kinds).Findings).Verdict);
        var submission = new PolicySubmission(new("release", "candidate", "exact"), "product", "1.0.0", new string('1', 40));
        PolicyEvaluation live = PolicyEvaluator.Evaluate(new(basis.Model, basis.AsOf, PolicyTimeOrigin.Submission, implementations: Kinds.Implementations, submission: submission), Kinds);
        Assert.Equal("unmet", Assert.Single(live.Findings).Verdict);
        RecordEnvelope intent = R("operation", "intent", new { action = Vericairn.Runtime.Release.ReleaseFlow.DispatchAction, target = new { product = "product", version = "1.0.0" } }, "2026-01-01T12:00:00Z");
        Assert.Equal(intent.Reference, Assert.Single(Evaluate([policy, decision, intent]).Findings).Anchor);
    }

    [Fact]
    public void ProductSelectionUsesOnlyTypedRelationsAndExplicitRepositorySources()
    {
        RecordEnvelope selected = Order(new { kind = "release", product = "product", carrier = "carrier", version = "1.0.0" });
        RecordEnvelope unrelated = R("work-order", "unrelated", new { key = "wrong", inputs = new[] { new { type = "product", id = "product" } } });
        var parameters = new { families = new[] { new { prefix = "TASK", separator = "-", digits = 2, suffixes = new[] { "" } } }, createdFrom = "2026-01-01T00:00:00Z" };
        PolicyEvaluation result = Run(FoundationPolicyKinds.KeyFamily, parameters, [selected, unrelated], selection: new { kind = "product", product = "product" });
        Assert.Equal("order", Assert.Single(result.Findings).Target.Identity);
        RecordEnvelope repository = R("resource", "source", new { kind = "repository" }, subject: new("product", "product", null));
        PolicyEvaluation files = Run(FoundationPolicyKinds.RevisionLine, new { repository = "source", selectors = MarkdownPaths, style = "estate-design/1" }, [repository],
            selection: new { kind = "product", product = "product" }, repositories: [Repository(PolicyBasisTests.File("docs/a.md", "# A\nRevision 1, January 1, 2026. Initial.")) with { Identity = "source" }]);
        Assert.Equal("satisfied", Assert.Single(files.Findings).Verdict);
    }

    internal static PolicyEvaluation Run(string kind, object parameters, RecordEnvelope[] records, string at = "2026-01-02T00:00:00Z", object? selection = null, PolicyRepository[]? repositories = null) =>
        Evaluate([P(kind, parameters, selection: selection), .. records], at, repositories);

    internal static PolicyEvaluation Evaluate(RecordEnvelope[] records, string at = "2026-01-02T00:00:00Z", PolicyRepository[]? repositories = null) => PolicyEvaluator.Evaluate(Basis(records, at, repositories), Kinds);

    internal static PolicyBasis Basis(RecordEnvelope[] records, string at = "2026-01-02T00:00:00Z", PolicyRepository[]? repositories = null) => new(PolicyBasisTests.Model([
        R("principal", "operator", new { kind = "human", roles = OperatorRoles }, by: "operator"),
        R("estate", "estate", new { @operator = "operator" }), R("product", "product", new { }), .. records]), at, PolicyTimeOrigin.Preview, repositories, implementations: Kinds.Implementations);

    internal static RecordEnvelope P(string kind, object parameters, string key = "policy", object? selection = null) => R("estate-policy", key, new
    {
        key, owner = "operator", state = "active", level = "refuse", startsAt = "2026-01-01T00:00:00Z", maxExceptionDays = 30,
        appliesTo = selection ?? new { kind = "estate" }, rules = new[] { new { key = "rule", kind, version = 1, parameters } },
    });

    internal static RecordEnvelope R(string type, string id, object body, string at = "2026-01-01T00:00:00Z", RecordReference? subject = null, string? supersedes = null, string by = "operator")
    {
        JsonElement element = JsonSerializer.SerializeToElement(body);
        string hash = PolicyBasis.Hash(new { type, id, at, by, subject, supersedes, body = element });
        return new(type, id, at, by, subject, supersedes, element, hash, hash, [], 3);
    }

    internal static RecordEnvelope Order(object? placement = null) => R("work-order", "order", placement is null ? new { key = "TASK-01" } : new { key = "TASK-01", placement });
    internal static object Reference(RecordEnvelope record) => new { type = record.Type, id = record.Id, hash = record.ComputedHash };
    internal static PolicyRepository Repository(params PolicyFile[] files) => new("estate", "working-tree", PolicyBasis.RepositoryContract, true, files.ToImmutableArray());
}
