// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.TestSupport;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// OPS-20: an automated observer that stops reporting becomes attention instead of reading as success. Every time below comes from a held
/// clock, so silence begins exactly where the interval plus the grace ends, and nothing waits on the machine's clock.
/// </summary>
public sealed class ObserverTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnObserverThatStopsReportingBecomesAttentionAndItsNextReportClearsIt()
    {
        using var f = new ObserverFixture();
        Assert.True(f.Declare("ci-main", "ci", 3600, 900).Ok);
        Assert.Equal("awaiting", f.Row("ci-main").State);

        // Its first report is due by the declaration plus the interval plus the grace; until then its silence is no attention.
        f.Clock.Now = Start.AddMinutes(75);
        ObserverRow due = f.Row("ci-main");
        Assert.Equal(("awaiting", false, "2026-11-21T10:15:00Z"), (due.State, due.Attention, due.ExpectedBy));
        Assert.Null(f.Notice("ci-main"));

        f.Clock.Now = Start.AddMinutes(75).AddSeconds(1);
        AttentionNotice never = f.Notice("ci-main")!;
        Assert.Equal(("failed check", "silent", "observer", "ci-main"), (never.Kind, never.State, never.SubjectType, never.SubjectId));
        Assert.Contains("has not reported since it was declared at 2026-11-21T09:00:00Z", never.Why, StringComparison.Ordinal);
        Assert.Contains("every PT1H, with PT15M of grace", never.Why, StringComparison.Ordinal);
        Assert.Contains("vericairn observers withdraw ci-main", never.NextAction, StringComparison.Ordinal);

        ObserversResponse reported = f.Report("ci-main", "passed", detail: "CI on main passed at its latest commit.");
        Assert.True(reported.Ok, reported.Message);
        ObserverRow observing = f.Row("ci-main");
        Assert.Equal(("observing", false), (observing.State, observing.Attention));
        Assert.Equal(("passed", f.Builder, "CI on main passed at its latest commit."), (observing.LastReport!.Outcome, observing.LastReport.By, observing.LastReport.Detail));
        Assert.Null(f.Notice("ci-main"));

        // The same notice stands while the silence lasts: elapsed time renews nothing, so an acknowledgment holds.
        f.Clock.Now = Start.AddMinutes(75).AddSeconds(1).AddMinutes(75).AddSeconds(1);
        AttentionNotice silent = f.Notice("ci-main")!;
        Assert.Equal("silent", silent.State);
        Assert.Contains("has not reported since 2026-11-21T10:15:01Z, when it reported passed", silent.Why, StringComparison.Ordinal);
        f.Clock.Now = f.Clock.Now.AddHours(5);
        Assert.Equal(silent.Revision, f.Notice("ci-main")!.Revision);
    }

    [Theory]
    [InlineData("quota-exhausted", "its quota is exhausted")]
    [InlineData("schedule-disabled", "its schedule is disabled")]
    [InlineData("unavailable", "it was unavailable")]
    public void AnExhaustedQuotaOrADisabledScheduleIsNotObservingAndNeverAPass(string reason, string said)
    {
        using var f = new ObserverFixture();
        Assert.True(f.Declare("nightly", "scheduled-workflow", 86400, 7200).Ok);
        Assert.True(f.Report("nightly", "passed").Ok);
        Assert.Null(f.Notice("nightly"));

        f.Clock.Now = Start.AddMinutes(10);
        Assert.True(f.Report("nightly", "not-observing", reason, detail: "The run did not start.").Ok);
        ObserverRow row = f.Row("nightly");
        Assert.Equal(("not-observing", true), (row.State, row.Attention));
        AttentionNotice notice = f.Notice("nightly")!;
        Assert.Equal(("failed check", "not-observing"), (notice.Kind, notice.State));
        Assert.Contains("could not observe: " + said + ": The run did not start.", notice.Why, StringComparison.Ordinal);
        Assert.Contains("Not observing is never a pass.", notice.Why, StringComparison.Ordinal);
        Assert.Contains("Only a report that it observed and passed clears this.", notice.NextAction, StringComparison.Ordinal);

        f.Clock.Now = Start.AddMinutes(20);
        Assert.True(f.Report("nightly", "passed").Ok);
        Assert.Null(f.Notice("nightly"));
    }

    [Fact]
    public void AReportNamesWhyItDidNotObserveAndOnlyThen()
    {
        using var f = new ObserverFixture();
        Assert.True(f.Declare("intake", "intake", 300, 600).Ok);
        Assert.Equal("refused", f.Report("intake", "not-observing").Problem!.Code);
        Assert.Equal("refused", f.Report("intake", "not-observing", "rate-limited").Problem!.Code);
        Assert.Equal("refused", f.Report("intake", "passed", "quota-exhausted").Problem!.Code);
        Assert.Equal("refused", f.Report("intake", "skipped").Problem!.Code);
        Assert.Null(f.Row("intake").LastReport);
    }

    [Fact]
    public void AFailedReportIsAttentionUntilAPassingReport()
    {
        using var f = new ObserverFixture();
        Assert.True(f.Declare("ci-main", "ci", 3600, 900).Ok);
        Assert.True(f.Report("ci-main", "failed", detail: "Two tests failed on Windows.").Ok);
        AttentionNotice notice = f.Notice("ci-main")!;
        Assert.Equal(("failed check", "failed"), (notice.Kind, notice.State));
        Assert.Contains("observed a failure: Two tests failed on Windows.", notice.Why, StringComparison.Ordinal);
        f.Clock.Now = Start.AddMinutes(30);
        Assert.True(f.Report("ci-main", "passed").Ok);
        Assert.Null(f.Notice("ci-main"));
    }

    [Fact]
    public void OnlyTheOperatorDeclaresOrWithdrawsAndOnlyADeclaredObserverReports()
    {
        using var f = new ObserverFixture();
        Assert.Equal("denied", f.Declare("ci-main", "ci", 3600, 900, by: f.Builder).Problem!.Code);
        Assert.Equal("not-found", f.Report("ci-main", "passed").Problem!.Code);
        Assert.True(f.Declare("ci-main", "ci", 3600, 900).Ok);
        Assert.Equal("refused", f.Report("ci-main", "passed", by: "01M7AAAAAAAAAAAAAAAAAAAAAA").Problem!.Code);

        f.Clock.Now = Start.AddHours(2);
        Assert.NotNull(f.Notice("ci-main"));
        // Machine-local observations are the operator's attention alone.
        Assert.DoesNotContain(f.Api.AttentionGet(f.Builder).Items, n => n.SubjectType == "observer");

        Assert.Equal("denied", f.Api.ObserverWithdraw(new(f.Builder, "ci-main", "Retired.")).Problem!.Code);
        Assert.Equal("refused", f.Api.ObserverWithdraw(new(f.Operator, "ci-main", " ")).Problem!.Code);
        ObserversResponse withdrawn = f.Api.ObserverWithdraw(new(f.Operator, "ci-main", "The workflow was retired."));
        Assert.True(withdrawn.Ok, withdrawn.Message);
        Assert.Empty(withdrawn.Observers);
        Assert.Null(f.Notice("ci-main"));
        Assert.Equal("not-found", f.Api.ObserverWithdraw(new(f.Operator, "ci-main", "Again.")).Problem!.Code);
    }

    [Fact]
    public void ADeclarationIsCheckedAndItsGraceDefaultsToAQuarterOfTheInterval()
    {
        using var f = new ObserverFixture();
        Assert.Equal("refused", f.Declare("CI main", "ci", 3600, 900).Problem!.Code);
        Assert.Equal("refused", f.Declare("ci-main", "cron", 3600, 900).Problem!.Code);
        Assert.Equal("refused", f.Declare("ci-main", "ci", 59, 0).Problem!.Code);
        Assert.Equal("refused", f.Declare("ci-main", "ci", 32 * 86400, 0).Problem!.Code);
        Assert.Equal("refused", f.Declare("ci-main", "ci", 3600, 8 * 86400).Problem!.Code);
        Assert.Equal("refused", f.Api.ObserverDeclare(new(f.Operator, "ci-main", "ci", 3600, null, "line\nbreak")).Problem!.Code);
        ObserversResponse declared = f.Api.ObserverDeclare(new(f.Operator, "status", "status-refresh", 3600, null, "Keeps the pinned status discussion current."));
        Assert.True(declared.Ok, declared.Message);
        ObserverRow row = Assert.Single(declared.Observers);
        Assert.Equal((900, "Keeps the pinned status discussion current.", f.Operator), (row.GraceSeconds, row.Summary, row.DeclaredBy));
        Assert.Contains("reports every PT1H, and its silence past a further PT15M is attention", declared.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderReportNeverReplacesANewerOneAndNoReportIsFromTheFuture()
    {
        using var f = new ObserverFixture();
        Assert.True(f.Declare("ci-main", "ci", 3600, 900).Ok);
        f.Clock.Now = Start.AddMinutes(30);
        Assert.True(f.Report("ci-main", "passed", at: "2026-11-21T09:25:00Z").Ok);
        ObserversResponse older = f.Report("ci-main", "failed", at: "2026-11-21T09:10:00Z");
        Assert.True(older.Ok);
        Assert.Contains("before its latest report at 2026-11-21T09:25:00Z, which stands", older.Message, StringComparison.Ordinal);
        Assert.Equal("passed", f.Row("ci-main").LastReport!.Outcome);
        Assert.Equal("refused", f.Report("ci-main", "passed", at: "2026-11-21T09:32:00Z").Problem!.Code);
        Assert.Equal("refused", f.Report("ci-main", "passed", at: "yesterday").Problem!.Code);
        // Silence counts from when the observation was made, not when it was received.
        f.Clock.Now = new DateTimeOffset(2026, 11, 21, 10, 40, 1, TimeSpan.Zero);
        Assert.Equal("silent", f.Row("ci-main").State);
    }

    [Fact]
    public void DeclarationsLiveBesideTheLiveStoreAndAnUnreadableFileIsAttentionNeverAPass()
    {
        using var f = new ObserverFixture();
        Assert.True(f.Declare("ci-main", "ci", 3600, 900).Ok);
        Assert.True(f.Report("ci-main", "passed").Ok);
        string file = Path.Combine(Path.GetDirectoryName(f.Api.Session.Live.Path)!, ObserverStore.FileName);
        Assert.True(File.Exists(file));
        Assert.DoesNotContain(f.Estate.Root, File.ReadAllText(file), StringComparison.Ordinal);

        // A second runtime on the same clone reads the same declarations.
        using (EstateService again = EstateService.Open(f.Estate.Root))
        {
            again.PolicyClock = f.Clock;
            Assert.Equal("observing", Assert.Single(again.Observers().Observers).State);
        }

        File.WriteAllText(file, "{ not json");
        ObserversResponse unreadable = f.Api.Observers();
        Assert.False(unreadable.Ok);
        Assert.Equal("unavailable", unreadable.Problem!.Code);
        AttentionNotice notice = Assert.Single(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectType == "observers");
        Assert.Equal(("failed check", "unreadable"), (notice.Kind, notice.State));
        Assert.Equal("unavailable", f.Report("ci-main", "passed").Problem!.Code);
    }

    [Fact]
    public void ObserversNeverChangeTheCanonicalStatusDocument()
    {
        using var f = new ObserverFixture();
        string before = f.Api.Status().Json.GetRawText();
        Assert.True(f.Declare("ci-main", "ci", 3600, 900).Ok);
        f.Clock.Now = Start.AddDays(1);
        Assert.NotNull(f.Notice("ci-main"));
        Assert.Equal(before, f.Api.Status().Json.GetRawText());
    }

    [Fact]
    public void TheStateIsDecidedByTheClockAloneAtEachBoundary()
    {
        var entry = new ObserverStore.Entry("nightly", "scheduled-workflow", 86400, 3600, null, "2026-11-20T00:00:00Z", "01M4TRNEE10EJ1WXNDK3H2FKKS",
            new ObserverStore.Report("2026-11-20T02:00:00Z", "passed", null, "01M4TRNEE10EJ1WXNDK3H2FKKS", null, "2026-11-20T02:00:05Z"));
        DateTimeOffset reported = new(2026, 11, 20, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal("observing", EstateService.EvaluateObserver(entry, reported.AddHours(25)).State);
        Assert.Equal("silent", EstateService.EvaluateObserver(entry, reported.AddHours(25).AddSeconds(1)).State);
        Assert.Equal("2026-11-21T03:00:00Z", EstateService.EvaluateObserver(entry, reported).ExpectedBy);
        Assert.Equal("silent", EstateService.EvaluateObserver(entry with { LastReport = entry.LastReport! with { Outcome = "failed" } }, reported.AddDays(2)).State);
    }

    private sealed class ObserverFixture : IDisposable
    {
        internal TestEstate Estate { get; } = new();
        internal EstateService Api { get; }
        internal HeldClock Clock { get; } = new(Start);
        internal string Operator => Estate.OperatorId;
        internal string Builder => Estate.BuilderId;

        internal ObserverFixture()
        {
            Api = EstateService.Open(Estate.Root);
            Api.PolicyClock = Clock;
        }

        internal ObserversResponse Declare(string name, string kind, int interval, int? grace, string? by = null) =>
            Api.ObserverDeclare(new(by ?? Operator, name, kind, interval, grace));

        internal ObserversResponse Report(string name, string outcome, string? reason = null, string? at = null, string? detail = null, string? by = null) =>
            Api.ObserverReport(new(by ?? Builder, name, outcome, reason, at, detail));

        internal ObserverRow Row(string name) => Api.Observers().Observers.Single(o => o.Name == name);

        internal AttentionNotice? Notice(string name) => Api.AttentionGet(Operator).Items.SingleOrDefault(n => n.SubjectType == "observer" && n.SubjectId == name);

        public void Dispose()
        {
            Api.Dispose();
            Estate.Dispose();
        }
    }
}
