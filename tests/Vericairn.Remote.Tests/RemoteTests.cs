// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Vericairn.Records.Tests;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

/// <summary>First use explains the companion role and pairs only with an address and a code; a failed pairing says why.</summary>
public sealed class RemotePairingFormTests(HeadlessFixture fixture) : IDisposable
{
    private readonly TestEstate estate = RemoteFixtures.Seeded();

    [Fact]
    public async Task FirstUseExplainsTheCompanionAndPairsOnlyWithAnAddressAndACode()
    {
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections);
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            Assert.True(view.FindControl<Button>("PairButton")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<TabControl>("Home")!.IsVisible);
            Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == RemoteViewModel.Companion);
            Assert.False(model.PairCommand.CanExecute(null));

            model.Address = "not an address";
            model.Code = "ABCD-EFGH";
            Assert.False(model.PairCommand.CanExecute(null));
            model.Address = "192.168.1.20:7443";
            Assert.True(model.PairCommand.CanExecute(null));
            window.Close();
            await Task.CompletedTask;
            return true;
        });
    }

    [Fact]
    public async Task AFailedPairingKeepsTheFormAndSaysWhy()
    {
        using var connections = new FakeConnections(estate.Root) { PairFailure = new PairingException("denied", "The code was not accepted.") };
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections);
            model.Address = "192.168.1.20:7443";
            model.Code = "ABCD-EFGH";
            await model.PairCommand.ExecuteAsync();

            Assert.True(model.Pairing);
            Assert.Empty(model.Estates);
            Assert.Equal("Not paired (denied): The code was not accepted.", model.PairingMessage);
            return true;
        });
    }

    public void Dispose() => estate.Dispose();
}

/// <summary>The home shows the estate current after a check the runtime answered, cached with its time after one it did not, and denied when refused.</summary>
public sealed class RemoteConnectionStateTests(HeadlessFixture fixture) : IDisposable
{
    private readonly TestEstate estate = RemoteFixtures.Seeded();

    [Fact]
    public async Task PairingShowsTheHomeWithTheEstatesWorkAndProductsCurrent()
    {
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections, new RemoteFixtures.FixedTime());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            model.Address = "192.168.1.20:7443";
            model.Code = "ABCD-EFGH";
            await model.PairCommand.ExecuteAsync();

            Assert.False(model.Pairing, model.PairingMessage);
            Assert.True(view.FindControl<TabControl>("Home")!.IsVisible);
            Assert.Equal("Test Estate", model.Selected!.Name);
            Assert.Equal(ConnectionStates.Current, model.ConnectionState);
            Assert.Equal("Connection current · current as of 2026-11-01 12:00:00 UTC", model.StatusLine);
            Assert.Contains(model.Work, w => w.Key == "REMOTE-TEST" && w.StatusLabel == "planned");
            Assert.Contains(model.Products, p => p.Name == "Example");
            Assert.Equal("Test Estate", model.Node!.Estate);
            Assert.Equal("current", model.Node.State);
            window.Close();
            return true;
        });
    }

    [Fact]
    public async Task ALaterCheckThatCannotReachTheRuntimeShowsTheLastObservationAsCachedAndDeniedShowsDenied()
    {
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections, new RemoteFixtures.FixedTime());
            model.Address = "192.168.1.20:7443";
            model.Code = "ABCD-EFGH";
            await model.PairCommand.ExecuteAsync();
            int work = model.Work.Count;

            connections.Checks.Enqueue((ConnectionStates.Cached, "No answer; last current at 2026-11-01T12:00:00Z."));
            await model.RefreshCommand.ExecuteAsync();
            Assert.Equal("cached; freshness shown", model.ConnectionLabel);
            Assert.Equal("cached from 2026-11-01 12:00:00 UTC; refresh before relying on it", model.Freshness);
            Assert.Equal(work, model.Work.Count);

            connections.Checks.Enqueue((ConnectionStates.Denied, "The runtime refused this device."));
            await model.RefreshCommand.ExecuteAsync();
            Assert.Equal("denied", model.ConnectionLabel);
            Assert.Equal("The runtime refused this device.", model.ConnectionMessage);
            Assert.StartsWith("cached from ", model.Freshness, StringComparison.Ordinal);
            return true;
        });
    }

    public void Dispose() => estate.Dispose();
}

