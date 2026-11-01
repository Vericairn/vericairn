// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

public sealed class DisclosureHoldTests
{
    private const string Intent = "Close the widget cache's unchecked path traversal before anyone can reach it.";
    private const string Shared = "The three-platform run on the change";

    [Fact]
    public void HeldWorkIsFoundByItsKeyAndDistinctivePhrasesOnly()
    {
        IReadOnlyList<DisclosureHolds.Hold> holds = DisclosureHolds.Active(PolicyBasisTests.Model([
            Order("held", "FIX-90", "Cache fix", held: true), Order("open", "CODE-99", "Another step", held: false)]));
        DisclosureHolds.Hold hold = Assert.Single(holds);
        Assert.Equal("FIX-90", hold.Key);
        Assert.Equal("FIX-90", DisclosureHolds.Disclosed(holds, "Implements FIX-90."));
        Assert.Equal("FIX-90", DisclosureHolds.Disclosed(holds, "close the widget cache's unchecked\n  PATH traversal before anyone can reach it"));
        Assert.Null(DisclosureHolds.Disclosed(holds, "Implements FIX-901 and fix-9."));
        // A phrase every other work order shares, and a title too short to identify anything, are not matched alone.
        Assert.Null(DisclosureHolds.Disclosed(holds, Shared + ". Cache fix."));
        Assert.Empty(DisclosureHolds.Active(PolicyBasisTests.Model([Order("open", "CODE-99", "Another step", held: false)])));
    }

    [Fact]
    public void PublishingTheCarryingReleaseEndsTheHold()
    {
        RecordEnvelope order = Order("held", "FIX-90", "Cache fix", held: true, placement: new JsonObject
        {
            ["kind"] = "release", ["carrier"] = "carrier", ["product"] = "product", ["version"] = "9.9.9",
        });
        RecordEnvelope Release(string status, string version) => FoundationPolicyTests.R("release", "release-" + status + version,
            new { version, status }, subject: new RecordReference("product", "product", null));
        Assert.Single(DisclosureHolds.Active(PolicyBasisTests.Model([order, Release("candidate", "9.9.9"), Release("published", "9.9.8")])));
        Assert.Empty(DisclosureHolds.Active(PolicyBasisTests.Model([order, Release("published", "9.9.9")])));
    }

    [Theory]
    [InlineData("public", "Implements FIX-90.", false)]
    [InlineData("public", null, false)]
    [InlineData("public", "An unrelated change.", true)]
    [InlineData("private", "Implements FIX-90.", true)]
    public void APublicPushThatWouldDiscloseHeldWorkIsRefusedBeforeAnyEffect(string visibility, string? text, bool pushed)
    {
        using var f = new ChangesFlowTests.Fixture(visibility: visibility);
        f.Estate.Add("work-order", f.Estate.OperatorId, $$"""
            {"key":"FIX-90","kind":"ordinary","title":"Cache fix","intent":"{{Intent}}","scope":["{{Shared}}"],"exitQuestion":"Is it closed?","inputs":[],"deliverables":["The fix"],"disclosure":"held"}
            """);
        Assert.False(f.Estate.Load().Findings.HasErrors, string.Join('\n', f.Estate.Load().Findings));
        f.Adapter.Text = text;
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.Equal(pushed, response.Ok);
        Assert.Equal(pushed ? 1 : 0, f.Adapter.Pushes);
        if (!pushed) Assert.Equal("known-failure", response.Status);
    }

    [Fact]
    public void OnlyHeldIsADisclosureValue()
    {
        using var f = new ChangesFlowTests.Fixture();
        f.Estate.Add("work-order", f.Estate.OperatorId, """
            {"key":"FIX-91","kind":"ordinary","title":"Cache fix","intent":"Fix it.","scope":["Fix it."],"exitQuestion":"Fixed?","inputs":[],"deliverables":["The fix"],"disclosure":"public"}
            """);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Severity == Severity.Error && finding.Message.Contains("disclosure", StringComparison.Ordinal));
    }

    private static RecordEnvelope Order(string id, string key, string title, bool held, JsonObject? placement = null)
    {
        var body = new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = title, ["intent"] = held ? Intent : "Something else entirely, for another reason.",
            ["scope"] = new JsonArray(Shared), ["exitQuestion"] = "Done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("Done"),
        };
        if (held) body["disclosure"] = DisclosureHolds.Held;
        if (placement is not null) body["placement"] = placement;
        return FoundationPolicyTests.R("work-order", id, body);
    }
}
