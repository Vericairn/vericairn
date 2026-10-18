// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal static class CommandProviders
{
    /// <summary>The git adapter registered in this process; tests register a recorded one.</summary>
    internal static ISourceAdapter? SourceAdapter
    {
        get
        {
            Composition.Register(ProviderRegistry.Current);
            return ProviderRegistry.Current.Source("git");
        }

        set
        {
            if (value is null)
            {
                ProviderRegistry.Current.RemoveSource("git");
            }
            else
            {
                ProviderRegistry.Current.Register(value);
            }
        }
    }

    /// <summary>The implementation registered for the github-releases provider in this process; tests register the in-memory one and restore the previous afterwards.</summary>
    internal static IReleaseProvider? ReleaseProvider
    {
        get
        {
            Composition.Register(ProviderRegistry.Current);
            return ProviderRegistry.Current.Release("github-releases");
        }

        set
        {
            if (value is null)
            {
                ProviderRegistry.Current.RemoveRelease("github-releases");
            }
            else
            {
                ProviderRegistry.Current.Register(value);
            }
        }
    }

    public static ISourceAdapter? Git(CommandContext context, out int failure)
    {
        ISourceAdapter? adapter = SourceAdapter;
        failure = adapter is null ? context.Fail("No git adapter is available in this distribution; repositories cannot be observed here.") : Clean;
        return adapter;
    }
}
