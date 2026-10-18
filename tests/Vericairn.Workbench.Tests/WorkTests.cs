// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>The work screens: the loop from a composed assignment through the inbox to a review and an acceptance, prepared actions pinned to their subject, products and comparisons.</summary>
[Collection("workbench")]
public class WorkTests(HeadlessFixture fixture)
{
    private const string Order = """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Exercise the loop.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget.txt"]}""";
    private const string Completed = """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"passed","summary":"Pass."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""";

    [Fact]
    public async Task TheLoopFromComposedAssignmentThroughTheInboxToAnAcceptance()
    {
        string root = Seed.Temp("wb-loop");
        string scratch = Seed.Temp("wb-loop-scratch");
        string op = Seed.Init(root);
        string agent = Seed.Admit(root, op, scratch, "Agent One", "agent", "contributor");
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "order.json", Order));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-loop"))) { ActingOverride = op };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("Operator (named with --as; no device of this principal is held here, so its records will be unsigned)", shell.ActingPrincipal);

            // The work order page shows the brief and no assignment yet.
            await fixture.UiAsync(() => shell.OpenWorkOrderAsync("W-1", cancellationToken: TestContext.Current.CancellationToken));
            WorkOrderDocument page = Assert.IsType<WorkOrderDocument>(shell.SelectedDocument);
            Assert.Equal("W-1: Build the widget", page.Heading);
            Assert.Empty(page.Assignments);

            // The composer sends nothing until asked, then the runtime validates and signs.
            AssignmentComposer composer = (await fixture.UiAsync(() => shell.OpenComposerAsync("W-1", cancellationToken: TestContext.Current.CancellationToken)))!;
            composer.Contributor = composer.Contributors.Single(c => c.Id == agent);
            composer.Outcome = "A widget that builds and passes its tests.";
            composer.PermittedChanges = "the widget sources\nthe widget tests";
            composer.ProhibitedEffects = "no publication";
            composer.Workspace = "example repository, main branch";
            composer.ExpectedOutputs = "widget.txt";
            composer.ReviewCriteria = "tests pass";
            composer.Recovery = "Record a result before stopping.";
            Assert.Equal("Not sent.", composer.Status);
            await fixture.UiAsync(() => composer.SubmitAsync(shell.Api!, shell.ActingPrincipalId, cancellationToken: TestContext.Current.CancellationToken));
            Assert.StartsWith("Written: assignment", composer.Status, StringComparison.Ordinal);
            string assignment = composer.WrittenId!;

            // The contributor's side: an attempt and a completed result through the same API.
            WriteOutcome attempt = shell.Api!.Attempt(new AttemptRequest(assignment, agent, "a clone of example at main", "Build it."));
            Assert.True(attempt.Ok, attempt.Problem?.Message);
            WriteOutcome result = shell.Api.Result(new ResultRequest(attempt.Written!.Id, agent, Seed.Json(Completed)));
            Assert.True(result.Ok, result.Problem?.Message);

            // Home and the inbox both show the claim to inspect.
            await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains(shell.Attention, a => a.SubjectType == "result" && a.SubjectId == result.Written!.Id);

            // The work order page lists the assignment; Enter on it opens the assignment page with the attempt and its result.
            await fixture.UiAsync(() => shell.OpenWorkOrderAsync("W-1", cancellationToken: TestContext.Current.CancellationToken));
            page = Assert.IsType<WorkOrderDocument>(shell.SelectedDocument);
            Assert.Equal(assignment, Assert.Single(page.Assignments).Id);
            await fixture.UiAsync(async () =>
            {
                window.UpdateLayout();
                ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.DataContext is WorkOrderDocument);
                list.SelectedIndex = 0;
                (list.ContainerFromIndex(0) as Control)?.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await shell.PendingOperation;
            });
            AssignmentDocument assignmentPage = Assert.IsType<AssignmentDocument>(shell.SelectedDocument);
            Assert.Equal("W-1: assignment " + assignment[..12] + " to Agent One", assignmentPage.Heading);
            Assert.Contains("- the widget tests", assignmentPage.BriefText, StringComparison.Ordinal);
            AttemptView attemptView = Assert.Single(assignmentPage.Attempts);
            Assert.Equal(result.Written!.Id, attemptView.ResultId);
            Assert.Equal(result.Written.Id, assignmentPage.LatestResultId);
            Assert.Contains("awaiting review", attemptView.Detail, StringComparison.Ordinal);
            ResultInboxDocument inbox = await fixture.Ui(shell.OpenInbox);
            InboxRow row = Assert.Single(inbox.Rows);
            Assert.Equal(result.Written!.Id, row.ResultId);
            Assert.Equal("Agent One", row.Contributor);

            // Keyboard: Enter on the inbox row opens the review workspace for that exact result.
            await fixture.UiAsync(async () =>
            {
                window.UpdateLayout();
                ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.DataContext is ResultInboxDocument);
                list.SelectedIndex = 0;
                (list.ContainerFromIndex(0) as Control)?.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await shell.PendingOperation;
            });
            ReviewWorkspace workspace = Assert.IsType<ReviewWorkspace>(shell.SelectedDocument);
            Assert.Equal(result.Written.Id, workspace.SubjectId);
            Assert.Equal($"result:{result.Written.Id}:{result.Written.Hash}", workspace.Prepared.Subject);
            Assert.Equal("operator", workspace.Independence);
            Assert.False(workspace.SelfAcceptance);
            Assert.Contains("Done.", workspace.SummaryText, StringComparison.Ordinal);
            Assert.Contains("tests: passed Pass.", workspace.SummaryText, StringComparison.Ordinal);
            Assert.Empty(workspace.Reviews);

            // A review, then an acceptance citing it; both pinned to the result's hash and signed by the runtime.
            // The workspace's own review is a new fact it has just shown, so the acceptance draft stays reusable.
            workspace.ReviewSummary = "Read the result, re-ran the checks it names.";
            workspace.ReviewFindings = "info: the limitations list is empty and that matches the checks";
            await fixture.UiAsync(() => workspace.SubmitReviewAsync(shell.Api, cancellationToken: TestContext.Current.CancellationToken));
            Assert.StartsWith("Written: review", workspace.Status, StringComparison.Ordinal);
            Assert.True(await fixture.UiAsync(() => workspace.CheckPreparedAsync(shell.Api, cancellationToken: TestContext.Current.CancellationToken)), workspace.PreparedState);
            ReviewRow review = Assert.Single(workspace.Reviews);
            await fixture.Ui(() => { review.Selected = true; workspace.AcceptanceStatement = "Accepted on the review."; });
            await fixture.UiAsync(() => workspace.SubmitAcceptanceAsync(shell.Api, cancellationToken: TestContext.Current.CancellationToken));
            Assert.StartsWith("Written: acceptance", workspace.Status, StringComparison.Ordinal);

            RecordResponse acceptance = shell.Api.Record(workspace.WrittenId!)!;
            Assert.Equal(review.Id, acceptance.Envelope.GetProperty("body").GetProperty("basis")[0].GetString());
            Assert.Equal(result.Written.Hash, acceptance.Envelope.GetProperty("subject").GetProperty("hash").GetString());

            await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.DoesNotContain(shell.Attention, a => a.SubjectType == "result");
            Assert.Equal("accepted", Assert.Single(shell.Assignments).State);
            Assert.Equal(0, Seed.Run("--estate", root, "estate", "check"));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task PreparedActionsStayPinnedToTheirSubjectAndRefusalsShowEveryFinding()
    {
        string root = Seed.Temp("wb-prepared");
        string scratch = Seed.Temp("wb-prepared-scratch");
        string op = Seed.Init(root);
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "order.json", Order));
        string orderId = Seed.LastId(root, "work-order");

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-prepared"))) { ActingOverride = op };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
        IEstateApi api = shell.Api!;

        // Reusable while the subject is unchanged.
        ReviewWorkspace workspace = (await fixture.UiAsync(() => shell.OpenReviewAsync(orderId, cancellationToken: TestContext.Current.CancellationToken)))!;
        Assert.True((await workspace.CheckPreparedAsync(api, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.StartsWith("reusable", workspace.PreparedState, StringComparison.Ordinal);

        // A new fact about the subject (a review written elsewhere) makes the preparation need review: the draft is kept and nothing is sent.
        RecordResponse order = api.Record(orderId)!;
        WriteOutcome elsewhere = api.NewRecord(new NewRecordRequest("review", op, Seed.Json("""{"independence":"self","outcome":"pass","summary":"Reviewed on another device.","findings":[]}"""), $"work-order:{orderId}:{order.Hash}"));
        Assert.True(elsewhere.Ok, elsewhere.Problem?.Message);
        Assert.False((await workspace.CheckPreparedAsync(api, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.StartsWith("needs review", workspace.PreparedState, StringComparison.Ordinal);
        workspace.ReviewSummary = "Would have been sent.";
        await workspace.SubmitReviewAsync(api, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(workspace.PreparedState, workspace.Status);
        Assert.Equal("Would have been sent.", workspace.ReviewSummary);
        Assert.Null(workspace.WrittenId);
        Assert.Single(api.Records("review"));

        // A newer revision supersedes the subject: the preparation is withdrawn, the draft stays readable, nothing can be sent.
        WriteOutcome revised = api.NewRecord(new NewRecordRequest("work-order", op, Seed.Json(Order.Replace("Build the widget", "Build the revised widget", StringComparison.Ordinal)), null, orderId));
        Assert.True(revised.Ok, revised.Problem?.Message);
        Assert.False((await workspace.CheckPreparedAsync(api, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.StartsWith("withdrawn", workspace.PreparedState, StringComparison.Ordinal);
        workspace.AcceptanceStatement = "Would have been accepted.";
        await workspace.SubmitAcceptanceAsync(api, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(workspace.PreparedState, workspace.Status);
        Assert.Empty(api.Records("acceptance"));
        Assert.Equal(PreparedState.Withdrawn, PreparedAction.Check(workspace.Prepared, null, string.Empty));

        // The comparison of the revision with the record it supersedes: raw sides and the line difference.
        await fixture.UiAsync(() => shell.OpenComparisonAsync(revised.Written!.Id, cancellationToken: TestContext.Current.CancellationToken));
        ComparisonDocument comparison = Assert.IsType<ComparisonDocument>(shell.SelectedDocument);
        Assert.Equal(orderId, comparison.LeftTitle);
        Assert.Contains("-", comparison.DiffText.Split('\n').Where(l => l.Contains("Build the widget", StringComparison.Ordinal)).Select(l => l[..1]));
        Assert.Contains("+", comparison.DiffText.Split('\n').Where(l => l.Contains("Build the revised widget", StringComparison.Ordinal)).Select(l => l[..1]));
        Assert.Contains("removed", comparison.Summary, StringComparison.Ordinal);

        // A refusal shows every finding the runtime returns; nothing is written.
        ReviewWorkspace fresh = (await fixture.UiAsync(() => shell.OpenReviewAsync(revised.Written!.Id, cancellationToken: TestContext.Current.CancellationToken)))!;
        Assert.True((await fresh.CheckPreparedAsync(api, cancellationToken: TestContext.Current.CancellationToken)), fresh.PreparedState);
        fresh.ReviewSummary = string.Empty;
        await fresh.SubmitReviewAsync(api, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(fresh.WrittenId);
        Assert.Contains("ERROR", fresh.Status, StringComparison.Ordinal);
        Assert.Contains("summary", fresh.Status, StringComparison.Ordinal);
        fresh.AcceptanceStatement = string.Empty;
        await fresh.SubmitAcceptanceAsync(api, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(fresh.WrittenId);
        Assert.Contains("ERROR", fresh.Status, StringComparison.Ordinal);
        Assert.Contains("statement", fresh.Status, StringComparison.Ordinal);
        Assert.Single(api.Records("review"));
        Assert.Empty(api.Records("acceptance"));

        // Without an admitted device held here nothing can be authored, and the surface says so before any request.
        var readOnly = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-readonly")));
        await fixture.UiAsync(() => readOnly.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(readOnly.ActingPrincipalId);
        AssignmentComposer composer = (await fixture.UiAsync(() => readOnly.OpenComposerAsync("W-1", cancellationToken: TestContext.Current.CancellationToken)))!;
        await composer.SubmitAsync(readOnly.Api!, readOnly.ActingPrincipalId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.StartsWith("No acting principal", composer.Status, StringComparison.Ordinal);
        ReviewWorkspace unsigned = (await fixture.UiAsync(() => readOnly.OpenReviewAsync(revised.Written!.Id, cancellationToken: TestContext.Current.CancellationToken)))!;
        Assert.StartsWith("No acting principal", unsigned.Declaration, StringComparison.Ordinal);
        await unsigned.SubmitAcceptanceAsync(readOnly.Api!, cancellationToken: TestContext.Current.CancellationToken);
        Assert.StartsWith("No acting principal", unsigned.Status, StringComparison.Ordinal);
        Assert.Empty(api.Records("acceptance"));
    }

    [Fact]
    public async Task ProductsPageInboxShortcutAndTargetsOfTheWorkScreens()
    {
        string root = Seed.Temp("wb-products");
        string scratch = Seed.Temp("wb-products-scratch");
        string op = Seed.Init(root);
        Seed.Cli("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", Seed.Write(scratch, "product.json", """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free.","license":"Apache-2.0"}],"surfaces":[{"name":"CLI","slug":"cli","summary":"The command line."}]}"""));
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "order.json", Order));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-products"))) { ActingOverride = op };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
            await fixture.UiAsync(() => shell.OpenProductsAsync(cancellationToken: TestContext.Current.CancellationToken));
            ProductsDocument products = Assert.IsType<ProductsDocument>(shell.SelectedDocument);
            ProductRow product = Assert.Single(products.Products);
            Assert.Equal("Widget", product.Name);
            Assert.Equal("Community", product.Editions);
            Assert.Equal("CLI", product.Surfaces);

            // Ctrl+I opens the inbox; empty, it says so.
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.I, RawInputModifiers.Control, PhysicalKey.I, null);
                await shell.PendingOperation;
            });
            ResultInboxDocument inbox = Assert.IsType<ResultInboxDocument>(shell.SelectedDocument);
            Assert.Empty(inbox.Rows);
            Assert.Equal("No result awaits review.", inbox.Note);

            // Every focusable target of the work screens meets the compact minimum.
            await fixture.UiAsync(() => shell.OpenWorkOrderAsync("W-1", cancellationToken: TestContext.Current.CancellationToken));
            await fixture.UiAsync(() => shell.OpenComposerAsync("W-1", cancellationToken: TestContext.Current.CancellationToken));
            foreach (Document document in shell.Documents.ToList())
            {
                await fixture.Ui(() => { shell.SelectedDocument = document; window.UpdateLayout(); });
                IReadOnlyList<Target> targets = await fixture.Ui(() => Probe.Measure(window));
                Assert.All(targets.Where(t => !t.Exempt), t => Assert.True(t.MeetsMinimum, $"{document.Kind}: {t.Type} {t.Name}: {t.Width} x {t.Height}"));
            }
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }
}
