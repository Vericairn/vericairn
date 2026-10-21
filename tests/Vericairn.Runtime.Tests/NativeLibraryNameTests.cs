// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Runtime.InteropServices;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The distributables are self-contained single-file builds, which link the runtime's own native libraries into the host and
/// resolve them only by their file names. 0.8.1's candidate imported System.Native, which every framework-dependent test run
/// resolves, so its macOS and Linux command lines threw DllNotFoundException on every command that starts a process.
/// </summary>
public class NativeLibraryNameTests
{
    private static readonly string[] Libraries =
    [
        "Vericairn.Records", "Vericairn.Runtime", "Vericairn.Sdk", "Vericairn.Mcp", "Vericairn.Adapters.Git", "Vericairn.Providers.GitHub",
        "Vericairn.Providers.NuGet", "Vericairn.Packs.Software",
    ];

    [Fact]
    public void EveryImportOfTheRuntimesOwnNativeLibrariesNamesItsFile()
    {
        (MethodInfo Method, string Library)[] imports = Libraries.Select(Assembly.Load)
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => (method, method.GetCustomAttribute<DllImportAttribute>()?.Value))
            .Where(import => import.Value is not null)
            .Select(import => (import.method, import.Value!))
            .ToArray();
        Assert.Contains(imports, import => import.Method.DeclaringType == typeof(ContainedProcess) && import.Method.Name == "SetDescriptorFlags" && import.Library == "libSystem.Native");
        foreach ((MethodInfo method, string library) in imports)
            Assert.False(library.StartsWith("System.", StringComparison.Ordinal), $"{method.DeclaringType}.{method.Name} imports {library}, which a single-file build resolves only as lib{library}.");
    }
}
