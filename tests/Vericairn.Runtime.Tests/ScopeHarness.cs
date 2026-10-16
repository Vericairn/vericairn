// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using Vericairn.Packs.Software;

namespace Vericairn.Runtime.Tests;

internal static class ScopeHarness
{
    [ModuleInitializer]
#pragma warning disable CA2255 // This test application also hosts the isolated filesystem observer.
    internal static void Intercept()
#pragma warning restore CA2255
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int at = arguments.Length is 3 or 6 && arguments[1] == WorkspaceScopeProbe.Marker ? 1 : -1;
        if (at >= 0 && WorkspaceScopeProbe.TryRun(arguments[at..]) is int result)
        {
            Environment.Exit(result);
        }
    }
}
