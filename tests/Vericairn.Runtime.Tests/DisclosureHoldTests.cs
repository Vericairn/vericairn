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
    // passes the hold once the operator has answered the ask that begins "Approve the Security section of <version>" with a decision
    // recording "The Security section of <version> is approved for <key>." and naming the held work order, after its current revision,
    // with no ask about the carrier still waiting; every other push stays held, and its refusal says why the push is not the set.
    [Theory]
    [InlineData("none", "carried", false, null)]
    [InlineData("answered-other-question", "carried", false, null)]
    [InlineData("answered-incidental", "carried", false, null)]
    [InlineData("answered-declining", "carried", false, null)]
    [InlineData("answered-for-another-key", "carried", false, null)]
    [InlineData("answered-not-naming", "carried", false, null)]
    [InlineData("waiting", "carried", false, null)]
    [InlineData("withdrawn", "carried", false, null)]
    [InlineData("answered-then-asked", "carried", false, null)]
    [InlineData("answered-before-revision", "carried", false, null)]
    [InlineData("answered-other-product", "carried", false, null)]
    [InlineData("answered", "carried", true, null)]
    [InlineData("answered", "carried-with-fix", true, null)]
    [InlineData("answered", "carried-with-mention", false, "is not a held fix")]
    [InlineData("answered", "carried-with-lowercase-key", false, "is not a held fix")]
    [InlineData("answered", "carried-with-joined-key", false, "is not a held fix")]
    [InlineData("answered", "carried-with-other-change", false, "is not a held fix")]
    [InlineData("answered", "carried-with-docs-code", false, "is not a held fix")]
    [InlineData("answered", "carried-with-build-change", false, "in more than the version")]
    [InlineData("answered", "carried-messages-unreadable", false, "messages of the commits it carries could not be read")]
    [InlineData("answered", "carried-paths-unreadable", false, "paths its commits change could not be read")]
    [InlineData("answered", "absent", false, "holds no documentation set")]
    [InlineData("answered", "merged", false, "already holds that release's documentation set")]
    [InlineData("answered", "other-version", false, "not that release's documentation set")]
    [InlineData("answered", "other-format", false, "not that release's documentation set")]
    public void OnlyTheCarryingReleasesDocumentationSetPassesOnceItsSecuritySectionIsApproved(string approval, string set, bool pushed, string? reason)
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
        // The Reviewer's probes: another question about the carrier, one that mentions the Security section only in passing, and
        // answers that decline, approve another work order, or leave the held work order out of what they affect.
        string question = approval switch
        {
            "answered-other-question" => "Which day should 9.9.9 be submitted?",
            "answered-incidental" => "Should the Security section wait until after the submission date?",
            _ => "Approve the Security section of 9.9.9 as drafted, covering FIX-90?",
        };
        RecordEnvelope Ask() => f.Estate.Add("ask", op, $$"""
            {"to":"{{op}}","question":"{{question}}","recommendation":"Approve it as drafted.","consequence":"The documentation set waits.","state":"open"}
            """, subject: carrier.Reference);
        string effect = approval switch
        {
            "answered-declining" => "FIX-90 stays out of the Security section and stays held.",
            "answered-for-another-key" => "The Security section of 9.9.9 is approved for FIX-91.",
            _ => "The Security section of 9.9.9 is approved for FIX-90.",
        };
        string affects = approval == "answered-not-naming" ? "" : "\"" + order.Id + "\"";
        void Answer(RecordEnvelope ask) => f.Estate.Add("decision", op, $$"""
            {"title":"Security section","statement":"As you recommend.","summary":"The Security section of 9.9.9.","synthesis":"The operator answered the ask about the section.","effects":["{{effect}}"],"affects":[{{affects}}],"decidedBy":"{{op}}","decidedAt":"{{f.Estate.Clock.AddSeconds(1):yyyy-MM-ddTHH:mm:ssZ}}","stake":"medium","notAuthorized":[],"answers":["{{ask.Id}}"]}
            """);
        if (approval != "none")
        {
            RecordEnvelope ask = Ask();
            if (approval == "withdrawn")
                f.Estate.Add("ask", op, $$"""
                    {"to":"{{op}}","question":"{{question}}","recommendation":"Approve it as drafted.","consequence":"The documentation set waits.","state":"withdrawn"}
                    """, subject: carrier.Reference, supersedes: ask.Id);
            else if (approval != "waiting") Answer(ask);
            if (approval == "answered-then-asked") Ask();
            if (approval == "answered-before-revision") f.Estate.Add("work-order", op, held.Replace("Cache fix", "Cache fix, revised", StringComparison.Ordinal), supersedes: order.Id);
        }
        Assert.False(f.Estate.Load().Findings.HasErrors, string.Join('\n', f.Estate.Load().Findings));

        const string path = "docs/releases/9.9.9/manifest.json";
        const string manifest = """{"format":"vericairn.docs/1","version":"9.9.9","documents":[]}""";
        const string props = "<Project>\n  <PropertyGroup>\n    <VersionPrefix>9.9.8</VersionPrefix>\n    <VersionSuffix>dev</VersionSuffix>\n  </PropertyGroup>\n</Project>\n";
        if (set.StartsWith("carried", StringComparison.Ordinal) || set == "merged") f.Adapter.Files[(ChangesFlowTests.Head, path)] = manifest;
        if (set == "merged") f.Adapter.Files[(ChangesFlowTests.Basis, path)] = manifest;
        if (set == "other-version") f.Adapter.Files[(ChangesFlowTests.Head, path)] = manifest.Replace("\"9.9.9\"", "\"9.9.8\"", StringComparison.Ordinal);
        if (set == "other-format") f.Adapter.Files[(ChangesFlowTests.Head, path)] = manifest.Replace("vericairn.docs/1", "vericairn.docs/9", StringComparison.Ordinal);
        // The set sets the new version in the build properties and changes nothing else there, compared with the target's file.
        f.Adapter.Files[(ChangesFlowTests.Basis, "Directory.Build.props")] = props;
        f.Adapter.Files[(ChangesFlowTests.Head, "Directory.Build.props")] = set == "carried-with-build-change"
            ? props.Replace("9.9.8", "9.9.9", StringComparison.Ordinal).Replace("  </PropertyGroup>", "    <Compile Include=\"docs/**/*.cs\" />\n  </PropertyGroup>", StringComparison.Ordinal)
            : props.Replace("9.9.8", "9.9.9", StringComparison.Ordinal);
        string other = new('d', 40);
        (string Message, string[] Paths)? second = set switch
        {
            "carried-with-fix" => ("FIX-90: Close the cache's path", ["src/Widget/Cache.cs", "tests/Widget/CacheTests.cs"]),
            "carried-with-mention" => ("Add an option; unlike FIX-90 it needs no hold", ["src/Widget/Options.cs"]),
            "carried-with-lowercase-key" => ("fix-90: Add an option", ["src/Widget/Options.cs"]),
            "carried-with-joined-key" => ("FIX-90_followup: Add an option", ["src/Widget/Options.cs"]),
            "carried-with-other-change" => ("Add an unrelated option", ["src/Widget/Options.cs"]),
            "carried-with-docs-code" => ("Add a documentation helper", ["docs/helper.cs"]),
            _ => null,
        };
        f.Adapter.Commits = set == "carried-messages-unreadable" ? null
            : [new(ChangesFlowTests.Head, "Release 9.9.9 documentation set"), .. second is { } c ? [new PushedCommit(other, c.Message)] : Array.Empty<PushedCommit>()];
        var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ChangesFlowTests.Head] = [path, "docs/operator-guide.md", "docs/releases/frozen-sha256.json", "Directory.Build.props", "llms.txt"],
        };
        if (second is { } added) paths[other] = added.Paths;
        f.Adapter.Paths = set == "carried-paths-unreadable" ? null : paths;
        f.Adapter.Text = "From 9.9.9 (FIX-90), close the widget cache's unchecked path traversal before anyone can reach it.";

        ChangeResponse response = f.Service.ChangesPush(f.Push);
        Assert.Equal(pushed, response.Ok);
        Assert.Equal(pushed ? 1 : 0, f.Adapter.Pushes);
        if (pushed) return;
        Assert.Equal("known-failure", response.Status);
        Assert.Contains("held work order FIX-90", response.Message, StringComparison.Ordinal);
        // The repository is read only for a release whose Security section is approved; the refusal then names the one push that may
        // pass, and why this one is not it.
        bool approved = approval == "answered";
        Assert.Equal(approved, f.Adapter.FileReads > 0);
        Assert.Equal(approved, response.Message.Contains("documentation set", StringComparison.Ordinal));
        if (reason is not null) Assert.Contains(reason, response.Message, StringComparison.Ordinal);
        if (set.StartsWith("carried-with", StringComparison.Ordinal) && set != "carried-with-build-change") Assert.Contains(other, response.Message, StringComparison.Ordinal);
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
