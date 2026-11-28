// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Provisions in use by this process, including an earlier provision still in use after replacement or removal.</summary>
internal static class ClaudeOutputSecrets
{
    private static readonly ConcurrentDictionary<Guard, WorkerConfigurationStore.ClaudeSetupToken> active = new();

    internal static IDisposable? Protect(WorkerConfigurationStore.ClaudeSetupToken? provision)
    {
        if (provision is null) return null;
        var guard = new Guard();
        active.TryAdd(guard, provision);
        return guard;
    }

    internal static bool ContainsActiveFragment(string text, WorkerConfigurationStore.ClaudeSetupToken? except = null)
    {
        if (text.Length < SecretShapes.ProvisionFragmentLength) return false;
        foreach (var entry in active)
        {
            if (!ReferenceEquals(entry.Value, except) && entry.Value.ContainsFragment(text)) return true;
        }
        return false;
    }

    internal static bool ContainsFragment(string text, WorkerConfigurationStore store)
    {
        if (text.Length < SecretShapes.ProvisionFragmentLength) return false;
        if (ContainsActiveFragment(text)) return true;

        // Check the current provision for each write. The log can outlive imports, removals and replacements.
        try
        {
            return store.ReadClaudeSetupToken(allowExpired: true)?.ContainsFragment(text) == true;
        }
        catch (ProviderRefusedException)
        {
            return false; // If the owning checks refuse the file, the writer has no value to compare. Shape checks still apply.
        }
    }

    private sealed class Guard : IDisposable
    {
        public void Dispose() => active.TryRemove(this, out _);
    }
}
