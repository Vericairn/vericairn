// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

// CODE-81: a checked push matches the estate's blocked terms against every commit message it carries, trailers included, at a level
// the estate configures, and requires the exact trailer lines the estate configures; a checked merge matches the message of the merge
// commit it would create.
public sealed class CommitMessageTests
{
    private const string Trailer = "Signed-off-by: Estate Builder <builder@example.invalid>";
    private static readonly string MergeCommit = new('e', 40);
    private static readonly string[] TernOnly = ["Tern"];
    private static readonly ImmutableArray<string> Terms = BlockedTerms.TermsOf(JsonSerializer.SerializeToElement(new { terms = new[] { "Tern", "orbit-7-lens" } }));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"blockedTerms\":\"block\"}")]
    [InlineData("{\"blockedTerms\":\"warn\",\"mode\":\"strict\"}")]
    [InlineData("{\"trailer\":\"no token here\"}")]
    [InlineData("{\"trailer\":\"Signed-off-by:no space\"}")]
    [InlineData("{\"trailer\":\" Signed-off-by: padded\"}")]
    [InlineData("{\"trailer\":\"Signed-off-by: two\\nlines\"}")]
    [InlineData("{\"trailer\":7}")]
    [InlineData("[]")]
    public void MalformedSettingsAreRefused(string parameters)
    {
        using JsonDocument json = JsonDocument.Parse(parameters);
        Assert.NotNull(CommitMessages.Validate(json.RootElement));
    }

    [Theory]
    [InlineData("{\"blockedTerms\":\"warn\"}")]
    [InlineData("{\"blockedTerms\":\"refuse\"}")]
    [InlineData("{\"trailer\":\"" + Trailer + "\"}")]
    [InlineData("{\"blockedTerms\":\"refuse\",\"trailer\":\"Co-authored-by: A Person <person@example.org>\"}")]
    public void WellFormedSettingsAreAccepted(string parameters)
    {
        using JsonDocument json = JsonDocument.Parse(parameters);
        Assert.Null(CommitMessages.Validate(json.RootElement));
    }

    [Theory]
    [InlineData("Add the widget\n\nCo-Authored-By: Tern 7 <agent@example.invalid>", CommitMessages.Term)]
    [InlineData("Tern adds the widget", CommitMessages.Term)]
    [InlineData("Add the widget\n\nWritten in a TERN session on orbit-7-lens.", CommitMessages.Term)]
    [InlineData("Add lanterns, terne plate and an intern\n\n" + Trailer, null)]
    [InlineData("Add the widget\n\n" + Trailer, null)]
    [InlineData("Add the widget\r\n\r\n" + Trailer + "  \r\n", null)]
    [InlineData("Add the widget", CommitMessages.Missing)]
    [InlineData("Add the widget\n\nWhy it changed.", CommitMessages.Missing)]
    [InlineData("Add the widget\n\n" + Trailer + "\n\nA paragraph after it.", CommitMessages.Missing)]
    [InlineData("Add the widget\n\nSigned-off-by: Someone Else <else@example.invalid>", CommitMessages.Altered)]
    [InlineData("Add the widget\n\nsigned-off-by: Estate Builder <builder@example.invalid>", CommitMessages.Altered)]
    public void EachMessageIsMatchedWholeAndCarriesTheRequiredTrailer(string message, string? kind)
    {
        var settings = new CommitMessages.Settings(CommitMessages.Refuse, [], [new("terms", Terms)], [new(Trailer, "trailers")]);
        (CommitMessages.Finding? refusal, IReadOnlyList<CommitMessages.Finding> warnings) = CommitMessages.Check(settings, [("Commit one", message)]);
        Assert.Empty(warnings);
        Assert.Equal(kind, refusal?.Kind);
        if (refusal is null) return;
        Assert.Equal(kind == CommitMessages.Term ? "terms" : "trailers", refusal.Policy);
        string described = CommitMessages.Describe(refusal);
        Assert.StartsWith("Commit one ", described, StringComparison.Ordinal);
        Assert.DoesNotContain("tern", described, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Estate Builder", described, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExactRequiredTrailerIsExemptFromTheTermsAndTheWarnLevelStillRequiresIt()
    {
        const string named = "Assisted-by: Tern <agent@example.invalid>";
        var settings = new CommitMessages.Settings(CommitMessages.Refuse, [], [new("terms", Terms)], [new(named, "trailers")]);
        Assert.Null(CommitMessages.Check(settings, [("Commit one", "Add the widget\n\n" + named)]).Refusal);
        Assert.Equal(CommitMessages.Term, CommitMessages.Check(settings, [("Commit one", "Add the widget\n\nAssisted-by: Tern 7 <agent@example.invalid>")]).Refusal?.Kind);

        var warned = settings with { Level = CommitMessages.Warn, LevelPolicies = ["levels"] };
        (CommitMessages.Finding? refusal, IReadOnlyList<CommitMessages.Finding> warnings) = CommitMessages.Check(warned,
            [("Commit one", "Tern adds the widget\n\n" + named), ("Commit two", "Add the widget\n\n" + named)]);
        Assert.Null(refusal);
        Assert.Equal("Commit one", Assert.Single(warnings).Commit);
        Assert.Equal(CommitMessages.Missing, CommitMessages.Check(warned, [("Commit one", "Tern adds the widget")]).Refusal?.Kind);
        // A merge commit's message is matched against the terms only: the trailers apply to pushed commits.
        Assert.Null(CommitMessages.Check(settings, [("The merge commit", "Merge pull request #1 from Example/work")], trailers: false).Refusal);
    }

    [Fact]
    public void TheSettingsComeFromCurrentActiveEstatePoliciesAndRefuseOutweighsWarn()
    {
        RecordEnvelope terms = FoundationPolicyTests.P(BlockedTerms.Kind, new { terms = TernOnly }, key: "terms");
        RecordEnvelope warn = FoundationPolicyTests.P(CommitMessages.Kind, new { blockedTerms = "warn", trailer = Trailer }, key: "warn");
        RecordEnvelope refuse = FoundationPolicyTests.P(CommitMessages.Kind, new { blockedTerms = "refuse" }, key: "refuse");
        RecordEnvelope product = FoundationPolicyTests.P(CommitMessages.Kind, new { trailer = "Reviewed-by: Product <product@example.invalid>" }, key: "product",
            selection: new { kind = "product", product = "widget" });
        RecordEnvelope future = FoundationPolicyTests.R("estate-policy", "future", new
        {
            key = "future", owner = "operator", state = "active", level = "refuse", startsAt = "2027-01-01T00:00:00Z", maxExceptionDays = 30,
            appliesTo = new { kind = "estate" }, rules = new[] { new { key = "rule", kind = CommitMessages.Kind, version = 1, parameters = new { trailer = "Later-by: Someone <later@example.invalid>" } } },
        });
        RecordEnvelope retired = FoundationPolicyTests.R("estate-policy", "retired", new
        {
            key = "retired", owner = "operator", state = "retired", level = "refuse", startsAt = "2026-01-01T00:00:00Z", maxExceptionDays = 30,
            appliesTo = new { kind = "estate" }, rules = new[] { new { key = "rule", kind = CommitMessages.Kind, version = 1, parameters = new { trailer = "Gone-by: Someone <gone@example.invalid>" } } },
        });
        DateTimeOffset now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        // A trailer alone is something to check; a level alone, with no term declared, is not.
        Assert.True(CommitMessages.In(PolicyBasisTests.Model([warn]), now).Checks);
        Assert.False(CommitMessages.In(PolicyBasisTests.Model([refuse]), now).Checks);

        CommitMessages.Settings warned = CommitMessages.In(PolicyBasisTests.Model([terms, warn, product, future, retired]), now);
        Assert.Equal(CommitMessages.Warn, warned.Level);
        Assert.Equal(["warn"], warned.LevelPolicies);
        Assert.Equal("terms", Assert.Single(warned.Lists).Policy);
        Assert.Equal(Trailer, Assert.Single(warned.Trailers).Line);

        CommitMessages.Settings refused = CommitMessages.In(PolicyBasisTests.Model([terms, warn, refuse]), now);
        Assert.Equal(CommitMessages.Refuse, refused.Level);
        Assert.Equal(["refuse"], refused.LevelPolicies);
        Assert.Equal(CommitMessages.Refuse, CommitMessages.In(PolicyBasisTests.Model([terms]), now).Level);
    }

    [Fact]
    public void TheRuleIsAFoundationKindForTheEstateOnly()
    {
        PolicyEvaluation evaluation = FoundationPolicyTests.Run(CommitMessages.Kind, new { blockedTerms = "warn" }, []);
        Assert.Equal("satisfied", Assert.Single(evaluation.Findings).Verdict);
        Assert.Equal("unavailable", Assert.Single(FoundationPolicyTests.Run(CommitMessages.Kind, new { blockedTerms = "never" }, []).Findings).Verdict);
        Assert.Equal("unavailable", Assert.Single(FoundationPolicyTests.Run(CommitMessages.Kind, new { blockedTerms = "warn" }, [],
            selection: new { kind = "product", product = "product" }).Findings).Verdict);
    }

    [Theory]
    [InlineData("trailer", false)]
    [InlineData("subject", false)]
    [InlineData("merge-in-range", false)]
    [InlineData("content-only", true)]
    public void APushCarryingABlockedTermInAnyCommitMessageIsRefusedBeforeAnyEffect(string where, bool pushed)
    {
        using var f = new ChangesFlowTests.Fixture();
        using var signing = new Signing(f.Estate);
        RecordEnvelope terms = Declare(f.Estate, signing, "model-names", Rule("terms", BlockedTerms.Kind, new JsonObject { ["terms"] = new JsonArray("Tern") }));
        f.Adapter.Commits = where switch
        {
            "trailer" => [new(ChangesFlowTests.Head, "Add the widget\n\nCo-Authored-By: Tern 7 <agent@example.invalid>")],
            "subject" => [new(ChangesFlowTests.Head, "Tern adds the widget")],
            "merge-in-range" => [new(ChangesFlowTests.Head, "Add the widget"), new(MergeCommit, "Merge branch 'main' into work/change\n\nResolved in a Tern session.")],
            _ => [new(ChangesFlowTests.Head, "Add the widget")],
        };
        f.Adapter.Text = "The widget's notes name Tern in a file's content, which the secret and disclosure scans read.";
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.Equal(pushed, response.Ok);
        Assert.Equal(pushed ? 1 : 0, f.Adapter.Pushes);
        Assert.Equal(1, f.Adapter.MessageReads);
        AssertNoTermRecorded(f.Service);
        if (pushed)
        {
            Assert.Contains(f.Current("change-push").Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!.Contains("none was refused", StringComparison.Ordinal));
            return;
        }
        Assert.Equal("known-failure", response.Status);
        Assert.Contains(where == "merge-in-range" ? MergeCommit : ChangesFlowTests.Head, response.Message, StringComparison.Ordinal);
        Assert.Contains("estate policy " + terms.Id, response.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tern", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AtTheWarnLevelTheTermIsReportedAndRecordedWithoutItsTextAndThePushGoesAhead()
    {
        using var f = new ChangesFlowTests.Fixture();
        using var signing = new Signing(f.Estate);
        Declare(f.Estate, signing, "model-names", Rule("terms", BlockedTerms.Kind, new JsonObject { ["terms"] = new JsonArray("Tern") }));
        RecordEnvelope level = Declare(f.Estate, signing, "commit-messages", Rule("level", CommitMessages.Kind, new JsonObject { ["blockedTerms"] = "warn" }));
        f.Adapter.Commits = [new(ChangesFlowTests.Head, "Tern adds the widget")];
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.True(response.Ok, response.Message);
        Assert.Equal(1, f.Adapter.Pushes);
        Assert.Contains("Warned, not refused, as estate policy " + level.Id, response.Message, StringComparison.Ordinal);
        Assert.Contains(ChangesFlowTests.Head, response.Message, StringComparison.Ordinal);
        Assert.Contains(f.Current("change-push").Body.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("summary").GetString()!.Contains("Warned", StringComparison.Ordinal));
        AssertNoTermRecorded(f.Service);
    }

    [Theory]
    [InlineData("present", true)]
    [InlineData("missing", false)]
    [InlineData("altered", false)]
    [InlineData("unreadable", false)]
    public void EveryPushedCommitCarriesTheRequiredTrailer(string trailer, bool pushed)
    {
        using var f = new ChangesFlowTests.Fixture();
        using var signing = new Signing(f.Estate);
        RecordEnvelope required = Declare(f.Estate, signing, "trailers", Rule("trailer", CommitMessages.Kind, new JsonObject { ["trailer"] = Trailer }));
        f.Adapter.Commits = trailer switch
        {
            "present" => [new(ChangesFlowTests.Head, "Add the widget\n\n" + Trailer), new(MergeCommit, "Merge branch 'main'\n\n" + Trailer)],
            "missing" => [new(ChangesFlowTests.Head, "Add the widget\n\n" + Trailer), new(MergeCommit, "Merge branch 'main'")],
            "altered" => [new(ChangesFlowTests.Head, "Add the widget\n\nSigned-off-by: Estate Builder 2 <builder@example.invalid>")],
            _ => null,
        };
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.Equal(pushed, response.Ok);
        Assert.Equal(pushed ? 1 : 0, f.Adapter.Pushes);
        if (pushed) return;
        Assert.Equal("known-failure", response.Status);
        Assert.DoesNotContain("Estate Builder", response.Message, StringComparison.Ordinal);
        if (trailer == "unreadable") Assert.Contains("could not be read", response.Message, StringComparison.Ordinal);
        else
        {
            Assert.Contains(trailer == "missing" ? MergeCommit : ChangesFlowTests.Head, response.Message, StringComparison.Ordinal);
            Assert.Contains((trailer == "missing" ? "lacks" : "altered form of") + " the trailer line estate policy " + required.Id, response.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WithNothingToCheckThePushReadsNoMessages()
    {
        using var f = new ChangesFlowTests.Fixture();
        using var signing = new Signing(f.Estate);
        Declare(f.Estate, signing, "commit-messages", Rule("level", CommitMessages.Kind, new JsonObject { ["blockedTerms"] = "warn" }));
        f.Adapter.Commits = null;
        Assert.True(f.Service.ChangesPush(f.Push).Ok);
        Assert.Equal(0, f.Adapter.MessageReads);
    }

    [Theory]
    [InlineData("clean", "refuse", false, true)]
    [InlineData("term", "refuse", false, false)]
    [InlineData("term", "refuse", true, false)]
    [InlineData("term", "warn", false, true)]
    [InlineData("trailer-only", "refuse", false, true)]
    public void AMergeChecksTheMessageOfTheMergeCommitItWouldCreate(string title, string level, bool queued, bool merged)
    {
        using var f = new ChangesFlowTests.Fixture();
        using var signing = new Signing(f.Estate);
        if (title != "trailer-only") Declare(f.Estate, signing, "model-names", Rule("terms", BlockedTerms.Kind, new JsonObject { ["terms"] = new JsonArray("Tern") }));
        if (level == "warn") Declare(f.Estate, signing, "commit-messages", Rule("level", CommitMessages.Kind, new JsonObject { ["blockedTerms"] = "warn" }));
        if (title == "trailer-only") Declare(f.Estate, signing, "trailers", Rule("trailer", CommitMessages.Kind, new JsonObject { ["trailer"] = Trailer }));
        f.Adapter.Commits = [new(ChangesFlowTests.Head, "Add the widget\n\n" + Trailer)];
        Assert.True(f.Service.ChangesPush(f.Push).Ok);
        f.Provider.Memory.QueueRequired = queued;
        string pullTitle = title == "term" ? "The widget, from a Tern session" : "Add the widget";
        f.Provider.Memory.Set("Example/widget", new("1", "work/change", "main", ChangesFlowTests.Head, "open", null, ChangesFlowTests.Checks(ChangesFlowTests.Head)) { Title = pullTitle });
        ChangeResponse response = f.Service.ChangesMerge(f.Merge);
        Assert.Equal(merged, response.Ok || response.Status == "observed-success");
        AssertNoTermRecorded(f.Service);
        if (!merged)
        {
            Assert.Equal("known-failure", response.Status);
            Assert.Null(f.Provider.MergedHead);
            Assert.Null(f.Provider.EnqueuedHead);
            Assert.StartsWith("The merge commit of pull request 1 carries a term", response.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("tern", response.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }
        Assert.Equal(ChangesFlowTests.Head, f.Provider.MergedHead);
        // The merge commit is created with exactly the message checked, where the estate checks any term; the trailer is not required of it.
        Assert.Equal(title == "trailer-only" ? null : "Merge pull request #1 from Example/work/change\n\n" + pullTitle, f.Provider.Memory.MergeMessage);
        Assert.Equal(level == "warn", response.Message.Contains("Warned, not refused", StringComparison.Ordinal));
    }

    // Each recorded revision of every operation, refused or not, holds no blocked term: the write boundary would refuse it, and a check
    // never repeats the matched text.
    private static void AssertNoTermRecorded(Service.EstateService service) =>
        Assert.All(service.Session.Load().OfType("operation"), o => Assert.DoesNotContain("tern", o.Body.GetRawText(), StringComparison.OrdinalIgnoreCase));

    internal static JsonObject Rule(string key, string kind, JsonObject parameters) => new() { ["key"] = key, ["kind"] = kind, ["version"] = 1, ["parameters"] = parameters };

    // An estate policy as the operator declares it: a decision approves its exact definition, and the policy names that decision.
    internal static RecordEnvelope Declare(TestEstate estate, Signing signing, string key, params JsonObject[] rules)
    {
        ArgumentNullException.ThrowIfNull(signing);
        var body = new JsonObject
        {
            ["key"] = key, ["title"] = "Commit messages", ["owner"] = estate.OperatorId, ["reason"] = "Keep the names agents run on out of pushed commits.",
            ["state"] = "active", ["appliesTo"] = new JsonObject { ["kind"] = "estate" }, ["level"] = "refuse",
            ["startsAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(2)), ["maxExceptionDays"] = 30, ["rules"] = new JsonArray(rules),
        };
        var decision = new JsonObject
        {
            ["title"] = "Policy " + key, ["statement"] = "Approve the stated definition only.", ["summary"] = "Approve a bounded test policy.", ["synthesis"] = "Test evidence, without external effects.",
            ["effects"] = new JsonArray("Apply the exact approved definition."), ["affects"] = new JsonArray(), ["decidedBy"] = estate.OperatorId,
            ["decidedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1)), ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
            ["policies"] = new JsonArray(new JsonObject
            {
                ["key"] = "definition", ["action"] = "declare", ["policy"] = key, ["change"] = "Approve this exact body.",
                ["definitionHash"] = PolicyRequirements.DefinitionHash(JsonSerializer.SerializeToElement(body)),
            }),
        };
        RecordEnvelope approval = estate.Add("decision", estate.OperatorId, decision.ToJsonString(), signer: signing.Signer);
        body["basis"] = new JsonArray(new JsonObject
        {
            ["decision"] = new JsonObject { ["type"] = approval.Type, ["id"] = approval.Id, ["hash"] = approval.ComputedHash }, ["requirement"] = "definition",
        });
        RecordEnvelope policy = estate.Add("estate-policy", estate.OperatorId, body.ToJsonString(), signer: signing.Signer);
        Assert.False(estate.Load().Findings.HasErrors, string.Join('\n', estate.Load().Findings));
        return policy;
    }
    // The operator's signing device, admitted in the estate: an estate policy and the decision approving it need its signature.
    internal sealed class Signing : IDisposable
    {
        private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-commit-message-keys-" + Ulid.NewUlid().Value);

        internal Signing(TestEstate estate)
        {
            var store = new FileKeyStore(home);
            string id = Ulid.NewUlid().Value;
            string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
            Signer = new StoreSigner(store, id);
            RecordEnvelope device = estate.Add("device", estate.OperatorId, $$"""
                {"principal":"{{estate.OperatorId}}","name":"Test policy signer","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"none","purpose":"signing"}
                """, id: id, signer: Signer);
            estate.Add("admission", estate.OperatorId, $$"""{"principal":"{{estate.OperatorId}}","status":"admitted","note":"Test policy signer."}""", subject: device.Reference, signer: Signer);
        }

        internal IRecordSigner Signer { get; }

        public void Dispose()
        {
            if (Directory.Exists(home)) Directory.Delete(home, true);
        }
    }
}
