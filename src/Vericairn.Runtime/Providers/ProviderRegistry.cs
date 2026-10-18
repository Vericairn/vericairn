// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Providers;

/// <summary>
/// The implementations a host makes available, by the provider or adapter they serve. The runtime registers none: a host (the
/// command line, which also hosts the daemon) registers what its distribution ships, and an operation resolves the provider its
/// records or its request name. A name with no registered implementation is refused; nothing ever selects another.
/// </summary>
public sealed class ProviderRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, IReleaseProvider> releases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPullRequestProvider> pullRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISourceAdapter> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentLauncher> launchers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IWorkspaceProvider> workspaces = new(StringComparer.Ordinal);

    /// <summary>The host's adapter for checked pushes. No implementation is selected implicitly by the runtime.</summary>
    public IPushAdapter? PushAdapter { get; set; }

    /// <summary>The host's scanner for exactly the commits being pushed.</summary>
    public ISecretScanner? SecretScanner { get; set; }

    /// <summary>Registers a pull-request provider and returns the previous implementation.</summary>
    public IPullRequestProvider? Register(IPullRequestProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            IPullRequestProvider? previous = pullRequests.GetValueOrDefault(provider.Provider);
            pullRequests[provider.Provider] = provider;
            return previous;
        }
    }

    /// <summary>Registers a pull-request provider only when no implementation is already registered.</summary>
    public bool RegisterIfAbsent(IPullRequestProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate) return pullRequests.TryAdd(provider.Provider, provider);
    }

    /// <summary>The implementation serving exactly the recorded provider, or null.</summary>
    public IPullRequestProvider? PullRequests(string provider)
    {
        lock (gate) return pullRequests.GetValueOrDefault(provider);
    }

    /// <summary>The registry of this process, which the host fills before any operation runs.</summary>
    public static ProviderRegistry Current { get; } = new();

    /// <summary>The release providers available, ordered by provider.</summary>
    public IReadOnlyList<IReleaseProvider> ReleaseProviders
    {
        get
        {
            lock (gate)
            {
                return releases.Values.OrderBy(p => p.Provider, StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <summary>The source adapters available, ordered by kind.</summary>
    public IReadOnlyList<ISourceAdapter> SourceAdapters
    {
        get
        {
            lock (gate)
            {
                return sources.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Value).ToList();
            }
        }
    }

    /// <summary>The coding-agent launchers available, ordered by launcher.</summary>
    public IReadOnlyList<IAgentLauncher> Launchers
    {
        get
        {
            lock (gate)
            {
                return launchers.Values.OrderBy(l => l.Launcher, StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <summary>The workspace providers available, ordered by kind.</summary>
    public IReadOnlyList<IWorkspaceProvider> Workspaces
    {
        get
        {
            lock (gate)
            {
                return sources.Keys.Union(workspaces.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .Select(Workspace).OfType<IWorkspaceProvider>().ToList();
            }
        }
    }

    /// <summary>Makes a launcher available for its launcher, replacing the one registered before; returns that one, or null.</summary>
    public IAgentLauncher? Register(IAgentLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        lock (gate)
        {
            launchers.TryGetValue(launcher.Launcher, out IAgentLauncher? previous);
            launchers[launcher.Launcher] = launcher;
            return previous;
        }
    }

    /// <summary>Makes a launcher available unless one already serves its launcher; true when it was registered.</summary>
    public bool RegisterIfAbsent(IAgentLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        lock (gate)
        {
            return launchers.TryAdd(launcher.Launcher, launcher);
        }
    }

    /// <summary>Makes a workspace provider available for its kind, replacing the one registered before; returns that one, or null.</summary>
    public IWorkspaceProvider? Register(IWorkspaceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            workspaces.TryGetValue(provider.Kind, out IWorkspaceProvider? previous);
            workspaces[provider.Kind] = provider;
            return previous;
        }
    }

    /// <summary>Makes a workspace provider available unless one already serves its kind; true when it was registered.</summary>
    public bool RegisterIfAbsent(IWorkspaceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            return workspaces.TryAdd(provider.Kind, provider);
        }
    }

    /// <summary>The launcher registered for a launcher id, or null.</summary>
    public IAgentLauncher? Launcher(string launcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcher);
        lock (gate)
        {
            return launchers.GetValueOrDefault(launcher);
        }
    }

    /// <summary>Removes the launcher of a launcher id; returns it, or null.</summary>
    public IAgentLauncher? RemoveLauncher(string launcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcher);
        lock (gate)
        {
            return launchers.Remove(launcher, out IAgentLauncher? removed) ? removed : null;
        }
    }

    /// <summary>The explicit workspace provider, or the registered source adapter's workspace operations for that kind.</summary>
    public IWorkspaceProvider? Workspace(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (gate)
        {
            return workspaces.GetValueOrDefault(kind) ?? sources.GetValueOrDefault(kind)?.Workspaces;
        }
    }

    /// <summary>Makes a release provider available for its provider, replacing the one registered before; returns that one, or null.</summary>
    public IReleaseProvider? Register(IReleaseProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            releases.TryGetValue(provider.Provider, out IReleaseProvider? previous);
            releases[provider.Provider] = provider;
            return previous;
        }
    }

    /// <summary>Makes a release provider available unless one already serves its provider; true when it was registered.</summary>
    public bool RegisterIfAbsent(IReleaseProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            return releases.TryAdd(provider.Provider, provider);
        }
    }

    /// <summary>Makes a source adapter available for its kind, replacing the one registered before; returns that one, or null.</summary>
    public ISourceAdapter? Register(ISourceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (gate)
        {
            sources.TryGetValue(adapter.Kind, out ISourceAdapter? previous);
            sources[adapter.Kind] = adapter;
            return previous;
        }
    }

    /// <summary>Makes a source adapter available unless one already serves its kind; true when it was registered.</summary>
    public bool RegisterIfAbsent(ISourceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (gate)
        {
            return sources.TryAdd(adapter.Kind, adapter);
        }
    }

    /// <summary>Removes the release provider of a provider; returns it, or null.</summary>
    public IReleaseProvider? RemoveRelease(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        lock (gate)
        {
            return releases.Remove(provider, out IReleaseProvider? removed) ? removed : null;
        }
    }

    /// <summary>Removes the source adapter of a kind; returns it, or null.</summary>
    public ISourceAdapter? RemoveSource(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (gate)
        {
            return sources.Remove(kind, out ISourceAdapter? removed) ? removed : null;
        }
    }

    /// <summary>The release provider registered for a provider, or null.</summary>
    public IReleaseProvider? Release(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        lock (gate)
        {
            return releases.GetValueOrDefault(provider);
        }
    }

    /// <summary>The release provider that serves a resource location key, or null when none or more than one does.</summary>
    public IReleaseProvider? ReleaseForLocation(string locationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationKey);
        lock (gate)
        {
            List<IReleaseProvider> serving = releases.Values.Where(p => p.Descriptor.ServedLocations.Contains(locationKey, StringComparer.Ordinal)).ToList();
            return serving.Count == 1 ? serving[0] : null;
        }
    }

    /// <summary>The source adapter registered for a kind, or null.</summary>
    public ISourceAdapter? Source(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (gate)
        {
            return sources.GetValueOrDefault(kind);
        }
    }
}
