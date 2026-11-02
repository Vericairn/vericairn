// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

public sealed class CatalogRequestReconciliationTests
{
    [Fact]
    public void SimultaneousFirstRequestsUseCreationTimeThenStableIssueIdentity()
    {
        CatalogRequestObservation late = CatalogFixture.Issue("a", "first", 4);
        CatalogRequestObservation tie = CatalogFixture.Issue("c", "second", 1);
        CatalogRequestObservation canonical = CatalogFixture.Issue("b", "third", 1);
        CatalogRequestObservation comment = CatalogFixture.Comment("comment", "fourth", late, 5);
        CatalogRequestObservation repeat = CatalogFixture.Comment("repeat", "first", canonical, 6);
        CatalogRequestSnapshot snapshot = CatalogFixture.Snapshot(late, tie, canonical, comment, repeat);
        CatalogReconciliation result = CatalogRequestReconciler.Reconcile(CatalogFixture.Inventory, snapshot);
        Assert.True(result.Complete);
        CatalogCanonicalIssue issue = Assert.Single(result.Issues);
        Assert.Equal("b", issue.IssueId);
        Assert.Equal(["c", "a"], issue.Duplicates);
        Assert.Equal(4, result.Votes.Length);
        Assert.Equal(4, Assert.Single(result.Totals, t => t.EntryKey == "fixture.active").Active);
        Assert.Equal(0, Assert.Single(result.Totals, t => t.EntryKey == "fixture.remainder").Active);
        Assert.Equal(result.Votes.Length, result.Votes.Select(v => (v.AccountId, v.EntryKey)).Distinct().Count());
        CatalogReconciliation replayed = CatalogRequestReconciler.Reconcile(CatalogFixture.Inventory,
            snapshot with { Observations = snapshot.Observations.Reverse().Concat(snapshot.Observations).ToArray() });
        Assert.Equal(CatalogFixture.Json(result), CatalogFixture.Json(replayed));
    }

