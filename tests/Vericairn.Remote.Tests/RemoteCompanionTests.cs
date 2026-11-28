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

            RuntimeConnection phone = Assert.Single(connections.Kept);
            RecordEnvelope admission = Assert.Single(estate.Load().Current("admission"), record => record.Subject?.Id == phone.Device);
            estate.SetClock(DateTimeOffset.Parse(admission.At, CultureInfo.InvariantCulture));
            var revoked = PolicyFixture.Body(admission);
            revoked["status"] = "revoked";
            revoked["revokedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
            policy.Add("admission", revoked, subject: admission.Subject, supersedes: admission.Id);
            policy.AssertValid();
            Assert.Equal("revoked", connections.Service.Standing(phone.Device)?.Admission);

            // The last connection probe still reports current. The reader must recheck the phone's admission itself.
            await model.RefreshCompanionCommand.ExecuteAsync();
            Assert.Empty(model.CompanionText);
            Assert.Empty(model.CompanionItems);
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
