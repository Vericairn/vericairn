// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Vericairn.Protocol;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class CommunicationTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task SettingsAreAccessibleInTheShellAndSaveResetAndPreviewUseTheRuntime()
    {
        string root = Seed.Temp("wb-communication");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-communication-prefs"))) { ActingOverride = person };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
            CommunicationDocument page = Assert.IsType<CommunicationDocument>(await fixture.UiAsync(() => shell.OpenCommunicationAsync(cancellationToken: TestContext.Current.CancellationToken)));
            Assert.Same(page, shell.SelectedDocument);
            Assert.NotNull(window.FindControl<MenuItem>("CommunicationMenu"));
            await fixture.Ui(() => Assert.All(Probe.Measure(window).Where(target => !target.Exempt),
                target => Assert.True(target.MeetsMinimum, $"{target.Type} {target.Name}: {target.Width} x {target.Height}")));
            Assert.Contains(person, page.Effective, StringComparison.Ordinal);
            await fixture.UiAsync(async () => { page.StatusDepth = "comprehensive"; page.ProgressFrequency = "none"; await page.SaveAsync(cancellationToken: TestContext.Current.CancellationToken); });
            Assert.Equal("comprehensive", shell.Api!.Communication(person).Fields["status"].Value);
            Assert.Equal("none", shell.Api.Communication(person).Fields["progress"].Value);
            Assert.Contains("status (comprehensive)", shell.HumanStatus, StringComparison.Ordinal);
            Assert.Contains("Complete example", page.Preview, StringComparison.Ordinal);
            int beforeView = shell.Api.Records("communication-setting").Count;
            await fixture.UiAsync(async () =>
            {
                shell.StatusViewDepth = "short";
                await shell.PendingOperation;
            });
            Assert.Contains("status (short)", shell.HumanStatus, StringComparison.Ordinal);
            Assert.Equal(beforeView, shell.Api.Records("communication-setting").Count);
            Assert.Equal("comprehensive", shell.Api.Communication(person).Fields["status"].Value);
            await fixture.UiAsync(async () =>
            {
                shell.StatusViewDepth = "inherit";
                await shell.PendingOperation;
            });
            await fixture.UiAsync(() => page.ResetAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("shipped", shell.Api.Communication(person).Fields["status"].Source);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task AStaleSettingsEditorDoesNotOverwriteAnotherDevicesChange()
    {
        string root = Seed.Temp("wb-communication-conflict");
        string person = Seed.Init(root);
        using var connection = new ServiceConnection(root);
        var first = new CommunicationDocument(() => connection.Api, () => person);
        var second = new CommunicationDocument(() => connection.Api, () => person);
        await first.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        await second.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken);
        first.StatusDepth = "standard";
        await first.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        second.StatusDepth = "comprehensive";
        await second.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("conflict", second.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("standard", connection.Api!.Communication(person).Fields["status"].Value);
    }

    [Fact]
    public async Task ProfileChangesNeverChangeRetainedHandoffBytes()
    {
        string root = Seed.Temp("wb-communication-handoff");
        string person = Seed.Init(root);
        using var connection = new ServiceConnection(root);
        CommunicationProfile profile = connection.Api!.Communication(person, view: new(Handoff: "short"));
        var page = new HandoffDocument(() => connection.Api, () => profile);
        await page.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);
        string original = page.PackageJson;
        Assert.Contains("handoff (short)", page.Text, StringComparison.Ordinal);
        profile = connection.Api.Communication(person, view: new(Handoff: "comprehensive"));
        await page.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(original, page.PackageJson);
        Assert.Contains("handoff (comprehensive)", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignmentAndResultPagesUseTheirExactAssignmentsOverride()
    {
        string root = Seed.Temp("wb-communication-assignment");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-communication-assignment-prefs"))) { ActingOverride = person };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
        IEstateApi api = shell.Api!;
        Assert.True(api.NewRecord(new("work-order", person, Seed.Json("""{"key":"COMM-1","kind":"ordinary","title":"Build","intent":"Full intent.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}"""))).Ok);
        WriteOutcome assigned = api.Assign(new("COMM-1", person, person, Seed.Json("""{"outcome":"Build it.","sources":[],"permittedChanges":["work"],"prohibitedEffects":["no publication"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["tests"],"recovery":"Record a result."}""")));
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        string assignment = assigned.Written!.Id;
        string attempt = api.Attempt(new(assignment, person, "local clone", "Build it.")).Written!.Id;
        WriteOutcome result = api.Result(new(attempt, person, Seed.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review."}""")));
        Assert.True(result.Ok, result.Problem?.Message);
        Assert.True(api.CommunicationSet(new(person, "assignment", new(Result: "short", Handoff: "short"), person, assignment)).Ok);
        await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
        AssignmentDocument page = (await fixture.UiAsync(() => shell.OpenAssignmentAsync(assignment, cancellationToken: TestContext.Current.CancellationToken)))!;
        Assert.Contains("handoff (short)", page.BriefText, StringComparison.Ordinal);
        Assert.Contains(api.Record(assignment)!.Envelope.GetRawText(), page.BriefText, StringComparison.Ordinal);
        ReviewWorkspace review = (await fixture.UiAsync(() => shell.OpenReviewAsync(result.Written!.Id, cancellationToken: TestContext.Current.CancellationToken)))!;
        Assert.Contains("result (short)", review.SummaryText, StringComparison.Ordinal);
        Assert.Contains(result.Written!.Hash, review.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Complete original record", review.SummaryText, StringComparison.Ordinal);
    }
}
