// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

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

    // FIX-137: the carrying release's documentation set, unmerged and carrying nothing but documentation and the release's held fixes,
    // passes the hold once the operator has answered an ask about the release's Security section with a decision that names the held
    // work order, after its current revision, with no ask about the carrier still waiting; every other push stays held.
    [Theory]
    [InlineData("none", "carried", false)]
    [InlineData("answered-other-question", "carried", false)]
    [InlineData("answered-not-naming", "carried", false)]
    [InlineData("answered", "carried-with-fix", true)]
    [InlineData("answered", "carried-with-other-change", false)]
    [InlineData("answered", "carried-unreadable", false)]
    [InlineData("answered", "carried-without-paths", false)]
    [InlineData("waiting", "carried", false)]
    [InlineData("withdrawn", "carried", false)]
    [InlineData("answered", "carried", true)]
    [InlineData("answered-then-asked", "carried", false)]
    [InlineData("answered-before-revision", "carried", false)]
    [InlineData("answered-other-product", "carried", false)]
    [InlineData("answered", "absent", false)]
    [InlineData("answered", "merged", false)]
    [InlineData("answered", "other-version", false)]
    [InlineData("answered", "other-format", false)]
    public void OnlyTheCarryingReleasesDocumentationSetPassesOnceItsSecuritySectionIsApproved(string approval, string set, bool pushed)
    {
        using var f = new ChangesFlowTests.Fixture(visibility: "public");
        string op = f.Estate.OperatorId;
        string product = f.Product;
        if (approval == "answered-other-product")
        {
            product = f.Estate.Add("product", op, """
                {"name":"Gadget","slug":"gadget","summary":"Another product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}]}
                """).Id;
        }
        RecordEnvelope carrier = f.Estate.Add("work-order", op, $$"""
            {"key":"REL-9","kind":"ordinary","title":"Release 9.9.9","intent":"Carry the release of 9.9.9 to publication.","scope":["Carry it."],"exitQuestion":"Is it released?","inputs":[],"deliverables":["The release"],"releasePlan":{"product":"{{product}}","version":"9.9.9"} }
            """);
        string held = $$"""
            {"key":"FIX-90","kind":"ordinary","title":"Cache fix","intent":"{{Intent}}","scope":["{{Shared}}"],"exitQuestion":"Is it closed?","inputs":[],"deliverables":["The fix"],"disclosure":"held","placement":{"kind":"release","carrier":"{{carrier.Id}}","product":"{{product}}","version":"9.9.9"} }
            """;
        RecordEnvelope order = f.Estate.Add("work-order", op, held);
        // The Reviewer's probe: a question about the carrier that is not the Security section's approval, answered after the revision.
        string question = approval == "answered-other-question" ? "Which day should 9.9.9 be submitted?" : "Approve the Security section of the release notes of 9.9.9?";
        RecordEnvelope Ask() => f.Estate.Add("ask", op, $$"""
            {"to":"{{op}}","question":"{{question}}","recommendation":"Approve it as drafted.","consequence":"The documentation set waits.","state":"open"}
            """, subject: carrier.Reference);
        string affects = approval == "answered-not-naming" ? "" : "\"" + order.Id + "\"";
        void Answer(RecordEnvelope ask) => f.Estate.Add("decision", op, $$"""
            {"title":"Security section approved","statement":"Approved.","summary":"The Security section of 9.9.9 is approved.","synthesis":"The operator approved the section as drafted.","effects":["The documentation set carries the section."],"affects":[{{affects}}],"decidedBy":"{{op}}","decidedAt":"{{f.Estate.Clock.AddSeconds(1):yyyy-MM-ddTHH:mm:ssZ}}","stake":"medium","notAuthorized":[],"answers":["{{ask.Id}}"]}
            """);
        if (approval != "none")
        {
            RecordEnvelope ask = Ask();
            if (approval == "withdrawn")
                f.Estate.Add("ask", op, $$"""
                    {"to":"{{op}}","question":"Approve the Security section of the release notes of 9.9.9?","recommendation":"Approve it as drafted.","consequence":"The documentation set waits.","state":"withdrawn"}
                    """, subject: carrier.Reference, supersedes: ask.Id);
            else if (approval != "waiting") Answer(ask);
            if (approval == "answered-then-asked") Ask();
            if (approval == "answered-before-revision") f.Estate.Add("work-order", op, held.Replace("Cache fix", "Cache fix, revised", StringComparison.Ordinal), supersedes: order.Id);
        }
        Assert.False(f.Estate.Load().Findings.HasErrors, string.Join('\n', f.Estate.Load().Findings));
        const string path = "docs/releases/9.9.9/manifest.json";
        const string manifest = """{"format":"vericairn.docs/1","version":"9.9.9","documents":[]}""";
        if (set.StartsWith("carried", StringComparison.Ordinal) || set == "merged") f.Adapter.Files[(ChangesFlowTests.Head, path)] = manifest;
        PushedCommit setCommit = new(ChangesFlowTests.Head, "Release 9.9.9 documentation set") { Paths = [path, "docs/operator-guide.md", "docs/releases/frozen-sha256.json", "Directory.Build.props"] };
        f.Adapter.Commits = set switch
        {
            "carried-with-fix" => [setCommit, new(new string('d', 40), "FIX-90: Close the cache's path") { Paths = ["src/Widget/Cache.cs", "tests/Widget/CacheTests.cs"] }],
            "carried-with-other-change" => [setCommit, new(new string('d', 40), "Add an unrelated option") { Paths = ["src/Widget/Options.cs"] }],
            "carried-unreadable" => null,
            "carried-without-paths" => [new(ChangesFlowTests.Head, "Release 9.9.9 documentation set")],
            _ => [setCommit],
        };
        if (set == "merged") f.Adapter.Files[(ChangesFlowTests.Basis, path)] = manifest;
        if (set == "other-version") f.Adapter.Files[(ChangesFlowTests.Head, path)] = manifest.Replace("\"9.9.9\"", "\"9.9.8\"", StringComparison.Ordinal);
        if (set == "other-format") f.Adapter.Files[(ChangesFlowTests.Head, path)] = manifest.Replace("vericairn.docs/1", "vericairn.docs/9", StringComparison.Ordinal);
        f.Adapter.Text = "From 9.9.9 (FIX-90), close the widget cache's unchecked path traversal before anyone can reach it.";
        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.Equal(pushed, response.Ok);
        Assert.Equal(pushed ? 1 : 0, f.Adapter.Pushes);
        if (pushed) return;
        Assert.Equal("known-failure", response.Status);
        Assert.Contains("held work order FIX-90", response.Message, StringComparison.Ordinal);
        // The repository is read only for a release whose Security section is approved; the refusal then names the one push that may pass.
        bool approved = approval == "answered";
        Assert.Equal(approved, f.Adapter.FileReads > 0);
        Assert.Equal(approved, response.Message.Contains("documentation set", StringComparison.Ordinal));
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
