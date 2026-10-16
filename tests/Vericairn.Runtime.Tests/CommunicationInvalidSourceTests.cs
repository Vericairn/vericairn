// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class CommunicationInvalidSourceTests
{
    [Theory]
    [InlineData("17")]
    [InlineData("{\"status\":\"short\",\"unknownField\":true}")]
    public void AnInvalidImportedSettingIsUnavailableAndNeverSilentlyBecomesADefault(string values)
    {
        using var estate = new TestEstate(2);
        estate.Add("communication-setting", estate.OperatorId, $$"""{"scope":"person","recipient":"{{estate.OperatorId}}","values":{{values}}}""");
        using EstateService service = EstateService.Open(estate.Root);
        CommunicationProfile profile = service.Communication(estate.OperatorId);
        Assert.NotNull(profile.Unavailable);
        Assert.Equal("comprehensive", CommunicationPresentation.Depth(profile, "status"));
    }

    [Fact]
    public void AnUnauthorizedImportedPreferenceIsNotUsedForPresentation()
    {
        using var estate = new TestEstate(2);
        estate.Add("communication-setting", estate.BuilderId, $$$"""{"scope":"person","recipient":"{{{estate.OperatorId}}}","values":{"status":"short","progress":"none"}}""");
        using EstateService service = EstateService.Open(estate.Root);
        Assert.NotNull(service.Communication(estate.OperatorId).Unavailable);
    }

    [Fact]
    public void UnsupportedFieldsInNewRequestContractsAreExplicitlyRefused()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CommunicationValues>("""{"status":"short","extra":true}""", ProtocolJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CommunicationSetRequest>("""{"by":"person","scope":"person","values":{},"extra":true}""", ProtocolJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CommunicationDelegateRequest>("""{"by":"person","recipient":"person","grantee":"agent","scope":"person","extra":true}""", ProtocolJson.Options));
    }

    [Fact]
    public void GenericRecordAuthoringCannotBackdateAChangeToReuseRevokedConsent()
    {
        using var estate = new TestEstate(2);
        RecordEnvelope grant = estate.Add("communication-delegation", estate.OperatorId,
            $$"""{"recipient":"{{estate.OperatorId}}","grantee":"{{estate.BuilderId}}","scope":"person","status":"active"}""");
        estate.Add("communication-delegation", estate.OperatorId,
            $$"""{"recipient":"{{estate.OperatorId}}","grantee":"{{estate.BuilderId}}","scope":"person","status":"revoked"}""", supersedes: grant.Id);
        using EstateService service = EstateService.Open(estate.Root);
        using JsonDocument body = JsonDocument.Parse($$"""{"scope":"person","recipient":"{{estate.OperatorId}}","values":{},"authority":"{{grant.Id}}","authorityHash":"{{grant.ComputedHash}}"}""");
        WriteOutcome outcome = service.NewRecord(new("communication-setting", estate.BuilderId, body.RootElement, At: grant.At));
        Assert.False(outcome.Ok);
        Assert.Empty(estate.Load().OfType("communication-setting"));
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void AnInvalidImportedReconciliationCannotQuietlySelectAProfile()
    {
        using var estate = new TestEstate(2);
        string body = $$$"""{"scope":"person","recipient":"{{{estate.OperatorId}}}","values":{"status":"short"}}""";
        RecordEnvelope original = estate.Add("communication-setting", estate.OperatorId, body);
        RecordEnvelope a = estate.Add("communication-setting", estate.OperatorId, body, supersedes: original.Id);
        RecordEnvelope b = estate.Add("communication-setting", estate.OperatorId, body.Replace("short", "comprehensive", StringComparison.Ordinal), supersedes: original.Id);
        estate.Add("reconciliation", estate.BuilderId, $$"""{"kept":"{{a.Id}}","withdrawn":["{{b.Id}}"],"note":"No consent."}""", new("communication-setting", original.Id, original.ComputedHash));
        using EstateService service = EstateService.Open(estate.Root);
        CommunicationProfile profile = service.Communication(estate.OperatorId);
        Assert.Contains("communication.authority", profile.Unavailable, StringComparison.Ordinal);
        Assert.Empty(profile.Fields);
        Assert.Equal(3, estate.Load().OfType("communication-setting").Count());
    }
}
