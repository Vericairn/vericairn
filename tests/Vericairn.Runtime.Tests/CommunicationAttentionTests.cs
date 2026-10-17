// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

public class CommunicationAttentionTests
{
    [Theory]
    [InlineData("verification", "failed", "failed-check", "Verification failed; no later pass of this subject and check.")]
    [InlineData("verification", "unknown", "unknown-check", "Verification unknown; no later pass of this subject and check.")]
    [InlineData("review", "fail", "failed-check", "Review fail; 1 findings await disposition.")]
    [InlineData("review", "findings", "required-decision", "Review findings; 1 findings await disposition.")]
    [InlineData("operation", "uncertain", "uncertain-effect", "Operation uncertain; reconcile the original effect before retrying.")]
    [InlineData("operation", "known-failure", "incident", "Operation known-failure; reconcile the original effect before retrying.")]
    public void GoldenOpenItemsCarryExactReferencesAndCurrentState(string type, string outcome, string kind, string reason)
    {
        var f = new Fixture();
        RecordResponse subject = f.Add("resource", "subject", "{}");
        RecordResponse source = f.Add(type, "source", type switch
        {
            "verification" => Verification(outcome),
            "review" => $$"""{"outcome":"{{outcome}}","findings":[{"summary":"Full finding retained only in the record."}]}""",
            _ => $$"""{"status":"{{outcome}}","evidence":["Full evidence retained only in the record."]}""",
        }, subject);
        CommunicationAttention item = Assert.Single(f.Attention());
        string target = type == "review" ? subject.Id : source.Id;
        Assert.Equal(kind, item.Kind);
        Assert.Equal(source.Id, item.Record);
        Assert.Equal(source.Type, item.RecordType);
        Assert.Equal(source.Hash, item.Hash);
        Assert.Equal(new AttentionReference(subject.Type, subject.Id, subject.Hash), item.Subject);
        Assert.Equal(outcome, item.State);
        Assert.Equal($"{type} source at {source.Hash}; subject resource subject at {subject.Hash}\n  {reason} State: {outcome}; recorded 2026-10-13T12:00:02Z.\n  vericairn records show {target}\n", item.Text);
        Assert.Equal(target, item.Target!.Id);
        Assert.DoesNotContain("Full", item.Text, StringComparison.Ordinal);
        Assert.Equal(source.Envelope, f.Api.Record(source.Id)!.Envelope);
        Assert.True(CommunicationPresentation.ShouldDeliver("none", "progress", [item]));
    }

