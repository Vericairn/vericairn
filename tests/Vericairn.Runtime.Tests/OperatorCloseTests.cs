// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class OperatorCloseTests
{
    [Fact]
    public void ListingContainsExactlyTheLatestCompletedReviewedUnacceptedResultsAndTheirEvidenceWithoutSigning()
    {
        using var f = new OperatorCloseFixture(device: false);
        var first = f.Add("STEP-1");
        var second = f.Add("STEP-2", kind: "milestone");
        RecordEnvelope selfReview = f.Review(first.Result, self: true);
        f.Add("PARTIAL", outcome: "partial");
        f.Add("BLOCKED", outcome: "blocked");
        f.Add("FAILED-REVIEW", reviewOutcome: "fail");
        f.Add("FINDINGS", reviewOutcome: "findings");
        var accepted = f.Add("ACCEPTED");
        f.Accept(accepted.Result);
        var superseded = f.Add("NEWER-PARTIAL");
        f.Result(f.Attempt(superseded.Assignment), "partial");
        var revised = f.Add("REVISED-REVIEW");
        f.Review(revised.Result, "fail", revised.Review.Id);
        var oldResult = f.Add("OLD-REVIEW");
        f.Result(oldResult.Attempt, supersedes: oldResult.Result.Id);
        int count = f.Estate.Load().Records.Count;
        f.Api.BeforeSigningRecord = () => Assert.Fail("Listing must never sign.");
        string? loadedAt = f.Api.Session.Live.Meta("lastLoadedAt");

        OperatorCloseCandidatesResponse listed = f.Api.OperatorCloseCandidates();

        Assert.True(listed.Ok, listed.Problem?.Message);
        Assert.Equal(["STEP-1", "STEP-2"], listed.Items.Select(i => i.Key));
        Assert.Equal(loadedAt, f.Api.Session.Live.Meta("lastLoadedAt"));
        Assert.Equal(count, f.Estate.Load().Records.Count);
        OperatorCloseItem item = listed.Items[0];
        Assert.Equal(new(first.Order.Id, first.Order.ComputedHash), item.WorkOrder);
        Assert.Equal("Is STEP-1 complete?", item.ExitQuestion);
        Assert.Equal(new(first.Result.Id, first.Result.ComputedHash), item.Result);
        Assert.Equal("completed", item.Outcome);
        Assert.Equal("Finished work for inspection.", item.Summary);
        Assert.Equal(new[] { first.Review.Id, selfReview.Id }.Order(StringComparer.Ordinal), item.Reviews.Select(r => r.Id));
        Assert.Contains(item.Reviews, r => r.Id == first.Review.Id && r.Hash == first.Review.ComputedHash && r.Independence == "operator" && r.Summary == "Inspected the exact result.");
        Assert.Contains(item.Reviews, r => r.Id == selfReview.Id && r.Hash == selfReview.ComputedHash && r.Independence == "self");
        Assert.Equal(second.Result.Id, listed.Items[1].Result.Id);
        Assert.DoesNotContain(new ToolRegistry(f.Api, f.Estate.OperatorId).All, t => t.Name.StartsWith("operator_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialAndFullSelectionsSignExactlyOneAcceptancePerResultWithTheDisplayedBasis(bool all)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var second = f.Add("STEP-2", kind: "milestone", self: true);
        f.Add("STEP-3");
        f.Review(second.Result, self: true);
        OperatorCloseRequest shown = f.Request(all);
        OperatorCloseRequest request = all ? shown : shown with { Selection = [shown.Selection[1]] };
        int count = f.Estate.Load().Records.Count;
        int signatures = 0;
        f.Api.BeforeSigningRecord = () => signatures++;

        OperatorCloseResponse response = f.Api.OperatorClose(request, TestContext.Current.CancellationToken);

        Assert.True(response.Ok, response.Problem?.Message);
        Assert.Equal(request.Selection.Count, response.Written.Count);
        Assert.Equal(request.Selection.Count, signatures);
        Assert.Equal(count + request.Selection.Count, f.Estate.Load().Records.Count);
        foreach (OperatorCloseSelection selected in request.Selection)
        {
            RecordEnvelope record = Assert.Single(f.Estate.Load().OfType("acceptance"), a => a.Subject?.Id == selected.Result.Id);
            Assert.Equal(selected.Result.Hash, record.Subject!.Hash);
            Assert.Equal("result", record.Subject.Type);
            Assert.Equal(f.Estate.OperatorId, record.By);
            Assert.Equal(f.Device, Assert.Single(record.Signatures).Device);
            Assert.Equal(selected.Reviews.Select(r => r.Id).Order(StringComparer.Ordinal), record.Body.GetProperty("basis").EnumerateArray().Select(r => r.GetString()));
            Assert.Contains("operator close", record.Body.GetProperty("statement").GetString()!, StringComparison.Ordinal);
            Assert.Equal(selected.Result.Id == second.Result.Id, record.Body.GetProperty("self").GetBoolean());
            Assert.Contains(response.Written, r => r.Id == record.Id && r.Hash == record.ComputedHash);
        }
        Assert.Equal(all ? 0 : 2, f.Api.OperatorCloseCandidates().Items.Count);
        Assert.Equal(0, f.Api.Validate().Errors);
        using EstateService reopened = EstateService.Open(f.Estate.Root);
        Assert.Equal(all ? 0 : 2, reopened.OperatorCloseCandidates().Items.Count);
        Assert.False(reopened.OperatorClose(request, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(count + request.Selection.Count, f.Estate.Load().Records.Count);
    }

    [Theory]
    [InlineData("result-revision")]
    [InlineData("newer-result")]
    [InlineData("review-revision")]
    [InlineData("review-failed")]
    [InlineData("new-review")]
    [InlineData("work-order")]
    [InlineData("accepted")]
    [InlineData("result-hash")]
    [InlineData("review-hash")]
    [InlineData("missing-review")]
    [InlineData("unknown-key")]
    [InlineData("membership")]
    public void AnyChangedSelectedEvidenceRefusesTheWholeBatchBeforeSigningOrStoring(string change)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var second = f.Add("STEP-2");
        OperatorCloseRequest request = f.Request();
        OperatorCloseSelection selected = request.Selection[1];
        switch (change)
        {
            case "result-revision": f.Result(second.Attempt, supersedes: second.Result.Id); break;
            case "newer-result": f.Review(f.Result(f.Attempt(second.Assignment))); break;
            case "review-revision": f.Review(second.Result, supersedes: second.Review.Id); break;
            case "review-failed": f.Review(second.Result, "fail", second.Review.Id); break;
            case "new-review": f.Review(second.Result, self: true); break;
            case "work-order": f.Revise(second.Order, "exitQuestion", "Does the revised requirement hold?"); break;
            case "accepted": f.Accept(second.Result); break;
            case "result-hash": selected = selected with { Result = selected.Result with { Hash = "sha256:" + new string('0', 64) } }; break;
            case "review-hash": selected = selected with { Reviews = [selected.Reviews[0] with { Hash = "sha256:" + new string('0', 64) }] }; break;
            case "missing-review": selected = selected with { Reviews = [] }; break;
            case "unknown-key": selected = selected with { Key = "MISSING" }; break;
            case "membership": f.Add("STEP-3"); break;
            default: Assert.Fail(change); break;
        }
        request = request with { Selection = [request.Selection[0], selected] };
        f.Api.BeforeSigningRecord = () => Assert.Fail("Stale selections must refuse before signing.");
        AssertRefusedWithoutWrites(f, request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChangeWhileSigningOrValidatingStillRefusesEveryAcceptance(bool duringValidation)
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var second = f.Add("STEP-2");
        OperatorCloseRequest request = f.Request();
        int before = f.Estate.Load().Records.Count;
        bool changed = false;
        void Change()
        {
            if (changed) return;
            changed = true;
            f.Review(second.Result, supersedes: second.Review.Id);
        }
        if (duringValidation) f.Api.BeforeValidatingRecords = _ => Change();
        else f.Api.BeforeSigningRecord = Change;
        f.Api.BeforeStoringTogether = _ => Assert.Fail("A changed batch cannot start storage.");
        OperatorCloseResponse response = f.Api.OperatorClose(request, TestContext.Current.CancellationToken);
        Assert.False(response.Ok);
        Assert.Empty(response.Written);
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
        Assert.Equal(before + 1, f.Estate.Load().Records.Count);
    }

    [Fact]
    public void AStepWithoutAPassingReviewAndANonOperatorBothRefuseTheWholeAction()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var unreviewed = f.Add("STEP-2", reviewOutcome: "fail");
        OperatorCloseRequest request = f.Request();
        OperatorCloseSelection unavailable = new("STEP-2", new(unreviewed.Order.Id, unreviewed.Order.ComputedHash),
            new(unreviewed.Result.Id, unreviewed.Result.ComputedHash), [new(unreviewed.Review.Id, unreviewed.Review.ComputedHash)]);
        AssertRefusedWithoutWrites(f, request with { Selection = [.. request.Selection, unavailable] });
        AssertRefusedWithoutWrites(f, request with { By = f.Estate.BuilderId });
        AssertRefusedWithoutWrites(f, request with { By = null! });
    }

    [Fact]
    public void OptionalEstateSignaturesNeverPermitUnsignedOperatorClosings()
    {
        using var f = new OperatorCloseFixture(device: false);
        f.Add("STEP-1");
        AssertRefusedWithoutWrites(f, f.Request());
    }

    [Fact]
    public void InvalidAndDuplicateSelectionsRefuseWithoutWritesAndEmptyAllIsANoop()
    {
        using var f = new OperatorCloseFixture(device: false);
        Assert.True(f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken).Ok);
        f.Add("STEP-1");
        OperatorCloseRequest request = f.Request();
        AssertRefusedWithoutWrites(f, request with { Selection = null! });
        AssertRefusedWithoutWrites(f, request with { Selection = [null!] });
        AssertRefusedWithoutWrites(f, request with { Selection = [request.Selection[0] with { Reviews = null! }] });
        AssertRefusedWithoutWrites(f, request with { Selection = [request.Selection[0], request.Selection[0]] });
        AssertRefusedWithoutWrites(f, request with { Selection = [], All = false });
        AssertRefusedWithoutWrites(f, request with { Selection = [], All = true });
    }

    [Fact]
    public void AnUnselectedStepMayChangeWithoutDisplacingTheOperatorsSelection()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        var second = f.Add("STEP-2");
        OperatorCloseRequest request = f.Request(false);
        request = request with { Selection = [request.Selection[0]] };
        f.Review(second.Result, "fail", second.Review.Id);
        Assert.True(f.Api.OperatorClose(request, TestContext.Current.CancellationToken).Ok);
        Assert.Single(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void ARefusedSecondSignatureLeavesTheFirstAcceptanceUnstored()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        int signatures = 0;
        f.Api.BeforeSigningRecord = () => { if (++signatures == 2) throw new IOException("The operator declined the second signature."); };
        AssertRefusedWithoutWrites(f, f.Request());
        Assert.Equal(2, signatures);
    }

    [Fact]
    public void InvalidEstateRecordsRefuseBothInspectionAndClosingWithoutWrites()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        OperatorCloseRequest request = f.Request();
        f.Estate.Add("unknown-record", f.Estate.OperatorId, "{}");
        Assert.False(f.Api.OperatorCloseCandidates().Ok);
        AssertRefusedWithoutWrites(f, request);
    }

    [Fact]
    public void AStorageInterruptionRetainsItsReceiptsAndJournalAndRequiresReconciliation()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        OperatorCloseRequest request = f.Request();
        int stored = 0;
        f.Api.BeforeStoringTogether = _ => { if (++stored == 2) throw new IOException("The fixture storage stopped."); };
        OperatorCloseResponse response = f.Api.OperatorClose(request, TestContext.Current.CancellationToken);
        Assert.False(response.Ok);
        Assert.Equal("reconcile-required", response.Problem!.Code);
        RecordWritten receipt = Assert.Single(response.Written);
        Assert.Equal(receipt.Id, Assert.Single(f.Estate.Load().OfType("acceptance")).Id);
        Assert.Single(BatchJournal.All(f.Api.Session).Journals);
        Assert.False(f.Api.OperatorClose(request, TestContext.Current.CancellationToken).Ok);
        Assert.Single(f.Estate.Load().OfType("acceptance"));
    }

    [Fact]
    public void CancellationAfterSigningStillWritesNoAcceptanceOrJournal()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        OperatorCloseRequest request = f.Request();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        f.Api.BeforeSigningRecord = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => f.Api.OperatorClose(request, cancellation.Token));
        Assert.Empty(f.Estate.Load().OfType("acceptance"));
        Assert.Empty(BatchJournal.All(f.Api.Session).Journals);
    }

    private static void AssertRefusedWithoutWrites(OperatorCloseFixture f, OperatorCloseRequest request)
    {
        string[] before = [.. f.Estate.Load().Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal)];
        OperatorCloseResponse response = f.Api.OperatorClose(request, TestContext.Current.CancellationToken);
        Assert.False(response.Ok);
        Assert.NotNull(response.Problem);
        Assert.Empty(response.Written);
        Assert.Equal(before, f.Estate.Load().Records.Select(r => r.ComputedHash).Order(StringComparer.Ordinal));
        Assert.Empty(BatchJournal.All(f.Api.Session).Journals);
    }
}
