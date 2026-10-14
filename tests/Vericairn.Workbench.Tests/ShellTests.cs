// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia.Headless;
using Avalonia.Input;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Workbench;

namespace Vericairn.Workbench.Tests;

/// <summary>A test connection: the embedded service stands in for the daemon; the window still only sees the API.</summary>
internal sealed class ServiceConnection(string root) : IEstateConnection
{
    private EstateService? service = EstateService.Open(root);

    public string Root { get; } = root;

    public IEstateApi? Api => service;

    public string Mode => service is null ? "unavailable" : "daemon";

    public string Where => "in-process service standing in for the daemon";

    public void Refresh()
    {
    }

    public void Dispose()
    {
        service?.Dispose();
        service = null;
    }
}

/// <summary>A runtime manager that records what was asked and answers as a daemon would.</summary>
internal sealed class FakeRuntimeManager : IRuntimeManager
{
    public List<string> Calls { get; } = [];

    public Task<RuntimeOutcome> StartAsync(string root)
    {
        Calls.Add("start");
        return Task.FromResult(new RuntimeOutcome(true, "daemon answered"));
    }

    public Task<RuntimeOutcome> StopAsync(string root)
    {
        Calls.Add("stop");
        return Task.FromResult(new RuntimeOutcome(true, "stopped"));
    }

    public Task<RuntimeOutcome> InstallPlanAsync(string root)
    {
        Calls.Add("plan");
        return Task.FromResult(new RuntimeOutcome(true, "unit: example"));
    }
}

