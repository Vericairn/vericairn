// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

internal static class HarnessWorkspace
{
    public static string Canonical(string workspace) => WorkspacePaths.Canonical(workspace);
}
