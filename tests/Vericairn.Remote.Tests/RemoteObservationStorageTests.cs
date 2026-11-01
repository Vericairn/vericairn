// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class RemoteObservationStorageTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AStorageFailureRemainsVisibleAndAConnectionCheckDoesNotInventAStoredView()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        var assignment = RemoteInspectionSeed.Assign(estate);
        _ = RemoteInspectionSeed.Result(estate, assignment);
        using var connections = new FakeConnections(estate.Root) { CannotKeepObservation = true };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            model.SelectedAssignment = Assert.Single(model.Assignments);
            await model.OpenAssignmentCommand.ExecuteAsync();
            Assert.NotNull(model.WorkDetail);
            Assert.Contains("could not be kept", model.CacheMessage, StringComparison.Ordinal);
            var reopened = new RemoteViewModel(connections);
            Assert.Equal(ConnectionStates.Unavailable, reopened.ConnectionState);
            Assert.True(reopened.ObservationUnavailable);
            Assert.False(reopened.AttentionEmpty);
            Assert.False(reopened.WorkEmpty);
            return true;
        });
    }

    [Fact]
    public async Task AppStorageRestoresCompleteObservationsOnlyForTheSameConnectionAndPrincipal()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        var assignment = RemoteInspectionSeed.Assign(estate);
        _ = RemoteInspectionSeed.Result(estate, assignment);
        using var fake = new FakeConnections(estate.Root);
        DirectoryInfo directory = Directory.CreateTempSubdirectory("vericairn-phone-observations-");
        try
        {
            await fixture.UiAsync(async () =>
            {
                RemoteViewModel model = await RemoteInspectionSeed.Pair(fake, new InspectionClock());
                model.SelectedAssignment = Assert.Single(model.Assignments);
                await model.OpenAssignmentCommand.ExecuteAsync();
                var store = new LocalNetworkConnections(directory.FullName, new FileKeyStore(directory.FullName));
                RuntimeConnection connection = model.Selected!.Connection;
                RemoteObservation observation = model.Selected.Observation!;
                store.SaveObservation(connection, observation);
                var restarted = new LocalNetworkConnections(directory.FullName, new FileKeyStore(directory.FullName));
                RemoteObservation restored = restarted.LoadObservation(connection)!;
                Assert.NotNull(restored);
                Assert.Equal(observation.AsOf, restored.AsOf);
                Assert.Equal(observation.Attention.Items, restored.Attention.Items);
                Assert.Equal(observation.Communication.RevisionHash, restored.Communication.RevisionHash);
                Assert.Equal(model.WorkDetail!.History, restored.Details[assignment.Id].History);
                Assert.Null(restarted.LoadObservation(connection with { Principal = estate.BuilderId }));
                Assert.Null(restarted.LoadObservation(connection with { Device = "replacement-device" }));
                Assert.Null(restarted.LoadObservation(connection with { Identity = "another-runtime" }));
                Assert.Null(restarted.LoadObservation(connection with { Estate = "another-estate" }));

                string cache = Assert.Single(Directory.GetFiles(Path.Combine(directory.FullName, "remote-observations")));
                File.WriteAllText(cache, "{\"version\":1}");
                Assert.Null(restarted.LoadObservation(connection));
                File.WriteAllText(cache, "interrupted data");
                Assert.Null(restarted.LoadObservation(connection));
                Assert.Empty(Directory.GetFiles(directory.FullName, "*.tmp", SearchOption.AllDirectories));
                return true;
            });
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task AnUnobservedEstateDoesNotClaimThatNoWorkOrAttentionExists()
    {
        using TestEstate estate = RemoteFixtures.Seeded();
        using var fake = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            fake.Checks.Enqueue((ConnectionStates.Unavailable, "Not reachable."));
            var model = new RemoteViewModel(fake) { Address = "192.168.1.20:7443", Code = "ABCD-EFGH" };
            await model.PairCommand.ExecuteAsync();
            Assert.True(model.ObservationUnavailable);
            Assert.False(model.AttentionEmpty);
            Assert.False(model.WorkEmpty);
            Assert.Equal("age unknown", model.ObservationAge);
            Assert.Equal(0, fake.ApiCalls);
            return true;
        });
    }
}
