// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Daemon.Tests;

public sealed class ServiceInstallerTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(unchecked((int)0x80070002), false)] // HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)
    [InlineData(unchecked((int)0x80070005), true)] // HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED)
    public void WindowsLooksUpTheLegacyTaskWhenItsRegistrationIsSeparateFromTheXml(int queryExitCode, bool refuses)
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("vericairn-legacy-task-");
        try
        {
            ServicePlan plan = WindowsPlan(scratch.FullName);
            ServicePlan legacy = LegacyTask(scratch.FullName);
            var calls = new List<IReadOnlyList<string>>();
            ServiceCommandResult Manager(IReadOnlyList<string> command)
            {
                calls.Add(command);
                return new ServiceCommandResult(command[1] == "/Query" ? queryExitCode : 0);
            }

            if (refuses)
            {
                InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => ServiceInstaller.Install(plan, legacy, Manager));
                Assert.Contains(legacy.Label, refusal.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(plan.UnitPath));
                Assert.Single(calls);
            }
            else
            {
                ServiceInstaller.Install(plan, legacy, Manager);
                Assert.True(File.Exists(plan.UnitPath));
                Assert.Equal(["/Query", "/Create", "/Run"], calls.Select(command => command[1]));
            }

            Assert.Equal(["schtasks", "/Query", "/TN", legacy.Label, "/HRESULT"], calls[0]);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void WindowsUninstallReportsDeletedTasksWithoutRequiringTheirXml()
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("vericairn-uninstall-task-");
        try
        {
            ServicePlan plan = WindowsPlan(scratch.FullName);
            ServicePlan legacy = LegacyTask(scratch.FullName);
            var deleted = new List<string>();
            ServiceCommandResult Manager(IReadOnlyList<string> command)
            {
                if (command[1] == "/End")
                    return new ServiceCommandResult(1, Error: "The task is not running.");
                Assert.Equal("/Delete", command[1]);
                deleted.Add(command[^1]);
                return new ServiceCommandResult(0);
            }

            (IReadOnlyList<string> labels, _, string? error) = ServiceInstaller.Uninstall(plan, legacy, Manager);

            Assert.Null(error);
            Assert.Equal([plan.Label, legacy.Label], labels);
            Assert.Equal(labels, deleted);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void LinuxUninstallReportsTheRemovedLabelWhenReloadFails()
    {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("vericairn-uninstall-unit-");
        try
        {
            string unit = Path.Combine(scratch.FullName, "fixture.service");
            File.WriteAllText(unit, "fixture unit");
            var plan = new ServicePlan("linux", "fixture", unit, "", [],
                [["systemctl", "--user", "disable", "--now", "fixture.service"]], [["systemctl", "--user", "daemon-reload"]]);

            (IReadOnlyList<string> labels, _, string? error) = ServiceInstaller.Uninstall(plan, null,
                command => command[2] == "daemon-reload" ? new ServiceCommandResult(1, Error: "reload failed") : new ServiceCommandResult(0));

            Assert.Equal([plan.Label], labels);
            Assert.Contains("reload failed", error, StringComparison.Ordinal);
            Assert.False(File.Exists(unit));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static ServicePlan WindowsPlan(string scratch) =>
        ServiceInstaller.Plan("windows", Path.Combine(scratch, "estate"), "01ESTATE", ["vericairn"], scratch,
            Path.Combine(scratch, "home"), null);

    private static ServicePlan LegacyTask(string scratch)
    {
        const string label = "Vericairn Runtime 01ESTATE";
        return new ServicePlan("windows", label, Path.Combine(scratch, "legacy.xml"), "", [],
            [["schtasks", "/End", "/TN", label], ["schtasks", "/Delete", "/F", "/TN", label]], [])
        {
            Lookup = ["schtasks", "/Query", "/TN", label, "/HRESULT"],
        };
    }
}