/// <summary>The shell over a test estate: target context, home attention, keyboard alternatives, runtime management, layout.</summary>
public class ShellTests(HeadlessFixture fixture)
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    static ShellTests()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Temp("workbench-home"));
        }
    }

    [Fact]
    public async Task OpensAnEstateShowsTheTargetContextAndDerivesAttention()
    {
        string root = Temp("wb-shell");
        string scratch = Temp("wb-shell-scratch");
        string op = Cli("--estate", root, "--json", "estate", "init", "--name", "Example Estate", "--slug", "example", "--purpose", "Shell test.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
        op = System.Text.Json.JsonDocument.Parse(op).RootElement.GetProperty("operator").GetString()!;
        Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Write(scratch, "order.json", """{"key":"M9","kind":"milestone","title":"A milestone","intent":"Test.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}"""));
        Cli("--estate", root, "--json", "records", "new", "work-status", "--by", op, "--subject", "work-order:" + LastId(root, "work-order"), "--body", Write(scratch, "status.json", """{"status":"submitted","note":"Submitted for acceptance."}"""));

        var runtime = new FakeRuntimeManager();
        var shell = new ShellViewModel(r => new ServiceConnection(r), runtime, new Preferences(Temp("wb-prefs")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));
            Assert.True(shell.HasEstate);
            Assert.Equal("Example Estate", shell.EstateName);
            Assert.Equal("daemon", shell.RuntimeMode);
            Assert.Contains("read-only", shell.ActingPrincipal, StringComparison.Ordinal);
            Assert.StartsWith("current as of", shell.Freshness, StringComparison.Ordinal);
            Assert.Empty(runtime.Calls);
            Assert.True(shell.Attention.Any(a => a.Kind == "decision needed" && a.Subject.StartsWith("M9", StringComparison.Ordinal)), $"status: {shell.Status} | orders: {string.Join(", ", shell.WorkOrders.Select(w => w.Key + ":" + w.Status))} | activity: {string.Join(" / ", shell.Activity)}");
            Assert.Equal("Example Estate", await fixture.Ui(() => window.EstateNameText.Text));

            // Enter on the attention item opens the work order as a document and explains it in the inspector.
            await fixture.Ui(() =>
            {
                window.Focus();
                window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            });
            Assert.True(shell.Documents.Any(d => d.Kind == "work-order"), $"documents: {string.Join(", ", shell.Documents.Select(d => d.Title + "/" + d.Kind))} | focused: {await fixture.Ui(() => window.FocusManager?.GetFocusedElement()?.GetType().Name ?? "nothing")} | activity: {string.Join(" / ", shell.Activity)}");
            Assert.Contains("decision needed", shell.Inspector, StringComparison.Ordinal);
            Assert.Contains("M9", ((WorkOrderDocument)shell.SelectedDocument!).Heading, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task KeyboardMovesFocusTogglesPanelsAndResetsTheLayout()
    {
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Temp("wb-prefs-keys")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.Ui(() => { window.Focus(); window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, null); });
            Assert.True(await fixture.Ui(() => window.Navigation.IsKeyboardFocusWithin), await fixture.Ui(() => "focused: " + (window.FocusManager?.GetFocusedElement()?.GetType().Name ?? "nothing")));
            await fixture.Ui(() => window.KeyPress(Key.L, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.L, null));
            Assert.False(shell.LeftVisible);
            Assert.False(await fixture.Ui(() => window.LeftPanel.IsVisible));
            await fixture.Ui(() => window.KeyPress(Key.B, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.B, null));
            Assert.False(shell.BottomVisible);
            await fixture.Ui(() => window.KeyPress(Key.D0, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Digit0, null));
            Assert.True(shell.LeftVisible && shell.BottomVisible && shell.RightVisible);
            Assert.True(await fixture.Ui(() => window.LeftPanel.IsVisible));
            await fixture.Ui(() => window.KeyPress(Key.O, RawInputModifiers.Control, PhysicalKey.O, null));
            Assert.True(await fixture.Ui(() => window.EstatePathBox.IsKeyboardFocusWithin || window.EstatePathBox.IsFocused));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task StartsTheRuntimeWhenNoDaemonAnswersAndKeepsTheClientOutOfTheRuntime()
    {
        string root = Temp("wb-runtime");
        Cli("--estate", root, "--json", "estate", "init", "--name", "Runtime", "--slug", "runtime", "--purpose", "Runtime test.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
        var runtime = new FakeRuntimeManager();
        var unavailable = new UnavailableThenAvailable(root);
        var shell = new ShellViewModel(_ => unavailable, runtime, new Preferences(Temp("wb-prefs-runtime")));
        await fixture.UiAsync(() => shell.OpenEstateAsync(root));
        Assert.Equal(["start"], runtime.Calls);
        Assert.Equal("daemon", shell.RuntimeMode);
        await fixture.UiAsync(shell.ShowInstallPlanAsync);
        Assert.Contains(shell.Documents, d => d.Title == "Runtime install plan" && d is TextDocument text && text.Text.Contains("unit: example", StringComparison.Ordinal));
        await fixture.UiAsync(shell.StopRuntimeAsync);
        Assert.Equal(["start", "plan", "stop"], runtime.Calls);
        unavailable.Dispose();
    }

    [Fact]
    public async Task RefusesToOpenADirectoryThatIsNotAnEstateAndCreatesNothing()
    {
        string root = Temp("wb-not-estate");
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Temp("wb-prefs-none")));
        await fixture.UiAsync(() => shell.OpenEstateAsync(root));
        Assert.False(shell.HasEstate);
        Assert.Contains("not an estate root", shell.Status, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public async Task EveryFocusableTargetMeetsTheCompactMinimum()
    {
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Temp("wb-prefs-probe")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            IReadOnlyList<Target> targets = await fixture.Ui(() => Probe.Measure(window));
            Assert.NotEmpty(targets);
            Assert.All(targets.Where(t => !t.Exempt), t => Assert.True(t.MeetsMinimum, $"{t.Type} {t.Name}: {t.Width} x {t.Height}"));
            Assert.Contains(targets, t => t.Exempt && t.Type == "GridSplitter");
            string report = await fixture.Ui(() => Probe.Report(window, "headless", true));
            Assert.Contains("\"belowMinimum\": 0", report, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    private sealed class UnavailableThenAvailable(string root) : IEstateConnection
    {
        private ServiceConnection? inner;

        public string Root { get; } = root;

        public IEstateApi? Api => inner?.Api;

        public string Mode => inner is null ? "unavailable" : "daemon";

        public string Where => inner?.Where ?? "no daemon answers";

        public void Refresh() => inner ??= new ServiceConnection(Root);

        public void Dispose() => inner?.Dispose();
    }

    private static string Cli(params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        TextWriter previous = Console.Out;
        Console.SetOut(output);
        try
        {
            int code = CliApp.Build().Parse(args).Invoke();
            Assert.True(code == 0, $"vericairn {string.Join(' ', args)} exited {code}: {output}");
        }
        finally
        {
            Console.SetOut(previous);
        }

        return output.ToString().Trim().Split('\n').Last();
    }

    private static string LastId(string root, string type)
    {
        string directory = Directory.EnumerateDirectories(Path.Combine(root, "records", type)).Single();
        return Path.GetFileNameWithoutExtension(Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Last());
    }

    private static string Write(string directory, string name, string json)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static string Temp(string purpose)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vericairn-{purpose}-{Ulid.NewUlid().Value}");
        Directory.CreateDirectory(path);
        return path;
    }
}
