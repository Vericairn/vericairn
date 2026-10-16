// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class CommunicationPresentationTests
{
    public static TheoryData<string> Frequencies => new() { "none", "completion-and-need", "milestones", "verbose" };

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void EveryMandatoryAttentionKindBypassesEveryFrequency(string frequency)
    {
        foreach (string kind in CommunicationPresentation.MandatoryKinds)
        {
            Assert.True(CommunicationPresentation.ShouldDeliver(frequency, "progress", [new(kind, "Complete required information.")]));
        }

        Assert.True(CommunicationPresentation.ShouldDeliver(frequency, "progress", [], onDemand: true));
    }

    [Fact]
    public void OptionalDeliveryFollowsTheFourFrequencies()
    {
        Assert.False(CommunicationPresentation.ShouldDeliver("none", "completion", []));
        Assert.False(CommunicationPresentation.ShouldDeliver("completion-and-need", "milestone", []));
        Assert.True(CommunicationPresentation.ShouldDeliver("completion-and-need", "completion", []));
        Assert.True(CommunicationPresentation.ShouldDeliver("milestones", "milestone", []));
        Assert.False(CommunicationPresentation.ShouldDeliver("milestones", "progress", []));
        Assert.True(CommunicationPresentation.ShouldDeliver("verbose", "progress", []));
    }

    [Theory]
    [InlineData("decision")]
    [InlineData("result")]
    [InlineData("handoff")]
    public void DepthChangesTheLeadAndNeverTouchesProtectedContent(string kind)
    {
        const string original = "Original words: do not publish. Check unknown; 7 ms; target exact-hash. Evidence retained.\n";
        string[] views = CommunicationResolution.Depths.Select(depth => CommunicationPresentation.Protected(
            CommunicationResolution.Resolve("person", null, [], new(Decision: depth, Result: depth, Handoff: depth)), kind, "record at hash", "Outcome.", original)).ToArray();
        Assert.Equal(3, views.Distinct(StringComparer.Ordinal).Count());
        Assert.All(views, text => Assert.EndsWith(original, text, StringComparison.Ordinal));
    }

    [Fact]
    public void ShortStatusKeepsFindingsStatesAndSourcesButOmitsOptionalPrincipalNavigation()
    {
        const string text = "Estate: Example\nVericairn: release 0.7.0\nLast observed by a person: unknown\nPrincipals:\n  Person [operator]\nWork orders:\n  W-1 blocked\nFindings: 1 error\n  ERROR unknown: check was not run\n";
        using JsonDocument json = JsonDocument.Parse("""{"workOrders":[{"key":"W-1","id":"source-record","status":"blocked"}]}""");
        var response = new StatusResponse(text, json.RootElement, true);
        string shortView = CommunicationPresentation.Status(response, CommunicationResolution.Resolve(null, null, []));
        Assert.DoesNotContain("Person [operator]", shortView, StringComparison.Ordinal);
        Assert.Contains("check was not run", shortView, StringComparison.Ordinal);
        Assert.Contains("source-record", shortView, StringComparison.Ordinal);
        Assert.Contains("unknown", shortView, StringComparison.Ordinal);
    }

    [Fact]
    public void BriefPresentationNamesRecipientScopeAndFreshnessWithoutChangingItsCanonicalJson()
    {
        using var estate = new TestEstate(2);
        estate.Add("work-order", estate.OperatorId, """{"key":"W-1","kind":"ordinary","title":"Build","intent":"Complete original intent.","scope":["src/"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        using EstateService service = EstateService.Open(estate.Root);
        string originalJson = service.Brief("W-1")!.Json.GetRawText();
        BriefResponse shortView = service.BriefFor("W-1", estate.OperatorId, view: new(Handoff: "short"))!;
        BriefResponse longView = service.BriefFor("W-1", estate.OperatorId, view: new(Handoff: "comprehensive"))!;
        Assert.Equal(originalJson, shortView.Json.GetRawText());
        Assert.Equal(originalJson, longView.Json.GetRawText());
        Assert.NotEqual(shortView.Markdown, longView.Markdown);
        Assert.Contains("Complete original intent.", shortView.Markdown, StringComparison.Ordinal);
        Assert.Contains(estate.OperatorId, shortView.Markdown, StringComparison.Ordinal);
        Assert.Contains("Communication", shortView.Markdown, StringComparison.Ordinal);
        Assert.Contains("shipped", shortView.Markdown, StringComparison.Ordinal);
        Assert.Contains("Freshness", shortView.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignmentValuesCannotBeAppliedToAnUnrelatedWorkBrief()
    {
        using var estate = new TestEstate(2);
        foreach (string key in new[] { "A", "B" })
        {
            estate.Add("work-order", estate.OperatorId, $$"""{"key":"{{key}}","kind":"ordinary","title":"Build","intent":"Complete original intent.","scope":["src/"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        }

        using EstateService service = EstateService.Open(estate.Root);
        using JsonDocument brief = JsonDocument.Parse("""{"outcome":"Build it.","sources":[],"permittedChanges":["src/"],"prohibitedEffects":[],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["tests"],"recovery":"Record a result."}""");
        WriteOutcome assigned = service.Assign(new("A", estate.BuilderId, estate.OperatorId, brief.RootElement));
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        string assignment = assigned.Written!.Id;
        Assert.True(service.CommunicationSet(new(estate.OperatorId, "assignment", new(Handoff: "short"), estate.OperatorId, assignment)).Ok);
        Assert.Equal("assignment", service.Communication(estate.OperatorId, assignment).Fields["handoff"].Source);
        Assert.Contains("handoff (short)", service.BriefFor("A", estate.OperatorId, assignment)!.Markdown, StringComparison.Ordinal);
        BriefResponse unrelated = service.BriefFor("B", estate.OperatorId, assignment)!;
        Assert.Contains("does not belong", unrelated.Markdown, StringComparison.Ordinal);
        Assert.Contains("Complete original intent.", unrelated.Markdown, StringComparison.Ordinal);
        Assert.Equal(service.Brief("B")!.Json.GetRawText(), unrelated.Json.GetRawText());
    }
}
