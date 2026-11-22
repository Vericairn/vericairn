// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Records.Tests.GoalEvaluationTests;
using static Vericairn.Records.Tests.ProgressReportTests;

namespace Vericairn.Records.Tests;

/// <summary>
/// From 0.9.4 (WATCH-06): the progress report's sections as a progress target carries them. The private form carries what the report
/// carries for them; the public form follows the disclosure rules (decision 01M7824E3CQF5PC9P7X04KXYD7): no held security fix, no
/// private estate detail and no open ask before each is public, every count counting only what it shows; and the payload names every
/// record whose text it carries, which an outward effect is charged with.
/// </summary>
public sealed class ProgressTargetFormTests
{
    private const string Marker = "vericairn progress-target status 0123456789abcdef";
    private static readonly DateTimeOffset Later = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private const string HeldIntent = "Close the widget cache's unchecked path traversal before anyone can reach it.";

    [Fact]
    public void ThePrivateFormCarriesTheReportsSectionsAndNamesEveryRecordItCarries()
    {
        using Fixture f = Fixture.Write(held: false);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        ProgressPayload payload = ProgressReport.RenderTarget(model, ProgressReport.PrivateForm, null, Marker, Later);
        string text = payload.Text;
        Assert.StartsWith("# Progress of Test Estate\n\n_Vericairn renders this from the estate's records, as of ", text, StringComparison.Ordinal);
        Assert.Contains("This is the private form, for the estate's own people._\n", text, StringComparison.Ordinal);
        Assert.Contains("| REL-094 Order REL-094 | planned | 1 of 2 accepted | — |\n", text, StringComparison.Ordinal);
        Assert.Contains("| Example 0.1.0 | published | 0 work orders accepted | 2026-10-13 |\n", text, StringComparison.Ordinal);
        Assert.Contains("\n## Next: REL-094 Order REL-094, 1 of 2 work orders accepted\n\n| Work order | What | State |\n| --- | --- | --- |\n| W-2 | Order W-2 | planned |\n| W-1 | Order W-1 | accepted |\n", text, StringComparison.Ordinal);
        Assert.Contains("\n## In flight\n\n| Work order | What | State | Who |\n| --- | --- | --- | --- |\n| W-2 | Order W-2 | awaiting review | Test Builder |\n", text, StringComparison.Ordinal);
        Assert.Contains("\n## Milestones\n\n| Milestone | What | State |\n| --- | --- | --- |\n| M-1 | Order M-1 | planned |\n", text, StringComparison.Ordinal);
        Assert.Contains("- **Asks awaiting an answer: 1**\n  - Ship the release? (to Test Operator, open)\n    recommendation: Answer yes.\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n<!-- " + Marker + " -->\n", text, StringComparison.Ordinal);
        Assert.Null(ProgressReport.TargetRefusal(model, ProgressReport.PrivateForm, text, f.Estate.Clock));

        // Every record whose text it carries, once: the charter (its name), the release work order, the product and release, the work
        // orders, the contributor, the milestone, the ask and its principal.
        string[] carried = [.. payload.Carried.Select(r => r.Id)];
        Assert.Equal(carried.Length, carried.Distinct().Count());
        foreach (RecordEnvelope record in new[] { model.CurrentEstate!, f.Release, f.Product, f.Published, f.Done, f.Open, f.Milestone, f.Ask, model.ById[f.Estate.BuilderId], model.ById[f.Estate.OperatorId] })
            Assert.Contains(new RecordReference(record.Type, record.Id, record.ComputedHash), payload.Carried);
        Assert.All(payload.Carried, r => Assert.Equal(model.ById[r.Id].ComputedHash, r.Hash));

        // The same records give the same text.
        Assert.Equal(text, ProgressReport.RenderTarget(f.Estate.Load(), ProgressReport.PrivateForm, null, Marker, Later).Text);
    }

