// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// A decision on the phone is signed only by its presence key, which asks for its owner's presence; the request key, which signs
/// requests unattended, never signs one. A phone without a presence key signs nothing and is told how to add one.
/// </summary>
public sealed class RemotePresenceKeyTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task OnlyThePresenceKeySignsAnAcceptanceAndTheRequestKeyNever()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate);
        estate.Add("review", estate.OperatorId, """{"independence":"operator","outcome":"pass","summary":"Read the exact result.","findings":[]}""", result.Reference);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            Assert.True(model.HasPresenceKey);
            int before = connections.PresenceKeys.Signatures;
            await RemoteDecisionSeed.Open(model, result);
            Assert.Contains("Presence key " + model.Selected!.Connection.PresenceDevice, model.DecisionTarget, StringComparison.Ordinal);
            Assert.False(model.NeedsPresenceKey);
            RemoteDecisionSeed.Ready(model, "I accept this exact result.");
            await model.SubmitDecisionCommand.ExecuteAsync();

            RecordEnvelope accepted = Assert.Single(estate.Load().Current("acceptance"));
            RecordSignature signature = Assert.Single(accepted.Signatures);
            Assert.Equal(model.Selected.Connection.PresenceDevice, signature.Device);
            Assert.NotEqual(model.Selected.Connection.Device, signature.Device);
            Assert.True(RecordSigning.Verify(model.Selected.Connection.PresenceDevicePublicKey!, RecordSigning.Digest(accepted.ComputedHash), signature.Signature));
            Assert.False(RecordSigning.Verify(model.Selected.Connection.DevicePublicKey, RecordSigning.Digest(accepted.ComputedHash), signature.Signature));
            Assert.Equal(before + 1, connections.PresenceKeys.Signatures);
            Assert.Equal("completed", model.Decision!.State);
            Assert.Contains("presence-key signature", model.DecisionMessage, StringComparison.Ordinal);
            Assert.Equal(0, estate.Load().Findings.Errors);
            return true;
        });
    }

    [Fact]
    public async Task APhoneWithoutAPresenceKeySignsNothingUntilANewCodeAddsOne()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true, WithoutPresenceKey = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            Assert.Null(model.Selected!.Connection.PresenceDevice);
            Assert.False(model.HasPresenceKey);
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            Assert.True(model.NeedsPresenceKey);
            Assert.Contains("no presence key", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Contains("pairing code", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Contains("No presence key on this phone", model.DecisionTarget, StringComparison.Ordinal);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("decision"));

            // The route the phone offers: a new code for the same person adds the presence key, which signs nothing by being added.
            Assert.False(model.AddPresenceKeyCommand.CanExecute(null));
            model.PresenceCode = "ABCD-EFGH-JKMN-PQRS";
            await model.AddPresenceKeyCommand.ExecuteAsync();
            Assert.Equal(1, connections.PresenceKeysAdded);
            Assert.True(model.HasPresenceKey);
            Assert.False(model.NeedsPresenceKey);
            Assert.Contains("added and admitted", model.PresenceMessage, StringComparison.Ordinal);
            Assert.Equal("", model.PresenceCode);
            Assert.False(model.MaterialReviewed);
            Assert.Empty(estate.Load().Current("decision"));

            model.MaterialReviewed = true;
            Assert.True(model.SubmitDecisionCommand.CanExecute(null), model.DecisionEligibility);
            await model.SubmitDecisionCommand.ExecuteAsync();
            RecordEnvelope answer = Assert.Single(estate.Load().Current("decision"));
            Assert.Equal(model.Selected!.Connection.PresenceDevice, Assert.Single(answer.Signatures).Device);
            Assert.Equal(0, estate.Load().Findings.Errors);
            return true;
        });
    }

    [Fact]
    public async Task ADeclinedPresencePromptLeavesADraftThatCanBeSignedAfterAnotherReview()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            connections.PresenceKeys.Decline = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(1, connections.PreparedCalls);
            Assert.Equal(0, connections.SignedWrites);
            Assert.Equal("draft", model.Decision!.State);
            Assert.False(model.Decision.Attempted);
            Assert.Null(model.Decision.Prepared);
            Assert.Contains("Nothing was signed or sent", model.DecisionMessage, StringComparison.Ordinal);
            Assert.False(Assert.Single(connections.LoadDecisions(model.Selected!.Connection)).Attempted);
            Assert.Empty(estate.Load().Current("decision"));
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));

            connections.PresenceKeys.Decline = false;
            await model.RefreshDecisionCommand.ExecuteAsync();
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("completed", model.Decision!.State);
            Assert.Equal(model.Selected.Connection.PresenceDevice, Assert.Single(Assert.Single(estate.Load().Current("decision")).Signatures).Device);
            Assert.Equal(1, connections.SignedWrites);
            return true;
        });
    }
}
