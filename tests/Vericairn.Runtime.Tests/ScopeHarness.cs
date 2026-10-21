// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Packs.Software;

namespace Vericairn.Runtime.Tests;

public static class ScopeHarness
{
    public static void Intercept()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int at = arguments.Length is 3 or 4 or 7 && arguments[1] == WorkspaceScopeProbe.Marker ? 1 : -1;
        if (at >= 0 && WorkspaceScopeProbe.TryRun(arguments[at..]) is int result)
        {
            Environment.Exit(result);
        }
    }
}
