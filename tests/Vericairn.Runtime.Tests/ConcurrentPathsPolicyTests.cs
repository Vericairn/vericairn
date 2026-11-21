// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (POLICY-08; the estate's design/measured-work.md § 5 and design/estate-policies.md §§ 5.11 and 6 to 8): the
/// <c>vericairn.estate/concurrent-paths</c> kind evaluates the pairs of active assignments whose effective paths meet within its
/// repository selection, in the estate check and status; at refuse the write boundary refuses a new assignment, a run admission and a
/// new attempt that leave an unmet or unavailable pair, naming each pair and both remedies, and honors an exception for the exact pair.
/// </summary>
[Collection("home")]
public class ConcurrentPathsPolicyTests
{
    [Fact]
    public void ARefuseLevelPolicyRefusesANewAssignmentThatLeavesAnUnmetPairAndNamesBothRemedies()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        RecordEnvelope policy = Declare(run, "held-widget", ["widget/**"], "refuse");
        int records = run.Estate.Load().Records.Count;

        WriteOutcome refused = run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource });
        Assert.False(refused.Ok);
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == "assignment.concurrent-paths");
        Assert.StartsWith("The new assignment is refused: a refuse-level concurrent-paths policy holds a pair it leaves unresolved.", finding.Message, StringComparison.Ordinal);
        Assert.Contains($"Policy held-widget ({policy.Id}), rule concurrent, unmet: W-1's assignment {run.Assignment} and W-3's assignment ", finding.Message, StringComparison.Ordinal);
        Assert.Contains($" in widget ({run.Resource}) share paths such as widget", finding.Message, StringComparison.Ordinal);
        // A new assignment is offered only the remedies that can apply to it: an exception cannot pin a revision not yet recorded.
        Assert.Contains("Record it resolved: name the earlier assignment in after (sequence), or narrow its paths with excludedChanges so that they share none within the selection (one owner).", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("exception for the exact pair", finding.Message, StringComparison.Ordinal);
        Assert.Equal(records, run.Estate.Load().Records.Count);

        // Either remedy records the assignment: a sequence, or one owner, the other excluding the shared paths.
        string sequenced = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, After(RunEstate.Brief, run.Assignment)) { Repository = run.Resource })).Id;
        Order(run, "W-4");
        RunEstate.Ok(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId,
            Brief(["path:widget/", "path:docs/"], ["path:widget/"])) { Repository = run.Resource }));
        // Paths outside the selection overlap and are reported, never held.
        Order(run, "W-5");
        Order(run, "W-6");
        RunEstate.Ok(run.Service.Assign(new AssignRequest("W-5", run.Estate.BuilderId, run.Estate.OperatorId, Brief(["path:docs/"], [])) { Repository = run.Resource }));
        WriteOutcome reported = run.Service.Assign(new AssignRequest("W-6", run.Estate.BuilderId, run.Estate.OperatorId, Brief(["path:docs/"], [])) { Repository = run.Resource });
        Assert.True(reported.Ok);
        Assert.Contains(reported.Notices!, n => n.StartsWith("Overlap: W-4's assignment", StringComparison.Ordinal));

        // The estate check and the evaluation carry the sequenced pair as satisfied, keyed by the pair of lineages.
        PolicyFindingDto pair = Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("pair", "satisfied", "overlap"), (pair.Target.Kind, pair.Verdict, pair.Item));
        Assert.Equal(string.Join('+', new[] { run.Assignment, sequenced }.Order(StringComparer.Ordinal)), pair.Target.Identity);
        Assert.Equal(run.Resource, pair.Target.Repository);
        Assert.Equal(new[] { run.Assignment, sequenced }.Order(StringComparer.Ordinal), pair.Evidence.Select(e => e.Id));
    }

    [Fact]
    public void APolicyAtWarnOrNotYetInForceHoldsNothing()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        Declare(run, "reported-widget", ["widget/**"], "warn");
        WriteOutcome reported = run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource });
        Assert.True(reported.Ok);
        PolicyFindingDto pair = Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("unmet", "warn", false), (pair.Verdict, pair.Level, pair.ReleaseHold));

        // A refuse-level policy whose activation is later holds nothing until then.
        Order(run, "W-4");
        Declare(run, "later-widget", ["widget/**"], "refuse", startsIn: TimeSpan.FromDays(1));
        Assert.True(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource }).Ok);
    }

    [Fact]
    public void AnUnresolvedPairHoldsAttemptsAndRunsUntilAnExceptionPinsItsExactRevisions()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        // Recorded before the policy, the pair is reported, never refused.
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        RecordEnvelope policy = Declare(run, "held-widget", ["widget/**"], "refuse");

        PolicyResponse evaluation = run.Service.PolicyEvaluate();
        PolicyFindingDto unmet = Assert.Single(evaluation.Findings, f => f.Kind == ConcurrentPaths.Kind);
        // It holds work starts, below, but never a release submission.
        Assert.Equal(("unmet", "none", false), (unmet.Verdict, unmet.Disposition, unmet.ReleaseHold));
        Assert.Equal(0, evaluation.Holds);
        Assert.Equal(later, unmet.Anchor!.Id);
        Assert.Contains(run.Service.Check().Findings, f => f.Code == "policy.unmet" && f.Message.Contains("concurrent", StringComparison.Ordinal));

        int records = run.Estate.Load().Records.Count;
        WriteOutcome attempt = run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start."));
        Assert.False(attempt.Ok);
        FindingDto held = Assert.Single(attempt.Problem!.Findings, f => f.Code == "attempt.concurrent-paths");
        Assert.StartsWith("A new attempt is refused:", held.Message, StringComparison.Ordinal);
        Assert.Contains("or the operator grants a policy exception for the exact pair of assignment revisions.", held.Message, StringComparison.Ordinal);
        Assert.Contains($"W-1's assignment {run.Assignment} and W-3's assignment {later}", held.Message, StringComparison.Ordinal);
        Assert.False(run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget repository, branch work/w-1", "Start.")).Ok);
        // An attempt dated back before the policy's activation is held as one written now.
        WriteOutcome backdated = run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.", At: run.Estate.Load().ById[later].At));
        Assert.Contains(backdated.Problem!.Findings, f => f.Code == "attempt.concurrent-paths");
        Assert.Equal(records, run.Estate.Load().Records.Count);

        // A corrective revision is never held by a pair it inherited, and the pair still holds its attempts.
        EstateModel model = run.Estate.Load();
        string revised = Revise(run, later, b => b["note"] = "The same brief, noted.").Id;
        Assert.False(run.Service.Attempt(new AttemptRequest(revised, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.")).Ok);

        // The operator's exception for the exact pair of revisions lets work on both start.
        model = run.Estate.Load();
        RecordEnvelope decision = Decide(run, "Accept the hand merge of the widget's two changes.");
        JsonObject grant = Exception(run, decision, [model.ById[run.Assignment].Reference, model.ById[revised].Reference]);
        RecordWritten excepted = RunEstate.Ok(run.Service.PolicyExcept("held-widget", new PolicyChangeRequest(run.Estate.OperatorId, JsonSerializer.SerializeToElement(grant), policy.ComputedHash)));
        PolicyFindingDto covered = Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("unmet", "excepted", false), (covered.Verdict, covered.Disposition, covered.ReleaseHold));
        Assert.Equal(excepted.Id, Assert.Single(covered.Exceptions).Id);
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(revised, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.")));

        // A newer revision of either party is another pair: the exception pins the revisions it names, so a new attempt waits for a fresh one.
        string again = Revise(run, revised, b => b["note"] = "Noted again.").Id;
        WriteOutcome lapsed = run.Service.Attempt(new AttemptRequest(again, run.Estate.BuilderId, "widget repository, branch work/w-3", "Again."));
        Assert.Contains(lapsed.Problem!.Findings, f => f.Code == "attempt.concurrent-paths");

        // A revision that drops a sequence its predecessor had is a new unresolved pair, and is refused.
        Order(run, "W-4");
        string after = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId,
            AfterAll(RunEstate.Brief, run.Assignment, again)) { Repository = run.Resource })).Id;
        WriteOutcome dropped = Submit(run, after, b => b["brief"]!.AsObject().Remove("after"));
        Assert.Contains(dropped.Problem!.Findings, f => f.Code == "assignment.concurrent-paths" && f.Message.StartsWith("This revision of the assignment is refused", StringComparison.Ordinal));

        // Once one party is released, here by its work order's acceptance, its pairs are no longer subjects, and the other's work starts.
        RecordEnvelope first = run.Estate.Load().Current("work-order").Single(o => EstateModel.BodyString(o, "key") == "W-1");
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("acceptance", run.Estate.OperatorId, RunEstate.Json("""{"statement":"Accepted.","basis":[],"self":true}"""), $"work-order:{first.Id}:{first.ComputedHash}")));
        RunEstate.Ok(run.Service.Attempt(new AttemptRequest(again, run.Estate.BuilderId, "widget repository, branch work/w-3", "Again.")));
    }

    [Fact]
    public void RunAdmissionIsAnAssignmentStartThatAnUnresolvedPairRefuses()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        RecordEnvelope policy = Declare(run, "held-widget", ["widget/**"], "refuse");
        // The operator's key leaves this machine, as the worker's custody guard requires before a run launches.
        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);
        int records = run.Estate.Load().Records.Count;
        RunResponse refused = run.Start("generic", "generic", run.Report("held"));
        Assert.False(refused.Ok);
        Assert.True(refused.Message.StartsWith("Its run is refused: a refuse-level concurrent-paths policy holds a pair it leaves unresolved.", StringComparison.Ordinal), refused.Message);
        Assert.True(refused.Message.Contains($"Policy held-widget ({policy.Id}), rule concurrent, unmet: W-1's assignment {run.Assignment} and W-3's assignment {later}", StringComparison.Ordinal), refused.Message);
        Assert.Empty(run.Service.Records("operation"));
        Assert.Equal(records, run.Estate.Load().Records.Count);
    }

    [Fact]
    public void APairWithAnAssignmentNamingNoRepositoryIsUnavailableAndNoExceptionCoversIt()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        RecordEnvelope order = run.Estate.Load().Current("work-order").Single(o => EstateModel.BodyString(o, "key") == "W-3");
        // An assignment an older runtime wrote, naming no repository; none of its runs named one either.
        JsonObject body = new() { ["contributor"] = run.Estate.BuilderId, ["brief"] = JsonNode.Parse(RunEstate.Brief) };
        run.Estate.SetClock(DateTimeOffset.UtcNow);
        RecordEnvelope unnamed = run.Estate.Add("assignment", run.Estate.OperatorId, body.ToJsonString(), order.Reference);
        RecordEnvelope policy = Declare(run, "held-widget", ["widget/**"], "refuse");

        PolicyFindingDto unavailable = Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("unavailable", false), (unavailable.Verdict, unavailable.ReleaseHold));
        Assert.Contains($"W-3's assignment {unnamed.Id} names no repository", unavailable.Actual, StringComparison.Ordinal);
        WriteOutcome attempt = run.Service.Attempt(new AttemptRequest(run.Assignment, run.Estate.BuilderId, "widget repository, branch work/w-1", "Start."));
        FindingDto held = Assert.Single(attempt.Problem!.Findings, f => f.Code == "attempt.concurrent-paths");
        Assert.Contains("unavailable", held.Message, StringComparison.Ordinal);
        Assert.Contains("no exception covers it", held.Message, StringComparison.Ordinal);

        // An unavailable pair is never excepted into satisfaction.
        EstateModel model = run.Estate.Load();
        RecordEnvelope decision = Decide(run, "Except the pair.");
        JsonObject grant = Exception(run, decision, [model.ById[run.Assignment].Reference, unnamed.Reference]);
        Assert.False(run.Service.PolicyExcept("held-widget", new PolicyChangeRequest(run.Estate.OperatorId, JsonSerializer.SerializeToElement(grant), policy.ComputedHash)).Ok);
    }

    [Fact]
    public void APairTargetBelongsToAConcurrentPathsRuleAndNamesTwoDifferentAssignments()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        RecordEnvelope policy = Declare(run, "held-widget", ["widget/**"], "refuse");
        EstateModel model = run.Estate.Load();
        RecordEnvelope decision = Decide(run, "Except the pair.");
        RecordReference first = model.ById[run.Assignment].Reference, second = model.ById[later].Reference;
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.OperatorDevice);

        RecordEnvelope record = run.Estate.Add("policy-exception", run.Estate.OperatorId, Exception(run, decision, [first, second]).ToJsonString(), policy.Reference, signer: signer);
        Assert.DoesNotContain(run.Estate.Load().Findings, f => f.Location.Contains(record.Id, StringComparison.Ordinal) && f.Severity == Severity.Error);

        JsonObject one = Exception(run, decision, [first]);
        JsonObject same = Exception(run, decision, [first, first]);
        JsonObject record2 = Exception(run, decision, [first, second]);
        record2["targets"] = new JsonArray(new JsonObject { ["kind"] = "record", ["record"] = Reference(first) });
        foreach (JsonObject wrong in new[] { one, same, record2 })
        {
            RecordEnvelope refused = run.Estate.Add("policy-exception", run.Estate.OperatorId, wrong.ToJsonString(), policy.Reference, signer: signer);
            Assert.Contains(run.Estate.Load().Findings, f => f.Code is "exception.pair" or "policy.duplicate" && f.Location.Contains(refused.Id, StringComparison.Ordinal));
            File.Delete(Path.Combine(run.Estate.Root, refused.ExpectedPath));
        }
    }

    [Fact]
    public void ASelectionNoPairMeetsIsSatisfiedWithNoSubjectAndHoldsNothing()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        // Only the widget's assignment holds paths in the selection, so no pair meets it.
        RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, Brief(["path:docs/"], [])) { Repository = run.Resource }));
        Declare(run, "held-widget", ["widget/**"], "refuse");
        PolicyResponse evaluation = run.Service.PolicyEvaluate();
        PolicyFindingDto none = Assert.Single(evaluation.Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("policy", "satisfied", 0, false), (none.Target.Kind, none.Verdict, none.SubjectCount, none.ReleaseHold));
        Assert.Equal("No subjects selected.", none.Actual);
        Assert.True(evaluation.Complete);
        Assert.DoesNotContain(run.Service.Check().Findings, f => f.Code is "policy.unavailable" or "policy.unmet");

        // A conformance vector with no pair: the complete canonical result, the same whatever the records' order.
        PolicyKindRegistry registry = FoundationPolicyTests.Kinds;
        PolicyBasis recorded = PolicyBasis.Recorded(run.Estate.Load(), implementations: registry.Implementations);
        PolicyBasis vector = new(PolicyBasisTests.Model(recorded.Records), recorded.AsOf, recorded.Origin, implementations: registry.Implementations);
        PolicyEvaluation result = PolicyEvaluator.Evaluate(vector, registry);
        Assert.Equal(("satisfied", 0), Assert.Single(result.Findings, f => f.Kind == ConcurrentPaths.Kind) is var f ? (f.Verdict, f.SubjectCount) : default);
        var reversed = new PolicyBasis(PolicyBasisTests.Model(vector.Records.Reverse()), vector.AsOf, vector.Origin, implementations: registry.Implementations);
        Assert.Equal(result.Canonical(), PolicyEvaluator.Evaluate(reversed, registry).Canonical());
        Assert.True(PolicyKindRegistry.Conforms(registry, registry, [vector]));
    }

    [Fact]
    public void ACorrectiveExclusionThatRemovesTheLastPairLeavesTheRuleSatisfied()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, Brief(["path:widget/", "path:docs/"], [])) { Repository = run.Resource })).Id;
        Declare(run, "held-widget", ["widget/**"], "refuse");
        Assert.Equal("unmet", Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind).Verdict);

        // One owner: the later excludes the shared paths, which narrows it and needs no amendment.
        Revise(run, later, b => b["brief"]!["excludedChanges"] = new JsonArray("path:widget/"));
        PolicyResponse evaluation = run.Service.PolicyEvaluate();
        PolicyFindingDto resolved = Assert.Single(evaluation.Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("satisfied", 0, false), (resolved.Verdict, resolved.SubjectCount, resolved.ReleaseHold));
        Assert.True(evaluation.Complete);
        Assert.DoesNotContain(run.Service.Check().Findings, f => f.Code is "policy.unavailable" or "policy.unmet");
    }

    [Fact]
    public void ARevisionWideningWithinAnInheritedPairIsAdmittedAndWideningIntoANewPairIsRefused()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        Order(run, "W-4");
        string inherited = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        string apart = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId, Brief(["path:docs/"], [])) { Repository = run.Resource })).Id;
        Declare(run, "held-widget", ["widget/**"], "refuse");

        // W-3 already overlaps W-1 unresolved: widening it within that pair is admitted, and the pair still holds its attempts.
        string widened = Revise(run, inherited, b => b["brief"]!["permittedChanges"] = new JsonArray("path:widget/", "path:tools/")).Id;
        Assert.Contains(run.Service.Attempt(new AttemptRequest(widened, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.")).Problem!.Findings,
            f => f.Code == "attempt.concurrent-paths");
        // W-4 overlaps nothing in the selection: widening it into the widget's paths makes new pairs, and is refused with the remedies that can apply.
        WriteOutcome refused = Submit(run, apart, b => b["brief"]!["permittedChanges"] = new JsonArray("path:docs/", "path:widget/"));
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == "assignment.concurrent-paths");
        Assert.StartsWith("This revision of the assignment is refused: refuse-level concurrent-paths policies hold 2 pairs it leaves unresolved.", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("exception for the exact pair", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APolicyOnAnotherRepositoryHoldsNothingHere()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        Declare(run, "held-estate", ["**"], "refuse", repository: run.Estate.Load().FirstEstate!.Id);
        WriteOutcome assigned = run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource });
        Assert.True(assigned.Ok);
        Assert.Contains(assigned.Notices!, n => n.StartsWith("Overlap: W-1's assignment", StringComparison.Ordinal));
        Assert.True(run.Service.Attempt(new AttemptRequest(assigned.Written!.Id, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.")).Ok);
        Assert.Equal("satisfied", Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind).Verdict);
    }

    [Fact]
    public void AnExpiredPairExceptionCoversNothingAndADelegateCannotExceptItsOwnAssignment()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        Order(run, "W-3");
        string later = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        RecordEnvelope policy = Declare(run, "held-widget", ["widget/**"], "refuse");
        EstateModel model = run.Estate.Load();
        RecordEnvelope decision = Decide(run, "Accept the hand merge for a week.");
        RecordReference[] pair = [model.ById[run.Assignment].Reference, model.ById[later].Reference];
        JsonObject grant = Exception(run, decision, pair);
        RunEstate.Ok(run.Service.PolicyExcept("held-widget", new PolicyChangeRequest(run.Estate.OperatorId, JsonSerializer.SerializeToElement(grant), policy.ComputedHash)));
        DateTimeOffset expiry = DateTimeOffset.Parse(grant["expiresAt"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        Assert.Equal("excepted", Assert.Single(run.Service.PolicyEvaluate().Findings, f => f.Kind == ConcurrentPaths.Kind).Disposition);

        // At its exclusive expiry the exception covers nothing: the finding is unmet again and the boundary holds the pair's attempts.
        PolicyFindingDto expired = Assert.Single(run.Service.PolicyEvaluate(RecordParser.FormatTimestamp(expiry)).Findings, f => f.Kind == ConcurrentPaths.Kind);
        Assert.Equal(("unmet", "none"), (expired.Verdict, expired.Disposition));
        run.Service.AuthoringClock = new Clock(expiry);
        Assert.Contains(run.Service.Attempt(new AttemptRequest(later, run.Estate.BuilderId, "widget repository, branch work/w-3", "Start.")).Problem!.Findings,
            f => f.Code == "attempt.concurrent-paths");
        run.Service.AuthoringClock = null;

        // The builder holds both assignments: its own exception of the pair is refused as a self-exception, whatever grant it holds.
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.BuilderDevice);
        RecordEnvelope own = run.Estate.Add("policy-exception", run.Estate.BuilderId, Exception(run, decision, pair).ToJsonString(), policy.Reference, signer: signer);
        Assert.Contains(run.Estate.Load().Findings, f => f.Code == "exception.self" && f.Location.Contains(own.Id, StringComparison.Ordinal));
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    [Fact]
    public void ThePolicyKindTakesNoParametersAndOnlyARepositorySelection()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        PolicyKindRegistry registry = FoundationPolicyTests.Kinds;
        Assert.Contains(registry.Registrations, r => r.Kind == ConcurrentPaths.Kind && r.Version == 1 && r.Selections.SequenceEqual(["repository"]));
        Declare(run, "with-parameters", ["widget/**"], "refuse", parameters: new JsonObject { ["minimum"] = 1 });
        PolicyBasis basis = PolicyBasis.Recorded(run.Estate.Load(), implementations: registry.Implementations);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, registry);
        Assert.Equal("unavailable", Assert.Single(evaluation.Findings, f => f.Kind == ConcurrentPaths.Kind).Verdict);
        Assert.Contains(evaluation.Diagnostics, f => f.Code == "policy.parameters");
    }

    [Fact]
    public void ConformanceVectorsAreCompleteCanonicalResultsInvariantAcrossInputOrdersAndCultures()
    {
        using var run = new RunEstate(recordFormat: 3, keepOperatorKey: true);
        PolicyKindRegistry registry = FoundationPolicyTests.Kinds;
        Order(run, "W-3");
        Order(run, "W-4");
        Order(run, "W-5");
        string unmet = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-3", run.Estate.BuilderId, run.Estate.OperatorId, RunEstate.Json(RunEstate.Brief)) { Repository = run.Resource })).Id;
        string sequenced = RunEstate.Ok(run.Service.Assign(new AssignRequest("W-4", run.Estate.BuilderId, run.Estate.OperatorId, AfterAll(RunEstate.Brief, run.Assignment, unmet)) { Repository = run.Resource })).Id;
        RunEstate.Ok(run.Service.Assign(new AssignRequest("W-5", run.Estate.BuilderId, run.Estate.OperatorId, Brief(["path:widget/", "path:docs/"], ["path:widget/"])) { Repository = run.Resource }));
        Declare(run, "held-widget", ["widget/**"], "refuse");
        PolicyBasis recorded = PolicyBasis.Recorded(run.Estate.Load(), implementations: registry.Implementations);
        PolicyBasis vector = new(PolicyBasisTests.Model(recorded.Records), recorded.AsOf, recorded.Origin, implementations: registry.Implementations);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(vector, registry);
        Assert.True(evaluation.Complete);
        // Three pairs meet within the selection: one unmet and two sequenced; the owner's disjoint brief makes none.
        Assert.Equal(
            [("satisfied", Pair(run.Assignment, sequenced)), ("satisfied", Pair(unmet, sequenced)), ("unmet", Pair(run.Assignment, unmet))],
            evaluation.Findings.Where(f => f.Kind == ConcurrentPaths.Kind).Select(f => (f.Verdict, f.Target.Identity)).OrderBy(p => p.Item1, StringComparer.Ordinal).ThenBy(p => p.Item2, StringComparer.Ordinal));

        string expected = evaluation.Canonical();
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "en-US", "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var shuffled = new PolicyBasis(PolicyBasisTests.Model(vector.Records.Reverse()), vector.AsOf, vector.Origin, implementations: registry.Implementations.Reverse());
                Assert.Equal(expected, PolicyEvaluator.Evaluate(shuffled, registry).Canonical());
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }

        // A second implementation conforms only by matching the complete canonical results.
        Assert.True(PolicyKindRegistry.Conforms(registry, registry, [vector]));
        var flipped = new PolicyKindRegistry(registry.Registrations.Select(r => r.Kind != ConcurrentPaths.Kind ? r
            : r with { Evaluate = c => [.. ConcurrentPaths.Evaluate(c).Select(f => f with { Verdict = f.Verdict == "unmet" ? "satisfied" : f.Verdict })] }));
        Assert.False(PolicyKindRegistry.Conforms(flipped, registry, [vector]));
    }

    private static string Pair(string first, string second) => string.Join('+', new[] { first, second }.Order(StringComparer.Ordinal));

    private static JsonObject Reference(RecordReference reference) => new() { ["type"] = reference.Type, ["id"] = reference.Id, ["hash"] = reference.Hash };

    private static JsonObject Exception(RunEstate run, RecordEnvelope decision, RecordReference[] assignments) => new()
    {
        ["rule"] = "concurrent",
        ["targets"] = new JsonArray(new JsonObject { ["kind"] = "pair", ["assignments"] = new JsonArray([.. assignments.Select(a => (JsonNode)Reference(a))]) }),
        ["reason"] = "The two changes are merged by hand.",
        ["expiresAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddDays(7)),
        ["basis"] = new JsonObject { ["decision"] = Reference(decision.Reference) },
        ["state"] = "granted",
    };

    private static RecordEnvelope Decide(RunEstate run, string statement)
    {
        TestEstate estate = run.Estate;
        estate.SetClock(Later(estate));
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.OperatorDevice);
        var decision = new JsonObject
        {
            ["title"] = "Except a pair", ["statement"] = statement, ["summary"] = "An exception for one exact pair.", ["synthesis"] = "Test evidence.",
            ["effects"] = new JsonArray("Grant the exception."), ["affects"] = new JsonArray(), ["decidedBy"] = estate.OperatorId, ["decidedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1)),
            ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
        };
        RecordEnvelope recorded = estate.Add("decision", estate.OperatorId, decision.ToJsonString(), signer: signer);
        AwaitClock(recorded.AtTime);
        return recorded;
    }

    // A concurrent-paths policy on the widget's repository, approved and signed with the operator's key held here, in force from its own
    // record's time unless it starts later; the test then waits for this machine's clock to reach that time, so the runtime's later
    // records are dated at or after it.
    private static RecordEnvelope Declare(RunEstate run, string key, string[] selectors, string level, TimeSpan? startsIn = null, JsonObject? parameters = null, string? repository = null)
    {
        TestEstate estate = run.Estate;
        estate.SetClock(Later(estate));
        var signer = new StoreSigner(new FileKeyStore(VericairnHome.Path()), run.OperatorDevice);
        DateTimeOffset declared = estate.Clock.AddSeconds(2);
        var definition = new JsonObject
        {
            ["key"] = key, ["title"] = "Held paths", ["owner"] = estate.OperatorId, ["reason"] = "Keep concurrent work on shared paths in sequence.", ["state"] = "active",
            ["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository ?? run.Resource, ["selectors"] = new JsonArray([.. selectors.Select(s => (JsonNode)s)]) },
            ["rules"] = new JsonArray(new JsonObject { ["key"] = "concurrent", ["kind"] = AssignmentOverlaps.PolicyKind, ["version"] = 1, ["parameters"] = parameters ?? new JsonObject() }),
            ["level"] = level, ["startsAt"] = RecordParser.FormatTimestamp(declared + (startsIn ?? TimeSpan.Zero)), ["maxExceptionDays"] = 30,
        };
        var decision = new JsonObject
        {
            ["title"] = "Hold shared paths", ["statement"] = "Approve the stated definition only.", ["summary"] = "Hold concurrent work on shared paths.", ["synthesis"] = "Test evidence.",
            ["effects"] = new JsonArray("Apply the exact approved definition."), ["affects"] = new JsonArray(), ["decidedBy"] = estate.OperatorId, ["decidedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1)),
            ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
            ["policies"] = new JsonArray(new JsonObject { ["key"] = "definition", ["action"] = "declare", ["policy"] = key, ["change"] = "Approve this exact body.", ["definitionHash"] = PolicyRequirements.DefinitionHash(JsonSerializer.SerializeToElement(definition)) }),
        };
        RecordEnvelope approval = estate.Add("decision", estate.OperatorId, decision.ToJsonString(), signer: signer);
        definition["basis"] = new JsonArray(new JsonObject { ["decision"] = new JsonObject { ["type"] = "decision", ["id"] = approval.Id, ["hash"] = approval.ComputedHash }, ["requirement"] = "definition" });
        RecordEnvelope policy = estate.Add("estate-policy", estate.OperatorId, definition.ToJsonString(), signer: signer);
        Assert.Equal(declared, policy.AtTime);
        AwaitClock(policy.AtTime);
        return policy;
    }

    // The next test-authored record's clock: after every record in the estate, and no earlier than this machine's clock, to the second.
    private static DateTimeOffset Later(TestEstate estate)
    {
        DateTimeOffset latest = estate.Load().Records.Max(r => r.AtTime);
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return latest > now ? latest : now;
    }

    // Waits for this machine's clock to reach a record's time, the precondition for the runtime's next record to be dated at or after it.
    private static void AwaitClock(DateTimeOffset at)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (DateTimeOffset.UtcNow < at.AddSeconds(1))
        {
            deadline.Token.ThrowIfCancellationRequested();
            Thread.Sleep(TimeSpan.FromMilliseconds(50));
        }
    }

    private static RecordWritten Revise(RunEstate run, string assignment, Action<JsonObject> change) => RunEstate.Ok(Submit(run, assignment, change));

    private static WriteOutcome Submit(RunEstate run, string assignment, Action<JsonObject> change)
    {
        EstateModel model = run.Estate.Load();
        RecordEnvelope current = model.ById[assignment];
        RecordEnvelope order = WorkView.OrderOfAssignment(model, current)!;
        JsonObject body = JsonNode.Parse(current.Body.GetRawText())!.AsObject();
        change(body);
        return run.Service.NewRecord(new NewRecordRequest("assignment", run.Estate.OperatorId, JsonSerializer.SerializeToElement(body), $"work-order:{order.Id}:{order.ComputedHash}", Supersedes: assignment));
    }

    private static JsonElement Brief(string[] permitted, string[] excluded)
    {
        JsonObject body = JsonNode.Parse(RunEstate.Brief)!.AsObject();
        body["permittedChanges"] = new JsonArray([.. permitted.Select(p => (JsonNode)p)]);
        if (excluded.Length > 0) body["excludedChanges"] = new JsonArray([.. excluded.Select(p => (JsonNode)p)]);
        return JsonSerializer.SerializeToElement(body);
    }

    private static JsonElement After(string brief, string assignment) => AfterAll(brief, assignment);

    private static JsonElement AfterAll(string brief, params string[] assignments)
    {
        JsonObject body = JsonNode.Parse(brief)!.AsObject();
        body["after"] = new JsonArray([.. assignments.Select(a => (JsonNode)a)]);
        return JsonSerializer.SerializeToElement(body);
    }

    private static void Order(RunEstate run, string key, string? goal = null) =>
        RunEstate.Ok(run.Service.NewRecord(new NewRecordRequest("work-order", run.Estate.OperatorId, RunEstate.Json($$"""{"key":"{{key}}","kind":"ordinary","title":"Work {{key}}","intent":"Exercise held overlaps.","scope":["the widget"],"exitQuestion":"Done?","inputs":[{{(goal is null ? string.Empty : $$"""{"type":"goal","id":"{{goal}}"}""")}}],"deliverables":["widget"]}"""))));
}
