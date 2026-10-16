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
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));
            CommunicationDocument page = await fixture.Ui(shell.OpenCommunication);
            Assert.Same(page, shell.SelectedDocument);
            Assert.NotNull(window.FindControl<MenuItem>("CommunicationMenu"));
            Assert.Contains(person, page.Effective, StringComparison.Ordinal);
            await fixture.Ui(() => { page.StatusDepth = "comprehensive"; page.ProgressFrequency = "none"; page.Save(); });
            Assert.Equal("comprehensive", shell.Api!.Communication(person).Fields["status"].Value);
            Assert.Equal("none", shell.Api.Communication(person).Fields["progress"].Value);
            Assert.Contains("status (comprehensive)", shell.HumanStatus, StringComparison.Ordinal);
            Assert.Contains("Complete example", page.Preview, StringComparison.Ordinal);
            int beforeView = shell.Api.Records("communication-setting").Count;
            await fixture.Ui(() => shell.StatusViewDepth = "short");
            Assert.Contains("status (short)", shell.HumanStatus, StringComparison.Ordinal);
            Assert.Equal(beforeView, shell.Api.Records("communication-setting").Count);
            Assert.Equal("comprehensive", shell.Api.Communication(person).Fields["status"].Value);
            await fixture.Ui(() => shell.StatusViewDepth = "inherit");
            await fixture.Ui(page.Reset);
            Assert.Equal("shipped", shell.Api.Communication(person).Fields["status"].Source);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public void AStaleSettingsEditorDoesNotOverwriteAnotherDevicesChange()
    {
        string root = Seed.Temp("wb-communication-conflict");
        string person = Seed.Init(root);
        using var connection = new ServiceConnection(root);
        var first = new CommunicationDocument(() => connection.Api, () => person);
        var second = new CommunicationDocument(() => connection.Api, () => person);
        first.Refresh();
        second.Refresh();
        first.StatusDepth = "standard";
        first.Save();
        second.StatusDepth = "comprehensive";
        second.Save();
        Assert.Contains("conflict", second.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("standard", connection.Api!.Communication(person).Fields["status"].Value);
    }

    [Fact]
    public void ProfileChangesNeverChangeRetainedHandoffBytes()
    {
        string root = Seed.Temp("wb-communication-handoff");
        string person = Seed.Init(root);
        using var connection = new ServiceConnection(root);
        CommunicationProfile profile = connection.Api!.Communication(person, view: new(Handoff: "short"));
        var page = new HandoffDocument(() => connection.Api, () => profile);
        page.Prepare();
        string original = page.PackageJson;
        Assert.Contains("handoff (short)", page.Text, StringComparison.Ordinal);
        profile = connection.Api.Communication(person, view: new(Handoff: "comprehensive"));
        page.Prepare();
        Assert.Equal(original, page.PackageJson);
        Assert.Contains("handoff (comprehensive)", page.Text, StringComparison.Ordinal);
    }
}
