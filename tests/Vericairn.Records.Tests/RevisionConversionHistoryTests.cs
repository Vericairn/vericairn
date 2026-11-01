// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class RevisionConversionHistoryTests
{
    internal static TestEstate RecordedFixture()
    {
        var estate = new TestEstate(3);
        RecordEnvelope decision = estate.Add("decision", estate.OperatorId, $$"""{"title":"Generate history","statement":"Generate the lines.","summary":"Generate the lines.","synthesis":"Keep every revision.","effects":["Generate history."],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":[]}""");
        const string path = "design/example.md";
        const string text = "# Example\n\nRevision 1, October 13, 2026. Original wording.\n\nBody.\n";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        JsonObject initial = JsonNode.Parse(RevisionHistoryOrderingTests.Body(1, "Initial summary."))!.AsObject();
        initial.Remove("hashMode"); initial["hash"] = ContentHash.Sha256(bytes); initial["bytes"] = bytes.Length;
        RecordEnvelope old = estate.Add("document-revision", estate.OperatorId, initial.ToJsonString());
        var converted = RevisionLines.Convert(estate.Load(), path, bytes);
        JsonObject body = JsonNode.Parse(RevisionHistoryOrderingTests.Body(2, "Generate the history."))!.AsObject();
        body["hash"] = ContentHash.Sha256(converted.Content); body["bytes"] = converted.Content.Length;
        body["decision"] = decision.Id; body["revisionHistory"] = converted.History;
        estate.Add("document-revision", estate.OperatorId, body.ToJsonString(), supersedes: old.Id);
        estate.WriteFile(path, Encoding.UTF8.GetString(RevisionLines.Refresh(estate.Load(), path, converted.Content)));
        return estate;
    }

    [Fact]
    public void ConversionKeepsEveryLegacyExplanationAndRecordWithoutRewritingHistory()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope first = estate.Add("document-revision", estate.OperatorId, RevisionHistoryOrderingTests.Body(1, "A shorter record summary."));
        RecordEnvelope second = estate.Add("document-revision", estate.OperatorId, RevisionHistoryOrderingTests.Body(2, "Second summary."), supersedes: first.Id);
        string one = "  Revision 1, October 13, 2026. The complete original explanation.\r\nIts second line is retained.";
        string two = "Revision 2, October 13, 2026: carries decision `example`: Every word stays.";
        string body = "## Body\r\n\r\nCafé and trailing spaces. \r\n";
        byte[] original = Encoding.UTF8.GetBytes("\uFEFF# Example\r\n\r\n" + two + "\r\n\r\n" + one + "\r\n\r\n" + body);
        var converted = RevisionLines.Convert(estate.Load(), "design/example.md", original);
        Assert.Equal(2, converted.History.Count);
        JsonObject pin = JsonNode.Parse(RevisionHistoryOrderingTests.Body(3, "Convert history."))!.AsObject();
        pin["revisionHistory"] = converted.History;
        estate.Add("document-revision", estate.OperatorId, pin.ToJsonString(), supersedes: second.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        string rendered = Encoding.UTF8.GetString(RevisionLines.Refresh(model, "design/example.md", converted.Content));
        Assert.Contains(one, rendered, StringComparison.Ordinal);
        Assert.Contains(two, rendered, StringComparison.Ordinal);
        Assert.EndsWith(body, rendered, StringComparison.Ordinal);
        Assert.StartsWith("\uFEFF# Example\r\n\r\n<!-- vericairn:generated revisions -->\r\n", rendered, StringComparison.Ordinal);
        Assert.Equal(converted.Content, RevisionLines.AuthoredContent(Encoding.UTF8.GetBytes(rendered)));
        Assert.Equal(first.ComputedHash, model.ById[first.Id].ComputedHash);
    }

    [Theory]
    [InlineData("Revision 2, October 13, 2026: No record.")]
    [InlineData("   Revision 2, October 13, 2026: No record.")]
    [InlineData("Revision 1, October 13, 2026: First.\n\nRevision 1, October 13, 2026: Duplicate.")]
    [InlineData("Revision 1, October 13, 2026: First.\n\n## Body\n\nRevision 2, October 13, 2026: Hidden history.")]
    public void MissingDuplicateOrDisplacedHistoryIsRefused(string history)
    {
        using var estate = new TestEstate(3);
        estate.Add("document-revision", estate.OperatorId, RevisionHistoryOrderingTests.Body(1, "First."));
        Assert.Throws<InvalidOperationException>(() => RevisionLines.Convert(estate.Load(), "design/example.md", Encoding.UTF8.GetBytes("# Example\n\n" + history + "\n")));
    }
}