    [Theory]
    [InlineData("failed", "partial", false, "failed-check")]
    [InlineData("unknown", "partial", false, "unknown-check")]
    [InlineData("not-performed", "partial", false, "unknown-check")]
    [InlineData("skipped", "partial", false, "unknown-check")]
    [InlineData("passed", "partial", true, "uncertain-effect")]
    [InlineData("passed", "blocked", false, "blocker")]
    public void GoldenUnresolvedResults(string check, string outcome, bool effects, string kind)
    {
        var f = new Fixture();
        RecordResponse result = f.Result("open", check, outcome, effects);
        CommunicationAttention item = Assert.Single(f.Attention());
        Assert.Equal((kind, result.Id, result.Hash, outcome), (item.Kind, item.Record, item.Hash, item.State));
        Assert.Equal(result.Envelope.GetProperty("subject").GetProperty("id").GetString(), item.Subject!.Id);
        Assert.Contains("not accepted or succeeded", item.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Full", item.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("verification", "passed")]
    [InlineData("review", "pass")]
    [InlineData("operation", "observed-success")]
    [InlineData("operation", "canceled")]
    [InlineData("operation", "not-started")]
    public void GoldenSettledItemsAreEmpty(string type, string outcome)
    {
        var f = new Fixture();
        f.Add(type, "settled", $$"""{"outcome":"{{outcome}}","status":"{{outcome}}"}""");
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void LaterPassMatchesSubjectMethodEnvironmentAndCommandButNotCommitOrEnvironmentKeyOrder()
    {
        var f = new Fixture();
        RecordResponse subject = f.Add("resource", "repo", "{}");
        f.Add("verification", "failed", Verification("failed"), subject);
        f.Add("verification", "unknown", Verification("unknown"), subject);
        f.Add("verification", "passed", """{"outcome":"passed","method":"github-actions","environment":{"runner":"hosted","workflow":"ci.yml"},"commit":"new-commit"}""", subject);
        Assert.Empty(f.Attention());
        RecordResponse failedAgain = f.Add("verification", "failed-again", Verification("failed"), subject);
        Assert.Equal(failedAgain.Id, Assert.Single(f.Attention()).Record);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("environment")]
    [InlineData("command")]
    [InlineData("subject")]
    [InlineData("hash")]
    [InlineData("earlier")]
    public void APassOfAnotherCheckOrAnEarlierPassDoesNotResolveFailure(string difference)
    {
        var f = new Fixture();
        RecordResponse subject = f.Add("resource", "repo", "{}");
        RecordResponse failure = f.Add("verification", "failure", Verification("failed"), subject);
        string body = Verification("passed");
        if (difference == "method") { body = body.Replace("github-actions", "manual", StringComparison.Ordinal); }
        if (difference == "environment") { body = body.Replace("ci.yml", "release.yml", StringComparison.Ordinal); }
        if (difference == "command") { body = body.Replace("\"commit\"", "\"command\":\"different command\",\"commit\"", StringComparison.Ordinal); }
        if (difference == "subject") { subject = f.Add("resource", "other-repo", "{}"); }
        if (difference == "hash") { subject = subject with { Hash = "sha256:another-pin" }; }
        f.Add("verification", "pass", body, subject, at: difference == "earlier" ? "2026-10-13T12:00:00Z" : null);
        Assert.Equal(failure.Id, Assert.Single(f.Attention()).Record);
    }

    [Fact]
    public void SameTimestampUsesIdOrderingRatherThanInputOrder()
    {
        var f = new Fixture();
        RecordResponse subject = f.Add("resource", "repo", "{}");
        const string at = "2026-10-13T12:01:00Z";
        f.Add("verification", "z-pass", Verification("passed"), subject, at: at);
        f.Add("verification", "a-failed", Verification("failed"), subject, at: at);
        Assert.Empty(f.Attention());
    }

    [Theory]
    [InlineData("result")]
    [InlineData("assignment-result")]
    [InlineData("work-order")]
    public void EachResultResolutionClearsChecksEffectsAndBlockedOutcome(string resolution)
    {
        var f = new Fixture();
        RecordResponse result = f.Result("old", "failed", "blocked", effects: true);
        RecordResponse attempt = f.Api.Record(result.Envelope.GetProperty("subject").GetProperty("id").GetString()!)!;
        RecordResponse assignment = f.Api.Record(attempt.Envelope.GetProperty("subject").GetProperty("id").GetString()!)!;
        switch (resolution)
        {
            case "result": f.Add("acceptance", "accepted", "{}", result); break;
            case "work-order": f.Add("acceptance", "accepted", "{}", f.Api.Record("old-order")); break;
            default:
                RecordResponse nextAttempt = f.Add("attempt", "next-attempt", "{}", assignment);
                f.Add("result", "next-result", ResultBody("passed"), nextAttempt);
                break;
        }
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void AnotherAssignmentOrWrongAcceptancePinCannotClearAResult()
    {
        var f = new Fixture();
        RecordResponse result = f.Result("old");
        f.Result("other", "passed");
        f.Add("acceptance", "wrong-pin", "{}", result with { Hash = "sha256:wrong" });
        Assert.Equal(result.Id, Assert.Single(f.Attention()).Record);
    }

    [Fact]
    public void NewerProblemResultReplacesOldResultAttentionForTheSameAssignment()
    {
        var f = new Fixture();
        f.Result("old");
        RecordResponse attempt = f.Add("attempt", "new-attempt", "{}", f.Api.Record("old-assignment"));
        RecordResponse result = f.Add("result", "new-result", ResultBody("unknown"), attempt);
        Assert.Equal((result.Id, "unknown-check"), (Assert.Single(f.Attention()).Record, Assert.Single(f.Attention()).Kind));
    }

    [Theory]
    [InlineData("later-review")]
    [InlineData("acceptance")]
    [InlineData("supersession")]
    [InlineData("withdrawal")]
    [InlineData("work-order")]
    [InlineData("retired-order")]
    public void EachReviewResolutionClearsItsFinding(string resolution)
    {
        var f = new Fixture();
        RecordResponse subject = resolution is "work-order" or "retired-order" ? f.Result("subject", "passed") : f.Add("release", "subject", "{}");
        f.Add("review", "review", """{"outcome":"findings","findings":[{}]}""", subject);
        switch (resolution)
        {
            case "later-review": f.Add("review", "later-review", """{"outcome":"pass"}""", subject); break;
            case "acceptance": f.Add("acceptance", "acceptance", "{}", subject); break;
            case "supersession": f.Revise(subject, "revision", "{}"); break;
            case "withdrawal": f.Replace(subject with { Envelope = JsonSerializer.SerializeToElement(new { at = "2026-10-13T12:00:01Z", body = new { status = "withdrawn" } }) }); break;
            case "work-order": f.Add("acceptance", "acceptance", "{}", f.Api.Record("subject-order")); break;
            case "retired-order": f.OrderStates.Add(new { id = "subject-order", status = "retired" }); break;
        }
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void ANewerReviewWithFindingsBecomesTheOnlyAttentionForThatSubject()
    {
        var f = new Fixture();
        RecordResponse subject = f.Add("resource", "subject", "{}");
        f.Add("review", "old", """{"outcome":"fail","findings":[{}]}""", subject);
        f.Add("review", "latest", """{"outcome":"findings","findings":[{}]}""", subject);
        RecordResponse other = f.Add("resource", "other", "{}");
        f.Add("review", "unrelated-pass", """{"outcome":"pass"}""", other);
        Assert.Equal("latest", Assert.Single(f.Attention()).Record);
    }

    [Fact]
    public void SupersededOperationIsClearedOnlyByItsOwnSettledRevision()
    {
        var f = new Fixture();
        RecordResponse operation = f.Add("operation", "operation", """{"status":"uncertain"}""");
        f.Add("operation", "other", """{"status":"observed-success"}""");
        Assert.Equal(operation.Id, Assert.Single(f.Attention()).Record);
        f.Revise(operation, "settled", """{"status":"observed-success"}""");
        Assert.Empty(f.Attention());
    }

    [Theory]
    [InlineData("after", "planned", true, null)]
    [InlineData("after", "active", true, null)]
    [InlineData("after", "submitted", true, null)]
    [InlineData("after", "accepted", false, null)]
    [InlineData("after", "paused", true, "blocker")]
    [InlineData("after", "retired", true, "blocker")]
    [InlineData("after", "withdrawn", true, "blocker")]
    [InlineData("after", "failed", true, "blocker")]
    [InlineData("blockedBy", "open", true, "blocker")]
    [InlineData("blockedBy", "planned", true, "blocker")]
    [InlineData("blockedBy", "answered", false, null)]
    [InlineData("after", "absent", true, "unknown-check")]
    [InlineData("after", "new-state", true, "unknown-check")]
    public void PlanningAndActualBlockersStayDistinct(string relation, string state, bool holds, string? kind)
    {
        var f = new Fixture();
        f.Hold("work", relation, state, holds);
        if (kind is null) { Assert.Empty(f.Attention()); }
        else
        {
            CommunicationAttention item = Assert.Single(f.Attention());
            Assert.Equal(kind, item.Kind);
            Assert.Equal("work", item.Record);
            Assert.Equal(kind == "blocker" ? "blocked" : "unknown", item.State);
        }
    }

    [Theory]
    [InlineData("acceptance")]
    [InlineData("attempt")]
    [InlineData("assignment")]
    public void UnavailableResolutionEvidenceKeepsTheSpecificItemUnknown(string unavailable)
    {
        var f = new Fixture();
        RecordResponse result = f.Result("open");
        f.UnavailableType = unavailable;
        CommunicationAttention item = Assert.Single(f.Attention());
        Assert.Equal((result.Id, "unknown-check", "unknown"), (item.Record, item.Kind, item.State));
        Assert.Equal(result.Hash, item.Hash);
    }

    [Fact]
    public void MissingOrMismatchedSubjectKeepsItsSourceVisibleAsUnknown()
    {
        var f = new Fixture();
        RecordResponse result = f.Result("open");
        f.Replace(f.Api.Record("open-attempt")! with { Hash = "sha256:unexpected" });
        Assert.Equal((result.Id, "unknown"), (Assert.Single(f.Attention()).Record, Assert.Single(f.Attention()).State));
        f.Records.RemoveAll(r => r.Id == "open-attempt");
        Assert.Equal("unknown-check", Assert.Single(f.Attention()).Kind);
    }

    [Fact]
    public void MissingSourcesCannotSuppressMandatoryAttention()
    {
        var f = new Fixture { Unavailable = true };
        IReadOnlyList<CommunicationAttention> attention = f.Attention();
        Assert.NotEmpty(attention);
        Assert.All(attention, item => Assert.Equal("unknown-check", item.Kind));
        Assert.True(CommunicationPresentation.ShouldDeliver("none", "progress", attention));
    }

    [Fact]
    public void EstateShapedHistoryLeavesOnlyTheGenuinelyOpenItemsAndStaysCompact()
    {
        var f = new Fixture();
        RecordResponse repository = f.Add("resource", "repository", "{}");
        for (int i = 0; i < 49; i++)
        {
            RecordResponse result = f.Result("history-" + i, i < 28 ? "failed" : "not-performed", effects: i == 0);
            if (i < 44) { f.Add("review", "historical-review-" + i, """{"outcome":"findings","findings":[{}]}""", result); }
            f.Add("acceptance", "accepted-order-" + i, "{}", f.Api.Record("history-" + i + "-order"));
        }
        for (int i = 0; i < 22; i++) { f.Hold("planned-" + i, "after", "planned"); }
        for (int i = 0; i < 500; i++) { f.Add("verification", "failed-run-" + i, Verification("failed"), repository); }
        f.Add("verification", "passing-run", Verification("passed"), repository);
        f.Result("open", "unknown");
        f.Hold("blocked", "after", "paused");
        f.Add("review", "open-review", """{"outcome":"findings","findings":[{}]}""", repository);
        f.Add("operation", "open-effect", """{"status":"uncertain"}""");
        IReadOnlyList<CommunicationAttention> attention = f.Attention();
        Assert.Equal(new[] { ("unknown-check", "open-result"), ("required-decision", "open-review"), ("uncertain-effect", "open-effect"), ("blocker", "blocked") }, attention.Select(a => (a.Kind, a.Record!)));
        Assert.True(Encoding.UTF8.GetByteCount(CommunicationPresentation.RenderAttention(attention)) < 2500);
        string json = JsonSerializer.Serialize(attention, ProtocolJson.Options);
        Assert.All(attention, a => Assert.Contains(a.Hash!, json, StringComparison.Ordinal));
        Assert.DoesNotContain("Full", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("blocked", "blocker")]
    [InlineData("budget-limited", "exhausted-budget")]
    public void GoalAttentionIsCompactAndResolvesWithItsCurrentState(string state, string kind)
    {
        var f = new Fixture();
        RecordResponse goal = f.Add("goal", "goal", "{}");
        f.GoalRows.Add(new(goal.Id, goal.Hash, "Goal", state, "person", null));
        CommunicationAttention item = Assert.Single(f.Attention());
        Assert.Equal((kind, state, goal.Hash), (item.Kind, item.State, item.Hash));
        f.GoalRows[0] = f.GoalRows[0] with { Status = "active" };
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void AskAndRunAndBudgetGoldenItemsHaveReferencesWithoutTheirPayloads()
    {
        var f = new Fixture();
        RecordResponse ask = f.Add("ask", "ask", """{"state":"open","question":"Full question and alternatives."}""");
        f.AskPrincipals.Add(new { waiting = 1, queued = System.Array.Empty<object>(), delivered = new[] { new { id = ask.Id, revision = ask.Id, hash = ask.Hash, state = "open" } } });
        RecordResponse operation = f.Add("operation", "run", """{"status":"known-failure"}""");
        RecordResponse goal = f.Add("goal", "goal", "{}");
        f.RunRows.Add(new(operation.Id, "identity", "generic", null, null, goal.Id, null, "known-failure", JsonSerializer.Deserialize<JsonElement>("{}"), null, JsonSerializer.Deserialize<JsonElement>("{}"), null, null, false, true,
            Signals: [new("incident", "critical", "now", "Full worker observation.")]));
        f.Ledgers.Add(new(goal.Id, "Goal", "active", [new("tokens", 100, 100, 0)], 0, 1, 1, ["Full budget fencing."]));
        IReadOnlyList<CommunicationAttention> attention = f.Attention();
        Assert.Equal(new[] { ("required-decision", "ask"), ("incident", "run"), ("exhausted-budget", "goal") }, attention.Select(a => (a.Kind, a.Record!)));
        Assert.All(attention, a => Assert.NotNull(a.Hash));
        Assert.Contains("1 worker signals", attention[1].Text, StringComparison.Ordinal);
        Assert.Equal("vericairn worker runs", attention[1].Command);
        Assert.DoesNotContain("Full", CommunicationPresentation.RenderAttention(attention), StringComparison.Ordinal);
        f.AskPrincipals.Clear();
        f.RunRows.Clear();
        f.Ledgers.Clear();
        f.Revise(operation, "settled-run", """{"status":"observed-success"}""");
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void AcceptanceOfAnEarlierWorkOrderRevisionDoesNotCloseTheCurrentWork()
    {
        var f = new Fixture();
        RecordResponse result = f.Result("old");
        RecordResponse order = f.Api.Record("old-order")!;
        f.Add("acceptance", "old-acceptance", "{}", order);
        f.Revise(order, "new-order", "{}");
        Assert.Equal(result.Id, Assert.Single(f.Attention()).Record);
        f.Add("acceptance", "new-acceptance", "{}", f.Api.Record("new-order"));
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void ALaterResultIsSufficientWhenTheAcceptanceSourceIsUnavailable()
    {
        var f = new Fixture();
        f.Result("old");
        RecordResponse attempt = f.Add("attempt", "next-attempt", "{}", f.Api.Record("old-assignment"));
        f.Add("result", "next-result", ResultBody("passed"), attempt);
        f.UnavailableType = "acceptance";
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void ExactAcceptanceIsSufficientEvenWhenTheAttemptCannotBeRead()
    {
        var f = new Fixture();
        RecordResponse result = f.Result("accepted");
        f.Add("acceptance", "acceptance", "{}", result);
        f.Records.RemoveAll(r => r.Type == "attempt");
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void AReadableLaterResultResolvesDespiteAnotherCandidatesUnavailableAttempt()
    {
        var f = new Fixture();
        f.Result("old");
        RecordResponse unrelated = f.Result("unrelated", "passed");
        f.Records.RemoveAll(r => r.Id == unrelated.Envelope.GetProperty("subject").GetProperty("id").GetString());
        RecordResponse attempt = f.Add("attempt", "next-attempt", "{}", f.Api.Record("old-assignment"));
        f.Add("result", "next-result", ResultBody("passed"), attempt);
        Assert.Empty(f.Attention());
    }

    [Fact]
    public void AReadableMatchingPassResolvesDespiteAnotherCandidatesUnavailableCheckIdentity()
    {
        var f = new Fixture();
        RecordResponse subject = f.Add("resource", "repository", "{}");
        f.Add("verification", "failed", Verification("failed"), subject);
        f.Add("verification", "incomplete-pass", """{"outcome":"passed"}""", subject);
        f.Add("verification", "matching-pass", Verification("passed"), subject);
        Assert.Empty(f.Attention());
    }

    [Theory]
    [InlineData("result")]
    [InlineData("attempt")]
    [InlineData("assignment")]
    public void MissingAssignmentChainPinsCannotResolveAttention(string type)
    {
        var f = new Fixture();
        RecordResponse result = f.Result("open");
        RecordResponse record = f.Records.Single(r => r.Type == type);
        JsonElement subject = record.Envelope.GetProperty("subject");
        f.Replace(record with { Envelope = JsonSerializer.SerializeToElement(new
        {
            at = record.Envelope.GetProperty("at").GetString(),
            body = record.Envelope.GetProperty("body"),
            subject = new { type = subject.GetProperty("type").GetString(), id = subject.GetProperty("id").GetString() },
        }) });
        Assert.Equal((result.Id, "unknown"), (Assert.Single(f.Attention()).Record, Assert.Single(f.Attention()).State));
    }

    [Fact]
    public void ProvenanceCorrectionReviewsHaveNoImplicitClosureFromOtherDocuments()
    {
        var f = new Fixture();
        RecordResponse document = f.Add("document-review", "historical-document-review", "{}");
        RecordResponse review = f.Add("review", "correction-review", """{"outcome":"findings","summary":"The historical label was corrected in prose.","findings":[{}]}""", document);
        f.Add("document-review", "new-document-review", "{}");
        Assert.Equal(review.Id, Assert.Single(f.Attention()).Record);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("null")]
    public void MalformedCanonicalAttentionFallsBackWithoutPretendingNothingNeedsAttention(string malformed)
    {
        var f = new Fixture { ProgressOverride = JsonSerializer.Deserialize<JsonElement>("{\"attention\":" + malformed + "}") };
        IReadOnlyList<CommunicationAttention> attention = f.Attention();
        Assert.Contains(attention, a => a.Kind == "unknown-check" && a.State == "unknown");
    }

    private static string Verification(string outcome) => $$"""{"outcome":"{{outcome}}","method":"github-actions","environment":{"workflow":"ci.yml","runner":"hosted"},"commit":"old-commit","summary":"Full observation."}""";
    private static string ResultBody(string check, string outcome = "partial", bool effects = false) => $$"""{"outcome":"{{outcome}}","checks":[{"outcome":"{{check}}","summary":"Full check."}],"unresolvedEffects":{{(effects ? "[\"Full unresolved effect.\"]" : "[]")}}}""";

    public class AttentionApi : DispatchProxy
    {
        public Fixture Source { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Source.Unavailable) { throw new IOException("Unavailable."); }
            return targetMethod?.Name switch
            {
                nameof(IEstateApi.Records) when (string)args![0]! == Source.UnavailableType => throw new IOException("Unavailable."),
                nameof(IEstateApi.Records) => Source.Records.Where(r => r.Type == (string)args![0]! && r.Current).ToList(),
                nameof(IEstateApi.Record) => Source.Records.SingleOrDefault(r => r.Id == (string)args![0]!),
                nameof(IEstateApi.Asks) => new AsksResponse("", JsonSerializer.SerializeToElement(new { principals = Source.AskPrincipals })),
                nameof(IEstateApi.Progress) => new ProgressResponse("", Source.ProgressOverride ?? JsonSerializer.SerializeToElement(new { eligible = new { held = Source.Held }, workOrders = new { states = new[] { new { workOrders = Source.OrderStates } } } }), false),
                nameof(IEstateApi.Goals) => Source.GoalRows,
                nameof(IEstateApi.Runs) => new RunsResponse(Source.RunRows, Source.Ledgers),
                _ => throw new NotSupportedException(),
            };
        }
    }

    public sealed class Fixture
    {
        public Fixture()
        {
            Api = DispatchProxy.Create<IEstateApi, AttentionApi>();
            ((AttentionApi)Api).Source = this;
        }
        public IEstateApi Api { get; }
        public List<RecordResponse> Records { get; } = [];
        public List<object> Held { get; } = [];
        public List<object> AskPrincipals { get; } = [];
        public List<GoalRow> GoalRows { get; } = [];
        public List<RunRow> RunRows { get; } = [];
        public List<LedgerRow> Ledgers { get; } = [];
        public JsonElement? ProgressOverride { get; set; }
        public List<object> OrderStates { get; } = [];
        public bool Unavailable { get; set; }
        public string? UnavailableType { get; set; }
        public IReadOnlyList<CommunicationAttention> Attention() => CommunicationPresentation.Attention(Api);
        public RecordResponse Add(string type, string id, string body, RecordResponse? subject = null, string? at = null, string? supersedes = null)
        {
            var record = new RecordResponse("record.json", id, type, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))), true,
                JsonSerializer.SerializeToElement(new { at = at ?? new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero).AddSeconds(Records.Count + 1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), subject = subject is null ? null : new { type = subject.Type, id = subject.Id, hash = subject.Hash }, body = JsonSerializer.Deserialize<JsonElement>(body), supersedes }));
            Records.Add(record);
            return record;
        }
        public RecordResponse Result(string prefix, string check = "failed", string outcome = "partial", bool effects = false)
        {
            RecordResponse order = Add("work-order", prefix + "-order", "{}");
            RecordResponse assignment = Add("assignment", prefix + "-assignment", "{}", order);
            RecordResponse attempt = Add("attempt", prefix + "-attempt", "{}", assignment);
            return Add("result", prefix + "-result", ResultBody(check, outcome, effects), attempt);
        }
        public void Replace(RecordResponse record) => Records[Records.FindIndex(r => r.Id == record.Id)] = record;
        public void Revise(RecordResponse record, string id, string body)
        {
            Replace(record with { Current = false });
            Add(record.Type, id, body, supersedes: record.Id);
        }
        public void Hold(string id, string relation, string state, bool holds = true)
        {
            Add("work-order", id, "{}");
            Held.Add(new { id, relations = new[] { new { relation, type = relation == "blockedBy" && state is "open" or "answered" ? "ask" : "work-order", id = "predecessor", holder = "predecessor", state, holds } } });
        }
    }
}
