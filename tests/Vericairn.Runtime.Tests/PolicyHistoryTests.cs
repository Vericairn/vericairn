// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Policies.PolicyInputs;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

public sealed class PolicyHistoryTests
{
    [Fact]
    public void IndexedWorkQueriesMatchTheOriginalScansIncludingHistoricalAmbiguity()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            EstateModel model = Basis(PolicyStalenessScaleTests.History(new Random(seed), unrelated: 20)).Model;
            DateTimeOffset[] times = model.Records.Select(r => r.AtTime).Distinct().ToArray();
            foreach (RecordEnvelope order in model.OfType("work-order"))
            foreach (DateTimeOffset at in times)
            {
                Assert.Equal(Outcome(() => ScanStatus(model, order, at)), Outcome(() => PolicyWork.Status(model, order, at)));
                Assert.Equal(Outcome(() => ScanResults(model, order, at)), Outcome(() => PolicyWork.Results(model, order, at)));
                Assert.Equal(Outcome(() => ScanAcceptance(model, order, at)), Outcome(() => PolicyWork.Acceptance(model, order, at)));
                Assert.Equal(PlacementOutcome(() => ScanReleasePlacement(model, order, at)), PlacementOutcome(() => PolicyWork.ReleasePlacement(model, order, at)));
            }
        }
    }

    [Fact]
    public void RelatedLookupsAndChangeInstantsDoNotEnumerateTheEstateAgain()
    {
        RecordEnvelope order = Order();
        RecordEnvelope assignment = R("assignment", "assignment", new { }, subject: order.Reference);
        RecordEnvelope attempt = R("attempt", "attempt", new { }, subject: assignment.Reference);
        RecordEnvelope result = R("result", "result", new { }, subject: attempt.Reference);
        var records = new CountedRecords([order, assignment, attempt, result,
            .. Enumerable.Range(0, 10_000).Select(i => R("decision", "noise-" + i, new { }))]);
        var model = new EstateModel("", records, records.ToDictionary(r => r.Id, StringComparer.Ordinal), new Dictionary<string, string>(), new());
        PolicyHistory history = PolicyHistory.For(model);
        _ = model.About(order.Id, "work-status").ToArray();
        records.Reads = 0;
        DateTimeOffset at = order.AtTime.AddDays(1);
        Assert.Null(PolicyWork.Status(model, order, at));
        Assert.Null(PolicyWork.Acceptance(model, order, at));
        Assert.Equal(result, Assert.Single(PolicyWork.Results(model, order, at)));
        Assert.Equal("unmet", PolicyWork.ReleasePlacement(model, order, at).Verdict);
        IReadOnlySet<DateTimeOffset> changes = FoundationPolicyKinds.ChangeInstants(model, order.Id, [order]);
        Assert.Equal(order.AtTime, Assert.Single(changes));
        Assert.Equal(assignment, history.EventAt(order, order.AtTime));
        Assert.Equal(0, records.Reads);
    }

    [Fact]
    public void UnrelatedWorkAcceptancesAndReleasesDoNotAddAnchorInstants()
    {
        RecordEnvelope order = Order();
        RecordEnvelope other = R("work-order", "other", new { });
        RecordEnvelope[] noise = Enumerable.Range(0, 2_000).Select(i => i % 2 == 0
            ? R("acceptance", "accepted-" + i, new { }, RecordParser.FormatTimestamp(order.AtTime.AddSeconds(i + 1)), other.Reference)
            : R("release", "released-" + i, new { version = "1.0.0", status = "published" }, RecordParser.FormatTimestamp(order.AtTime.AddSeconds(i + 1)), new("product", "elsewhere", null))).ToArray();
        EstateModel model = Basis([order, other, .. noise]).Model;
        Assert.Equal([order.AtTime], FoundationPolicyKinds.ChangeInstants(model, order.Id, [order]).Order());
    }

    [Fact]
    public void SubjectIndexPreservesEveryOccurrenceAndInputOrder()
    {
        RecordEnvelope order = Order();
        RecordEnvelope wrongType = R("attempt", "wrong-type", new { }, subject: new("decision", order.Id, null));
        RecordEnvelope[] records = [R("result", "absent-subject", new { }), wrongType,
            R("attempt", "later", new { }, "2026-01-02T00:00:00Z", order.Reference),
            R("attempt", "earlier", new { }, subject: order.Reference), order, wrongType];
        var model = new EstateModel("", records, records.Distinct().ToDictionary(r => r.Id, StringComparer.Ordinal), new Dictionary<string, string>(), new());
        foreach (string type in new[] { "work-order", "attempt", "result", "absent" })
        foreach (string subject in new[] { order.Id, "absent" })
            Assert.Equal(records.Where(r => r.Type == type && r.Subject?.Id == subject), model.About(subject, type));
    }

    [Fact]
    public void SubjectIndexUsesTheSameRecordSnapshotAsTheTypeIndex()
    {
        RecordEnvelope order = Order();
        RecordEnvelope original = R("assignment", "original", new { }, subject: order.Reference);
        var records = new List<RecordEnvelope> { order, original };
        var model = new EstateModel("", records, records.ToDictionary(r => r.Id, StringComparer.Ordinal), new Dictionary<string, string>(), new());
        records.Remove(original);
        records.Add(R("assignment", "added", new { }, subject: order.Reference));
        Assert.Equal([original], model.About(order.Id, "assignment"));
        Assert.Equal(model.OfType("assignment"), model.About(order.Id, "assignment"));
    }

    [Fact]
    public void AnswerIndexMatchesTheScanAcrossRevisionsDuplicateReferencesAndMalformedBodies()
    {
        RecordEnvelope ask = R("ask", "ask", new { state = "open" });
        RecordEnvelope notified = R("ask", "notified", new { state = "notified" }, supersedes: ask.Id);
        RecordEnvelope other = R("ask", "other", new { state = "open" });
        RecordEnvelope answer = R("decision", "answer", new { answers = new[] { ask.Id } });
        RecordEnvelope revised = R("decision", "revised-answer", new { answers = new[] { other.Id } }, supersedes: answer.Id);
        RecordEnvelope multiple = R("decision", "multiple", new { answers = new object[] { notified.Id, ask.Id, ask.Id, 7 } });
        RecordEnvelope malformed = R("decision", "malformed", new { answers = ask.Id });
        RecordEnvelope withdrawn = R("ask", "withdrawn", new { state = "withdrawn" }, supersedes: notified.Id);
        RecordEnvelope[] all = [ask, notified, other, answer, revised, multiple, malformed, multiple, withdrawn];
        for (int count = 3; count <= all.Length; count++)
        {
            RecordEnvelope[] records = all.Take(count).ToArray();
            var model = new EstateModel("", records, records.Distinct().ToDictionary(r => r.Id, StringComparer.Ordinal),
                records.Where(r => r.Supersedes is not null).ToDictionary(r => r.Supersedes!, r => r.Id, StringComparer.Ordinal), new());
            foreach (RecordEnvelope original in new[] { ask, notified, other })
            {
                RecordEnvelope latest = model.Latest(original);
                string recorded = EstateModel.BodyString(latest, "state")!;
                IReadOnlyList<string> chain = model.Chain(latest);
                RecordEnvelope[] expected = recorded == "withdrawn" ? [] : model.Current("decision")
                    .Where(d => d.Body.TryGetProperty("answers", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array
                        && ids.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.String && chain.Contains(i.GetString()!)))
                    .OrderBy(d => d.At, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal).ToArray();
                (string state, IReadOnlyList<RecordEnvelope> answers) = Asks.State(model, original);
                Assert.Equal(expected.Length > 0 ? "answered" : recorded, state);
                Assert.Equal(expected, answers);
            }
        }
    }

    private static string Outcome<T>(Func<T> action)
    {
        try { return PolicyBasis.Canonical(action()); }
        catch (PolicyInputException error) { return "unavailable: " + error.Message; }
    }

    private static string PlacementOutcome(Func<(string Verdict, string Reason, RecordEnvelope? Evidence)> action) => Outcome(() =>
    {
        var placement = action();
        return new { placement.Verdict, placement.Reason, placement.Evidence };
    });

    // The pre-index queries are deliberately kept independent: an optimized helper cannot validate itself.
    private static RecordEnvelope? ScanStatus(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        IReadOnlyList<string> lineage = model.Chain(order);
        RecordEnvelope[] states = model.OfType("work-status").Where(s => s.Subject is not null && lineage.Contains(s.Subject.Id, StringComparer.Ordinal) && PolicyWork.CurrentAt(model, s, at))
            .OrderBy(s => s.At, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (states.Length == 0) return null;
        RecordEnvelope latest = states[^1];
        if (states.Where(s => s.At == latest.At).Select(s => EstateModel.BodyString(s, "status")).Distinct(StringComparer.Ordinal).Count() > 1)
            throw new PolicyInputException("Conflicting work states at the same recorded time.");
        return latest;
    }

    private static List<RecordEnvelope> ScanResults(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        List<RecordEnvelope> results = [];
        foreach (RecordEnvelope assignment in model.OfType("assignment").Where(a => PolicyWork.CurrentAt(model, a, at) && a.Subject is not null && model.Chain(order).Contains(a.Subject.Id, StringComparer.Ordinal)))
        {
            RecordEnvelope[] attempts = model.OfType("attempt").Where(a => a.Subject == assignment.Reference && PolicyWork.CurrentAt(model, a, at)).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray();
            if (attempts.Length == 0) continue;
            RecordEnvelope attempt = attempts[^1];
            if (attempts.Count(a => a.At == attempt.At) > 1) throw new PolicyInputException("The latest attempt is ambiguous.");
            RecordEnvelope[] current = model.OfType("result").Where(r => r.Subject == attempt.Reference && PolicyWork.CurrentAt(model, r, at)).ToArray();
            if (current.Length > 1) throw new PolicyInputException("The current submitted result is ambiguous.");
            results.AddRange(current);
        }
        return results;
    }

    private static RecordEnvelope? ScanAcceptance(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        RecordEnvelope[] acceptances = model.OfType("acceptance").Where(a => PolicyWork.CurrentAt(model, a, at)).ToArray();
        RecordEnvelope? exact = acceptances.FirstOrDefault(a => a.Subject == order.Reference);
        if (exact is not null) return exact;
        RecordEnvelope[] resultAcceptances = acceptances.Where(a => a.Subject is { Type: "result" } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? result) &&
            subject == result.Reference && WorkView.OrderOfResult(model, result)?.Reference == order.Reference).ToArray();
        if (resultAcceptances.Length == 0) return null;
        IReadOnlyList<RecordEnvelope> current = ScanResults(model, order, at);
        return resultAcceptances.FirstOrDefault(a => current.Any(r => a.Subject == r.Reference));
    }

    private static (string Verdict, string Reason, RecordEnvelope? Evidence) ScanReleasePlacement(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        JsonElement placement = Get(order.Body, "placement");
        if (Text(placement, "kind") != "release") return ("unmet", "No release placement.", null);
        if (!model.ById.TryGetValue(Text(placement, "carrier") ?? "", out RecordEnvelope? identity)) return ("unmet", "The release carrier is dangling.", null);
        RecordEnvelope[] carriers = model.OfType("work-order").Where(r => PolicyWork.Identity(model, r) == identity.Id && PolicyWork.CurrentAt(model, r, at)).ToArray();
        if (carriers.Length != 1) return ("unavailable", "The release carrier history is missing or conflicting.", identity);
        RecordEnvelope carrier = carriers[0];
        if (ScanStatus(model, carrier, at) is { } status && EstateModel.BodyString(status, "status") == "retired") return ("unmet", "The release carrier is retired.", status);
        JsonElement plan = Get(carrier.Body, "releasePlan");
        if (Text(plan, "product") != Text(placement, "product") || Text(plan, "version") != Text(placement, "version")) return ("unmet", "The carrier's current plan contradicts this placement.", carrier);
        RecordEnvelope? ended = model.OfType("release").FirstOrDefault(r => r.AtTime <= at && ReleaseMatchesProduct(model, r, Text(plan, "product")) &&
            EstateModel.BodyString(r, "version") == Text(plan, "version") && EstateModel.BodyString(r, "status") is "published" or "withdrawn");
        return ended is null ? ("satisfied", "The exact release placement is valid.", carrier) : ("unmet", "The release target has been published or withdrawn.", ended);
    }

    private sealed class CountedRecords(IReadOnlyList<RecordEnvelope> records) : IReadOnlyList<RecordEnvelope>
    {
        internal int Reads { get; set; }
        public int Count => records.Count;
        public RecordEnvelope this[int index] { get { Reads++; return records[index]; } }
        public IEnumerator<RecordEnvelope> GetEnumerator()
        {
            foreach (RecordEnvelope record in records) { Reads++; yield return record; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
