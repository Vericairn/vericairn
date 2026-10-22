// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// The golden cases of the record-predicate language (the estate's design/goals.md § 5): each row is a condition over a small estate,
/// with the verdict, the reason and the records read that the evaluator must give. Names in at-signs stand for the records the
/// scenario writes, in the order it writes them.
/// </summary>
public class GoalPredicateTests
{
    private const string Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Scenario, condition, verdict, reason, and the records read in order.</summary>
    public static TheoryData<string, string, string, string, string> Cases => new()
    {
        // Existence, field equality and the subject, followed across the revisions of the record it names.
        { "releases", """{"record":{"type":"release","subject":"@P@","fields":{"version":"0.1.0","status":"published"}}}""", "met",
            "release records about @P@ with version 0.1.0 and status published: 1 current record matches (@PUBLISHED@); at least one is required.", "@PUBLISHED@" },
        { "releases", """{"record":{"type":"release","subject":"@P2@","fields":{"version":"0.1.0","status":"published"}}}""", "met",
            "release records about @P2@ with version 0.1.0 and status published: 1 current record matches (@PUBLISHED@); at least one is required.", "@PUBLISHED@" },
        { "releases", """{"record":{"type":"release","subject":"@Q@","fields":{"version":"0.1.0"}}}""", "not-met",
            "release records about @Q@ with version 0.1.0: no current record matches; at least one is required.", "" },

        // Current revisions only: the candidate its publication superseded no longer counts.
        { "releases", """{"record":{"type":"release","subject":"@P@","fields":{"version":"0.1.0","status":"candidate"}}}""", "not-met",
            "release records about @P@ with version 0.1.0 and status candidate: no current record matches; at least one is required.", "" },
        { "releases", """{"record":{"type":"release","fields":{"status":"candidate"}}}""", "met",
            "release records with status candidate: 1 current record matches (@NEXT@); at least one is required.", "@NEXT@" },

        // Counts.
        { "releases", """{"record":{"type":"release","subject":"@P@","min":2}}""", "met",
            "release records about @P@: 2 current records match (@PUBLISHED@, @NEXT@); at least 2 are required.", "@PUBLISHED@,@NEXT@" },
        { "releases", """{"record":{"type":"release","subject":"@P@","max":1}}""", "not-met",
            "release records about @P@: 2 current records match (@PUBLISHED@, @NEXT@); at most 1 is allowed.", "@PUBLISHED@,@NEXT@" },
        { "releases", """{"record":{"type":"release","subject":"@P@","min":2,"max":2}}""", "met",
            "release records about @P@: 2 current records match (@PUBLISHED@, @NEXT@); exactly 2 are required.", "@PUBLISHED@,@NEXT@" },

        // The derived state of a work order, as status shows it, with the records it rests on.
        { "work", """{"record":{"type":"work-order","fields":{"key":"W-2"},"state":"accepted"}}""", "met",
            "work-order records with key W-2 that are accepted: 1 current record matches (@W2@); at least one is required.", "@W2@,@W2ACCEPTED@" },
        { "work", """{"record":{"type":"work-order","fields":{"key":"W-1"},"state":"accepted"}}""", "not-met",
            "work-order records with key W-1 that are accepted: no current record matches; at least one is required.", "@W1ACCEPTED@,@W1REVISED@" },
        { "work", """{"record":{"type":"work-order","fields":{"key":"W-1"},"state":"accepted (earlier revision)"}}""", "met",
            "work-order records with key W-1 that are accepted (earlier revision): 1 current record matches (@W1REVISED@); at least one is required.", "@W1ACCEPTED@,@W1REVISED@" },
        { "work", """{"record":{"type":"work-order","fields":{"key":"W-3"},"state":"active"}}""", "met",
            "work-order records with key W-3 that are active: 1 current record matches (@W3@); at least one is required.", "@W3@,@W3ACTIVE@" },

        // An ask's state: answered by a decision that names it, else the state its asker recorded; a body field is what the record says.
        { "work", """{"record":{"type":"ask","state":"open","max":0}}""", "met",
            "ask records that are open: no current record matches; none is allowed.", "@ASKED@,@ANSWER@,@NOTIFIED@,@WITHDRAWN@" },
        { "work", """{"record":{"type":"ask","state":"notified","max":0}}""", "not-met",
            "ask records that are notified: 1 current record matches (@NOTIFIED@); none is allowed.", "@ASKED@,@ANSWER@,@NOTIFIED@,@WITHDRAWN@" },
        { "work", """{"record":{"type":"ask","state":"answered"}}""", "met",
            "ask records that are answered: 1 current record matches (@ASKED@); at least one is required.", "@ASKED@,@ANSWER@,@NOTIFIED@,@WITHDRAWN@" },
        { "work", """{"record":{"type":"ask","fields":{"state":"open"},"max":0}}""", "not-met",
            "ask records with state open: 1 current record matches (@ASKED@); none is allowed.", "@ASKED@" },

        // No living document of product P is due, following the product across its revisions.
        { "documents-due", """{"record":{"type":"living-document","product":"@P@","state":"due","max":0}}""", "not-met",
            "living-document records of product @P@ that are due: 2 current records match (@GUIDE@, @NOTES@); none is allowed. docs/guide.md: due at refuse: release 0.1.0 was published (@PUBLISHED@) after its latest clearance. docs/notes.md: due at warn: release 0.1.0 was published (@PUBLISHED@) after its latest clearance.",
            "@GUIDE@,@NOTES@,@PUBLISHED@" },
        { "documents-due", """{"record":{"type":"living-document","product":"@P2@","state":"due","max":0}}""", "not-met",
            "living-document records of product @P2@ that are due: 2 current records match (@GUIDE@, @NOTES@); none is allowed. docs/guide.md: due at refuse: release 0.1.0 was published (@PUBLISHED@) after its latest clearance. docs/notes.md: due at warn: release 0.1.0 was published (@PUBLISHED@) after its latest clearance.",
            "@GUIDE@,@NOTES@,@PUBLISHED@" },
        { "documents-due", """{"record":{"type":"living-document","product":"@Q@","state":"due","max":0}}""", "met",
            "living-document records of product @Q@ that are due: no current record matches; none is allowed.", "@OTHER@" },
        { "documents-cleared", """{"record":{"type":"living-document","product":"@P@","state":"due","max":0}}""", "met",
            "living-document records of product @P@ that are due: no current record matches; none is allowed.", "@GUIDE@,@NOTES@,@GUIDECLEARED@,@NOTESCLEARED@" },
        { "documents-cleared", """{"record":{"type":"living-document","product":"@P@","state":"current","min":2}}""", "met",
            "living-document records of product @P@ that are current: 2 current records match (@GUIDE@, @NOTES@); at least 2 are required.", "@GUIDE@,@NOTES@,@GUIDECLEARED@,@NOTESCLEARED@" },

        // A document whose contributions no installed pack provides cannot be decided here: unknown, unless another is due.
        { "documents-unavailable", """{"record":{"type":"living-document","product":"@P@","state":"due","max":0}}""", "unknown",
            "living-document records of product @P@ that are due: no current record matches for certain, and 1 more cannot be decided here (docs/page.md is unavailable, since it names vericairn.example/never-installed, which no installed pack provides); none is allowed.",
            "@PAGE@" },
        { "documents-unavailable-and-due", """{"record":{"type":"living-document","product":"@P@","state":"due","max":0}}""", "not-met",
            "living-document records of product @P@ that are due: 1 current record matches for certain (@GUIDE@), and 1 more cannot be decided here (docs/page.md is unavailable, since it names vericairn.example/never-installed, which no installed pack provides); none is allowed. docs/guide.md: due at refuse: release 0.1.0 was published (@PUBLISHED@) after its latest clearance.",
            "@GUIDE@,@PAGE@,@PUBLISHED@,@PAGECLEARED@" },

        // An evidence statement is the evaluator's to judge; a predicate over a type this build does not read decides nothing.
        { "releases", """{"evidence":"The guide describes every command.","check":"An evaluator reads the guide against the command tree."}""", "unknown",
            "An evidence statement, judged by an evaluator who did none of the work: An evaluator reads the guide against the command tree.", "" },
        { "releases", """{"record":{"type":"widget"}}""", "unknown",
            "The predicate names no record type this build reads (widget).", "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachConditionHasItsVerdictReasonAndTheRecordsItRead(string scenario, string condition, string verdict, string reason, string read)
    {
        using var estate = new TestEstate(recordFormat: 2);
        Dictionary<string, RecordEnvelope> named = Scenario(estate, scenario);
        string Fill(string text) => named.Aggregate(text, (t, n) => t.Replace("@" + n.Key + "@", n.Value.Id, StringComparison.Ordinal));
        using JsonDocument parsed = JsonDocument.Parse(Fill(condition));

        GoalPredicates.Verdict actual = GoalPredicates.Evaluate(estate.Load(), parsed.RootElement);

        Assert.Equal(verdict, actual.Value);
        Assert.Equal(Fill(reason), actual.Reason);
        Assert.Equal(read.Length == 0 ? [] : read.Split(',').Select(n => named[n.Trim('@')].Id).ToArray(), actual.Read.Select(r => r.Id).ToArray());
        Assert.All(actual.Read, r => Assert.Equal(estate.Load().ById[r.Id].ComputedHash, r.Hash));
    }

    [Fact]
    public void TheSameRecordsGiveTheSameVerdictsOnEveryLoad()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Dictionary<string, RecordEnvelope> named = Scenario(estate, "documents-due");
        using JsonDocument condition = JsonDocument.Parse($$$"""{"record":{"type":"living-document","product":"{{{named["P"].Id}}}","state":"due","max":0}}""");
        GoalPredicates.Verdict first = GoalPredicates.Evaluate(estate.Load(), condition.RootElement);
        GoalPredicates.Verdict second = GoalPredicates.Evaluate(estate.Load(), condition.RootElement);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.Read, second.Read);
    }