/// <summary>Every area, input and command has a screen-reader name, and what is not built is said plainly.</summary>
public sealed class RemoteScreenReaderTests(HeadlessFixture fixture) : IDisposable
{
    private readonly TestEstate estate = RemoteFixtures.Seeded();

    [Fact]
    public async Task EveryAreaInputAndCommandHasAScreenReaderNameAndHandoffsSayPlainlyTheyAreNotHereYet()
    {
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections, new RemoteFixtures.FixedTime());
            (Window window, RemoteView view) = RemoteFixtures.Show(model);
            foreach (Control control in view.GetLogicalDescendants().OfType<Control>().Where(c => c is TextBox or Button or ComboBox or TabItem))
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), $"{control.GetType().Name} {control.Name} has no screen-reader name.");
            }

            string[] areas = [.. view.FindControl<TabControl>("Home")!.Items.OfType<TabItem>().Select(t => (string)t.Header!)];
            Assert.Equal(["Attention", "Work", "Products", "Nodes", "Handoffs"], areas);
            Assert.Contains("not in Remote yet", RemoteViewModel.HandoffsLater, StringComparison.Ordinal);
            Assert.Contains("Developer build", RemoteViewModel.BuildLabel, StringComparison.Ordinal);
            window.Close();
            await Task.CompletedTask;
            return true;
        });
    }

    public void Dispose() => estate.Dispose();
}

/// <summary>The seeded estate, the shown view and the fixed clock the Remote tests share.</summary>
internal static class RemoteFixtures
{
    public static (Window Window, RemoteView View) Show(RemoteViewModel model)
    {
        var view = new RemoteView { DataContext = model };
        var window = new Window { Content = view, Width = 390, Height = 844 };
        window.Show();
        return (window, view);
    }

    public static TestEstate Seeded(long recordFormat = 2)
    {
        var estate = new TestEstate(recordFormat);
        estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        estate.Add("work-order", estate.OperatorId, """{"key":"REMOTE-TEST","kind":"ordinary","title":"Shown on the phone","intent":"Exercise Remote's home.","scope":["tests"],"exitQuestion":"Shown?","inputs":[],"deliverables":["evidence"]}""");
        return estate;
    }

    public sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>The phone's text-size setting scales Remote's text, within the range its layouts were checked at.</summary>
public sealed class RemoteTextScaleTests(HeadlessFixture fixture) : IDisposable
{
    private readonly TestEstate estate = RemoteFixtures.Seeded();

    [Fact]
    public async Task ThePhonesTextSizeScalesEveryTextAndStaysWithinItsRange()
    {
        using var connections = new FakeConnections(estate.Root);
        await fixture.UiAsync(async () =>
        {
            IResourceDictionary resources = Application.Current!.Resources;
            try
            {
                RemoteApp.ApplyTextScale(resources, 1.5);
                Assert.Equal(21d, resources["ControlContentThemeFontSize"]);
                Assert.Equal(18d, resources["RemoteSmallFontSize"]);
                Assert.Equal(27d, resources["RemoteHeadingFontSize"]);
                (Window window, RemoteView view) = RemoteFixtures.Show(new RemoteViewModel(connections));
                Assert.Equal(21d, view.FindControl<TextBlock>("PairingResult")!.FontSize);
                window.Close();

                RemoteApp.ApplyTextScale(resources, 10);
                Assert.Equal(42d, resources["ControlContentThemeFontSize"]);
                RemoteApp.ApplyTextScale(resources, double.NaN);
                Assert.Equal(14d, resources["ControlContentThemeFontSize"]);
            }
            finally
            {
                RemoteApp.ApplyTextScale(resources, 1);
            }

            await Task.CompletedTask;
            return true;
        });
    }

    public void Dispose() => estate.Dispose();
}
