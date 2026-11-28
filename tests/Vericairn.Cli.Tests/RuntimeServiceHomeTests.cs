// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vericairn.Daemon;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class RuntimeServiceHomeTests
{
    [Fact]
    public void TheInstallPlanShowsTheHome()
    {
        string root = CliHarness.TempDirectory("service-plan");
        try
        {
            Init(root);
            string home = VericairnHome.Path();
            Assert.Contains($"home  {home}", CliHarness.Ok("--estate", root, "runtime", "install", "--plan").Out, StringComparison.Ordinal);
            Assert.Equal(home, CliHarness.Ok("--estate", root, "--json", "runtime", "install", "--plan").Json.GetProperty("vericairnHome").GetString());
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Fact]
    public async Task TheServiceOpensTheInstalledHomesLiveStoreBeforeBinding()
    {
        string root = CliHarness.TempDirectory("service-store");
        string scratch = CliHarness.TempDirectory("service-store-homes");
        string selected = Path.Combine(scratch, "selected home");
        string other = Path.Combine(scratch, "other home");
        string variable = VericairnHome.EnvironmentVariable;
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, selected);
            string estate = Init(root);
            string live = Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!, "live.db");
            Directory.CreateDirectory(live);
            ServicePlan plan = ServiceInstaller.Plan(VericairnHome.Platform(), root, estate, [CliHarness.BuiltExecutable], scratch, selected, null);

            using Process process = Process.Start(ReadService(plan, other))!;
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            try
            {
                await process.WaitForExitAsync(cancellation).WaitAsync(TestTiming.HangTimeout, cancellation);
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains("live.db", await process.StandardError.ReadToEndAsync(cancellation), StringComparison.Ordinal);
                Assert.False(Directory.Exists(other));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TestTiming.HangTimeout, CancellationToken.None);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            CliHarness.Delete(root, scratch);
        }
    }

    [Fact]
    public async Task AServiceStartsWithItsInstalledHomeAndStatusRefusesADifferentHome()
    {
        string root = CliHarness.TempDirectory("service-home");
        string scratch = CliHarness.TempDirectory("service-homes");
        string selected = Path.Combine(scratch, "selected home");
        string other = Path.Combine(scratch, "other home");
        string variable = VericairnHome.EnvironmentVariable;
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, selected);
            string estate = Init(root);
            string endpoint = DaemonHost.EndpointFilePath(root);
            ServicePlan plan = ServiceInstaller.Plan(VericairnHome.Platform(), root, estate, [CliHarness.BuiltExecutable], scratch, selected, null);
            ProcessStartInfo info = ReadService(plan, other);
            CliHarness.RequireDaemonTransport();

            using Process process = Process.Start(info)!;
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            try
            {
                var deadline = DateTime.UtcNow + TestTiming.HangTimeout;
                while (DaemonHost.Discover(root) is null && !process.HasExited && DateTime.UtcNow < deadline)
                    await Task.Delay(100, cancellation);

                Assert.True(DaemonHost.Discover(root) is not null, process.HasExited
                    ? await process.StandardError.ReadToEndAsync(cancellation)
                    : "The daemon did not publish its endpoint in the installed home.");
                var status = CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json;
                Assert.Equal(process.Id, status.GetProperty("daemon").GetProperty("processId").GetInt32());
                Assert.Equal(selected, status.GetProperty("vericairnHome").GetString());
                Assert.Contains($"home  {selected}", CliHarness.Ok("--estate", root, "runtime", "status").Out, StringComparison.Ordinal);

                Environment.SetEnvironmentVariable(variable, other);
                Assert.Equal("embedded", CliHarness.Ok("--estate", root, "--json", "runtime", "status").Json.GetProperty("mode").GetString());
                string otherEndpoint = DaemonHost.EndpointFilePath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(otherEndpoint)!);
                File.Copy(endpoint, otherEndpoint);
                CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "runtime", "status");
                Assert.NotEqual(0, refused.Code);
                Assert.Equal(string.Empty, refused.Out);
                Assert.Contains(selected, refused.Err, StringComparison.Ordinal);
                Assert.Contains(other, refused.Err, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, selected);
                if (!process.HasExited)
                {
                    _ = CliHarness.Run("--estate", root, "runtime", "stop");
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TestTiming.HangTimeout, CancellationToken.None);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            CliHarness.Delete(root, scratch);
        }
    }

    private static string Init(string root) => CliHarness.Ok("--estate", root, "--json", "estate", "init",
        "--name", "Example", "--slug", "example", "--purpose", "Service home journey.",
        "--operator-name", "Operator", "--organization", "Example",
        "--commit", "0123456789abcdef0123456789abcdef01234567").Json.GetProperty("estate").GetString()!;

    private static ProcessStartInfo ReadService(ServicePlan plan, string inheritedHome)
    {
        var info = new ProcessStartInfo { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment[VericairnHome.EnvironmentVariable] = inheritedHome;

        if (plan.Platform == "windows")
        {
            XElement task = XDocument.Parse(plan.UnitContent).Root!;
            XNamespace ns = task.Name.Namespace;
            XElement action = task.Descendants(ns + "Exec").Single();
            info.FileName = action.Element(ns + "Command")!.Value;
            info.Arguments = action.Element(ns + "Arguments")!.Value;
            Assert.Contains("--service-home", info.Arguments, StringComparison.Ordinal);
            return info;
        }

        IEnumerable<string> arguments;
        if (plan.Platform == "macos")
        {
            XElement plist = XDocument.Parse(plan.UnitContent).Root!.Element("dict")!;
            var settings = plist.Elements().Chunk(2).ToDictionary(pair => pair[0].Value, pair => pair[1], StringComparer.Ordinal);
            info.Environment[VericairnHome.EnvironmentVariable] = settings["EnvironmentVariables"]
                .Elements().Chunk(2).Single(pair => pair[0].Value == VericairnHome.EnvironmentVariable)[1].Value;
            arguments = settings["ProgramArguments"].Elements().Select(entry => entry.Value);
        }
        else
        {
            string exec = plan.UnitContent.Split('\n').Single(line => line.StartsWith("ExecStart=", StringComparison.Ordinal));
            arguments = Regex.Matches(exec, "\"([^\"]*)\"").Select(match => match.Groups[1].Value);
            string setting = plan.UnitContent.Split('\n').Single(line => line.StartsWith("Environment=\"VERICAIRN_HOME=", StringComparison.Ordinal));
            info.Environment[VericairnHome.EnvironmentVariable] = setting["Environment=\"VERICAIRN_HOME=".Length..^1];
        }

        string[] command = arguments.ToArray();
        info.FileName = command[0];
        foreach (string argument in command.Skip(1))
            info.ArgumentList.Add(argument);
        return info;
    }
}
