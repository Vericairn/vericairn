// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;

namespace Vericairn.Daemon.Tests;

[CollectionDefinition("service home identity", DisableParallelization = true)]
public sealed class ServiceHomeIdentityIsolation;

[Collection("service home identity")]
public sealed class ServiceHomeIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryLinksGiveTheSameServiceLabelAndHomeComparison(bool createHomeAfterPlanning)
    {
        using var scratch = new Scratch();
        string realParent = Directory.CreateDirectory(Path.Combine(scratch.Path, "real parent")).FullName;
        string aliasParent = Path.Combine(scratch.Path, "alias parent");
        CreateDirectoryLink(aliasParent, realParent);
        string home = Path.Combine(realParent, "service home");
        string alias = Path.Combine(aliasParent, "service home");
        if (!createHomeAfterPlanning)
            Directory.CreateDirectory(home);

        string label = Plan(home).Label;
        AssertSameHome(home, alias, label);

        Directory.CreateDirectory(home);
        AssertSameHome(home, alias, label);
    }

    [Fact]
    public void TempAndPrivateTempGiveTheSameServiceLabelAndHomeComparison()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The system temp-directory alias is specific to macOS.");
        using var scratch = new Scratch("/private/tmp");
        string alias = scratch.Path["/private".Length..];
        Assert.True(Directory.Exists(alias));
        AssertSameHome(scratch.Path, alias, Plan(scratch.Path).Label);
    }

    [Theory]
    [InlineData("Service Home", "service home")]
    [InlineData("Caf\u00e9", "Cafe\u0301")]
    public void HomeLabelsAndComparisonsFollowTheVolumesSpellingRules(string first, string second)
    {
        using var scratch = new Scratch();
        string home = Directory.CreateDirectory(Path.Combine(scratch.Path, first)).FullName;
        string alternate = Path.Combine(scratch.Path, second);
        string label = Plan(home).Label;

        if (Directory.Exists(alternate))
        {
            AssertSameHome(home, alternate, label);
            return;
        }

        Directory.CreateDirectory(alternate);
        Assert.NotEqual(label, Plan(alternate).Label);
        Assert.False(VericairnHome.SamePath(home, alternate));
    }

    [Fact]
    public void PreparingTheFirstInstallCreatesTheHomeBeforeChoosingItsLabel()
    {
        using var scratch = new Scratch();
        using var estate = new TestEstate();
        string home = Path.Combine(scratch.Path, "New Home");
        string alias = Path.Combine(scratch.Path, "new home");
        string variable = VericairnHome.EnvironmentVariable;
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, home);
            string planned = ServiceInstaller.PlanHere(estate.Root).Label;
            Assert.False(Directory.Exists(home));
            Assert.Equal(planned, ServiceInstaller.PlanHereForInstall(estate.Root).Label);
            Assert.True(Directory.Exists(home));

            if (Directory.Exists(alias))
            {
                Environment.SetEnvironmentVariable(variable, alias);
                Assert.Equal(planned, ServiceInstaller.PlanHereForInstall(estate.Root).Label);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    private static ServicePlan Plan(string home) =>
        ServiceInstaller.Plan(VericairnHome.Platform(), home, "01ESTATE", ["vericairn"], home, home, 501);

    private static void AssertSameHome(string home, string alias, string expectedLabel)
    {
        Assert.Equal(expectedLabel, Plan(home).Label);
        Assert.Equal(expectedLabel, Plan(alias).Label);
        Assert.True(VericairnHome.SamePath(home, alias));
    }

    private static void CreateDirectoryLink(string alias, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, target);
            return;
        }

        // A junction exercises directory aliases on Windows without needing symlink privileges.
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            Arguments = "/d /v:off /c mklink /J \"%VERICAIRN_TEST_JUNCTION%\" \"%VERICAIRN_TEST_TARGET%\"",
        };
        start.Environment["VERICAIRN_TEST_JUNCTION"] = alias;
        start.Environment["VERICAIRN_TEST_TARGET"] = target;
        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private sealed class Scratch(string? parent = null) : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(parent ?? System.IO.Path.GetTempPath(),
            "vericairn-service-identity-" + Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
