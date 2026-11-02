// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Runtime.Objects;

namespace Vericairn.Remote.Tests;

public sealed class RemoteReleaseDecisionTests(HeadlessFixture fixture)
{
    private const string Candidate = """{"version":"0.1.0","repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":[],"recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[{"summary":"The candidate's complete available evidence."}],"qualified":[],"signing":"unsigned developer build","limitations":["No distributable artifacts are attached."]}""";

    [Fact]
    public async Task ACandidateAcceptanceUsesOnlyReviewsOfTheExactCandidateAndCorrectSelfAttribution()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope product = estate.Load().Current("product").Single();
        RecordEnvelope first = estate.Add("release", estate.OperatorId, Candidate, product.Reference);
        RecordEnvelope historicalReview = estate.Add("review", estate.OperatorId, """{"independence":"self","outcome":"findings","summary":"Earlier candidate review.","findings":[]}""", first.Reference);
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, Candidate.Replace("available evidence", "reviewed evidence", StringComparison.Ordinal), product.Reference, first.Id);
        RecordEnvelope review = estate.Add("review", estate.OperatorId, """{"independence":"self","outcome":"pass","summary":"The current candidate was read.","findings":[]}""", candidate.Reference);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, candidate);
            Assert.Contains(candidate.ComputedHash, model.DecisionIdentity, StringComparison.Ordinal);
            Assert.Contains("No distributable artifacts are attached.", model.DecisionMaterial, StringComparison.Ordinal);
            Assert.Contains(review.ComputedHash, model.DecisionMaterial, StringComparison.Ordinal);
            Assert.DoesNotContain(historicalReview.ComputedHash, model.DecisionMaterial, StringComparison.Ordinal);
            RemoteDecisionSeed.Ready(model, "I accept this candidate with its stated limitations.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            RecordEnvelope acceptance = Assert.Single(estate.Load().Current("acceptance"));
            Assert.Equal(candidate.Reference, acceptance.Subject);
            Assert.True(acceptance.Body.GetProperty("self").GetBoolean());
            Assert.Equal(review.Id, Assert.Single(acceptance.Body.GetProperty("basis").EnumerateArray()).GetString());
            Assert.Equal(model.Selected!.Connection.Device, Assert.Single(acceptance.Signatures).Device);
            Assert.Equal("completed", model.Decision!.State);
            Assert.Equal("candidate", Assert.Single(estate.Load().Current("release")).Body.GetProperty("status").GetString());
            Assert.Equal(0, estate.Load().Findings.Errors);
            return true;
        });
    }

    [Fact]
    public async Task AWithdrawnCandidateLeavesItsAcceptanceADraftAndOffersTheDesktop()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope product = estate.Load().Current("product").Single();
        byte[] artifact = "Candidate material.\n"u8.ToArray();
        string hash = new FileObjectStore(Path.Combine(estate.Root, "objects")).Put(artifact);
        JsonObject body = JsonNode.Parse(Candidate)!.AsObject();
        body["artifacts"] = new JsonArray(new JsonObject { ["name"] = "candidate.txt", ["hash"] = hash, ["bytes"] = artifact.Length, ["platform"] = "any" });
        RecordEnvelope candidate = estate.Add("release", estate.OperatorId, body.ToJsonString(), product.Reference);
        using var connections = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await RemoteDecisionSeed.Open(model, candidate);
            RemoteDecisionSeed.Ready(model, "Accept the candidate.");
            body["status"] = "withdrawn";
            RecordEnvelope withdrawn = estate.Add("release", estate.OperatorId, body.ToJsonString(), product.Reference, candidate.Id);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("draft", model.Decision!.State);
            Assert.Equal(withdrawn.Id, model.Decision.Review.Subject.Id);
            Assert.Contains("Only a current release candidate", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Contains("desktop", model.DecisionEligibility, StringComparison.Ordinal);
            Assert.Equal(0, connections.PreparedCalls);
            Assert.Empty(estate.Load().Current("acceptance"));
            Assert.Equal(0, estate.Load().Findings.Errors);
            return true;
        });
    }
}
