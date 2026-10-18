// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using System.Text.Json;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class HandoffTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task TheHandOverIsReachableByKeyboard()
    {
        string root = Seed.Temp("wb-handoff");
        string op = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-handoff-preferences")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.H, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.H, null);
                await shell.PendingOperation;
            });
            HandoffDocument page = Assert.IsType<HandoffDocument>(shell.SelectedDocument);
            Assert.NotNull(page.Package);
            string basis = page.PackageJson;
            Assert.EndsWith(shell.Api!.Handoff().Text, page.Text, StringComparison.Ordinal);
            Assert.StartsWith("Presentation measurement: " + System.Text.Encoding.UTF8.GetByteCount(page.Text).ToString(System.Globalization.CultureInfo.InvariantCulture) + " UTF-8 bytes", page.Text, StringComparison.Ordinal);
            Assert.Contains("Originals (complete)", page.Text, StringComparison.Ordinal);
            await fixture.UiAsync(async () =>
            {
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "PrepareHandoffButton");
                Assert.All(Probe.Measure(window).Where(t => !t.Exempt), t => Assert.True(t.MeetsMinimum, $"{t.Type} {t.Name}: {t.Width} x {t.Height}"));
                Assert.True(window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "HandoffViews").Bounds.Height >= 180);
                Button resume = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResumeHandoffButton");
                resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await shell.PendingOperation;
            });
            Assert.Contains("No changes observed", page.ResumeText, StringComparison.Ordinal);

            // A refresh updates the return comparison, never the retained hand-over.
            Assert.True(shell.Api.NewRecord(new NewRecordRequest("work-order", op, Seed.Json("""{"key":"H-1","kind":"ordinary","title":"Continue","intent":"Handoff.","scope":["widget"],"exitQuestion":"Complete?","inputs":[],"deliverables":["widget"]}"""))).Ok);
            await fixture.UiAsync(async () =>
            {
                window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
                await shell.PendingOperation;
            });
            Assert.Equal(basis, page.PackageJson);
            Assert.Contains("H-1", page.ResumeText, StringComparison.Ordinal);
            Assert.Contains("The world may have moved", page.ResumeText, StringComparison.Ordinal);
            await fixture.UiAsync(async () => Assert.Same(page, (await shell.OpenHandoffAsync(cancellationToken: TestContext.Current.CancellationToken))));
            Assert.Equal(basis, page.PackageJson);

            // The person must explicitly observe; opening and refreshing did not manufacture a timestamp.
            Assert.Equal(JsonValueKind.Null, shell.Api.Status().Json.GetProperty("lastObservedByPerson").ValueKind);
            shell.ActingOverride = op;
            await fixture.UiAsync(() => shell.ObserveEstateAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(op, shell.Api.Status().Json.GetProperty("lastObservedByPerson").GetProperty("by").GetString());
            Assert.Contains("observation", shell.LastPersonObservation, StringComparison.Ordinal);

            // Losing the read after discovery must mark the document's comparison cached too.
            using var locked = new FileStream(Path.Combine(root, "estate", "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("cached", shell.Freshness);
            Assert.Contains("cached", page.Note, StringComparison.Ordinal);
            Assert.Equal(basis, page.PackageJson);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task TheSamePackageCanBeSavedLoadedAndResumedAndAnInvalidImportKeepsTheBasis()
    {
        string root = Seed.Temp("wb-retained");
        string scratch = Seed.Temp("wb-retained-scratch");
        Seed.Init(root);
        using var connection = new ServiceConnection(root);
        IEstateApi? available = connection.Api;
        var page = new HandoffDocument(() => available);
        await fixture.UiAsync(() => page.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken));
        string basis = page.PackageJson;
        Assert.Equal(CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(connection.Api!.Handoff().Package, ProtocolJson.Options)), basis);
        string path = Path.Combine(scratch, "handoff.json");
        await fixture.UiAsync(async () => Assert.True((await page.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken))));
        Assert.Equal(basis, File.ReadAllText(path));
        await fixture.UiAsync(async () => Assert.False((await page.SaveAsync(path, cancellationToken: TestContext.Current.CancellationToken))));
        Assert.Equal(basis, File.ReadAllText(path));

        var next = new HandoffDocument(() => available);
        await fixture.UiAsync(async () => Assert.True((await next.LoadAsync(File.ReadAllText(path), cancellationToken: TestContext.Current.CancellationToken))));
        Assert.Equal(basis, next.PackageJson);
        Assert.Contains("Originals (complete)", next.Text, StringComparison.Ordinal);
        Assert.Contains("No changes observed", next.ResumeText, StringComparison.Ordinal);
        await fixture.UiAsync(async () => Assert.False((await next.LoadAsync("{}", cancellationToken: TestContext.Current.CancellationToken))));
        Assert.Equal(basis, next.PackageJson);
        available = null;
        await fixture.UiAsync(() => next.CompareAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("cached", next.Note, StringComparison.Ordinal);
        Assert.Equal(basis, next.PackageJson);
        Assert.Contains("Remote is not built", next.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedHandOverShowsAllFindingsAndKeepsTheRetainedOriginals()
    {
        string root = Seed.Temp("wb-refused-handoff");
        Seed.Init(root);
        using var connection = new ServiceConnection(root);
        var page = new HandoffDocument(() => connection.Api);
        await fixture.UiAsync(() => page.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken));
        string basis = page.PackageJson;
        // A missing charter is explicit; neither a new hand-over nor a successful unchanged view is manufactured.
        string charter = Directory.EnumerateFiles(Path.Combine(root, "records", "estate"), "*.json", SearchOption.AllDirectories).Single();
        File.Delete(charter);
        await fixture.UiAsync(() => page.PrepareAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(basis, page.PackageJson);
        Assert.Contains("refused", page.Note, StringComparison.Ordinal);
        Assert.Contains("estate.missing", page.Note, StringComparison.Ordinal);
        await fixture.UiAsync(() => page.CompareAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("incomplete", page.Note, StringComparison.Ordinal);
        Assert.Contains("missing", page.ResumeText, StringComparison.Ordinal);
    }
}
