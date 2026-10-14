// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Workbench;

namespace Vericairn.Workbench.Tests;

/// <summary>The shell opens headless.</summary>
public class ShellSmokeTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task TheShellOpens()
    {
        bool visible = await fixture.Ui(() =>
        {
            var window = new MainWindow();
            window.Show();
            bool shown = window.IsVisible;
            window.Close();
            return shown;
        });
        Assert.True(visible);
    }
}
