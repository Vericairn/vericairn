// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// The production path against a live estate: the shell over <see cref="DaemonConnection"/> and the SDK client to the daemon
/// serving the estate named by VERICAIRN_LIVE_ESTATE, acting as VERICAIRN_LIVE_ACT_AS, reviewing and accepting the result
/// VERICAIRN_LIVE_RESULT with the texts VERICAIRN_LIVE_REVIEW and VERICAIRN_LIVE_STATEMENT. Skipped unless every variable is set,
/// because it writes real records; the builder runs it once per release against the first-party estate and records what it wrote.
/// </summary>
[Collection("workbench")]
public class LiveEstateTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task ReviewsAndAcceptsAResultThroughTheDaemon()
    {
        string? root = Environment.GetEnvironmentVariable("VERICAIRN_LIVE_ESTATE");
        string? actAs = Environment.GetEnvironmentVariable("VERICAIRN_LIVE_ACT_AS");
        string? result = Environment.GetEnvironmentVariable("VERICAIRN_LIVE_RESULT");
        string? reviewText = Environment.GetEnvironmentVariable("VERICAIRN_LIVE_REVIEW");
        string? statement = Environment.GetEnvironmentVariable("VERICAIRN_LIVE_STATEMENT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(actAs) || string.IsNullOrWhiteSpace(result) || string.IsNullOrWhiteSpace(reviewText) || string.IsNullOrWhiteSpace(statement), "VERICAIRN_LIVE_ESTATE, VERICAIRN_LIVE_ACT_AS, VERICAIRN_LIVE_RESULT, VERICAIRN_LIVE_REVIEW and VERICAIRN_LIVE_STATEMENT name a live estate, an acting principal, a result and the texts; unset, nothing is written.");

        var shell = new ShellViewModel(r => new DaemonConnection(r), new CliRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-live"))) { ActingOverride = actAs };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root!));
            Assert.True(shell.HasEstate, shell.Status);
            Assert.Equal("daemon", shell.RuntimeMode);
            Assert.NotNull(shell.Api);
            Assert.Equal(actAs, shell.ActingPrincipalId);

            ResultInboxDocument inbox = await fixture.Ui(shell.OpenInbox);
            Assert.Contains(inbox.Rows, r => r.ResultId == result);
            ReviewWorkspace workspace = (await fixture.Ui(() => shell.OpenReview(result!)))!;
            Assert.True(workspace.CheckPrepared(shell.Api!), workspace.PreparedState);
            workspace.ReviewSummary = reviewText!;
            await fixture.Ui(() => workspace.SubmitReview(shell.Api!));
            Assert.StartsWith("Written: review", workspace.Status, StringComparison.Ordinal);
            string reviewId = workspace.WrittenId!;
            ReviewRow row = Assert.Single(workspace.Reviews, r => r.Id == reviewId);
            row.Selected = true;
            workspace.AcceptanceStatement = statement!;
            await fixture.Ui(() => workspace.SubmitAcceptance(shell.Api!));
            Assert.StartsWith("Written: acceptance", workspace.Status, StringComparison.Ordinal);
            Console.Out.WriteLine($"live: review {reviewId} acceptance {workspace.WrittenId} through {shell.RuntimeWhere}");
            File.WriteAllText(Path.Combine(VericairnHome.Path(), "workbench", "live-journey.txt"), $"{reviewId}\n{workspace.WrittenId}\n{shell.RuntimeWhere}\n");
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }
}
