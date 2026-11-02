// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Avalonia.Controls;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class RemoteDecisionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AnAnswerPreservesTheExactWordsAskRevisionAndPhoneSignature()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            Assert.Contains(ask.ComputedHash, model.DecisionIdentity, StringComparison.Ordinal);
            Assert.Contains(estate.Load().Current("work-order").Single().ComputedHash, model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains("by: " + ask.By, model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains("at: " + ask.At, model.DecisionMaterial, StringComparison.Ordinal);
            Assert.StartsWith("Recommendation: Keep the limit.", model.DecisionQuestion, StringComparison.Ordinal);
            Assert.Equal("Keep the limit.", model.DecisionOptions[0]);
            Assert.Null(model.SelectedDecisionOption);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            model.SelectedDecisionOption = model.DecisionOptions[0];
            Assert.Equal("Keep the limit.", model.DecisionStatement);
            const string words = "  Keep the limit.\nDo not widen it.  ";
            RemoteDecisionSeed.Ready(model, words);
            Assert.True(model.SubmitDecisionCommand.CanExecute(null), model.DecisionEligibility);
            await model.SubmitDecisionCommand.ExecuteAsync();
            RecordEnvelope answer = Assert.Single(estate.Load().Current("decision"));
            Assert.Equal(words, answer.Body.GetProperty("statement").GetString());
            Assert.Equal(words, Assert.Single(answer.Body.GetProperty("effects").EnumerateArray()).GetString());
            Assert.Equal(estate.OperatorId, answer.By);
            Assert.Equal(ask.Id, Assert.Single(answer.Body.GetProperty("answers").EnumerateArray()).GetString());
            Assert.Equal(ask.Id, answer.Body.GetProperty("answer").GetProperty("ask").GetString());
            Assert.Equal(ask.ComputedHash, answer.Body.GetProperty("answer").GetProperty("hash").GetString());
            Assert.Equal(answer.Body.GetProperty("decidedAt").GetString(), answer.Body.GetProperty("answer").GetProperty("at").GetString());
            Assert.Equal(model.Selected!.Connection.PresenceDevice, Assert.Single(answer.Signatures).Device);
            Assert.True(RecordSigning.Verify(model.Selected.Connection.PresenceDevicePublicKey!, RecordSigning.Digest(answer.ComputedHash), answer.Signatures[0].Signature));
            Assert.Equal(connections.Basis!.Head, answer.Body.GetProperty("changeSet").GetProperty("head").GetString());
            Assert.Equal("completed", model.Decision!.State);
            Assert.Contains("no publication", model.DecisionMessage, StringComparison.Ordinal);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            Assert.Equal(0, estate.Load().Findings.Errors);
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AChangedAskBeforePreparationOrImmediatelyBeforeSigningLeavesTheOriginalDraft(bool duringPrepare)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        RecordEnvelope? revised = null;
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            void Change() => revised = estate.Add("ask", estate.BuilderId, ask.Body.GetRawText().Replace("\"open\"", "\"notified\"", StringComparison.Ordinal), ask.Subject, ask.Id);
            if (duringPrepare) connections.BeforeReview = Change; else Change();
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.SignedWrites);
            Assert.Empty(estate.Load().Current("decision"));
            Assert.Equal("draft", model.Decision!.State);
            Assert.Equal("Keep the limit.", model.Decision.Statement);
            Assert.Equal(ask.Id, model.Decision.Original.Subject.Id);
            Assert.Equal(ask.ComputedHash, model.Decision.Original.Subject.Hash);
            Assert.Equal(revised!.Id, model.Decision.Review.Subject.Id);
            Assert.Contains(revised.ComputedHash, model.DecisionIdentity, StringComparison.Ordinal);
            Assert.Contains("changed", model.DecisionMessage, StringComparison.Ordinal);
            Assert.False(model.MaterialReviewed);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            return true;
        });
    }

    [Fact]
    public async Task AChangedNamedSubjectAndANewReviewInvalidateTheMaterialConfirmation()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            RecordEnvelope order = estate.Load().Current("work-order").Single();
            RecordEnvelope changed = estate.Add("work-order", estate.OperatorId, order.Body.GetRawText().Replace("Exercise Remote's home.", "Inspect a changed purpose.", StringComparison.Ordinal), supersedes: order.Id);
            estate.Add("review", estate.OperatorId, """{"independence":"operator","outcome":"findings","summary":"Read the new purpose.","findings":[{"severity":"warning","summary":"The purpose changed."}]}""", ask.Reference);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Equal("draft", model.Decision!.State);
            Assert.Contains(changed.ComputedHash, model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains("The purpose changed.", model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task OfflineInputSurvivesRestartAndReconnectNeverSubmitsIt()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            connections.Checks.Enqueue((ConnectionStates.Cached, "Offline."));
            await model.RefreshCommand.ExecuteAsync();
            int calls = connections.ApiCalls;
            model.DecisionStatement = "Wait until the evidence is complete.";
            model.MaterialReviewed = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(calls, connections.ApiCalls);
            Assert.Equal(0, connections.PreparedCalls);
            var reopened = new RemoteViewModel(connections, new InspectionClock());
            reopened.SelectedDraft = Assert.Single(reopened.DecisionDrafts);
            await reopened.OpenDraftCommand.ExecuteAsync();
            Assert.Equal(model.DecisionStatement, reopened.DecisionStatement);
            Assert.Equal(ask.ComputedHash, reopened.Decision!.Original.Subject.Hash);
            Assert.False(reopened.MaterialReviewed);
            Assert.Equal(calls, connections.ApiCalls);
            await reopened.RefreshCommand.ExecuteAsync();
            Assert.Equal(ConnectionStates.Current, reopened.ConnectionState);
            Assert.False(reopened.MaterialReviewed);
            Assert.Contains("Review your draft", reopened.DecisionMessage, StringComparison.Ordinal);
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("decision"));
            return true;
        });
    }

    [Fact]
    public async Task UnavailableEvidenceAndANonOperatorAreDirectedToTheDesktop()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate, evidence: """[{"summary":"Evidence not held here.","hash":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]""");
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, result);
            Assert.NotNull(model.Decision);
            Assert.Contains("Evidence not held here.", model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            model.DecisionStatement = "Accept.";
            model.MaterialReviewed = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);

            RecordEnvelope ask = RemoteDecisionSeed.Ask(estate, to: estate.BuilderId);
            RemoteDecisionReview review = await new RemoteDecisionReader(connections.Service).ReadAsync(ask.Id, estate.BuilderId, "2026-11-01T12:00:00Z");
            Assert.False(review.Eligible);
            Assert.Contains(review.Reasons, r => r.Contains("operator", StringComparison.Ordinal));
            return true;
        });
    }

    [Fact]
    public async Task AcceptingAResultUsesOnlyItsExactReviewsAndNeverClaimsPublication()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate);
        RecordEnvelope review = estate.Add("review", estate.OperatorId, """{"independence":"operator","outcome":"pass","summary":"Read every field and evidence summary.","findings":[]}""", result.Reference);
        RecordEnvelope unrelated = estate.Add("review", estate.BuilderId, """{"independence":"independent-agent","outcome":"pass","summary":"Different subject.","findings":[]}""", estate.Load().Current("work-order").Single().Reference);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, result);
            Assert.Contains("Read every field", model.DecisionMaterial, StringComparison.Ordinal);
            Assert.DoesNotContain(unrelated.ComputedHash, model.DecisionMaterial, StringComparison.Ordinal);
            RemoteDecisionSeed.Ready(model, "I accept this exact result and its stated limitations.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            RecordEnvelope accepted = Assert.Single(estate.Load().Current("acceptance"));
            Assert.Equal(result.Reference, accepted.Subject);
            Assert.Equal(review.Id, Assert.Single(accepted.Body.GetProperty("basis").EnumerateArray()).GetString());
            Assert.False(accepted.Body.GetProperty("self").GetBoolean());
            Assert.Equal(model.Selected!.Connection.PresenceDevice, Assert.Single(accepted.Signatures).Device);
            Assert.Equal("completed", model.Decision!.State);
            Assert.Contains("no publication", model.DecisionMessage, StringComparison.Ordinal);
            Assert.Equal(0, estate.Load().Findings.Errors);
            return true;
        });
    }

    [Fact]
    public async Task RepeatedTapsAndALostResponseKeepOneOriginalAcrossRestart()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var connections = new FakeConnections(estate.Root)
        {
            EnableSignedWrites = true, LoseSignedResponse = true,
            WaitBeforeReview = async () => { entered.SetResult(); await resume.Task; },
        };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            Task first = model.SubmitDecisionCommand.ExecuteAsync();
            await entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            await model.SubmitDecisionCommand.ExecuteAsync();
            resume.SetResult();
            await first;
            Assert.Equal(1, connections.PreparedCalls);
            Assert.Equal(1, connections.SignedWrites);
            Assert.Equal("uncertain", model.Decision!.State);
            string original = model.Decision.Prepared!.Id;
            await model.SubmitDecisionCommand.ExecuteAsync();
            var reopened = new RemoteViewModel(connections, new InspectionClock());
            reopened.SelectedDraft = Assert.Single(reopened.DecisionDrafts);
            await reopened.OpenDraftCommand.ExecuteAsync();
            Assert.Equal(original, reopened.Decision!.Prepared!.Id);
            await reopened.RefreshCommand.ExecuteAsync();
            await reopened.SubmitDecisionCommand.ExecuteAsync();
            await reopened.ReconcileDecisionCommand.ExecuteAsync();
            Assert.Equal("completed", reopened.Decision.State);
            Assert.Single(estate.Load().Current("decision"));
            Assert.Equal(1, connections.SignedWrites);
            return true;
        });
    }

    [Fact]
    public async Task AReceiptContradictionAndARecoveryProblemBothPreventResubmission()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, HideWrittenRecord = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, result);
            RemoteDecisionSeed.Ready(model, "Accept the result.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.NotNull(model.Decision!.Receipt);
            Assert.Equal("uncertain", model.Decision.State);
            Assert.Contains("contradicts", model.DecisionMessage, StringComparison.Ordinal);
            await model.SubmitDecisionCommand.ExecuteAsync();
            connections.HideWrittenRecord = false;
            await model.ReconcileDecisionCommand.ExecuteAsync();
            Assert.Equal("completed", model.Decision.State);
            Assert.Equal(1, connections.SignedWrites);

            RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
            await model.CloseDecisionCommand.ExecuteAsync();
            await model.RefreshCommand.ExecuteAsync();
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            connections.WriteProblem = new("reconcile-required", "The companion write needs recovery.", []);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("uncertain", model.Decision!.State);
            Assert.NotNull(model.Decision.Receipt);
            Assert.Contains("companion write needs recovery", model.DecisionMessage, StringComparison.Ordinal);
            await model.ReconcileDecisionCommand.ExecuteAsync();
            Assert.Equal("uncertain", model.Decision.State);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            return true;
        });
    }

    [Fact]
    public async Task AResponseWithoutAReceiptOrRefusalLeavesTheOriginalUncertainUntilRead()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, EmptySignedResponse = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("uncertain", model.Decision!.State);
            Assert.True(model.Decision.Attempted);
            Assert.Null(model.Decision.Receipt);
            Assert.Contains("neither a receipt nor a refusal", model.DecisionMessage, StringComparison.Ordinal);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(1, connections.SignedWrites);
            await model.ReconcileDecisionCommand.ExecuteAsync();
            Assert.Equal("completed", model.Decision.State);
            Assert.Equal(1, connections.PreparedCalls);
            Assert.Single(estate.Load().Current("decision"));
            return true;
        });
    }

    [Fact]
    public async Task StorageFailureAndAnAlteredPreparedRecordSignNothing()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            connections.CannotKeepDecision = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Contains("could not be saved", model.DecisionStorageProblem, StringComparison.Ordinal);
            connections.CannotKeepDecision = false;
            await model.RefreshDecisionCommand.ExecuteAsync();
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            connections.ChangePrepared = p => p with { Hash = "sha256:" + new string('b', 64) };
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.SignedWrites);
            Assert.Equal("draft", model.Decision!.State);
            Assert.Empty(estate.Load().Current("decision"));
            return true;
        });
    }

    [Fact]
    public async Task SwitchingTheEstateDuringPreparationSignsNeitherTarget()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, WaitBeforeReview = async () => { entered.SetResult(); await resume.Task; } };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            Task submit = model.SubmitDecisionCommand.ExecuteAsync();
            await entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            model.Selected = null;
            resume.SetResult();
            await submit;
            Assert.Equal(0, connections.SignedWrites);
            Assert.Null(model.Decision);
            Assert.Empty(estate.Load().Current("decision"));
            return true;
        });
    }

    [Fact]
    public async Task DeferAndRequestChangesNeverInventADecisionAndTheViewExposesTheCompleteReview()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            await RemoteDecisionSeed.Open(model, ask);
            Assert.True(view.FindControl<ScrollViewer>("DecisionScroll")!.IsVisible);
            Assert.Equal(model.DecisionMaterial, view.FindControl<TextBlock>("DecisionMaterial")!.Text);
            Assert.False(view.FindControl<Button>("SignDecision")!.IsEffectivelyEnabled);
            model.DecisionStatement = "Please supply the missing comparison.";
            await model.RequestDecisionChangesCommand.ExecuteAsync();
            Assert.Contains("desktop", model.DecisionMessage, StringComparison.Ordinal);
            int before = estate.Load().Records.Count;
            await model.DeferDecisionCommand.ExecuteAsync();
            Assert.Equal(before, estate.Load().Records.Count);
            Assert.Equal("acknowledged", model.SelectedAttention!.AttentionState);
            Assert.Equal("Please supply the missing comparison.", model.DecisionStatement);
            Assert.Equal(0, connections.PreparedCalls);
            await model.AddEstateCommand.ExecuteAsync();
            Assert.False(view.FindControl<ScrollViewer>("DecisionScroll")!.IsVisible);
            await model.CancelPairingCommand.ExecuteAsync();
            Assert.True(view.FindControl<ScrollViewer>("DecisionScroll")!.IsVisible);
            Assert.Equal("Please supply the missing comparison.", model.DecisionStatement);
            window.Close();
            return true;
        });
    }
}