    [Fact]
    public void AGoalsPredicatesNameARealSubjectProductAndAStateStatusDerives()
    {
        using var estate = new TestEstate(recordFormat: 2);
        Dictionary<string, RecordEnvelope> named = Scenario(estate, "releases");
        string product = named["P"].Id;
        RecordEnvelope good = estate.Add("goal", estate.BuilderId, Goal(estate, """[{"record":{"type":"release","subject":"@P@","fields":{"version":"0.1.0","status":"published"}}},{"record":{"type":"living-document","product":"@P@","state":"due","max":0}},{"record":{"type":"work-order","fields":{"key":"W-1"},"state":"accepted (earlier revision)"}}]""".Replace("@P@", product, StringComparison.Ordinal)));
        RecordEnvelope ghost = estate.Add("goal", estate.BuilderId, Goal(estate, """[{"record":{"type":"release","subject":"01M4TRNE1GYEK4P8MXQAB3C8Q4"}}]"""));
        RecordEnvelope misplaced = estate.Add("goal", estate.BuilderId, Goal(estate, $$$"""[{"record":{"type":"release","product":"{{{product}}}"}}]"""));
        RecordEnvelope notProduct = estate.Add("goal", estate.BuilderId, Goal(estate, $$$"""[{"record":{"type":"living-document","product":"{{{named["PUBLISHED"].Id}}}"}}]"""));
        RecordEnvelope ownField = estate.Add("goal", estate.BuilderId, Goal(estate, """[{"record":{"type":"release","state":"published"}}]"""));
        RecordEnvelope noSuchState = estate.Add("goal", estate.BuilderId, Goal(estate, """[{"record":{"type":"work-order","state":"done"}}]"""));
        List<Finding> findings = estate.Load().Findings.Where(f => f.Severity == Severity.Error).ToList();

        Assert.DoesNotContain(findings, f => f.Location.StartsWith(good.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.reference" && f.Location == ghost.ExpectedPath + "#body.completion[0].record.subject");
        Assert.Contains(findings, f => f.Code == "goal.predicate" && f.Location == misplaced.ExpectedPath + "#body.completion[0].record.product");
        Assert.Contains(findings, f => f.Code == "body.reference" && f.Location == notProduct.ExpectedPath + "#body.completion[0].record.product");
        Assert.Contains(findings, f => f.Code == "goal.predicate" && f.Location == ownField.ExpectedPath + "#body.completion[0].record.state");
        Assert.Contains(findings, f => f.Code == "goal.predicate" && f.Location == noSuchState.ExpectedPath + "#body.completion[0].record.state" && f.Message.Contains("accepted (earlier revision)", StringComparison.Ordinal));
    }

    /// <summary>Writes a scenario's records in order and names them.</summary>
    private static Dictionary<string, RecordEnvelope> Scenario(TestEstate estate, string scenario)
    {
        var named = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        string op = estate.OperatorId;
        switch (scenario)
        {
            case "releases":
                Products(estate, named);
                named["CANDIDATE"] = estate.Add("release", op, Release("0.1.0", "candidate"), new RecordReference("product", named["P2"].Id, null));
                estate.Add("acceptance", op, """{"statement":"Accepted the candidate.","basis":[],"self":true}""", named["CANDIDATE"].Reference);
                named["PUBLISHED"] = estate.Add("release", op, Release("0.1.0", "published"), new RecordReference("product", named["P2"].Id, null), supersedes: named["CANDIDATE"].Id);
                named["NEXT"] = estate.Add("release", op, Release("0.2.0", "candidate"), new RecordReference("product", named["P"].Id, null));
                break;

            case "work":
                named["W1"] = estate.Add("work-order", op, Order("W-1"));
                named["W1ACCEPTED"] = estate.Add("acceptance", op, """{"statement":"Accepted.","basis":[],"self":true}""", named["W1"].Reference);
                named["W1REVISED"] = estate.Add("work-order", op, Order("W-1").Replace("Work.", "Work, revised.", StringComparison.Ordinal), supersedes: named["W1"].Id);
                named["W2"] = estate.Add("work-order", op, Order("W-2"));
                named["W2ACCEPTED"] = estate.Add("acceptance", op, """{"statement":"Accepted.","basis":[],"self":true}""", named["W2"].Reference);
                named["W3"] = estate.Add("work-order", op, Order("W-3"));
                named["W3ACTIVE"] = estate.Add("work-status", op, """{"status":"active","note":"Started."}""", new RecordReference("work-order", named["W3"].Id, null));
                named["ASKED"] = estate.Add("ask", estate.BuilderId, Ask(op, "Publish?", "open"));
                named["ANSWER"] = estate.Add("decision", estate.BuilderId, $$"""{"title":"Publish","statement":"Yes.","summary":"Publish.","synthesis":"The operator answers.","effects":["publication"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":[],"answers":["{{named["ASKED"].Id}}"]}""");
                RecordEnvelope second = estate.Add("ask", estate.BuilderId, Ask(op, "Spend?", "open"));
                named["NOTIFIED"] = estate.Add("ask", estate.BuilderId, Ask(op, "Spend?", "notified"), supersedes: second.Id);
                RecordEnvelope third = estate.Add("ask", estate.BuilderId, Ask(op, "Rename?", "open"));
                named["WITHDRAWN"] = estate.Add("ask", estate.BuilderId, Ask(op, "Rename?", "withdrawn"), supersedes: third.Id);
                break;

            case "documents-due":
            case "documents-cleared":
                Products(estate, named);
                named["GUIDE"] = Declare(estate, "docs/guide.md", "refuse", named["P"].Id);
                named["NOTES"] = Declare(estate, "docs/notes.md", "warn", named["P2"].Id);
                named["OTHER"] = Declare(estate, "docs/other.md", "refuse", named["Q"].Id);
                Publish(estate, named, named["P2"].Id);
                if (scenario == "documents-cleared")
                {
                    named["GUIDECLEARED"] = estate.Add("document-review", op, DocumentReview(), named["GUIDE"].Reference);
                    named["NOTESCLEARED"] = estate.Add("document-review", op, DocumentReview(), named["NOTES"].Reference);
                }

                break;

            case "documents-unavailable":
            case "documents-unavailable-and-due":
                Products(estate, named);
                if (scenario == "documents-unavailable-and-due")
                {
                    named["GUIDE"] = Declare(estate, "docs/guide.md", "refuse", named["P"].Id);
                }

                named["PAGE"] = estate.Add("living-document", op, $$"""{"path":"docs/page.md","title":"Page","kind":"authored","source":"vericairn.example/never-installed","triggers":[{"on":"release","level":"refuse","product":"{{named["P"].Id}}"}],"summary":"x"}""");
                if (scenario == "documents-unavailable-and-due")
                {
                    Publish(estate, named, named["P"].Id);
                    estate.WriteFile("docs/page.md", "# Guide\n");
                    named["PAGECLEARED"] = estate.Add("document-review", op, DocumentReview(), named["PAGE"].Reference);
                }

                break;

            default:
                throw new ArgumentException("No scenario " + scenario, nameof(scenario));
        }

        return named;
    }

    private static void Products(TestEstate estate, Dictionary<string, RecordEnvelope> named)
    {
        named["P"] = estate.Add("product", estate.OperatorId, """{"name":"Widget","slug":"widget","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        named["P2"] = estate.Add("product", estate.OperatorId, named["P"].Body.GetRawText().Replace("A product.", "A product, revised.", StringComparison.Ordinal), supersedes: named["P"].Id);
        named["Q"] = estate.Add("product", estate.OperatorId, """{"name":"Gadget","slug":"gadget","summary":"Another product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
    }

    private static void Publish(TestEstate estate, Dictionary<string, RecordEnvelope> named, string product)
    {
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, Release("0.1.0", "candidate"), new RecordReference("product", product, null));
        estate.Add("acceptance", estate.OperatorId, """{"statement":"Accepted the candidate.","basis":[],"self":true}""", candidate.Reference);
        named["PUBLISHED"] = estate.Add("release", estate.OperatorId, Release("0.1.0", "published"), new RecordReference("product", product, null), supersedes: candidate.Id);
    }

    private static RecordEnvelope Declare(TestEstate estate, string path, string level, string product)
    {
        estate.WriteFile(path, "# Guide\n");
        return estate.Add("living-document", estate.OperatorId, $$"""{"path":"{{path}}","title":"Document","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"{{level}}","product":"{{product}}"}],"summary":"Follows the releases."}""");
    }

    private static string Release(string version, string status)
    {
        string published = status == "published" ? ""","publishedAt":"2026-10-13T20:00:00Z","url":"https://example.invalid/releases" """ : string.Empty;
        return $$"""{"version":"{{version}}","repository":"Example/widget","commit":"{{Commit}}","status":"{{status}}","artifacts":[{"name":"widget.zip","hash":"{{Hash}}","bytes":1,"platform":"any"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]{{published}}}""";
    }

    private static string Order(string key) =>
        $$"""{"key":"{{key}}","kind":"ordinary","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""";

    private static string Ask(string to, string question, string state) =>
        $$"""{"to":"{{to}}","question":"{{question}}","recommendation":"Yes.","consequence":"The work waits.","state":"{{state}}"}""";

    private static string DocumentReview() =>
        $$"""{"outcome":"current","content":{"hash":"{{ContentHash.Sha256("# Guide\n"u8)}}","bytes":8},"answers":[],"independence":"operator","summary":"Read it.","findings":[],"checks":[]}""";

    private static string Goal(TestEstate estate, string completion) =>
        $$"""{"title":"Release the widget","objective":"Ship the widget.","completion":{{completion}},"constraints":[],"kind":"finite","sponsor":"{{estate.OperatorId}}","budget":{"wallMinutes":600,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""";
}
