// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>Home's progress section: the report's summary in the state language, and the whole report opened from it, keyboard only.</summary>
[Collection("workbench")]
public class ProgressTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task HomeSummarizesTheProgressReportAndOpensTheWholeOfItKeyboardOnly()
    {
        string root = Seed.Temp("wb-progress");
        string scratch = Seed.Temp("wb-progress-scratch");
        string op = Seed.Init(root);
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "w1.json", """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Progress.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget"]}"""));
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "m1.json", """{"key":"M-1","kind":"milestone","title":"A milestone","intent":"Progress.","scope":["widget"],"exitQuestion":"Done?","inputs":[],"deliverables":["widget"]}"""));
        Seed.Cli("--estate", root, "--json", "records", "new", "work-status", "--by", op, "--subject", "work-order:" + Seed.LastId(root, "work-order"), "--body", Seed.Write(scratch, "submitted.json", """{"status":"submitted","note":"For the operator."}"""));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-progress")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));

            // Home shows one row per section of the report, worded in the state language.
            Assert.Equal(["Where the estate stands", "Work orders", "Published releases", "What comes next", "Next eligible work", "Waits on the operator"], shell.Progress.Select(r => r.Heading));
            Assert.Equal("2 current: 1 planned; 1 submitted, awaiting acceptance", shell.Progress[1].Detail);
            Assert.Equal("1 of 1 planned work order; 0 held by an after or a blocker", shell.Progress[4].Detail);
            Assert.Equal("asks: none; acceptances only the operator gives: 1", shell.Progress[5].Detail);
            Assert.StartsWith("pinned to source Vericairn/vericairn@0123456789ab · record format 2 · check: 0 errors, 0 warnings", shell.Progress[0].Detail, StringComparison.Ordinal);

            // Ctrl+Shift+P opens the whole report as the runtime renders it, and the keyboard lands in it.
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, null);
                await shell.PendingOperation;
            });
            ProgressDocument page = Assert.IsType<ProgressDocument>(shell.SelectedDocument);
            Assert.EndsWith(shell.Api!.Progress().Text, page.Text, StringComparison.Ordinal);
            Assert.Contains("    milestone work orders submitted: 1\n", page.Text, StringComparison.Ordinal);

            // A record written meanwhile, and F5: the summary and the open report follow the runtime, in place.
            Assert.True(shell.Api.NewRecord(new NewRecordRequest("work-order", op, Seed.Json("""{"key":"W-2","kind":"ordinary","title":"Ship the widget","intent":"Progress.","scope":["widget"],"exitQuestion":"Is it shipped?","inputs":[],"deliverables":["widget"]}"""))).Ok);
            await fixture.UiAsync(async () =>
            {
                window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
                await shell.PendingOperation;
            });
            Assert.Same(page, shell.Documents.OfType<ProgressDocument>().Single());
            Assert.Contains("    W-2  ordinary   Ship the widget [work-order ", page.Text, StringComparison.Ordinal);
            Assert.Equal("3 current: 2 planned; 1 submitted, awaiting acceptance", shell.Progress[1].Detail);

            using JsonDocument brief = JsonDocument.Parse("""{"outcome":"Widget","sources":[],"permittedChanges":["src/"],"prohibitedEffects":[],"workspace":["widget repository"],"limits":[],"expectedOutputs":["widget"],"reviewCriteria":["tests"],"recovery":"Record a result."}""");
            string assignment = shell.Api.Assign(new AssignRequest("W-1", op, op, brief.RootElement)).Written!.Id;
            string attempt = shell.Api.Attempt(new AttemptRequest(assignment, op, "widget repository", "Build it.")).Written!.Id;
            Assert.True(shell.Api.WorkProgress(new WorkProgressRequest(attempt, op, "build", "A live claim.")).Ok);
            await fixture.UiAsync(() => shell.RefreshProgressAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("A live claim.", page.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(shell.Progress, row => row.Heading == "Agent claim · build");

            // Enter on a row of the summary opens the report again once it was closed.
            await fixture.Ui(() => shell.Close(page));
            await fixture.UiAsync(async () =>
            {
                ListBox list = window.FindControl<ListBox>("ProgressList")!;
                list.SelectedIndex = 1;
                list.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await shell.PendingOperation;
            });
            Assert.IsType<ProgressDocument>(shell.SelectedDocument);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void TheSummaryReadsTheReportsOwnFiguresAndSaysWhenThereIsNoReport()
    {
        Assert.Equal([new ProgressRow("Progress", "unavailable: the runtime gave no progress report")], ProgressView.Summary(default));
        const string report = """
        {"format":"vericairn.progress/1",
         "standing":{"pin":{"describe":"release 0.5.2"},"recordFormat":{"format":2},"check":{"errors":1,"warnings":2,"findings":[]}},
         "workOrders":{"total":3,"states":[{"state":"planned","count":1,"workOrders":[]},{"state":"accepted (earlier revision)","count":2,"workOrders":[]}]},
         "releases":{"recorded":4,"published":[{"version":"0.5.1","publishedAt":"2026-10-14T10:00:00Z"},{"version":"0.5.2","publishedAt":"2026-10-15T13:28:26Z"}]},
         "next":{"releases":[{"workOrder":{"key":"REL-060","status":"planned"},"carries":[{},{},{}],"accepted":1}]},
         "eligible":{"planned":5,"eligible":[{},{}],"held":[{},{},{}]},
         "operator":{"asks":[{"state":"open"},{"state":"notified"},{"state":"open"}],"acceptances":{"milestoneWorkOrders":[{}],"milestoneResults":[],"releaseCandidates":[{}],"goals":[]}}}
        """;
        using JsonDocument document = JsonDocument.Parse(report);
        Assert.Equal(
            [
                new ProgressRow("Where the estate stands", "pinned to release 0.5.2 · record format 2 · check: 1 error, 2 warnings"),
                new ProgressRow("Work orders", "3 current: 1 planned; 2 accepted at an earlier revision; the current revision is not"),
                new ProgressRow("Published releases", "2 of the 4 recorded; the latest 0.5.2, published 2026-10-15"),
                new ProgressRow("What comes next", "REL-060 planned, 1 of 3 work orders it carries accepted"),
                new ProgressRow("Next eligible work", "2 of 5 planned work orders; 3 held by an after or a blocker"),
                new ProgressRow("Waits on the operator", "asks: 1 notified, delivery unknown; 2 open, awaiting an answer; acceptances only the operator gives: 2"),
            ],
            ProgressView.Summary(document.RootElement));

        using JsonDocument claims = JsonDocument.Parse("""{"format":"vericairn.progress/1","agentClaims":[{"attempt":"attempt-id","phase":"build","note":"Compiling."}]}""");
        ProgressRow row = ProgressView.Summary(claims.RootElement)[^1];
        Assert.Equal("Agent claim · build", row.Heading);
        Assert.Contains("Compiling.", row.Detail, StringComparison.Ordinal);
        Assert.Contains("not a worker observation", row.Detail, StringComparison.Ordinal);

        using JsonDocument signals = JsonDocument.Parse("""{"format":"vericairn.progress/1","runSignals":[{"run":"run-id","signals":[{"severity":"critical","code":"scope","summary":"Outside the brief.","attention":"ask-id"}]}]}""");
        ProgressRow observed = ProgressView.Summary(signals.RootElement)[^1];
        Assert.Equal("Worker · critical scope", observed.Heading);
        Assert.Contains("Outside the brief.", observed.Detail, StringComparison.Ordinal);
        Assert.Contains("ask ask-id", observed.Detail, StringComparison.Ordinal);
    }
}