internal static class RemoteDecisionSeed
{
    private static readonly string[] Options = ["Change the limit.", "Keep the limit."];
    internal static RecordEnvelope Ask(TestEstate estate, string? to = null) => estate.Add("ask", estate.BuilderId, JsonSerializer.Serialize(new
    {
        to = to ?? estate.OperatorId, question = "Which limit should apply?", recommendation = "Keep the limit.",
        consequence = "The work waits for this answer.", options = Options, state = "open",
    }), estate.Load().Current("work-order").Single().Reference);

    internal static RecordEnvelope Result(TestEstate estate, string evidence = "[]")
    {
        RecordEnvelope assignment = RemoteInspectionSeed.Assign(estate);
        RecordEnvelope attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"widget branch work/phone","note":"Inspect the exact result."}""", assignment.Reference);
        return estate.Add("result", estate.BuilderId, """{"outcome":"completed","summary":"The result is ready to inspect.","outputs":[],"evidence":EVIDENCE,"checks":[{"name":"behavior","outcome":"passed","summary":"The intended behavior was observed."}],"limitations":["No downstream effect is claimed."],"unresolvedEffects":[],"nextStep":"Review the result."}""".Replace("EVIDENCE", evidence, StringComparison.Ordinal), attempt.Reference);
    }

    internal static async Task Open(RemoteViewModel model, RecordEnvelope subject)
    {
        model.SelectedAttention = model.Attention.First(a => a.SubjectId == subject.Id);
        await model.OpenAttentionCommand.ExecuteAsync();
        Assert.NotNull(model.Decision);
    }

    internal static void Ready(RemoteViewModel model, string statement)
    {
        model.DecisionStatement = statement;
        if (model.IsAsk) model.DecisionStake = "low";
        model.MaterialReviewed = true;
    }
}
