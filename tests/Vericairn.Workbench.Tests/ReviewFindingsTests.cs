// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Vericairn.Protocol;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class ReviewFindingsTests(HeadlessFixture fixture)
{
    private static readonly string[] Findings = ["error: The returned identity is wrong.", "src/identity.cs", "warning: The platform evidence is incomplete.", "tests/platform.cs"];

    [Theory]
    [InlineData("short", 720, 480)]
    [InlineData("standard", 720, 480)]
    [InlineData("comprehensive", 720, 480)]
    [InlineData("short", 1200, 800)]
    [InlineData("standard", 1200, 800)]
    [InlineData("comprehensive", 1200, 800)]
    public async Task ExistingReviewsExposeEveryFindingAlongsideTheUnperformedChecks(string depth, int width, int height)
    {
        string root = Seed.Temp("wb-review-findings");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-review-findings-prefs"))) { ActingOverride = person };
        MainWindow window = await fixture.Ui(() =>
        {
            // Home's hidden attention items can contain the same originals and mask an unrealized review tab.
            shell.SelectedRegion = shell.Regions.First(r => r.Name == "Work");
            var w = new MainWindow(shell) { Width = width, Height = height };
            w.Show();
            return w;
        });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
            IEstateApi api = shell.Api!;
            Assert.True(api.NewRecord(new("work-order", person, Seed.Json("""{"key":"COMMS-REVIEW","kind":"ordinary","title":"Review the original","intent":"Keep every finding visible.","scope":["tests"],"exitQuestion":"Reviewed?","inputs":[],"deliverables":["result"]}"""))).Ok);
            string assignment = api.Assign(new("COMMS-REVIEW", person, person, Seed.Json("""{"outcome":"Inspect the result.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["all findings visible"],"recovery":"Review the unperformed checks."}"""))).Written!.Id;
            string attempt = api.Attempt(new(assignment, person, "local clone", "Review.")).Written!.Id;
            WriteOutcome result = api.Result(new(attempt, person, Seed.Json("""{"outcome":"partial","summary":"Ready for review.","outputs":[],"evidence":[],"checks":[{"name":"platform matrix","outcome":"not-performed","summary":"The other platforms have not run."},{"name":"live harness","outcome":"skipped","summary":"No real coding agent was launched."}],"limitations":["Independent review remains."],"unresolvedEffects":[],"nextStep":"Inspect all findings."}""")));
            Assert.True(result.Ok, result.Problem?.Message);
            WriteOutcome reviewed = api.NewRecord(new("review", person, Seed.Json("""{"independence":"self","outcome":"findings","summary":"Two issues remain.","findings":[{"severity":"error","summary":"The returned identity is wrong.","location":"src/identity.cs"},{"severity":"warning","summary":"The platform evidence is incomplete.","location":"tests/platform.cs"}]}"""), $"result:{result.Written!.Id}:{result.Written.Hash}"));
            Assert.True(reviewed.Ok, reviewed.Problem?.Message);
            Assert.True(api.CommunicationSet(new(person, "person", new(Progress: "none", Result: depth), person)).Ok);
            await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
            ReviewWorkspace page = await fixture.UiAsync(async () =>
            {
                ReviewWorkspace opened = Assert.IsType<ReviewWorkspace>((await shell.OpenReviewAsync(result.Written.Id, cancellationToken: TestContext.Current.CancellationToken)));
                // Inspect in the same dispatcher turn: no incidental render tick may realize the new tab for us.
                AssertReachable(window, opened, "The other platforms have not run.");
                AssertReachable(window, opened, "No real coding agent was launched.");
                ReviewRow review = Assert.Single(opened.Reviews);
                AssertReachable(window, review, "Two issues remain.");
                foreach (string finding in Findings)
                {
                    AssertReachable(window, review, finding);
                }

                return opened;
            });
            Assert.Contains($"result ({depth})", page.SummaryText, StringComparison.Ordinal);

            await fixture.UiAsync(async () =>
            {
                page.ReviewOutcome = "findings";
                page.ReviewSummary = "A further check is needed.";
                page.ReviewFindings = "warning: The imported evidence needs review.";
                await page.SubmitReviewAsync(api, cancellationToken: TestContext.Current.CancellationToken);
                Assert.StartsWith("Written: review", page.Status, StringComparison.Ordinal);
                Assert.Equal(2, page.Reviews.Count);
                AssertReachable(window, page.Reviews.Last(), "warning: The imported evidence needs review.");
            });

            Assert.Empty(api.Records("acceptance"));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    private static void AssertReachable(MainWindow window, object context, string expected)
    {
        // Draining dispatcher jobs alone does not guarantee that a new tab's template has had a layout pass.
        window.UpdateLayout();
        TextBlock block = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), b =>
            b.Text?.Contains(expected, StringComparison.Ordinal) == true &&
            (ReferenceEquals(b.DataContext, context) || b.GetVisualAncestors().OfType<Control>().Any(c => ReferenceEquals(c.DataContext, context))));
        Assert.True(block.IsEffectivelyVisible);
        Assert.Equal(TextTrimming.None, block.TextTrimming);
        ScrollContentPresenter viewport = Assert.IsType<ScrollContentPresenter>(block.FindAncestorOfType<ScrollContentPresenter>());
        Assert.True(viewport.Viewport.Width > 0 && viewport.Viewport.Height > 0);
        int start = block.Text!.IndexOf(expected, StringComparison.Ordinal);

        // A finding can wrap over several lines in a small window. Reach every character, not just the control's top edge.
        foreach (Rect character in Enumerable.Range(start, expected.Length).Select(block.TextLayout.HitTestTextPosition).Distinct())
        {
            Rect target = character.Translate(new Vector(block.Padding.Left, block.Padding.Top));
            Assert.True(target.Width > 0 && target.Height > 0);
            block.BringIntoView(target);
            window.UpdateLayout();
            Rect inViewport = target.TransformToAABB(block.TransformToVisual(viewport)!.Value);
            Rect inWindow = target.TransformToAABB(block.TransformToVisual(window)!.Value);
            Assert.True(new Rect(viewport.Bounds.Size).Inflate(0.5).Contains(inViewport), $"'{expected}' is clipped in the review viewport: {inViewport} in {viewport.Bounds.Size}.");
            Assert.True(new Rect(window.ClientSize).Inflate(0.5).Contains(inWindow), $"'{expected}' is outside the window: {inWindow} in {window.ClientSize}.");
        }
    }
}
