// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.AcceptanceBasisOutcomeTests;

namespace Vericairn.Runtime.Tests;

public sealed class AcceptanceBasisOutcomeWriteTests
{
    [Theory]
    [InlineData("findings")]
    [InlineData("fail")]
    public void ANewNonOperatorAcceptanceOnAReviewThatDidNotPassIsRefused(string outcome)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope order, RecordEnvelope review) = OrdinaryWorkWithReview(estate, outcome);
        using EstateService service = EstateService.Open(estate.Root);
        int before = service.Session.Load().Records.Count;
        WriteOutcome refused = service.NewRecord(new("acceptance", estate.BuilderId, Json(Body(review.Id, self: true)), $"work-order:{order.Id}:{order.ComputedHash}"));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == Code);
        Assert.Equal("error", finding.Severity);
        Assert.Contains($"{review.Id} ({outcome})", finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Empty(service.Session.Load().OfType("acceptance"));
    }

    [Theory]
    [InlineData("pass", true)]
    [InlineData("findings", false)]
    [InlineData("fail", false)]
    public void ANewAcceptanceOnPassingReviewsAndTheOperatorsAcceptanceAreUnchanged(string outcome, bool contributor)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope order, RecordEnvelope review) = OrdinaryWorkWithReview(estate, outcome);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome accepted = service.NewRecord(new("acceptance", contributor ? estate.BuilderId : estate.OperatorId,
            Json(Body(review.Id, self: contributor)), $"work-order:{order.Id}:{order.ComputedHash}"));
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        Assert.DoesNotContain(service.Validate().Findings, f => f.Code == Code);
    }

    [Fact]
    public void AnEarlierAcceptanceIsReportedAsInformationAndDoesNotBlockLaterWrites()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope order, RecordEnvelope review) = OrdinaryWorkWithReview(estate, "findings");
        RecordEnvelope earlier = estate.Add("acceptance", estate.BuilderId, Body(review.Id, self: true), order.Reference);
        using EstateService service = EstateService.Open(estate.Root);
        foreach (FindingsResponse check in new[] { service.Validate(), service.Check() })
        {
            Assert.Equal(0, check.Errors);
            FindingDto info = Assert.Single(check.Findings, f => f.Code == Code);
            Assert.Equal("info", info.Severity);
            Assert.StartsWith(earlier.ExpectedPath, info.Location, StringComparison.Ordinal);
        }
        WriteOutcome later = service.NewRecord(new("review", estate.OperatorId,
            Json("""{"independence":"operator","outcome":"pass","summary":"The findings were resolved.","findings":[]}"""), $"work-order:{order.Id}:{order.ComputedHash}"));
        Assert.True(later.Ok, later.Problem?.Message);
        Assert.Equal(0, service.Validate().Errors);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
