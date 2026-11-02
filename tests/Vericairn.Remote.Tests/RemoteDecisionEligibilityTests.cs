// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Runtime.Objects;

namespace Vericairn.Remote.Tests;

public sealed class RemoteDecisionEligibilityTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AnAskNeedsAnExplicitEstateCommitAndNoDocumentChangesBeforeAFreshConfirmation()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            model.DecisionStatement = "Keep the limit.";
            model.DecisionStake = "low";
            model.MaterialReviewed = true;
            Assert.Equal("", model.DecisionBaseCommit);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);

            string commit = new('d', 40);
            model.DecisionBaseCommit = commit;
            Assert.False(model.MaterialReviewed);
            model.MaterialReviewed = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);

            model.DecisionHasNoDocumentChanges = true;
            Assert.False(model.MaterialReviewed);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            model.MaterialReviewed = true;
            await model.SubmitDecisionCommand.ExecuteAsync();
            RecordEnvelope answer = Assert.Single(estate.Load().Current("decision"));
            JsonElement change = answer.Body.GetProperty("changeSet");
            Assert.Equal(commit, change.GetProperty("baseCommit").GetString());
            Assert.Equal(commit, change.GetProperty("head").GetString());
            Assert.Empty(change.GetProperty("pins").EnumerateArray());
            Assert.Equal(1, connections.SignedWrites);
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BinaryOrOversizedEvidenceCannotBeApprovedFromASummary(bool oversized)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        byte[] bytes = oversized ? Enumerable.Repeat((byte)'a', 256 * 1024 + 1).ToArray() : [0, 1, 2, 255];
        string hash = new FileObjectStore(Path.Combine(estate.Root, "objects")).Put(bytes);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate,
            JsonSerializer.Serialize(new[] { new { summary = "The summary is not the complete evidence.", hash } }));
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, result);
            Assert.Contains("The summary is not the complete evidence.", model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains(hash, model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Empty(model.Decision!.Review.Evidence);
            RemoteDecisionSeed.Ready(model, "Accept only after inspecting all evidence.");
            Assert.False(model.MaterialReviewed);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("acceptance"));
            return true;
        });
    }

    [Fact]
    public async Task AnAcceptanceObservedElsewhereMakesTheOpenReviewIneligible()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, result);
            RemoteDecisionSeed.Ready(model, "Accept the result.");
            RecordEnvelope accepted = estate.Add("acceptance", estate.OperatorId,
                """{"statement":"The operator already accepted this exact result elsewhere.","basis":[],"self":false}""", result.Reference);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("draft", model.Decision!.State);
            Assert.Contains("already recorded", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.False(model.MaterialReviewed);
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Equal(accepted.Id, Assert.Single(estate.Load().Current("acceptance")).Id);
            return true;
        });
    }
}
