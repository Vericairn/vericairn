// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class DocumentRepairTests
{
    [Fact]
    public void RepairPreservesAuthoredCrLfBytesAndLeavesTheWholeFilePinToItsOwnWorkflow()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", Authored());
        const string before = "# Guide\r\n\r\nBefore.\r\n<!-- vericairn:generated releases -->\r\nstale\r\n<!-- vericairn:end releases -->\r\nAfter.\r\n";
        f.Estate.WriteFile("docs/guide.md", before);
        f.Add("document-revision", new JsonObject
        {
            ["path"] = "docs/guide.md", ["title"] = "Guide", ["hash"] = ContentHash.Sha256(Encoding.UTF8.GetBytes(before)),
            ["bytes"] = Encoding.UTF8.GetByteCount(before), ["mediaType"] = "text/markdown", ["revision"] = 1, ["summary"] = "Pin authored text.",
        });
        DocumentChangePlan plan = DocumentRepair.Prepare(f.Estate.Load(), declaration.Id);
        Assert.Equal(DocumentGenerationStatus.Success, plan.Status);
        DocumentOutputChange change = Assert.Single(plan.Changes);
        const string expected = "# Guide\r\n\r\nBefore.\r\n<!-- vericairn:generated releases -->\r\n| Version | Published | Release |\n|---|---|---|\n<!-- vericairn:end releases -->\r\nAfter.\r\n";
        Assert.Equal(expected, Encoding.UTF8.GetString(change.After!.Value.AsSpan()));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Estate.Root, "docs/guide.md")));
        File.WriteAllBytes(Path.Combine(f.Estate.Root, "docs/guide.md"), change.After.Value.ToArray());
        Assert.Contains(EstateChecks.Run(f.Estate.Load()), x => x.Code == "document.drift");
        Assert.Single(f.Estate.Load().OfType("document-revision"));
        Assert.Empty(DocumentChecks.Generated(f.Estate.Load(), declaration, expected));
    }

    [Fact]
    public void OneMissingRegionOrWholeGeneratorProducesNoRepairPlanOrFileChanges()
    {
        using var f = new PolicyFixture();
        JsonObject body = Authored();
        body["regions"]!.AsArray().Add(new JsonObject { ["id"] = "missing", ["generator"] = "example.documents/unavailable-region" });
        RecordEnvelope declaration = f.Add("living-document", body);
        const string before = "<!-- vericairn:generated releases -->\nstale\n<!-- vericairn:end releases -->\n<!-- vericairn:generated missing -->\nold\n<!-- vericairn:end missing -->\n";
        f.Estate.WriteFile("docs/guide.md", before);
        DocumentChangePlan refused = DocumentRepair.Prepare(f.Estate.Load(), declaration.Id);
        Assert.Equal(DocumentGenerationStatus.Unavailable, refused.Status);
        Assert.Empty(refused.Changes);
        Assert.Empty(refused.Rewritten);
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Estate.Root, "docs/guide.md")));
        using var old = new TestEstate(2);
        old.Add("living-document", old.OperatorId, """{"path":"docs/whole.md","title":"Whole","kind":"generated","source":"estate-file","generator":{"id":"example.documents/unavailable-whole","inputs":[]},"triggers":[{"on":"decision","level":"warn"}],"summary":"Retained legacy declaration."}""");
        Assert.Equal(DocumentGenerationStatus.Unavailable, DocumentRepair.Prepare(old.Load(), "docs/whole.md").Status);
        Assert.False(File.Exists(Path.Combine(old.Root, "docs/whole.md")));
    }

    [Theory]
    [InlineData("<!-- vericairn:end a -->\n<!-- vericairn:generated a -->\ntext\n<!-- vericairn:end a -->\n")]
    [InlineData("<!-- vericairn:generated a -->\n<!-- vericairn:generated b -->\n<!-- vericairn:end b -->\n<!-- vericairn:end a -->\n")]
    [InlineData("<!-- vericairn:generated a -->\n<!-- vericairn:end b -->\n")]
    [InlineData("<!-- vericairn:generated a -->\n<!-- vericairn:end a -->\n<!-- vericairn:generated a -->\n<!-- vericairn:end a -->\n")]
    public void StrayNestedCrossingAndDuplicateMarkersRefuseTheEntireRepair(string text)
    {
        Assert.False(GeneratedRegions.ValidMarkers(text));
        Assert.Null(GeneratedRegions.Content(text, "a"));
        Assert.Null(GeneratedRegions.Replace(text, "a", "new"));
    }

    [Fact]
    public void CaseAliasesAndUnprovedPathsCannotSelectOrRedirectARepair()
    {
        using var f = new PolicyFixture();
        RecordEnvelope declaration = f.Add("living-document", DocumentGenerationTests.Declaration());
        Directory.CreateDirectory(Path.Combine(f.Estate.Root, "Docs"));
        Assert.Equal(DocumentGenerationStatus.Error, DocumentRepair.Prepare(f.Estate.Load(), declaration.Id).Status);
        Directory.Delete(Path.Combine(f.Estate.Root, "Docs"));
        Assert.Equal(DocumentGenerationStatus.Error, DocumentRepair.Prepare(f.Estate.Load(), "docs/registers/decisions/unproved.md").Status);
        Assert.Throws<InvalidOperationException>(() => GeneratedDocumentVerification.SafeFile(f.Estate.Root, "../outside.md"));
        Assert.Throws<InvalidOperationException>(() => GeneratedDocumentVerification.SafeFile(f.Estate.Root, "records/decision.md"));
    }

    private static JsonObject Authored() => new()
    {
        ["path"] = "docs/guide.md", ["title"] = "Guide", ["kind"] = "authored", ["source"] = "estate-file",
        ["regions"] = new JsonArray(new JsonObject { ["id"] = "releases", ["generator"] = "vericairn.records/releases" }),
        ["checks"] = new JsonArray("matches-generator"), ["triggers"] = new JsonArray(new JsonObject { ["on"] = "decision", ["level"] = "warn" }),
        ["summary"] = "Authored guidance with generated releases.",
    };
}
