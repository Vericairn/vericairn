// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

public sealed class GeneratedRevisionPinTests
{
    [Fact]
    public void ConversionRetainsOldPinsAndLinesAndNewPinsHashOnlyAuthoredBytes()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string line = "Revision 1, October 13, 2026. Preserve the full original wording.";
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\n" + line + "\n\nBody café.\n");
        byte[] oldBytes = RevisionPinFixture.Bytes(estate);
        RecordEnvelope old = RevisionPinFixture.Pin(service, estate.OperatorId, "A shorter summary.");
        Assert.Equal(ContentHash.Sha256(oldBytes), EstateModel.BodyString(old, "hash"));
        Assert.Equal(oldBytes, RevisionLines.PinnedContent(oldBytes, old));
        Assert.DoesNotContain(service.Check().Findings, f => f.Code == "document.drift");
        byte[] oldRecord = File.ReadAllBytes(Path.Combine(estate.Root, old.ExpectedPath));
        RecordEnvelope conversion = RevisionPinFixture.Pin(service, estate.OperatorId, "Convert the history.", convert: true);
        Assert.True(RevisionLines.IsAuthored(conversion));
        Assert.Equal(3, conversion.Format);
        Assert.Contains(line, Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)), StringComparison.Ordinal);
        Assert.Equal(oldRecord, File.ReadAllBytes(Path.Combine(estate.Root, old.ExpectedPath)));
        estate.WriteFile(RevisionPinFixture.Document, Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)).Replace("Body café.", "Changed body café.", StringComparison.Ordinal));
        RecordEnvelope newest = RevisionPinFixture.Pin(service, estate.OperatorId, "Change the authored body.");
        byte[] document = RevisionPinFixture.Bytes(estate);
        byte[] authored = RevisionLines.AuthoredContent(document);
        Assert.Equal(ContentHash.Sha256(authored), EstateModel.BodyString(newest, "hash"));
        Assert.Equal(authored.Length, newest.Body.GetProperty("bytes").GetInt64());
        Assert.DoesNotContain("Change the authored body.", Encoding.UTF8.GetString(authored), StringComparison.Ordinal);
        Assert.StartsWith("Revision 3, ", RevisionLines.FirstProseLine(Encoding.UTF8.GetString(document)), StringComparison.Ordinal);
        Assert.DoesNotContain(service.Check().Findings, f => f.Code is "document.drift" or "document.revisions");

        estate.WriteFile(RevisionPinFixture.Document, Encoding.UTF8.GetString(document).Replace("Change the authored body.", "A hand-edited generated line.", StringComparison.Ordinal));
        Assert.Equal(EstateModel.BodyString(newest, "hash"), ContentHash.Sha256(RevisionLines.AuthoredContent(RevisionPinFixture.Bytes(estate))));
        Assert.Contains(service.Check().Findings, f => f.Code == "document.revisions");
        RevisionPinFixture.Pin(service, estate.OperatorId, "Refresh the history.", render: true);
        Assert.DoesNotContain("A hand-edited generated line.", Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)), StringComparison.Ordinal);
        Assert.DoesNotContain(service.Check().Findings, f => f.Code is "document.drift" or "document.revisions");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConversionRefusesAnUnrecordedLineWithoutChangingFilesOrRecords(bool hasPin)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\nRevision 2, October 13, 2026: Unrecorded.\n\nBody.\n");
        if (hasPin) RevisionPinFixture.Pin(service, estate.OperatorId, "First pin.");
        byte[] before = RevisionPinFixture.Bytes(estate);
        string[] records = service.Session.Load().Records.Select(r => r.ComputedHash).ToArray();
        DocumentPinResponse response = service.DocumentPin(new(RevisionPinFixture.Document, estate.OperatorId, "Convert.") { ConvertRevisions = true });
        Assert.False(response.Ok);
        Assert.Contains("no record", response.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, RevisionPinFixture.Bytes(estate));
        Assert.Equal(records, service.Session.Load().Records.Select(r => r.ComputedHash));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedRevisionPublicationRecoversButPreservesAnInterveningEdit(bool intervening)
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\nBody.\n");
        using EstateService service = EstateService.Open(estate.Root);
        service.BeforePublishingDocuments = _ => throw new IOException("Injected document publication interruption.");
        DocumentPinResponse result = service.DocumentPin(new(RevisionPinFixture.Document, estate.OperatorId, "Initial history.") { ConvertRevisions = true });
        Assert.NotNull(result.Written);
        Assert.Equal("reconcile-required", result.Problem!.Code);
        DocumentOutputChange output = Assert.Single(Assert.Single(BatchJournal.All(service.Session).Journals).Outputs);
        if (intervening) estate.WriteFile(RevisionPinFixture.Document, "# Keep the intervening edit\n");
        service.Dispose();
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Single(reopened.Session.Load().OfType("document-revision"));
        if (intervening)
        {
            Assert.Equal("# Keep the intervening edit\n", Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)));
            Assert.Single(BatchJournal.All(reopened.Session).Journals);
        }
        else
        {
            Assert.Equal(output.AfterHash, ContentHash.Sha256(RevisionPinFixture.Bytes(estate)));
            Assert.Empty(BatchJournal.All(reopened.Session).Journals);
            Assert.DoesNotContain(reopened.Check().Findings, f => f.Code is "document.drift" or "document.revisions");
        }
    }

    [Fact]
    public void LivingDocumentsCompareThePinModeAndDetectStaleGeneratedLines()
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\nBody.\n");
        RecordEnvelope declaration = estate.Add("living-document", estate.OperatorId, JsonSerializer.Serialize(new
        {
            path = RevisionPinFixture.Document, title = "Example", kind = "authored", source = "estate-file",
            triggers = new[] { new { on = "decision", level = "warn" } }, summary = "Keep the design current.",
        }));
        using EstateService service = EstateService.Open(estate.Root);
        RevisionPinFixture.Pin(service, estate.OperatorId, "First history.", convert: true);
        Assert.False(Assert.Single(LivingDocuments.Derive(service.Session.Load())).Due);
        estate.WriteFile(RevisionPinFixture.Document, Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)).Replace("First history.", "Stale line.", StringComparison.Ordinal));
        Assert.True(Assert.Single(LivingDocuments.Derive(service.Session.Load())).Due);
        WriteOutcome review = service.DocumentReview(declaration.Id, new(estate.OperatorId, "current", "Attempt to clear stale generated lines."));
        Assert.False(review.Ok);
        Assert.Contains(review.Problem!.Findings, f => f.Message.Contains("revision-history", StringComparison.Ordinal));
        Assert.Empty(service.Session.Load().OfType("document-review"));
    }

    [Fact]
    public void AConcurrentPinDuringValidationCannotPublishAnIncompleteHistory()
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\nOriginal body.\n");
        using EstateService service = EstateService.Open(estate.Root);
        RevisionPinFixture.Pin(service, estate.OperatorId, "Initial history.", convert: true);
        estate.WriteFile(RevisionPinFixture.Document, Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)).Replace("Original body.", "Revised body.", StringComparison.Ordinal));
        RecordEnvelope? concurrent = null;
        // The concurrent revision arrives by a sync while this pin is judged: a runtime on this tree would wait for its write lock instead.
        service.BeforeValidatingRecords = _ =>
        {
            service.BeforeValidatingRecords = null;
            concurrent = UnlockedWriter.Sync(estate.Root, clone =>
            {
                using EstateService other = EstateService.Open(clone);
                return RevisionPinFixture.Pin(other, estate.OperatorId, "Concurrent revision.");
            });
        };
        DocumentPinResponse result = service.DocumentPin(new(RevisionPinFixture.Document, estate.OperatorId, "Losing revision."));
        Assert.False(result.Ok);
        Assert.Null(result.Written);
        Assert.Equal("conflict", result.Problem!.Code);
        Assert.Equal(concurrent!.Id, Assert.Single(service.Session.Load().Current("document-revision")).Id);
        Assert.DoesNotContain("Losing revision.", Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)), StringComparison.Ordinal);
        Assert.Contains("Concurrent revision.", Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)), StringComparison.Ordinal);
    }
}

internal static class RevisionPinFixture
{
    internal const string Document = "design/example.md";
    internal static byte[] Bytes(TestEstate estate) => File.ReadAllBytes(Path.Combine(estate.Root, Document));
    internal static RecordEnvelope Pin(EstateService service, string by, string summary, bool convert = false, bool render = false)
    {
        DocumentPinResponse response = service.DocumentPin(new(Document, by, summary) { ConvertRevisions = convert, RenderTemplate = render });
        Assert.True(response.Ok, JsonSerializer.Serialize(response.Problem));
        RecordEnvelope pin = service.Session.Load().ById[response.Written!.Id];
        Assert.Equal(RevisionLines.Ordinal(service.Session.Load(), pin), response.Revision);
        return pin;
    }
}
