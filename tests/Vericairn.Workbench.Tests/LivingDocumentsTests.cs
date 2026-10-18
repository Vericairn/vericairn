// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Headless;
using Avalonia.Input;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>The Documents page: what is due and why, and a review the runtime records, keyboard only.</summary>
[Collection("workbench")]
public class LivingDocumentsTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task TheDocumentsPageShowsWhatIsDueAndRecordsAReviewKeyboardOnly()
    {
        string root = Seed.Temp("wb-documents");
        string scratch = Seed.Temp("wb-documents-scratch");
        string op = Seed.Init(root);
        string builder = Seed.Admit(root, op, scratch, "Builder", "agent", "builder");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "guide.md"), "# Guide\n");
        Seed.Cli("--estate", root, "--json", "docs", "declare", "--by", op, "--body", Seed.Write(scratch, "declaration.json", """{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"summary":"Follows the decisions that name it."}"""));
        Seed.Cli("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", Seed.Write(scratch, "decision.json", $$"""{"title":"Change","stake":"low","notAuthorized":[],"statement":"Change the guide.","summary":"Change.","synthesis":"The operator changes the guide.","effects":["change"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","documents":["docs/guide.md"]}"""));

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-documents"))) { ActingOverride = builder };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));

            // Ctrl+Shift+D opens the page with the due document selected and the reason it is due.
            await fixture.UiAsync(async () =>
            {
                window.Focus(); window.KeyPress(Key.D, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.D, null);
                await shell.PendingOperation;
            });
            LivingDocumentsWorkspace page = Assert.IsType<LivingDocumentsWorkspace>(shell.SelectedDocument);
            LivingDocumentRow row = Assert.Single(page.Rows);
            Assert.Equal("due", row.Row.State);
            Assert.Contains("decision (warn)", row.Detail, StringComparison.Ordinal);
            Assert.Same(row, page.Selected);
            Assert.StartsWith("1 of 1 need attention", page.Note, StringComparison.Ordinal);

            // An empty summary is refused before anything is sent; then the review is typed and Ctrl+Enter records it.
            await fixture.UiAsync(async () =>
            {
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
                await shell.PendingOperation;
            });
            Assert.Equal("Say what was checked.", page.Status);
            await fixture.Ui(() => Keys.Focus(window, "Summary"));
            await fixture.Ui(() => Keys.Type(window, "The guide holds after the decision.", "Summary"));
            await fixture.UiAsync(async () =>
            {
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
                await shell.PendingOperation;
            });
            Assert.True(page.Status.StartsWith("Written: document review", StringComparison.Ordinal), page.Status);
            Assert.Equal("current", Assert.Single(page.Rows).Row.State);
            Assert.StartsWith("0 of 1 need attention", page.Note, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }
}
