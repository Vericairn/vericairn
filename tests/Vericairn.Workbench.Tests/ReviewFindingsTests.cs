// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Avalonia.VisualTree;
using Vericairn.Protocol;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class ReviewFindingsTests(HeadlessFixture fixture)
{
    [Theory]
    [InlineData("short")]
    [InlineData("standard")]
    [InlineData("comprehensive")]
    public async Task ExistingReviewsExposeEveryFindingAlongsideTheUnperformedChecks(string depth)
    {
        string root = Seed.Temp("wb-review-findings");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-review-findings-prefs"))) { ActingOverride = person };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));
            IEstateApi api = shell.Api!;
            Assert.True(api.NewRecord(new("work-order", person, Seed.Json("""{"key":"COMMS-REVIEW","kind":"ordinary","title":"Review the original","intent":"Keep every finding visible.","scope":["tests"],"exitQuestion":"Reviewed?","inputs":[],"deliverables":["result"]}"""))).Ok);
            string assignment = api.Assign(new("COMMS-REVIEW", person, person, Seed.Json("""{"outcome":"Inspect the result.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["all findings visible"],"recovery":"Review the unperformed checks."}"""))).Written!.Id;
            string attempt = api.Attempt(new(assignment, person, "local clone", "Review.")).Written!.Id;
            WriteOutcome result = api.Result(new(attempt, person, Seed.Json("""{"outcome":"partial","summary":"Ready for review.","outputs":[],"evidence":[],"checks":[{"name":"platform matrix","outcome":"not-performed","summary":"The other platforms have not run."},{"name":"live harness","outcome":"skipped","summary":"No real coding agent was launched."}],"limitations":["Independent review remains."],"unresolvedEffects":[],"nextStep":"Inspect all findings."}""")));
            Assert.True(result.Ok, result.Problem?.Message);
            WriteOutcome reviewed = api.NewRecord(new("review", person, Seed.Json("""{"independence":"self","outcome":"findings","summary":"Two issues remain.","findings":[{"severity":"error","summary":"The returned identity is wrong.","location":"src/identity.cs"},{"severity":"warning","summary":"The platform evidence is incomplete.","location":"tests/platform.cs"}]}"""), $"result:{result.Written!.Id}:{result.Written.Hash}"));
            Assert.True(reviewed.Ok, reviewed.Problem?.Message);
            Assert.True(api.CommunicationSet(new(person, "person", new(Progress: "none", Result: depth), person)).Ok);
            await fixture.Ui(shell.Refresh);
            await fixture.Ui(() => shell.SelectedRegion = shell.Regions.First(r => r.Name == "Work"));
            ReviewWorkspace page = (await fixture.Ui(() => shell.OpenReview(result.Written.Id)))!;
            Assert.Contains($"result ({depth})", page.SummaryText, StringComparison.Ordinal);
            Assert.Contains("The other platforms have not run.", page.SummaryText, StringComparison.Ordinal);
            Assert.Contains("No real coding agent was launched.", page.SummaryText, StringComparison.Ordinal);
            string visible = await fixture.Ui(() => string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
                .Concat(window.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text))));
            Assert.Contains("The other platforms have not run.", visible, StringComparison.Ordinal);
            Assert.Contains("Two issues remain.", visible, StringComparison.Ordinal);
            foreach (string finding in new[] { "The returned identity is wrong.", "src/identity.cs", "The platform evidence is incomplete.", "tests/platform.cs" })
            {
                Assert.Contains(finding, visible, StringComparison.Ordinal);
            }

            await fixture.Ui(() =>
            {
                page.ReviewOutcome = "findings";
                page.ReviewSummary = "A further check is needed.";
                page.ReviewFindings = "warning: The imported evidence needs review.";
                page.SubmitReview(api);
            });
            Assert.StartsWith("Written: review", page.Status, StringComparison.Ordinal);
            Assert.Contains(page.Reviews, review => review.Findings.Contains("The imported evidence needs review.", StringComparison.Ordinal));

            Assert.Empty(api.Records("acceptance"));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }
}
