// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Packs.Software;

// The complete build inputs used by reference generation and retained-inventory policy checks.
internal static class BuildSourcePaths
{
    internal static ImmutableArray<string> All { get; } =
        ["src", "tools", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "Vericairn.slnx", ".gitattributes", "NuGet.Config"];
}
