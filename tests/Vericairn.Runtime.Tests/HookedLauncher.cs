// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A launcher that serves as another one does and runs hooks at known points of a run's start, so a test changes the estate there:
/// when the worker verifies it, after the preflight has read the records and before the workspace is prepared and the intent
/// recorded, its answer, when it gives one, being the launcher's refusal; and when the worker asks it for the command, after the
/// intent is recorded and before the launch.
/// </summary>
internal sealed class HookedLauncher(IAgentLauncher inner, Func<string?>? verify = null, Action? command = null, Func<string, IReadOnlyList<HarnessAction>>? readActions = null) : IAgentLauncher
{
    public string Launcher => inner.Launcher;

    public ProviderDescriptor Descriptor => inner.Descriptor;

    public TimeSpan StallLimit => inner.StallLimit;

    public RunWatchSettings WatchDefaults => inner.WatchDefaults;

    public string Limitations => inner.Limitations;

    public bool Generic => inner.Generic;

    public string? Harness => inner.Harness;

    public IReadOnlyList<string> EnvironmentNames => inner.EnvironmentNames;

    /// <summary>Puts a hooked Claude Code launcher in the place of the run estate's own, beside its other providers.</summary>
    public static void Install(RunEstate run, Func<string?>? verify = null, Action? command = null, Func<string, IReadOnlyList<HarnessAction>>? readActions = null)
    {
        var registry = new ProviderRegistry();
        registry.Register(new HookedLauncher(new ClaudeCodeLauncher(), verify, command, readActions));
        registry.Register(new CodexLauncher());
        registry.Register(new GenericLauncher());
        registry.Register(new GitWorktreeWorkspaces(FakeHarness.ScopeCommand()));
        run.Service.Providers = registry;
    }

    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => verify?.Invoke() ?? inner.Verify(settings, environment);

    public IReadOnlyList<string> Command(LaunchContext context)
    {
        command?.Invoke();
        return inner.Command(context);
    }

    public LaunchContext Prepare(LaunchContext context) => inner.Prepare(context);

    public HarnessSignal? Read(string line) => inner.Read(line);

    public IReadOnlyList<HarnessAction> ReadActions(string line) => readActions?.Invoke(line) ?? inner.ReadActions(line);
}
