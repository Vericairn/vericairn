// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class RunTemporaryDirectoryTests
{
    [Fact]
    public void EachRunHasItsOwnPrivateTemporaryDirectory()
    {
        using var home = new TestHome();
        var first = new RunDirectory(Ulid.NewUlid().Value, Ulid.NewUlid().Value);
        var second = new RunDirectory(Ulid.NewUlid().Value, Ulid.NewUlid().Value);
        first.Create();
        second.Create();
        Assert.True(Directory.Exists(first.Temporary));
        Assert.Equal(Path.Combine(first.Path, "tmp"), first.Temporary);
        Assert.NotEqual(first.Temporary, second.Temporary);
        Assert.StartsWith(Path.Combine(VericairnHome.Path(), "estates") + Path.DirectorySeparatorChar, first.Temporary, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(first.Temporary));
        Assert.Contains("TMPDIR", RunDirectory.TemporaryVariables);
    }
}
