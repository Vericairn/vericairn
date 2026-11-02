// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// An answer's basis is the runtime's: the estate's head and whether design documents changed since it. The phone shows it for
/// review, uses it as the change set, and a basis that changes or cannot be supplied leaves the answer a draft for the desktop.
/// </summary>
public sealed class RemoteAnswerBasisTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task ABasisThatMovesBeforeSigningLeavesTheAnswerADraftAndSignsNothing()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        string first = connections.Basis!.Head!;
        string moved = new('f', 40);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            Assert.Equal(first, model.Decision!.Review.Head);
            Assert.Contains("Estate head " + first, model.DecisionBasis, StringComparison.Ordinal);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            int signatures = connections.PresenceKeys.Signatures;
            connections.BeforeReview = () => connections.Basis = new(true, moved, moved, []);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(1, connections.PreparedCalls);
            Assert.Equal(0, connections.SignedWrites);
            Assert.Equal(signatures, connections.PresenceKeys.Signatures);
            Assert.Empty(estate.Load().Current("decision"));
            Assert.Equal("draft", model.Decision!.State);
            Assert.False(model.Decision.Attempted);
            Assert.Equal(first, model.Decision.Original.Head);
            Assert.Equal(moved, model.Decision.Review.Head);
            Assert.Equal("Keep the limit.", model.DecisionStatement);
            Assert.Contains("changed", model.DecisionMessage, StringComparison.Ordinal);
            Assert.False(model.MaterialReviewed);
            return true;
        });
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("not-a-commit")]
    [InlineData("other-base")]
    public async Task AnAnswerWithoutAnObservedBasisGoesToTheDesktopWhileAnAcceptanceNeedsNone(string kind)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        RecordEnvelope result = RemoteDecisionSeed.Result(estate);
        string head = new('a', 40);
        using var connections = new FakeConnections(estate.Root)
        {
            EnableSignedWrites = true,
            Basis = kind switch
            {
                "unavailable" => new(false, Problem: new("unavailable", "The estate's repository could not be observed.", [])),
                "not-a-commit" => new(true, "HEAD", "HEAD", []),
                _ => new(true, new string('b', 40), head, []),
            },
        };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            Assert.Null(model.Decision!.Review.Head);
            Assert.Contains("basis", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            if (kind == "unavailable") Assert.Contains("could not be observed", model.DecisionEligibility, StringComparison.Ordinal);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("decision"));

            // An acceptance states no change set, so it reads no basis.
            await model.CloseDecisionCommand.ExecuteAsync();
            int reads = connections.BasisReads;
            await RemoteDecisionSeed.Open(model, result);
            Assert.Equal(reads, connections.BasisReads);
            Assert.True(model.Decision!.Review.Eligible, string.Join(" ", model.Decision.Review.Reasons));
            Assert.Equal("", model.DecisionBasis);
            return true;
        });
    }
}
