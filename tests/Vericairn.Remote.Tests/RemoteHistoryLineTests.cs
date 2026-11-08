// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Controls;
using Vericairn.Presentation;
using Vericairn.Records.History;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Remote shows the estate's history line (design/signed-history.md § 9; OPS-07) from the same canonical status every surface reads, in
/// the words status prints: adopted or not, the heads by the short hash a fresh machine checks a copy against, the witnessed count, the
/// tail and the standing problems.
/// </summary>
public sealed class RemoteHistoryLineTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task RemoteShowsTheHistoryLineStatusPrints()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("REM-1"), signer: f.OperatorSigner);
        Records.Model.RecordEnvelope head = f.Checkpoint();
        using var connections = new FakeConnections(f.Root);
        Protocol.StatusResponse status = connections.Service.Status();
        string expected = HistoryLine.FromStatus(status.Json)!;
        Assert.Contains(expected + "\n", status.Text, StringComparison.Ordinal);
        Assert.Contains("head " + HistoryLine.ShortHash(head.ComputedHash), expected, StringComparison.Ordinal);
        Assert.Equal(expected, EstateView.From(status, "now").History);
        Assert.Equal("adopted", EstateView.From(status, "now").HistoryState);
        await fixture.UiAsync(async () =>
        {
            RemoteViewModel model = await RemoteInspectionSeed.Pair(connections, new InspectionClock());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            Assert.Equal(expected, model.History);
            TextBlock line = Assert.Single(view.GetLogicalDescendantsOfType<TextBlock>().Distinct(), t => Avalonia.Automation.AutomationProperties.GetName(t) == "History checkpoints");
            Assert.True(line.IsVisible);
            Assert.Equal(expected, line.Text);
            window.Close();
            return true;
        });
    }
}
