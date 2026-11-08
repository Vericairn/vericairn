// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// Workbench shows the estate's history line (design/signed-history.md § 9; OPS-07) in the words status prints, from the same canonical
/// status every surface reads: the cohesion contract's one wording for whether an estate has adopted checkpoints, its heads, what they
/// witness, the tail and the standing problems.
/// </summary>
[Collection("workbench")]
public class HistoryLineSurfaceTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task HomeShowsTheHistoryLineStatusPrints()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("WB-1"), signer: f.OperatorSigner);
        f.Checkpoint();
        f.Remove(order);
        string expected;
        using (EstateService service = EstateService.Open(f.Root))
        {
            StatusResponse status = service.Status();
            expected = HistoryLine.FromStatus(status.Json)!;
            Assert.Contains(expected + " with what history checkpoints witness", status.Text, StringComparison.Ordinal);
            Assert.EndsWith(" · 1 problem", expected, StringComparison.Ordinal);
            Assert.Equal(expected, EstateView.From(status, "now").History);
            Assert.Equal(StateLanguage.Label("history", "adopted"), EstateView.From(status, "now").HistoryLabel);
        }

        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-history")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            Assert.Equal(string.Empty, shell.History);
            await fixture.UiAsync(() => shell.OpenEstateAsync(f.Root, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(expected, shell.History);
            SelectableTextBlock line = await fixture.Ui(() => window.FindControl<SelectableTextBlock>("HistoryText")!);
            Assert.Equal(expected, await fixture.Ui(() => line.Text));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }
}
