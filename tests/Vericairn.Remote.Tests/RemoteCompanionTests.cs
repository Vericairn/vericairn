// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

public sealed class RemoteCompanionTests(HeadlessFixture fixture)
{
    [Fact]
    [Trait("Journey", "D19-A")]
    public async Task APermittedReaderOpensACollectionAndItsExactItem()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope order = Assert.Single(estate.Load().Current("work-order"));
        using var connections = CompanionConnections(estate);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            int recordCount = estate.Load().Records.Count;
            await OpenWorkOrdersAsync(model);
            Assert.Contains("Shown on the phone", model.CompanionText, StringComparison.Ordinal);
            RemoteCompanionItem item = Assert.Single(model.CompanionItems);
            Assert.Equal("Shown on the phone", item.Name);
            Assert.Equal(new AttentionReference(order.Type, order.Id, order.ComputedHash), item.Source);

            await OpenItemAsync(model);
            Assert.Contains(order.Body.GetProperty("intent").GetString()!, WebUtility.HtmlDecode(model.CompanionText), StringComparison.Ordinal);
            Assert.Contains(order.ComputedHash, model.CompanionText, StringComparison.Ordinal);
            Assert.Equal(recordCount, estate.Load().Records.Count);
            return true;
        });
    }

    [Fact]
    [Trait("Journey", "D19-E")]
    public async Task ARevokedReaderSeesNoCompanionTextOrSourceMetadata()
    {
        using var policy = new PolicyFixture();
        TestEstate estate = policy.Estate;
        RecordEnvelope order = policy.Add("work-order", JsonNode.Parse("""
            {"key":"REMOTE-READ","kind":"ordinary","title":"Private fixture title","intent":"A private fixture detail.","scope":["tests"],"exitQuestion":"Readable?","inputs":[],"deliverables":["evidence"]}
            """)!.AsObject());
        using var connections = CompanionConnections(estate);
        connections.PairAdmissionSigner = policy.OperatorSigner;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await OpenWorkOrdersAsync(model);
            await OpenItemAsync(model);
            Assert.Contains("A private fixture detail.", model.CompanionText, StringComparison.Ordinal);

            RevokePhone(policy, connections);

            // The last connection probe still reports current. The reader must recheck the phone's admission itself.
            await model.RefreshCompanionCommand.ExecuteAsync();
            Assert.Empty(model.CompanionText);
            Assert.Empty(model.CompanionItems);
            Assert.False(model.ComposeCompanionHandoffCommand.CanExecute(null));
            string readerStatus = model.CompanionFreshness + model.CompanionMessage;
            Assert.DoesNotContain(order.Id, readerStatus, StringComparison.Ordinal);
            Assert.DoesNotContain(order.ComputedHash, readerStatus, StringComparison.Ordinal);
            Assert.DoesNotContain("Private fixture title", readerStatus, StringComparison.Ordinal);

            await model.OpenCompanionsCommand.ExecuteAsync();
            Assert.Empty(model.CompanionCollections);
            Assert.Empty(model.CompanionItems);
            return true;
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Journey", "D19-E")]
    public async Task ARevokedReaderCannotReopenACachedPageWithoutRefresh(bool reopenItem)
    {
        using var policy = new PolicyFixture();
        RecordEnvelope order = policy.Add("work-order", JsonNode.Parse("""
            {"key":"REMOTE-READ","kind":"ordinary","title":"Private fixture title","intent":"A private fixture detail.","scope":["tests"],"exitQuestion":"Readable?","inputs":[],"deliverables":["evidence"]}
            """)!.AsObject());
        using var connections = CompanionConnections(policy.Estate);
        connections.PairAdmissionSigner = policy.OperatorSigner;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await OpenWorkOrdersAsync(model);
            Assert.Contains("Private fixture title", model.CompanionText, StringComparison.Ordinal);
            await OpenItemAsync(model);
            Assert.Contains("A private fixture detail.", model.CompanionText, StringComparison.Ordinal);

            if (reopenItem)
            {
                await model.BackCompanionCommand.ExecuteAsync();
                Assert.StartsWith("Cached", model.CompanionFreshness, StringComparison.Ordinal);
                RevokePhone(policy, connections);
            }

            bool rechecked = false;
            connections.BeforeApiCall = name =>
            {
                if (name != nameof(IEstateApi.CompanionSourcesAsync) || rechecked) return;
                rechecked = true;
                // The catalog can succeed before the collection's separate source read is refused.
                if (!reopenItem) RevokePhone(policy, connections);
            };
            if (reopenItem) await OpenItemAsync(model);
            else await model.BackCompanionCommand.ExecuteAsync();

            Assert.True(rechecked);
            Assert.Empty(model.CompanionText);
            Assert.Empty(model.CompanionItems);
            Assert.Empty(model.CompanionFreshness);
            Assert.False(model.CanHandOffCompanion);
            Assert.Contains("unavailable", model.CompanionMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(order.Id, model.CompanionMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(order.ComputedHash, model.CompanionMessage, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task ASupersededItemRendersAgainAfterASuccessfulSourceRecheck()
    {
        using var policy = new PolicyFixture();
        RecordEnvelope order = policy.Add("work-order", JsonNode.Parse("""
            {"key":"REMOTE-READ","kind":"ordinary","title":"Original work order","intent":"Read an exact revision.","scope":["tests"],"exitQuestion":"Readable?","inputs":[],"deliverables":["evidence"]}
            """)!.AsObject());
        using var connections = CompanionConnections(policy.Estate);
        connections.PairAdmissionSigner = policy.OperatorSigner;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await OpenWorkOrdersAsync(model);
            await OpenItemAsync(model);
            Assert.Contains("current at observation", model.CompanionFreshness, StringComparison.Ordinal);
            await model.BackCompanionCommand.ExecuteAsync();

            JsonObject revision = PolicyFixture.Body(order);
            revision["title"] = "Revised work order";
            policy.Add("work-order", revision, supersedes: order.Id);
            policy.AssertValid();

            List<string> calls = [];
            connections.BeforeApiCall = calls.Add;
            await OpenItemAsync(model);

            Assert.Equal([nameof(IEstateApi.CompanionSourcesAsync), nameof(IEstateApi.CompanionRenderAsync)], calls);
            Assert.StartsWith("Observed", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Contains("historical at observation", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Contains(order.ComputedHash, model.CompanionText, StringComparison.Ordinal);
            return true;
        });
    }

    [Fact]
    public async Task EstateRecordRowsIdentifyUntitledRecords()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = CompanionConnections(estate);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await model.OpenCompanionsCommand.ExecuteAsync();
            model.SelectedCompanionCollection = Assert.Single(model.CompanionCollections, collection => collection.Name == "Estate records");
            await model.OpenCompanionCollectionCommand.ExecuteAsync();

            RemoteCompanionItem[] principals = model.CompanionItems.Where(item => item.Source.Type == "principal").ToArray();
            Assert.True(principals.Length > 1);
            Assert.All(principals, item =>
            {
                Assert.Equal("item", item.Name);
                Assert.Contains("principal", item.Label, StringComparison.Ordinal);
                Assert.Contains(item.Source.Id[^8..], item.Label, StringComparison.Ordinal);
                Assert.Contains(item.Source.Hash![7..15], item.Label, StringComparison.Ordinal);
            });
            Assert.Equal(principals.Length, principals.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count());
            return true;
        });
    }

    [Fact]
    [Trait("Journey", "D19-A")]
    public async Task AnUnavailableRenditionHandsOffItsVerifiedExactSubjectToWorkbench()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        RecordEnvelope order = Assert.Single(estate.Load().Current("work-order"));
        using var connections = CompanionConnections(estate);
        connections.CompanionRenditionUnavailable = request => request.Revision == order.Id;
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            int recordCount = estate.Load().Records.Count;
            await OpenWorkOrdersAsync(model);
            await OpenItemAsync(model);
            Assert.Empty(model.CompanionText);
            Assert.True(model.ComposeCompanionHandoffCommand.CanExecute(null));
            Assert.Empty(connections.Service.DesktopHandoffs(estate.OperatorId).Handoffs);

            await model.ComposeCompanionHandoffCommand.ExecuteAsync();
            Assert.Contains(order.ComputedHash, model.HandoffSubject, StringComparison.Ordinal);
            Assert.Contains("Workbench", model.HandoffTask, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(model.HandoffReason));
            await model.PrepareHandoffCommand.ExecuteAsync();

            DesktopHandoff handoff = Assert.Single(connections.Service.DesktopHandoffs(estate.OperatorId).Handoffs);
            Assert.Equal(new AttentionReference(order.Type, order.Id, order.ComputedHash), handoff.Subject);
            Assert.Equal(Assert.Single(connections.Kept).Device, handoff.PreparedByDevice);
            Assert.Null(handoff.OpenedAt);
            Assert.Equal(recordCount, estate.Load().Records.Count);
            return true;
        });
    }

    [Fact]
    [Trait("Journey", "D19-E")]
    public async Task ReusedCollectionAndItemBytesShowTheirOwnVerifiedCacheAge()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = CompanionConnections(estate);
        await fixture.UiAsync(async () =>
        {
            var clock = new InspectionClock();
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, clock);
            await OpenWorkOrdersAsync(model);
            string collection = model.CompanionText;
            clock.Advance(TimeSpan.FromSeconds(15));
            await OpenItemAsync(model);
            string item = model.CompanionText;

            List<string> calls = [];
            connections.BeforeApiCall = calls.Add;
            clock.Advance(TimeSpan.FromSeconds(45));
            await model.BackCompanionCommand.ExecuteAsync();
            Assert.Equal(collection, model.CompanionText);
            Assert.StartsWith("Cached", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Contains("60 seconds old", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Contains(nameof(IEstateApi.CompanionSourcesAsync), calls);
            Assert.DoesNotContain(nameof(IEstateApi.CompanionRenderAsync), calls);

            calls.Clear();
            await OpenItemAsync(model);
            Assert.Equal(item, model.CompanionText);
            Assert.StartsWith("Cached", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Contains("45 seconds old", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Contains(nameof(IEstateApi.CompanionSourcesAsync), calls);
            Assert.DoesNotContain(nameof(IEstateApi.CompanionRenderAsync), calls);
            return true;
        });
    }

    [Theory]
    [InlineData(ConnectionStates.Denied)]
    [InlineData(ConnectionStates.Unavailable)]
    public async Task AFailedHomeRefreshDiscardsBothPagesWhileTheReaderIsClosed(string state)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = CompanionConnections(estate);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await OpenWorkOrdersAsync(model);
            await OpenItemAsync(model);
            await model.BackCompanionCommand.ExecuteAsync();
            Assert.StartsWith("Cached", model.CompanionFreshness, StringComparison.Ordinal);
            await model.BackCompanionCommand.ExecuteAsync();
            await model.BackCompanionCommand.ExecuteAsync();
            Assert.False(model.CompanionsOpen);

            connections.Checks.Enqueue((state, "The runtime could not serve this phone."));
            await model.RefreshCommand.ExecuteAsync();
            Assert.False(model.CompanionsOpen);

            // Subsequent probes succeed with exactly the same basis. Neither page may be reused.
            List<string> calls = [];
            connections.BeforeApiCall = calls.Add;
            await OpenWorkOrdersAsync(model);
            string collectionFreshness = model.CompanionFreshness;
            await OpenItemAsync(model);
            string itemFreshness = model.CompanionFreshness;
            Assert.Equal(2, calls.Count(name => name == nameof(IEstateApi.CompanionRenderAsync)));
            Assert.StartsWith("Observed", collectionFreshness, StringComparison.Ordinal);
            Assert.StartsWith("Observed", itemFreshness, StringComparison.Ordinal);
            return true;
        });
    }

    [Theory]
    [InlineData("0.9.4+test", "Item navigation requires a newer runtime.")]
    [InlineData("0.9.5", "The generator reports no pages.")]
    [InlineData("0.10.0-dev+test", "The generator reports no pages.")]
    [InlineData("unknown", "The generator reports no pages.")]
    public async Task ACollectionWithoutPageMetadataExplainsWhy(string reportedVersion, string expectedMessage)
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = CompanionConnections(estate);
        connections.Service.ReportedVersion = reportedVersion;
        connections.TransformCompanionRendition = (_, response) => response with { Page = null };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await OpenWorkOrdersAsync(model);
            Assert.Contains("Shown on the phone", model.CompanionText, StringComparison.Ordinal);
            Assert.False(model.ShowCompanionItems);
            Assert.False(model.ShowCompanionPages);
            Assert.Equal(expectedMessage, model.CompanionMessage);

            await model.BackCompanionCommand.ExecuteAsync();
            await OpenWorkOrdersAsync(model);
            Assert.StartsWith("Cached", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.Equal(expectedMessage, model.CompanionMessage);

            await model.BackCompanionCommand.ExecuteAsync();
            model.SelectedCompanionCollection = Assert.Single(model.CompanionCollections, collection => collection.Name == "Estate manifest");
            await model.OpenCompanionCollectionCommand.ExecuteAsync();
            Assert.NotEmpty(model.CompanionText);
            Assert.Empty(model.CompanionMessage);
            return true;
        });
    }

    [Fact]
    public async Task AReadableExactItemDoesNotOfferDesktopContinuation()
    {
        using TestEstate estate = RemoteFixtures.Seeded(3);
        using var connections = CompanionConnections(estate);
        // The runtime does not emit this marker for record items. A successful record read keeps its text on the phone.
        connections.TransformCompanionRendition = (request, response) => request.Revision is null
            ? response
            : response with { Completeness = "bounded-overview" };
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            await OpenWorkOrdersAsync(model);
            await OpenItemAsync(model);
            Assert.NotEmpty(model.CompanionText);
            Assert.False(model.CanHandOffCompanion);
            await model.BackCompanionCommand.ExecuteAsync();
            await OpenItemAsync(model);
            Assert.StartsWith("Cached", model.CompanionFreshness, StringComparison.Ordinal);
            Assert.False(model.CanHandOffCompanion);
            return true;
        });
    }

    private static void RevokePhone(PolicyFixture policy, FakeConnections connections)
    {
        TestEstate estate = policy.Estate;
        RuntimeConnection phone = Assert.Single(connections.Kept);
        RecordEnvelope admission = Assert.Single(estate.Load().Current("admission"), record => record.Subject?.Id == phone.Device);
        estate.SetClock(DateTimeOffset.Parse(admission.At, CultureInfo.InvariantCulture));
        var revoked = PolicyFixture.Body(admission);
        revoked["status"] = "revoked";
        revoked["revokedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
        policy.Add("admission", revoked, subject: admission.Subject, supersedes: admission.Id);
        policy.AssertValid();
        Assert.Equal("revoked", connections.Service.Standing(phone.Device)?.Admission);
    }

    private static FakeConnections CompanionConnections(TestEstate estate) =>
        new(estate.Root) { EnableSignedWrites = true, WithoutPresenceKey = true };

    private static async Task OpenWorkOrdersAsync(RemoteViewModel model)
    {
        await model.OpenCompanionsCommand.ExecuteAsync();
        model.SelectedCompanionCollection = Assert.Single(model.CompanionCollections, collection => collection.Name == "Work orders");
        await model.OpenCompanionCollectionCommand.ExecuteAsync();
    }

    private static async Task OpenItemAsync(RemoteViewModel model)
    {
        model.SelectedCompanionItem = Assert.Single(model.CompanionItems);
        await model.OpenCompanionItemCommand.ExecuteAsync();
    }
}
