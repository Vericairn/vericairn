// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Runtime.Providers;

namespace Vericairn.Cli;

/// <summary>Compatibility for the build tooling's output and the existing journey fixtures' provider substitutions.</summary>
internal static class Commands
{
    internal const int Clean = CommandContext.Clean;
    private static readonly CommandContext Context = new();

    internal static int Fail(string message) => Context.Fail(message);

    internal static int ReportFindings(FindingsResponse findings, bool json) => Context.ReportFindings(findings, json);

    internal static ISourceAdapter? SourceAdapter
    {
        get => CommandProviders.SourceAdapter;
        set => CommandProviders.SourceAdapter = value;
    }

    internal static IReleaseProvider? ReleaseProvider
    {
        get => CommandProviders.ReleaseProvider;
        set => CommandProviders.ReleaseProvider = value;
    }
}
