// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public sealed class StoredCompanionTests(HeadlessFixture ui)
{
    [Theory]
    [InlineData(false, "assignment")]
    [InlineData(true, "assignment")]
    [InlineData(false, "review")]
    [InlineData(true, "review")]
    [InlineData(false, "acceptance")]
    [InlineData(true, "acceptance")]
    [InlineData(false, "communication-setting")]
    [InlineData(true, "communication-setting")]
    public async Task EveryWriteEditorKeepsTheStoredIdentityAndRecoveryAcrossBothTransports(bool daemon, string type)
    {
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        fixture.DeclareCompanions(type);
        string status = string.Empty;
        string? writtenId = null;
        await ui.UiAsync(async () =>
        {
            if (type == "assignment")
            {
                var page = Composer("COMPANION-1", fixture.Author);
                fixture.InterruptPublication();
                await page.SubmitAsync(fixture.Api, fixture.Author, TestContext.Current.CancellationToken);
                status = page.Status;
                writtenId = page.WrittenId;
                Assert.Null(page.UncertainEffect);
            }
            else if (type is "review" or "acceptance")
            {
                RecordResponse subject = fixture.Api.Record(fixture.Order.Id)!;
                var page = new ReviewWorkspace(subject, await SubjectFacts.AboutAsync(fixture.Api, subject.Id, TestContext.Current.CancellationToken),
                    "Review the work", "Read the original record.", [], fixture.Author, "operator")
                {
                    ReviewSummary = "Read the work order.", AcceptanceStatement = "Accept this test work.",
                };
                fixture.InterruptPublication();
                if (type == "review") await page.SubmitReviewAsync(fixture.Api, TestContext.Current.CancellationToken);
                else await page.SubmitAcceptanceAsync(fixture.Api, TestContext.Current.CancellationToken);
                status = page.Status;
                writtenId = page.WrittenId;
                if (type == "review") Assert.Equal(writtenId, Assert.Single(page.Reviews).Id);
                Assert.Null(page.UncertainEffect);
            }
            else
            {
                var page = new CommunicationDocument(() => fixture.Api, () => fixture.Author);
                await page.RefreshAsync(TestContext.Current.CancellationToken);
                page.StatusDepth = "short";
                fixture.InterruptPublication();
                await page.SaveAsync(TestContext.Current.CancellationToken);
                status = page.Note;
                Assert.Null(page.UncertainEffect);
            }
        });

        Assert.StartsWith(type == "communication-setting" ? "Recorded " : "Written: ", status, StringComparison.Ordinal);
        RecordResponse record = Assert.Single(fixture.Api.Records(type));
        if (type is "assignment" or "review" or "acceptance") Assert.Equal(record.Id, writtenId);
        Assert.Contains(record.Id, status, StringComparison.Ordinal);
        Assert.Contains("reconcile-required: Record " + record.Id + " was stored.", status, StringComparison.Ordinal);
        Assert.Contains("Companion publication stopped", status, StringComparison.Ordinal);
        Assert.Contains("Reopen the estate or run docs regenerate", status, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outcome is unknown", status, StringComparison.Ordinal);
        Assert.Single(BatchJournal.All(fixture.Service.Session).Journals);
        Assert.Equal(1, fixture.Publications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DocumentReviewPresentsBothFieldsOfTheWriteContractWithoutTurningARefusalIntoStorage(bool stored)
    {
        // Clearance records cannot be generator inputs, so exercise this editor's response contract directly.
        const string recovery = "Record review was stored. Reconcile the pending companion outputs; never resubmit the record.";
        var response = stored
            ? new WriteOutcome(new("records/document-review/review.json", "review", "sha256:" + new string('a', 64)), new("reconcile-required", recovery, []))
            : WriteOutcome.Fail(new("refused", "The document could not be reviewed.", []));
        using var client = new VericairnClient(new HttpClient(new ReviewResponse(response)) { BaseAddress = new Uri("http://fixture.invalid/") });
        var page = new LivingDocumentsWorkspace { Summary = "Read the guide." };
        page.Fill([new("docs/guide.md", null, "declaration", "Guide", "due", "warn", [], null, [], [])]);
        await ui.UiAsync(() => page.SubmitAsync(client, "reviewer", TestContext.Current.CancellationToken));
        Assert.Null(page.UncertainEffect);
        if (stored)
        {
            Assert.StartsWith("Written: document review review.", page.Status, StringComparison.Ordinal);
            Assert.Contains("reconcile-required: " + recovery, page.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("refused", page.Status, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal("refused: The document could not be reviewed.", page.Status);
            Assert.DoesNotContain("Written:", page.Status, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARealRefusalStillLeavesNoWrittenAssignment(bool daemon)
    {
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        var page = Composer("MISSING", fixture.Author);
        await ui.UiAsync(() => page.SubmitAsync(fixture.Api, fixture.Author, TestContext.Current.CancellationToken));
        Assert.Null(page.WrittenId);
        Assert.StartsWith("not-found: ", page.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("was stored", page.Status, StringComparison.Ordinal);
        Assert.Null(page.UncertainEffect);
        Assert.Empty(fixture.Api.Records("assignment"));
    }

    private static AssignmentComposer Composer(string key, string author) => new(key, [new(author, "Test operator", "human")])
    {
        Outcome = "A stored record.", PermittedChanges = "test", Workspace = "test repository", ExpectedOutputs = "result",
        ReviewCriteria = "receipt retained", Recovery = "Reconcile the original companion outputs.",
    };

    private sealed class ReviewResponse(WriteOutcome outcome) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/documents/declaration/reviews", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(outcome.Written is null ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK)
            {
                Content = JsonContent.Create(outcome, options: ProtocolJson.Options),
            });
        }
    }
}
