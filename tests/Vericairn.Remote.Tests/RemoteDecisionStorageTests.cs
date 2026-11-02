// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

public sealed class RemoteDecisionStorageTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AppStorageKeepsTheOriginalAndPreventsAnotherStoreReplacingAnAttemptedIdentity()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var fake = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        DirectoryInfo directory = Directory.CreateTempSubdirectory("vericairn-phone-decisions-");
        try
        {
            await fixture.UiAsync(async () =>
            {
                RemoteViewModel model = await RemoteInspectionSeed.Pair(fake, new InspectionClock());
                await RemoteDecisionSeed.Open(model, ask);
                RemoteDecisionSeed.Ready(model, "Keep the limit.");
                RemoteDecisionDraft original = model.Decision!;
                var connection = model.Selected!.Connection;
                var store = new LocalNetworkConnections(directory.FullName, new FileKeyStore(directory.FullName));
                store.SaveDecision(connection, original);
                var restarted = new LocalNetworkConnections(directory.FullName, new FileKeyStore(directory.FullName));
                RemoteDecisionDraft restored = Assert.Single(restarted.LoadDecisions(connection));
                Assert.Equal(original.Statement, restored.Statement);
                Assert.Equal(ask.ComputedHash, restored.Original.Subject.Hash);
                Assert.Equal(original.Review.Material.Select(r => r.Hash), restored.Review.Material.Select(r => r.Hash));
                Assert.Empty(restarted.LoadDecisions(connection with { Principal = estate.BuilderId }));
                Assert.Empty(restarted.LoadDecisions(connection with { Device = "replacement-device" }));
                Assert.Empty(restarted.LoadDecisions(connection with { Estate = "another-estate" }));
                Assert.Empty(restarted.LoadDecisions(connection with { Identity = "another-runtime" }));
                await model.SubmitDecisionCommand.ExecuteAsync();
                RemoteDecisionDraft submitted = model.Decision!;
                store.SaveDecision(connection, submitted);
                Assert.Equal(submitted.Prepared!.Id, Assert.Single(restarted.LoadDecisions(connection)).Prepared!.Id);
                Assert.Throws<InvalidOperationException>(() => restarted.SaveDecision(connection, original));
                Assert.Throws<InvalidOperationException>(() => restarted.SaveDecision(connection, submitted with { Statement = "Different words." }));
                Assert.Throws<InvalidOperationException>(() => restarted.SaveDecision(connection, submitted with { Receipt = null }));
                Assert.Equal(submitted.Receipt, Assert.Single(store.LoadDecisions(connection)).Receipt);
                Assert.Empty(Directory.GetFiles(directory.FullName, "*.tmp", SearchOption.AllDirectories));
                string cache = Assert.Single(Directory.GetFiles(Path.Combine(directory.FullName, "remote-decisions")));
                File.WriteAllText(cache, "{\"version\":1}");
                Assert.Throws<InvalidDataException>(() => restarted.LoadDecisions(connection));
                Assert.Throws<InvalidDataException>(() => restarted.SaveDecision(connection, original));
                File.WriteAllText(cache, "interrupted data");
                Assert.Throws<JsonException>(() => restarted.LoadDecisions(connection));
                return true;
            });
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task AReceiptSurvivesAPersistenceFailureAndAReadFailureDoesNotClaimCompletion()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var fake = new FakeConnections(estate.Root) { EnableSignedWrites = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(fake, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            fake.AfterWrite = () => { fake.CannotKeepDecision = true; fake.FailRecordRead = true; };
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("accepted for processing", model.Decision!.State);
            Assert.Equal(fake.LastWritten, model.Decision.Receipt);
            Assert.Contains("could not be saved", model.DecisionStorageProblem, StringComparison.Ordinal);
            Assert.Contains("unconfirmed", model.DecisionMessage, StringComparison.Ordinal);
            Assert.False(model.SubmitDecisionCommand.CanExecute(null));
            // The durable pre-sign journal already has the original, even though retaining the receipt failed.
            RemoteDecisionDraft pending = Assert.Single(fake.LoadDecisions(model.Selected!.Connection));
            Assert.True(pending.Attempted);
            Assert.Equal(model.Decision.Prepared!.Id, pending.Prepared!.Id);
            Assert.Null(pending.Receipt);
            fake.CannotKeepDecision = false;
            fake.FailRecordRead = false;
            var restarted = new RemoteViewModel(fake, new InspectionClock());
            restarted.SelectedDraft = Assert.Single(restarted.DecisionDrafts);
            await restarted.OpenDraftCommand.ExecuteAsync();
            await restarted.RefreshCommand.ExecuteAsync();
            await restarted.ReconcileDecisionCommand.ExecuteAsync();
            Assert.Equal("completed", restarted.Decision!.State);
            Assert.Equal(1, fake.SignedWrites);
            return true;
        });
    }

    [Fact]
    public async Task AnObservedPreparationRefusalIsRejectedWithoutSigning()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope ask = RemoteDecisionSeed.Ask(estate);
        using var fake = new FakeConnections(estate.Root) { EnableSignedWrites = true, RefusePrepared = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(fake, new InspectionClock());
            await RemoteDecisionSeed.Open(model, ask);
            RemoteDecisionSeed.Ready(model, "Keep the limit.");
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal("rejected", model.Decision!.State);
            Assert.Contains("refused preparation", model.DecisionMessage, StringComparison.Ordinal);
            Assert.False(model.Decision.Attempted);
            Assert.Equal("Keep the limit.", model.DecisionStatement);
            Assert.Empty(estate.Load().Current("decision"));
            Assert.Equal(0, fake.SignedWrites);
            await model.SubmitDecisionCommand.ExecuteAsync();
            Assert.Equal(1, fake.PreparedCalls);
            return true;
        });
    }
}
