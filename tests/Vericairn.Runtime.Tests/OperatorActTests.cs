// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// OPS-24 (0.9.4): vericairn operator act --ready. Every operator item whose command is ready is listed with what its signature records;
/// the operator leaves any waiting or dismisses a request with a reason, and the rest sign in one act whose presence session's announced
/// list names each record. An item changing between listing and signing refuses the act; an unselected item is not signed; an agent
/// cannot perform it; the announced list equals what is signed.
/// </summary>
public sealed class OperatorActTests
{
    private static readonly string[] RequestScope = ["request"];
    private static readonly string[] RequestDeliverables = ["result"];

    [Fact]
    public void TheListingShowsEveryReadyItemWithWhatItsSignatureRecords()
    {
        using var f = new OperatorCloseFixture();
        var step = f.Add("STEP-1");
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        RecordEnvelope request = Request(f, "action");
        Request(f, "publication");
        var milestone = f.Add("MS-1", kind: "milestone", reviewOutcome: "fail");

        OperatorActListResponse listed = f.Api.OperatorActItems(f.Estate.OperatorId);

        Assert.True(listed.Ok, listed.Problem?.Message);
        Assert.Equal(f.Estate.OperatorId, listed.By);
        Assert.Equal(["close:STEP-1", "answer:" + ask.Id, "milestone-result-acceptance:" + milestone.Result.Id, "request:" + request.Id], listed.Items.Select(i => i.Id));
        OperatorActItem close = listed.Items[0];
        Assert.Equal(("acceptance", "result", step.Result.Id, step.Result.ComputedHash), (close.RecordType, close.SubjectType, close.Subject.Id, close.Subject.Hash));
        Assert.Equal([step.Review.Id], close.Basis.Select(b => b.Id));
        Assert.StartsWith($"an acceptance of result {step.Result.Id} of STEP-1 at its exact hash", close.Records, StringComparison.Ordinal);
        OperatorActItem answer = listed.Items[1];
        Assert.Equal(("decision", "Ship it now.", OperatorAnswers.RecommendationHash(ask)), (answer.RecordType, answer.Recommendation, answer.RecommendationHash));
        Assert.Contains("with its recommendation", answer.Records, StringComparison.Ordinal);
        OperatorActItem close2 = listed.Items[3];
        Assert.True(close2.Dismissable);
        Assert.Equal("operator-request-close", close2.RecordType);
        Assert.All(listed.Items.Take(3), i => Assert.False(i.Dismissable));
        // The root operator's when none is named; an agent has no act.
        Assert.Equal(listed.Items.Select(i => i.Id), f.Api.OperatorActItems().Items.Select(i => i.Id));
        Assert.Equal("denied", f.Api.OperatorActItems(f.Estate.BuilderId).Problem?.Code);
    }

