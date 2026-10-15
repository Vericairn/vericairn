// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>The release flow and the runtime workspace, keyboard only, and the theme's roles.</summary>
[Collection("workbench")]
public class ReleaseJourneyTests(HeadlessFixture fixture)
{
    // The product declares its provider and one setting; the repository is left for the form, as a person would type it.
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free.","license":"Apache-2.0"}],"surfaces":[{"name":"CLI","slug":"cli","summary":"The command line."}],"release":{"destination":{"provider":"github-releases","settings":{"workflow":"release.yml"}}}}""";

    static ReleaseJourneyTests()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, Seed.Temp("workbench-home"));
        }
    }

    [Fact]
    public async Task TheReleaseFlowKeyboardOnlyFromIntentToObservedPublication()
    {
        string root = Seed.Temp("wb-release");
        string scratch = Seed.Temp("wb-release-scratch");
        string op = Seed.Init(root);
        string builder = Seed.Admit(root, op, scratch, "Builder", "agent", "builder");
        string decision = Seed.Id(Seed.Cli("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", Seed.Write(scratch, "decision.json", $$"""{"title":"Delegate","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")));
        Seed.Cli("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", Seed.Write(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
        Seed.Cli("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", Seed.Write(scratch, "product.json", Product));

        var provider = new FakeReleaseProvider();
        IReleaseProvider? previous = ProviderRegistry.Current.Register(provider);
        ServiceConnection? connection = null;
        // The builder's device submits and observes; the operator's device accepts. Both are held on this machine, as on the operator's own.
        var shell = new ShellViewModel(r => connection = new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-release"))) { ActingOverride = builder };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));
            Assert.Same(provider, connection!.Service!.Providers.Release("github-releases"));

            // Ctrl+Shift+E opens the release workspace with the keyboard in its first field; the form is typed, Ctrl+Enter submits.
            await fixture.Ui(() => { window.Focus(); window.KeyPress(Key.E, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.E, null); });
            ReleaseWorkspace workspace = Assert.IsType<ReleaseWorkspace>(shell.SelectedDocument);
            Assert.Equal("Widget", workspace.Product?.Name);
            Assert.Equal("github-releases", workspace.SelectedProvider?.Provider);
            Assert.Equal(["repository", "workflow", "ref", "checksums"], workspace.Settings.Select(s => s.Name));
            Assert.Equal("release.yml", workspace.Settings.Single(s => s.Name == "workflow").Value);
            Assert.Equal("ref (default main when empty)", workspace.Settings.Single(s => s.Name == "ref").Label);
            // The keyboard lands in the first field of the workspace.
            Assert.True(await fixture.Ui(() => window.DocumentTabs.IsKeyboardFocusWithin && window.GetVisualDescendants().OfType<TextBox>().Any(b => b.IsFocused && AutomationProperties.GetName(b) == "Version")), await fixture.Ui(() => "focused: " + string.Join("; ", window.GetVisualDescendants().OfType<InputElement>().Where(i => i.IsFocused).Select(i => i.GetType().Name + "/" + AutomationProperties.GetName(i)))));
            await fixture.Ui(() => Keys.Type(window, "0.9.0"));
            Assert.Equal("0.9.0", workspace.Version);
            // Tab order is the platform's traversal, which the headless harness does not reproduce from a focused control; the next field is focused by name.
            await fixture.Ui(() => Keys.Focus(window, "repository"));
            await fixture.Ui(() => Keys.Type(window, "Example/widget", "repository"));
            Assert.Equal("Example/widget", workspace.Settings.Single(s => s.Name == "repository").Value);
            await fixture.Ui(() => window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null));
            Assert.True(workspace.Status.Contains("intent was recorded before the dispatch", StringComparison.Ordinal), workspace.Status);
            Assert.Equal("dispatched", workspace.LastResponse!.Status);
            string run = workspace.LastResponse.Run!;
            DestinationRow destination = Assert.Single(workspace.Destinations);
            Assert.Equal("intent recorded", destination.Preparation);
            Assert.Equal($"run {run} dispatched", destination.Submission);
            Assert.Equal("run outcome not yet observed", destination.ExternalReview);
            Assert.Equal("no candidate recorded", destination.Availability);
            Assert.Single(provider.Dispatches);

            // A second submission under the same identity is refused the same way the command line refuses it; nothing is dispatched.
            await fixture.Ui(() => window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null));
            Assert.Contains("observe and reconcile", workspace.Status, StringComparison.Ordinal);
            Assert.Single(provider.Dispatches);
            Assert.Equal(2, Seed.Run("--estate", root, "release", "submit", "--product", workspace.Product!.Id, "--version", "0.9.0", "--repository", "Example/widget", "--by", builder));
            Assert.Single(provider.Dispatches);

            // Observing while the run is in progress records nothing; after the run succeeds the draft becomes a candidate with its hash.
            await fixture.Ui(() => window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null));
            Assert.Contains("pending", workspace.Status, StringComparison.Ordinal);
            Assert.Empty(shell.Api!.Records("release"));
            provider.CompleteRun("Example/widget", run, true, "0.9.0");
            workspace.Qualified = "linux-x64";
            workspace.Limitations = "Nothing signed.";
            await fixture.Ui(() => window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null));
            Assert.Equal("observed-success", workspace.LastResponse!.Status);
            Assert.NotNull(workspace.LastResponse.Candidate);
            Assert.Contains("--draft=false", workspace.Publish, StringComparison.Ordinal);
            destination = Assert.Single(workspace.Destinations);
            Assert.Equal("run succeeded and the draft was observed", destination.ExternalReview);
            Assert.Equal("draft observed; candidate recorded, not published", destination.Availability);
            Assert.Equal(workspace.LastResponse.Candidate!.Id, destination.ReleaseId);

            // The operator acts now: Enter on the destination opens the candidate in the review workspace; the acceptance is typed and sent with Ctrl+Shift+Enter.
            shell.ActingOverride = op;
            await fixture.Ui(shell.Refresh);
            Assert.Equal("Operator (named with --as; no device of this principal is held here, so its records will be unsigned)", shell.ActingPrincipal);
            await fixture.Ui(() =>
            {
                window.UpdateLayout();
                ListBox list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.DataContext is ReleaseWorkspace && l.ItemsSource is System.Collections.ObjectModel.ObservableCollection<DestinationRow>);
                list.SelectedIndex = 0;
                (list.ContainerFromIndex(0) as Control)?.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            });
            ReviewWorkspace review = Assert.IsType<ReviewWorkspace>(shell.SelectedDocument);
            Assert.Equal(destination.ReleaseId, review.SubjectId);
            Assert.Equal("operator", review.Independence);
            Assert.False(review.SelfAcceptance);
            await fixture.Ui(() =>
            {
                window.UpdateLayout();
                Keys.Focus(window, "Acceptance statement");
                Keys.Type(window, "Accepted 0.9.0 from the recorded evidence.", "Acceptance statement");
                window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null);
            });
            Assert.StartsWith("Written: acceptance", review.Status, StringComparison.Ordinal);

            // The operator publishes by hand; the builder observes again and the publication is recorded. Workbench published nothing itself.
            shell.ActingOverride = builder;
            await fixture.Ui(shell.Refresh);
            await fixture.Ui(() => { window.Focus(); window.KeyPress(Key.E, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.E, null); });
            Assert.Same(workspace, shell.SelectedDocument);
            await fixture.Ui(() => window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null));
            Assert.Contains("a draft", workspace.Status, StringComparison.Ordinal);
            provider.Publish("Example/widget", "v0.9.0");
            await fixture.Ui(() => window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null));
            Assert.Equal("published", workspace.LastResponse!.Status);
            Assert.NotNull(workspace.LastResponse.Release);
            destination = Assert.Single(workspace.Destinations);
            Assert.Equal("published, observed", destination.Availability);
            Assert.Contains(workspace.Releases, r => r.Version == "0.9.0" && r.Status == "published");
            Assert.Equal(0, Seed.Run("--estate", root, "estate", "check"));
        }
        finally
        {
            if (previous is null)
            {
                ProviderRegistry.Current.RemoveRelease("github-releases");
            }
            else
            {
                ProviderRegistry.Current.Register(previous);
            }

            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task TheRuntimeWorkspaceShowsThePlanBeforeInstallOrUninstall()
    {
        string root = Seed.Temp("wb-runtime-ws");
        Seed.Init(root);
        var runtime = new FakeRuntimeManager();
        var shell = new ShellViewModel(r => new ServiceConnection(r), runtime, new Preferences(Seed.Temp("wb-prefs-runtime-ws")));
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(root));
            await fixture.Ui(() => { window.Focus(); window.KeyPress(Key.U, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.U, null); });
            RuntimeDocument page = Assert.IsType<RuntimeDocument>(shell.SelectedDocument);
            Assert.Contains("API 1", page.Health, StringComparison.Ordinal);
            Assert.False(page.PlanShown);

            await fixture.UiAsync(shell.InstallRuntimeAsync);
            Assert.DoesNotContain("install", runtime.Calls);
            Assert.StartsWith("Show the install plan first", page.Output, StringComparison.Ordinal);

            await fixture.UiAsync(shell.ShowInstallPlanAsync);
            Assert.True(page.PlanShown);
            Assert.Equal("unit: example", page.Plan);
            await fixture.UiAsync(shell.InstallRuntimeAsync);
            await fixture.UiAsync(shell.UninstallRuntimeAsync);
            Assert.Equal(["plan", "install", "uninstall"], runtime.Calls);
            Assert.Equal("removed: example", page.Output);
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    [Fact]
    public async Task TheThemeBindsEveryRoleOfTheTokenContractInBothVariants()
    {
        string[] colors = ["BrandSurface", "BrandSurfaceRaised", "BrandText", "BrandTextMuted", "BrandAccent", "BrandAccentText", "BrandAttention", "BrandOutline"];
        string[] others = ["BrandTitleFont", "BrandHeadingFont", "BrandBodyFont", "BrandMonoFont", "BrandScaleCompact", "BrandScaleComfortable", "BrandScaleLarge", "BrandMotionRevealMilliseconds", "BrandMotionSettleMilliseconds"];
        await fixture.Ui(() =>
        {
            Application app = Application.Current!;
            foreach (string key in colors)
            {
                Assert.True(app.TryGetResource(key, ThemeVariant.Light, out object? light) && light is Avalonia.Media.IBrush, key + " light");
                Assert.True(app.TryGetResource(key, ThemeVariant.Dark, out object? dark) && dark is Avalonia.Media.IBrush, key + " dark");
                Assert.NotEqual(light!.ToString(), dark!.ToString());
            }

            foreach (string key in others)
            {
                Assert.True(app.TryGetResource(key, ThemeVariant.Default, out object? value) && value is not null, key);
            }

            // The first values: the bundled faces resolve by family name, and every pair the roles form meets its minimum in both variants.
            Assert.True(app.TryGetResource("BrandMonoFont", ThemeVariant.Default, out object? mono) && mono is Avalonia.Media.FontFamily monoFamily && monoFamily.Name == "JetBrains Mono", "mono face");
            Assert.True(app.TryGetResource("BrandBodyFont", ThemeVariant.Default, out object? body) && body is Avalonia.Media.FontFamily bodyFamily && bodyFamily.Name == "Inter", "body face");
            IReadOnlyList<ContrastPair> contrast = Probe.Contrast(app);
            Assert.Equal(16, contrast.Count);
            Assert.All(contrast, c => Assert.True(c.Meets, $"{c.Variant}: {c.Foreground} on {c.Background} is {c.Ratio}, minimum {c.Minimum}"));
        });
    }
}
