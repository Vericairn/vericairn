// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Tests;

namespace Vericairn.TestSupport;

internal static class TestProgram
{
    private static async Task<int> Main(string[] arguments)
    {
        // A module initializer holds the module's initialization lock. Stand-ins that wait for
        // callbacks from their own assembly must run after that lock has been released.
        ScopeHarness.Intercept();
#if PROCESS_HARNESS
        ProcessHarness.Intercept();
#endif
        FakeHarness.Intercept();
        return arguments.Any(argument => argument is "-automated" or "@@")
            ? await Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(arguments)
            : await Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(arguments, TestExtensions.AddSelfRegisteredExtensions);
    }
}
