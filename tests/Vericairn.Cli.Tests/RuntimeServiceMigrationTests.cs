// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class RuntimeServiceMigrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InstallRefusesAnExistingLegacyServiceOnTheDefaultHome(bool hasUnitFile)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The launchctl and systemctl stand-ins are POSIX scripts.");
        using var service = new ServiceFixture();
        if (hasUnitFile)
            ServiceFixture.WriteUnit(service.LegacyUnit);
        else
            File.WriteAllText(Path.Combine(service.Home, "legacy-registered"), "");

        CliHarness.Outcome refusal = CliHarness.Run("--estate", service.Root, "runtime", "install");

        Assert.Equal(2, refusal.Code);
        Assert.Contains(service.LegacyLabel, refusal.Err, StringComparison.Ordinal);
        Assert.Contains("runtime uninstall", refusal.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(service.Unit));
        Assert.Equal(hasUnitFile, File.Exists(service.LegacyUnit));
    }

    [Fact]
    public void InstallRefusesBeforeWritingWhenTheLegacyRegistrationCannotBeChecked()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The launchctl and systemctl stand-ins are POSIX scripts.");
        using var service = new ServiceFixture();
        File.WriteAllText(Path.Combine(service.Home, "legacy-unreadable"), "");

        CliHarness.Outcome refusal = CliHarness.Run("--estate", service.Root, "runtime", "install");

        Assert.Equal(2, refusal.Code);
        Assert.Contains(service.LegacyLabel, refusal.Err, StringComparison.Ordinal);
        Assert.Contains("could not read registration", refusal.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(service.Unit));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UninstallReportsBothLabelsItRemovedFromTheDefaultHome(bool json)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The launchctl and systemctl stand-ins are POSIX scripts.");
        using var service = new ServiceFixture();
        ServiceFixture.WriteUnit(service.Unit);
        ServiceFixture.WriteUnit(service.LegacyUnit);
        string[] args = ["--estate", service.Root, "runtime", "uninstall"];

        CliHarness.Outcome removed = CliHarness.Ok(json ? [.. args, "--json"] : args);

        string[] expected = [service.Label, service.LegacyLabel];
        if (json)
            Assert.Equal(expected, removed.Json.GetProperty("uninstalled").EnumerateArray().Select(label => label.GetString()));
        else
            Assert.Equal(expected.Select(label => "uninstalled " + label),
                removed.Out.Split('\n').Where(line => line.StartsWith("uninstalled ", StringComparison.Ordinal)));
        Assert.False(File.Exists(service.Unit));
        Assert.False(File.Exists(service.LegacyUnit));
        Assert.Empty(CliHarness.Ok([.. args, "--json"]).Json.GetProperty("uninstalled").EnumerateArray());
    }

    [Fact]
    public void AManagerRefusalKeepsTheUnitForAnUninstallRetry()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The launchctl and systemctl stand-ins are POSIX scripts.");
        using var service = new ServiceFixture();
        ServiceFixture.WriteUnit(service.Unit);
        service.RefuseUninstall(service.Label);

        CliHarness.Outcome refusal = CliHarness.Run("--estate", service.Root, "runtime", "uninstall", "--json");

        Assert.Equal(2, refusal.Code);
        Assert.Contains("could not unregister", refusal.Err, StringComparison.Ordinal);
        Assert.Equal("", refusal.Out);
        Assert.True(File.Exists(service.Unit));
    }

    [Fact]
    public void UninstallJsonNamesTheRemovedLabelWhenTheOtherRemovalFails()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The launchctl and systemctl stand-ins are POSIX scripts.");
        using var service = new ServiceFixture();
        ServiceFixture.WriteUnit(service.Unit);
        ServiceFixture.WriteUnit(service.LegacyUnit);
        service.RefuseUninstall(service.LegacyLabel);

        CliHarness.Outcome partial = CliHarness.Run("--estate", service.Root, "runtime", "uninstall", "--json");

        Assert.Equal(2, partial.Code);
        Assert.Contains(service.LegacyLabel, partial.Err, StringComparison.Ordinal);
        Assert.Equal(service.Label, Assert.Single(partial.Json.GetProperty("uninstalled").EnumerateArray()).GetString());
        Assert.False(File.Exists(service.Unit));
        Assert.True(File.Exists(service.LegacyUnit));
    }

    [Fact]
    public void InstallCreatesAnOwnerOnlyLogDirectoryBeforeRegisteringTheLaunchAgent()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("LaunchAgents require a log directory on macOS.");
            return;
        }
        using var service = new ServiceFixture();
        string logs = Path.Combine(service.Home, "Library", "Application Support", "Vericairn", "logs");
        Assert.False(Directory.Exists(logs));

        CliHarness.Ok("--estate", service.Root, "runtime", "install");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(logs));
        Assert.True(File.Exists(service.Unit));
    }

    private sealed class ServiceFixture : IDisposable
    {
        private readonly Dictionary<string, string?> previous;
        internal string Root { get; }
        internal string Home { get; }
        internal string Label { get; }
        internal string Unit { get; }
        internal string LegacyLabel { get; }
        internal string LegacyUnit { get; }

        internal ServiceFixture()
        {
            Root = CliHarness.TempDirectory("service-migration-estate");
            Home = CliHarness.TempDirectory("service-migration-home");
            previous = new[] { "HOME", "PATH", "XDG_CONFIG_HOME", VericairnHome.EnvironmentVariable }
                .ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
            try
            {
                CliHarness.Ok("--estate", Root, "estate", "init", "--name", "Example", "--slug", "example",
                    "--purpose", "Service migration.", "--operator-name", "Operator", "--organization", "Example",
                    "--commit", new string('a', 40));

                string bin = Directory.CreateDirectory(Path.Combine(Home, "bin")).FullName;
                WriteServiceManager(bin);
                Environment.SetEnvironmentVariable("HOME", Home);
                Environment.SetEnvironmentVariable("PATH", bin);
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(Home, ".config"));
                Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, null);

                var plan = CliHarness.Ok("--estate", Root, "--json", "runtime", "install", "--plan").Json;
                Label = plan.GetProperty("label").GetString()!;
                Unit = plan.GetProperty("unitPath").GetString()!;
                Assert.StartsWith(Home + Path.DirectorySeparatorChar, Unit, StringComparison.Ordinal);
                LegacyLabel = Label[..Label.LastIndexOf('-')];
                LegacyUnit = Unit.Replace(Label, LegacyLabel, StringComparison.Ordinal);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static void WriteUnit(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test unit");
        }

        internal void RefuseUninstall(string label) => File.WriteAllText(Path.Combine(Home, "uninstall-refused-" + label), "");

        public void Dispose()
        {
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
            CliHarness.Delete(Root, Home);
        }

        private static void WriteServiceManager(string bin)
        {
            if (OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException();
            bool mac = OperatingSystem.IsMacOS();
            string program = Path.Combine(bin, mac ? "launchctl" : "systemctl");
            File.WriteAllText(program, mac ? """
                #!/bin/sh
                case "$1" in
                    print)
                        if [ -f "$HOME/legacy-unreadable" ]; then
                            printf 'could not read registration\n' >&2; exit 77
                        fi
                        [ -f "$HOME/legacy-registered" ] || [ -f "$HOME/uninstall-refused-${2##*/}" ] || exit 113 ;;
                    bootstrap) [ -d "$HOME/Library/Application Support/Vericairn/logs" ] ;;
                    bootout)
                        if [ -f "$HOME/uninstall-refused-${2##*/}" ]; then
                            printf 'could not unregister\n' >&2; exit 77
                        fi
                        [ -f "$HOME/Library/LaunchAgents/${2##*/}.plist" ] || exit 113 ;;
                    *) exit 64 ;;
                esac
                """ : """
                #!/bin/sh
                case "$2" in
                    show)
                        if [ -f "$HOME/legacy-unreadable" ]; then
                            printf 'could not read registration\n' >&2; exit 77
                        fi
                        if [ -f "$HOME/legacy-registered" ] || [ -f "$HOME/uninstall-refused-${5%.service}" ]; then
                            printf 'LoadState=loaded\nActiveState=active\n'
                        else
                            printf 'LoadState=not-found\nActiveState=inactive\n'
                        fi ;;
                    enable|daemon-reload) exit 0 ;;
                    disable)
                        if [ -f "$HOME/uninstall-refused-${4%.service}" ]; then
                            printf 'could not unregister\n' >&2; exit 77
                        fi
                        [ -f "$XDG_CONFIG_HOME/systemd/user/$4" ] ;;
                    *) exit 64 ;;
                esac
                """);
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
