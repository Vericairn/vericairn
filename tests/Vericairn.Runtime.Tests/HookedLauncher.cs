// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// A launcher that serves as another one does and runs a hook when the worker verifies it: the worker verifies a launcher after its
/// preflight has read the records and before it prepares the workspace and records the intent, so the hook changes the estate at a
/// known point of a run's start. The hook's answer, when it gives one, is the launcher's refusal.
/// </summary>
internal sealed class HookedLauncher(IAgentLauncher inner, Func<string?> hook) : IAgentLauncher
{
    public string Launcher => inner.Launcher;

    public ProviderDescriptor Descriptor => inner.Descriptor;

    public bool Generic => inner.Generic;

    public string? Harness => inner.Harness;

    public IReadOnlyList<string> EnvironmentNames => inner.EnvironmentNames;

    /// <summary>Puts a hooked Claude Code launcher in the place of the run estate's own, beside its other providers.</summary>
    public static void Install(RunEstate run, Func<string?> hook)
    {
        var registry = new ProviderRegistry();
        registry.Register(new HookedLauncher(new ClaudeCodeLauncher(), hook));
        registry.Register(new CodexLauncher());
        registry.Register(new GenericLauncher());
        registry.Register(new GitWorktreeWorkspaces());
        run.Service.Providers = registry;
    }

    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => hook() ?? inner.Verify(settings, environment);

    public IReadOnlyList<string> Command(LaunchContext context) => inner.Command(context);

    public HarnessSignal? Read(string line) => inner.Read(line);
}