    [Fact]
    public void EditsWithdrawalDeletionAndRestorationChangeOneAccountEntryVote()
    {
        CatalogRequestObservation issue = CatalogFixture.Issue("issue", "author");
        CatalogRequestObservation added = CatalogFixture.Comment("comment", "voter", issue);
        CatalogRequestObservation edited = added with { Revision = 2, UpdatedAt = CatalogFixture.Time(3), Fields = added.Fields! with { IntendedUse = "Updated use", NoticeOptIn = true } };
        CatalogRequestObservation repeat = CatalogFixture.Comment("repeat", "voter", issue, 4);
        CatalogRequestObservation withdrawn = edited with { Revision = 3, UpdatedAt = CatalogFixture.Time(5), Fields = edited.Fields! with { Request = false } };
        CatalogRequestObservation restored = edited with { Revision = 4, UpdatedAt = CatalogFixture.Time(6) };
        CatalogRequestObservation deleted = restored with { Revision = 5, UpdatedAt = CatalogFixture.Time(7), Deleted = true, Fields = null };
        var history = new List<CatalogRequestObservation> { issue, added };
        Assert.Single(Reconcile(history).Votes, v => v.AccountId == "voter");
        history.Add(edited);
        Assert.True(Assert.Single(Reconcile(history).Votes, v => v.AccountId == "voter").NoticeOptIn);
        history.Add(repeat);
        Assert.False(Assert.Single(Reconcile(history).Votes, v => v.AccountId == "voter").NoticeOptIn);
        history.Add(withdrawn);
        Assert.DoesNotContain(Reconcile(history).Votes, v => v.AccountId == "voter");
        history.Add(restored);
        Assert.True(Assert.Single(Reconcile(history).Votes, v => v.AccountId == "voter").NoticeOptIn);
        history.Add(deleted);
        CatalogReconciliation result = Reconcile(history);
        Assert.DoesNotContain(result.Votes, v => v.AccountId == "voter");
        Assert.Single(result.Votes);
        Assert.DoesNotContain("Updated use", System.Text.Encoding.UTF8.GetString(CatalogFixture.Json(result)), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingAndReopeningDuplicatesNeverAddsVotesOrChangesCanonicalOrdering()
    {
        CatalogRequestObservation original = CatalogFixture.Issue("original", "author");
        CatalogRequestObservation duplicate = CatalogFixture.Issue("duplicate", "same-account", 2);
        CatalogRequestObservation closed = duplicate with { Revision = 2, UpdatedAt = CatalogFixture.Time(3), Closed = true };
        CatalogRequestObservation opened = closed with { Revision = 3, UpdatedAt = CatalogFixture.Time(4), Closed = false };
        CatalogReconciliation result = Reconcile([original, duplicate, closed, opened, duplicate]);
        Assert.Equal("original", Assert.Single(result.Issues).IssueId);
        Assert.Equal(["duplicate"], result.Issues[0].Duplicates);
        Assert.Equal(2, result.Votes.Length);
    }

    [Fact]
    public void MetadataOnlyRevisionsNeverUndoAWithdrawalOnAnotherObject()
    {
        CatalogRequestObservation issue = CatalogFixture.Issue("issue", "author");
        CatalogRequestObservation withdrawn = CatalogFixture.Comment("withdrawal", "author", issue, 2) with { Fields = issue.Fields! with { Request = false } };
        CatalogRequestObservation closed = issue with { Revision = 2, UpdatedAt = CatalogFixture.Time(3), Closed = true };
        CatalogRequestObservation reopened = closed with { Revision = 3, UpdatedAt = CatalogFixture.Time(4), Closed = false };
        CatalogReconciliation result = Reconcile([reopened, issue, withdrawn, closed]);
        Assert.Single(result.Issues);
        Assert.Empty(result.Votes);
        Assert.Equal(0, Assert.Single(result.Totals, t => t.EntryKey == "fixture.active").Active);
    }

    [Fact]
    public void OnlyAttributedHumanActionsOnTheMatchingEntryCount()
    {
        CatalogRequestObservation issue = CatalogFixture.Issue("issue", "author");
        CatalogRequestObservation comment = CatalogFixture.Comment("comment", "voter", issue);
        CatalogReconciliation result = Reconcile([
            issue,
            comment with { AccountKind = "bot" },
            comment with { ObjectId = "app", AccountKind = "app" },
            comment with { ObjectId = "unknown", Verified = false },
            comment with { ObjectId = "discussion", Fields = null },
            comment with { ObjectId = "offer", Fields = new("fixture.active", false, OfferToTest: true) },
            comment with { ObjectId = "mismatch", Fields = new("fixture.remainder", true) },
            comment with { ObjectId = "retired", Fields = new("fixture.retired", true) },
            CatalogFixture.Issue("bot-issue", "automated", 2) with { AccountKind = "bot" },
        ]);
        Assert.Equal("issue", Assert.Single(result.Issues).IssueId);
        Assert.Equal("author", Assert.Single(result.Votes).AccountId);
    }

    [Fact]
    public void EntryEditsMoveOnlyTheAuthorsRequestAndUncheckingKeepsTheTrackingIssue()
    {
        CatalogRequestObservation issue = CatalogFixture.Issue("issue", "author");
        CatalogRequestObservation moved = issue with { Revision = 2, UpdatedAt = CatalogFixture.Time(2), Fields = new("fixture.remainder", true) };
        CatalogReconciliation result = Reconcile([issue, moved]);
        Assert.Equal("fixture.remainder", Assert.Single(result.Votes).EntryKey);
        Assert.Equal("fixture.remainder", Assert.Single(result.Issues).EntryKey);
        CatalogRequestObservation withdrawn = moved with { Revision = 3, UpdatedAt = CatalogFixture.Time(3), Fields = moved.Fields! with { Request = false } };
        result = Reconcile([issue, moved, withdrawn]);
        Assert.Empty(result.Votes);
        Assert.Equal("fixture.remainder", Assert.Single(result.Issues).EntryKey);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("conflicting-replay")]
    [InlineData("account-change")]
    [InlineData("time-reversal")]
    [InlineData("missing-issue")]
    [InlineData("future")]
    [InlineData("wrong-timezone")]
    public void IncompleteConflictingAndUnattributableBasesNeverReportFreshZeroTotals(string fault)
    {
        CatalogRequestObservation issue = CatalogFixture.Issue("issue", "author");
        CatalogRequestSnapshot snapshot = CatalogFixture.Snapshot(issue);
        snapshot = fault switch
        {
            "incomplete" => snapshot with { Complete = false },
            "conflicting-replay" => snapshot with { Observations = [issue, issue with { Fields = new("fixture.remainder", true) }] },
            "account-change" => snapshot with { Observations = [issue, issue with { Revision = 2, UpdatedAt = CatalogFixture.Time(2), AccountId = "other" }] },
            "time-reversal" => snapshot with { Observations = [issue with { UpdatedAt = CatalogFixture.Time(5) }, issue with { Revision = 2, UpdatedAt = CatalogFixture.Time(4) }] },
            "missing-issue" => snapshot with { Observations = [CatalogFixture.Comment("comment", "voter", issue)] },
            "future" => snapshot with { Observations = [issue with { UpdatedAt = CatalogFixture.Time(60) }] },
            "wrong-timezone" => snapshot with { WindowEnd = snapshot.WindowEnd.ToOffset(TimeSpan.FromHours(1)) },
            _ => throw new InvalidOperationException(),
        };
        CatalogReconciliation result = CatalogRequestReconciler.Reconcile(CatalogFixture.Inventory, snapshot);
        Assert.False(result.Complete);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Votes);
        Assert.Empty(result.Totals);
        Assert.NotEmpty(result.Findings);
    }

    private static CatalogReconciliation Reconcile(IEnumerable<CatalogRequestObservation> observations)
    {
        CatalogReconciliation result = CatalogRequestReconciler.Reconcile(CatalogFixture.Inventory, CatalogFixture.Snapshot(observations.ToArray()));
        Assert.True(result.Complete, string.Join("; ", result.Findings));
        return result;
    }
}
