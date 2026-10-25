// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

/// <summary>The planned-without-release anchor search finds the same anchors as a walk over every instant, and unrelated records add no work to it.</summary>
public class PolicyStalenessScaleTests
{
    private const string At = "2026-01-02T00:00:00Z";
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly object Parameters = new { @class = "planned-without-release", maxAgeDays = 0 };

    [Fact]
    public void AnchorsMatchTheSearchOverEveryInstantOnGeneratedHistories()
    {
        int compared = 0, unavailable = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            RecordEnvelope[] records = History(new Random(seed), unrelated: 40);
            PolicyBasis basis = Basis([P(FoundationPolicyKinds.Staleness, Parameters), .. records], At);
            EstateModel model = basis.Model;
            DateTimeOffset at = basis.Time!.Value;
            var expected = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
            bool throws = false;
            foreach (RecordEnvelope order in model.Current("work-order"))
            {
                try
                {
                    (string verdict, string reason, _) = PolicyWork.ReleasePlacement(model, order, at);
                    if (verdict == "unavailable") throw new PolicyInputException(reason);
                    if (PolicyWork.Planned(model, order, at) && verdict != "satisfied") expected[PolicyWork.Identity(model, order)] = EveryInstant(model, order);
                }
                catch (PolicyInputException) { throws = true; }
            }

            PolicyFinding[] findings = PolicyEvaluator.Evaluate(basis, Kinds).Findings.Where(f => f.Kind == FoundationPolicyKinds.Staleness).ToArray();
            if (throws)
            {
                Assert.Contains(findings, f => f.Verdict == "unavailable");
                unavailable++;
                continue;
            }

            Assert.Equal(expected.Count == 0 ? 1 : expected.Count, findings.Length);
            foreach (PolicyFinding finding in findings.Where(f => f.SubjectCount == 1))
            {
                RecordEnvelope anchor = expected[finding.Target.Identity];
                Assert.Equal(anchor.Reference, finding.Anchor);
                Assert.Equal(anchor.At, finding.AnchorAt);
                compared++;
            }
        }