    [Fact]
    public void ThePublicFormLeavesOutHeldWorkAndEveryPrivateDetailAndCountsOnlyWhatItShows()
    {
        using Fixture f = Fixture.Write(held: true);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Single(DisclosureHolds.Active(model));

        ProgressPayload open = ProgressReport.RenderTarget(model, ProgressReport.PublicForm, null, Marker, Later);
        string text = open.Text;
        Assert.Contains("This is the public form, which leaves out what the disclosure rules hold._\n", text, StringComparison.Ordinal);
        // The held fix and a work order whose title discloses it are left out, and the counts are of what is shown.
        Assert.Equal(["DOCS-9", "FIX-90"], open.LeftOut);
        Assert.DoesNotContain("FIX-90", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DOCS-9", text, StringComparison.Ordinal);
        Assert.Contains("| REL-094 Order REL-094 | planned | 1 of 2 accepted | — |\n", text, StringComparison.Ordinal);
        Assert.Contains("\n## Next: REL-094 Order REL-094, 1 of 2 work orders accepted\n", text, StringComparison.Ordinal);
        // No principal's name or record identifier, and of the operator's asks only how many.
        Assert.Contains("\n## In flight\n\n| Work order | What | State |\n| --- | --- | --- |\n| W-2 | Order W-2 | awaiting review |\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Test Builder", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Test Operator", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ship the release?", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Answer yes.", text, StringComparison.Ordinal);
        Assert.Contains("- Asks awaiting an answer: 1\n- Acceptances only the operator gives: ", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("[0-7][0-9A-HJKMNP-TV-Z]{25}"), text);
        Assert.Null(ProgressReport.TargetRefusal(model, ProgressReport.PublicForm, text, f.Estate.Clock));
        Assert.DoesNotContain(open.Carried, r => r.Id == f.Held.Id || r.Id == f.Disclosing.Id || r.Id == f.Ask.Id || r.Type == "principal");

        // The private form of the same estate carries the held fix, as the report does.
        string inside = ProgressReport.RenderTarget(model, ProgressReport.PrivateForm, null, Marker, Later).Text;
        Assert.Contains("| FIX-90 | Cache fix | planned |", inside, StringComparison.Ordinal);
        Assert.Null(ProgressReport.TargetRefusal(model, ProgressReport.PrivateForm, inside, f.Estate.Clock));
        Assert.Equal("held work (FIX-90) before the release that carries it is published", ProgressReport.TargetRefusal(model, ProgressReport.PublicForm, inside, f.Estate.Clock));
    }

    [Fact]
    public void ThePublicFormLeavesOutALineWithABlockedTermOnceItsPolicyHasStarted()
    {
        using var fixture = new PolicyFixture();
        System.Text.Json.Nodes.JsonObject policy = fixture.PolicyBody("blocked-terms", duty: false);
        policy["rules"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
        {
            ["key"] = "terms", ["kind"] = BlockedTerms.Kind, ["version"] = 1,
            ["parameters"] = new System.Text.Json.Nodes.JsonObject { ["terms"] = new System.Text.Json.Nodes.JsonArray("Tern") },
        });
        fixture.Policy(policy);
        RecordEnvelope named = fixture.Estate.Add("work-order", fixture.Estate.OperatorId, OrderBody("W-7", "milestone", string.Empty).Replace("\"Order W-7\"", "\"Qualify the Tern harness\"", StringComparison.Ordinal));
        EstateModel model = fixture.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));

        ProgressPayload open = ProgressReport.RenderTarget(model, ProgressReport.PublicForm, null, Marker, Later);
        Assert.Equal(["W-7"], open.LeftOut);
        Assert.DoesNotContain("Tern", open.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(open.Carried, r => r.Id == named.Id);
        Assert.Null(ProgressReport.TargetRefusal(model, ProgressReport.PublicForm, open.Text, Later));
        string inside = ProgressReport.RenderTarget(model, ProgressReport.PrivateForm, null, Marker, Later).Text;
        Assert.Contains("| W-7 | Qualify the Tern harness | planned |", inside, StringComparison.Ordinal);
        Assert.Equal("the blocked term \"tern\"", ProgressReport.TargetRefusal(model, ProgressReport.PublicForm, inside, Later));
        // Before its policy starts, the term holds nothing.
        Assert.Empty(ProgressReport.RenderTarget(model, ProgressReport.PublicForm, null, Marker, fixture.Estate.Clock).LeftOut);
    }

    [Theory]
    [InlineData("Implements FIX-90.", "public", "held work (FIX-90) before the release that carries it is published")]
    [InlineData("close the widget cache's unchecked\n PATH traversal before anyone can reach it", "public", "held work (FIX-90) before the release that carries it is published")]
    [InlineData("The record 01M7824E3CQF5PC9P7X04KXYD7 says so.", "public", "a record identifier")]
    [InlineData("The record 01M7824E3CQF5PC9P7X04KXYD7 says so.", "private", null)]
    [InlineData("Written at {home}/someone/estate.", "private", "a machine path")]
    [InlineData("Write to someone@example.com.", "private", "a contact address")]
    [InlineData("Write to someone@⁠example.com.", "public", "a contact address")]
    [InlineData("An ordinary line about W-2.", "public", null)]
    public void TheWholeTextIsCheckedAsWrittenAndWithItsEscapesRemoved(string line, string form, string? refusal)
    {
        using Fixture f = Fixture.Write(held: true);
        // A machine path is written here only as the check reads it, so the repository holds none.
        line = line.Replace("{home}", "/" + "Users", StringComparison.Ordinal);
        Assert.Equal(refusal, ProgressReport.TargetRefusal(f.Estate.Load(), form, line, f.Estate.Clock));
    }

    [Fact]
    public void ACellIsOneLineThatCannotFormatLinkOrMentionAnyone()
    {
        Assert.Equal("a \\| b \\*c\\* \\[d\\](e) \\<!-- f --\\> @⁠someone x", ProgressReport.Cell("a | b *c* [d](e) <!-- f --> @someone\nx"));
        Assert.Equal("—", ProgressReport.Cell(null));
        Assert.Throws<ArgumentException>(() => ProgressReport.RenderTarget(new TestEstate().Load(), "internal", null, Marker, Later));
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(TestEstate estate) => Estate = estate;

        public TestEstate Estate { get; }

        public RecordEnvelope Product { get; private set; } = null!;

        public RecordEnvelope Published { get; private set; } = null!;

        public RecordEnvelope Done { get; private set; } = null!;

        public RecordEnvelope Open { get; private set; } = null!;

        public RecordEnvelope Held { get; private set; } = null!;

        public RecordEnvelope Disclosing { get; private set; } = null!;

        public RecordEnvelope Release { get; private set; } = null!;

        public RecordEnvelope Milestone { get; private set; } = null!;

        public RecordEnvelope Ask { get; private set; } = null!;

        // A published release; REL-094 carrying W-1 (accepted), W-2 (a result awaiting review) and, when held, the held FIX-90 and DOCS-9,
        // whose title quotes the held fix; a milestone and an ask to the operator.
        public static Fixture Write(bool held)
        {
            var f = new Fixture(new TestEstate(recordFormat: 3));
            TestEstate estate = f.Estate;
            f.Product = ProgressReportTests.Product(estate);
            f.Published = Publish(estate, f.Product, "0.1.0", Commit('a'));
            f.Done = Order(estate, "W-1");
            Accept(estate, f.Done);
            (f.Open, _) = Worked(estate, "W-2");
            var carried = new List<RecordEnvelope> { f.Done, f.Open };
            if (held)
            {
                f.Held = estate.Add("work-order", estate.OperatorId, $$"""
                    {"key":"FIX-90","kind":"ordinary","title":"Cache fix","intent":"{{HeldIntent}}","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"],"disclosure":"held"}
                    """);
                f.Disclosing = estate.Add("work-order", estate.OperatorId, OrderBody("DOCS-9", "ordinary", string.Empty).Replace("\"Order DOCS-9\"", "\"Document FIX-90\"", StringComparison.Ordinal));
                carried.AddRange([f.Held, f.Disclosing]);
            }

            f.Release = estate.Add("work-order", estate.OperatorId, OrderBody("REL-094", "ordinary", string.Join(",", carried.Select(c => $$"""{"type":"work-order","id":"{{c.Id}}"}"""))));
            f.Milestone = Order(estate, "M-1", kind: "milestone");
            f.Ask = estate.Add("ask", estate.OperatorId, ProgressReportTests.Ask(estate.OperatorId, "Ship the release?"));
            return f;
        }

        public void Dispose() => Estate.Dispose();
    }
}
