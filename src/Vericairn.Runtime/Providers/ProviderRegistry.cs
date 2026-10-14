// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

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
    private readonly Dictionary<string, ISourceAdapter> sources = new(StringComparer.Ordinal);

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