        Assert.True(compared > 300, $"Only {compared} anchors were compared.");
        Assert.True(unavailable > 0, "No generated history exercised an unavailable state.");
    }

    [Fact]
    public void UnrelatedRecordsAddNoInstantsToTheAnchorSearch()
    {
        RecordEnvelope[] history = History(new Random(7), unrelated: 0);
        RecordEnvelope[] noisy = [.. history, .. Enumerable.Range(0, 2_000).Select(i => R(i % 2 == 0 ? "decision" : "ask", "noise-" + i, new { note = i }, Time(1 + i % 3_000)))];
        EstateModel quiet = Basis(history, At).Model, loud = Basis(noisy, At).Model;
        foreach (RecordEnvelope order in quiet.Current("work-order"))
        {
            string identity = PolicyWork.Identity(quiet, order);
            int Instants(EstateModel model) => FoundationPolicyKinds.ChangeInstants(model, identity, model.OfType("work-order").Where(r => PolicyWork.Identity(model, r) == identity).ToArray()).Count;
            Assert.Equal(Instants(quiet), Instants(loud));
        }
    }

    [Fact]
    public void ALargeEstateFindsItsPlannedAnchorsWithinBudget()
    {
        // Two hundred waiting orders among five thousand other records at distinct instants: a walk over every instant
        // for every order, the search before FIX-63, takes about forty seconds on a fast machine; the bounded search, about one.
        var records = new List<RecordEnvelope>();
        for (int i = 0; i < 200; i++)
        {
            RecordEnvelope order = R("work-order", "order-" + i, new { key = "TASK-" + i, placement = new { kind = "backlog", key = "later" } }, Time(i));
            RecordEnvelope revision = R("work-order", "order-" + i + "-r", new { key = "TASK-" + i, placement = new { kind = "backlog", key = "later" } }, Time(10_000 + i), supersedes: order.Id);
            records.AddRange([order, revision, R("work-status", "status-" + i, new { status = "planned" }, Time(20_000 + i), revision.Reference)]);
        }
        records.AddRange(Enumerable.Range(0, 5_000).Select(i => R("decision", "decision-" + i, new { note = i }, Time(30_000 + i))));
        PolicyBasis basis = Basis([P(FoundationPolicyKinds.Staleness, Parameters), .. records], At);

        var clock = Stopwatch.StartNew();
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, Kinds);
        clock.Stop();
        PolicyFinding[] findings = evaluation.Findings.Where(f => f.Kind == FoundationPolicyKinds.Staleness).ToArray();
        Assert.Equal(200, findings.Length);
        // Each order has waited since it was created; its revision and planned status continue the same wait.
        Assert.All(findings, f => Assert.Equal(f.Target.Identity, f.Anchor!.Id));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Evaluating 200 waiting orders among {records.Count:N0} records took {clock.Elapsed}.");
    }

    // The search before FIX-63, kept as the oracle: the waiting state at every instant since the order was created.
    private static RecordEnvelope EveryInstant(EstateModel model, RecordEnvelope order)
    {
        string identity = PolicyWork.Identity(model, order);
        RecordEnvelope? anchor = null;
        foreach (IGrouping<string, RecordEnvelope> instant in model.Records.Where(r => string.CompareOrdinal(r.At, model.ById[identity].At) >= 0).GroupBy(r => r.At, StringComparer.Ordinal))
        {
            DateTimeOffset at = instant.First().AtTime;
            RecordEnvelope current = model.OfType("work-order").Single(r => PolicyWork.Identity(model, r) == identity && PolicyWork.CurrentAt(model, r, at));
            (string verdict, string reason, _) = PolicyWork.ReleasePlacement(model, current, at);
            if (verdict == "unavailable") throw new PolicyInputException(reason);
            bool waiting = PolicyWork.Planned(model, current, at) && verdict != "satisfied";
            if (!waiting) anchor = null;
            else anchor ??= instant.FirstOrDefault(r => r.Id == current.Id)
                ?? instant.FirstOrDefault(r => r.Type == "work-status" && r.Subject is { } s && model.Chain(current).Contains(s.Id, StringComparer.Ordinal))
                ?? instant.FirstOrDefault(r => PolicyWork.OrderOfEvent(model, r)?.Id == current.Id)
                ?? PolicyWork.ReleasePlacement(model, current, at).Evidence ?? current;
        }
        return anchor ?? order;
    }

    // Orders revised between placements, carriers whose plans change or retire, statuses (some conflicting at one instant),
    // work with attempts, results and acceptances, releases published and withdrawn, and unrelated records, on a coarse clock
    // so that many records share an instant.
    private static RecordEnvelope[] History(Random random, int unrelated)
    {
        var records = new List<RecordEnvelope>();
        string Tick() => Time(60 * random.Next(1, 240));
        RecordEnvelope carrierA = R("work-order", "carrier-a", new { key = "REL-A", releasePlan = new { product = "product", version = "1.0.0" } });
        RecordEnvelope carrierB = R("work-order", "carrier-b", new { key = "REL-B", releasePlan = new { product = "product", version = "2.0.0" } });
        records.AddRange([carrierA, carrierB]);
        if (random.Next(3) == 0) records.Add(R("work-order", "carrier-a-r", new { key = "REL-A", releasePlan = new { product = "product", version = "1.1.0" } }, Tick(), supersedes: carrierA.Id));
        if (random.Next(4) == 0) records.Add(R("work-status", "carrier-b-retired", new { status = "retired" }, Tick(), carrierB.Reference));
        if (random.Next(2) == 0) records.Add(R("release", "release-1", new { status = "published", version = "1.0.0" }, Tick(), new("product", "product", null)));
        if (random.Next(3) == 0) records.Add(R("release", "release-2", new { status = "withdrawn", version = "2.0.0" }, Tick(), new("product", "product", null)));

        object Placement() => random.Next(5) switch
        {
            0 => new { kind = "backlog", key = "later" },
            1 => new { kind = "release", carrier = "carrier-a", product = "product", version = "1.0.0" },
            2 => new { kind = "release", carrier = "carrier-a", product = "product", version = "1.1.0" },
            3 => new { kind = "release", carrier = "carrier-b", product = "product", version = "2.0.0" },
            _ => new { kind = "none" },
        };

        string[] states = ["planned", "active", "paused", "retired"];
        for (int k = 0; k < 6; k++)
        {
            var revisions = new List<RecordEnvelope> { R("work-order", "order-" + k, new { key = "TASK-0" + k, placement = Placement() }, Time(60 * random.Next(0, 60))) };
            for (int j = random.Next(0, 4); j > 0; j--)
            {
                RecordEnvelope previous = revisions[^1];
                string later = Time((int)(previous.AtTime - Start).TotalSeconds + 60 * random.Next(0, 60));
                revisions.Add(R("work-order", previous.Id + "-r", new { key = "TASK-0" + k, placement = Placement() }, later, supersedes: previous.Id));
            }
            records.AddRange(revisions);
            RecordEnvelope Revision() => revisions[random.Next(revisions.Count)];
            int gap = random.Next(Math.Max(revisions.Count - 1, 1));
            if (revisions.Count > 1 && random.Next(2) == 0 && revisions[gap + 1].AtTime - revisions[gap].AtTime >= TimeSpan.FromSeconds(120))
            {
                // An exact acceptance that the next revision leaves behind, with the wait resuming at that revision.
                records.Add(R("acceptance", "accepted-" + k, new { basis = Array.Empty<string>() }, Time((int)(revisions[gap].AtTime - Start).TotalSeconds + 60), revisions[gap].Reference));
            }
            for (int s = random.Next(0, 5); s > 0; s--)
            {
                RecordEnvelope status = R("work-status", "status-" + k + "-" + s, new { status = states[random.Next(states.Length)] }, Tick(), Revision().Reference);
                records.Add(status);
                if (random.Next(12) == 0) records.Add(R("work-status", status.Id + "-conflict", new { status = status.Body.GetProperty("status").GetString() == "paused" ? "active" : "paused" }, status.At, status.Subject));
                // A revision that moves the status to another subject ends it for this order at the revision's own instant.
                else if (random.Next(4) == 0) records.Add(R("work-status", status.Id + "-moved", new { status = "active" }, Time((int)(status.AtTime - Start).TotalSeconds + 60 * random.Next(1, 30)), new("work-order", "elsewhere", null), status.Id));
            }
            if (random.Next(2) == 0)
            {
                // Work on one revision; sometimes accepted before its result is recorded, all before the next revision.
                int on = random.Next(revisions.Count);
                bool early = on + 1 < revisions.Count && revisions[on + 1].AtTime - revisions[on].AtTime >= TimeSpan.FromSeconds(300) && random.Next(2) == 0;
                int from = (int)(revisions[on].AtTime - Start).TotalSeconds;
                string When(int step) => early ? Time(from + 60 * step) : Tick();
                RecordEnvelope assignment = R("assignment", "assignment-" + k, new { contributor = "operator" }, When(1), revisions[on].Reference);
                RecordEnvelope attempt = R("attempt", "attempt-" + k, new { note = "Try." }, When(2), assignment.Reference);
                RecordEnvelope result = R("result", "result-" + k, new { outcome = "done" }, When(4), attempt.Reference);
                records.AddRange([assignment, attempt, result]);
                if (early) records.Add(R("acceptance", "acceptance-" + k, new { basis = Array.Empty<string>() }, When(3), result.Reference));
                else if (random.Next(2) == 0) records.Add(R("acceptance", "acceptance-" + k, new { basis = Array.Empty<string>() }, Tick(), random.Next(2) == 0 ? result.Reference : Revision().Reference));
            }
        }

        string[] types = ["decision", "ask", "review", "operation"];
        records.AddRange(Enumerable.Range(0, unrelated).Select(i => R(types[i % types.Length], "unrelated-" + i, new { note = i }, Tick())));
        return [.. records];
    }

    private static string Time(int seconds) => RecordParser.FormatTimestamp(Start.AddSeconds(seconds));
}