    [Fact]
    public void TheChosenItemsSignTheUnselectedWaitAndADismissedRequestCarriesItsReason()
    {
        using var f = new OperatorCloseFixture();
        var step = f.Add("STEP-1");
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        RecordEnvelope waiting = OperatorAnswerTests.Ask(f, "Wait on it?", "Wait.");
        RecordEnvelope request = Request(f, "action");
        OperatorActListResponse listed = f.Api.OperatorActItems(f.Estate.OperatorId);
        OperatorActChoice[] choices = [.. listed.Items.Select(i => i.Id == "answer:" + waiting.Id ? i.Choose("wait") : i.Kind == "request" ? i.Choose("dismiss", "No longer needed.") : i.Choose())];

        OperatorActResponse acted = f.Api.OperatorAct(new(f.Estate.OperatorId, choices, All: true), TestContext.Current.CancellationToken);

        Assert.True(acted.Ok, acted.Problem?.Message);
        Assert.NotNull(acted.Act);
        Assert.Equal(["close:STEP-1", "answer:" + ask.Id, "request:" + request.Id], acted.Written.Select(s => s.Item));
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings.Select(x => x.Message)));
        RecordEnvelope acceptance = model.ById[acted.Written[0].Record.Id];
        Assert.Equal(step.Result.Reference, acceptance.Subject);
        Assert.Equal(OperatorActs.AcceptanceStatement("close"), EstateModel.BodyString(acceptance, "statement"));
        RecordEnvelope decision = model.ById[acted.Written[1].Record.Id];
        Assert.Equal(acted.Act, OperatorAnswers.Of(decision)!.Id);
        Assert.Equal(1, OperatorAnswers.Of(decision)!.Count);
        RecordEnvelope dismissed = model.ById[acted.Written[2].Record.Id];
        Assert.Equal(("dismissed", "No longer needed."), (EstateModel.BodyString(dismissed, "outcome"), EstateModel.BodyString(dismissed, "reason")));
        Assert.All(acted.Written, s => Assert.Equal(f.Device, Assert.Single(model.ById[s.Record.Id].Signatures).Device));
        // The item left waiting signed nothing and still waits.
        Assert.Equal("open", Asks.State(model, waiting).State);
        Assert.Equal(["answer:" + waiting.Id], f.Api.OperatorActItems(f.Estate.OperatorId).Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("review")]
    [InlineData("ask")]
    [InlineData("request")]
    [InlineData("arrived")]
    public void AnItemChangedBetweenListingAndSigningRefusesTheWholeAct(string change)
    {
        using var f = new OperatorCloseFixture();
        var step = f.Add("STEP-1");
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        RecordEnvelope request = Request(f, "action");
        OperatorActChoice[] choices = [.. f.Api.OperatorActItems(f.Estate.OperatorId).Items.Select(i => i.Choose())];
        switch (change)
        {
            case "review": f.Review(step.Result, supersedes: step.Review.Id); break;
            case "ask": f.Estate.Add("ask", ask.By, ask.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), supersedes: ask.Id); break;
            case "request":
                JsonObject body = JsonNode.Parse(request.Body.GetRawText())!.AsObject();
                body["why"] = "A changed reason.";
                f.Estate.Add("operator-request", request.By, body.ToJsonString(), request.Subject, request.Id);
                break;
            default: OperatorAnswerTests.Ask(f, "Another?", "Another."); break;
        }

        int before = f.Estate.Load().Records.Count;

        OperatorActResponse refused = f.Api.OperatorAct(new(f.Estate.OperatorId, choices, All: change == "arrived"), TestContext.Current.CancellationToken);

        Assert.Equal("conflict", refused.Problem?.Code);
        Assert.Empty(refused.Written);
        Assert.Null(refused.Act);
        Assert.Equal(before, f.Estate.Load().Records.Count);
    }

    [Fact]
    public void AnItemChangedWhileTheActSignsRefusesItBeforeAnyRecordIsStored()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        RecordEnvelope ask = OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        OperatorActChoice[] choices = [.. f.Api.OperatorActItems(f.Estate.OperatorId).Items.Select(i => i.Choose())];
        bool changed = false;
        f.Api.BeforeSigningRecord = () =>
        {
            if (changed) return;
            changed = true;
            f.Estate.Add("ask", ask.By, ask.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), supersedes: ask.Id);
        };
        int before = f.Estate.Load().Records.Count;

        OperatorActResponse refused = f.Api.OperatorAct(new(f.Estate.OperatorId, choices), TestContext.Current.CancellationToken);

        Assert.True(changed);
        Assert.False(refused.Ok);
        Assert.Empty(refused.Written);
        Assert.Equal(before + 1, f.Estate.Load().Records.Count);
    }

    [Fact]
    public void AnAgentCannotPerformTheActAndOnlyARequestIsDismissed()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        OperatorActItem item = Assert.Single(f.Api.OperatorActItems(f.Estate.OperatorId).Items);

        OperatorActResponse agent = f.Api.OperatorAct(new(f.Estate.BuilderId, [item.Choose()]), TestContext.Current.CancellationToken);
        OperatorActResponse dismissal = f.Api.OperatorAct(new(f.Estate.OperatorId, [item.Choose("dismiss", "Not this one.")]), TestContext.Current.CancellationToken);
        OperatorActResponse reasonless = f.Api.OperatorAct(new(f.Estate.OperatorId, [item.Choose("dismiss")]), TestContext.Current.CancellationToken);

        Assert.Equal("denied", agent.Problem?.Code);
        Assert.Contains("only a request is dismissed", dismissal.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal("refused", reasonless.Problem?.Code);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void WithPresenceTheAnnouncedListNamesEachRecordSignedAndNothingElse()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        OperatorAnswerTests.Ask(f, "Ship it?", "Ship it now.");
        RecordEnvelope waiting = OperatorAnswerTests.Ask(f, "Wait on it?", "Wait.");
        Request(f, "action");
        (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(f);
        OperatorActChoice[] choices = [.. f.Api.OperatorActItems(f.Estate.OperatorId).Items.Select(i => i.Id == "answer:" + waiting.Id ? i.Choose("wait") : i.Choose())];

        OperatorActResponse acted = f.Api.OperatorAct(new(f.Estate.OperatorId, choices, All: true), TestContext.Current.CancellationToken);

        Assert.True(acted.Ok, acted.Problem?.Message);
        PresenceHelperStandIn helper = Assert.Single(helpers);
        Assert.Empty(custody.Reasons);
        PresenceSessionRules terms = PresenceSessionRules.ReadTerms(Assert.Single(helper.Terms));
        Assert.Equal("operator close", terms.Action);
        EstateModel model = f.Estate.Load();
        // The announced list names exactly the records signed and stored, by exact hash; the item left waiting is not among them.
        Assert.Equal(acted.Written.Select(s => model.ById[s.Record.Id].ComputedHash).Order(StringComparer.Ordinal), terms.Entries.Select(e => e.Hash!).Order(StringComparer.Ordinal));
        Assert.Equal(acted.Written.Select(s => s.Record.Hash).Order(StringComparer.Ordinal), helper.SignedHashes.Order(StringComparer.Ordinal));
        Assert.Equal(["acceptance", "decision", "operator-request-close"], terms.Entries.Select(e => e.Type));
        Assert.DoesNotContain(model.Current("decision"), d => OperatorAnswers.Of(d)?.Ask == waiting.Id);
    }

    [Fact]
    public void AnActOfMoreThanSixteenItemsAnnouncesAListForEachSixteenEachBeforeItsOwnCheck()
    {
        using var f = new OperatorCloseFixture();
        for (int i = 1; i <= 10; i++) f.Add($"STEP-{i:00}");
        for (int i = 1; i <= 8; i++) OperatorAnswerTests.Ask(f, $"Question {i}?", $"Take {i}.");
        (List<PresenceHelperStandIn> helpers, RecordingCustody custody) = Presence(f);

        OperatorActResponse acted = f.Api.OperatorAct(new(f.Estate.OperatorId, [.. f.Api.OperatorActItems(f.Estate.OperatorId).Items.Select(i => i.Choose())], All: true),
            TestContext.Current.CancellationToken);

        Assert.True(acted.Ok, acted.Problem?.Message);
        Assert.Equal(18, acted.Written.Count);
        Assert.Equal(2, helpers.Count);
        Assert.Equal(16, PresenceSessionRules.ReadTerms(helpers[0].Terms[0]).ExactCount);
        Assert.Equal(2, PresenceSessionRules.ReadTerms(helpers[1].Terms[0]).ExactCount);
        Assert.Empty(custody.Reasons);
        Assert.Equal(18, helpers.Sum(h => h.SignedHashes.Count));
    }

    private static (List<PresenceHelperStandIn> Helpers, RecordingCustody Custody) Presence(OperatorCloseFixture f)
    {
        var custody = new RecordingCustody(VericairnHome.Path());
        var helpers = new List<PresenceHelperStandIn>();
        f.Api.KeyStores = [custody];
        f.Api.PresenceRequired = device => device == f.Device;
        f.Api.HostsEmbedded = true;
        f.Api.PresenceHost = StandInSessions.Host(helpers);
        return (helpers, custody);
    }

    /// <summary>An open request put to the operator about a fresh work order.</summary>
    public static RecordEnvelope Request(OperatorCloseFixture f, string kind)
    {
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, JsonSerializer.Serialize(new
        {
            key = "REQ-" + Records.Identity.Ulid.NewUlid().Value[^6..], kind = "ordinary", title = "Requested work", intent = "Serve a request.", scope = RequestScope,
            exitQuestion = "Done?", inputs = Array.Empty<string>(), deliverables = RequestDeliverables,
        }));
        JsonObject body = Records.Tests.OperatorRequestTests.Body(f.Estate);
        body["kind"] = kind;
        return f.Estate.Add("operator-request", f.Estate.BuilderId, body.ToJsonString(), order.Reference);
    }
}
