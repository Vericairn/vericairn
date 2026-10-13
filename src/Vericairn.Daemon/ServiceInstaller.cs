// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Daemon;

/// <summary>What installing the daemon as a per-user service means on a platform: a unit written for the user's service manager and the commands that register it.</summary>
/// <param name="Platform">macos, linux or windows.</param>
/// <param name="Label">The service name the platform knows it by.</param>
/// <param name="UnitPath">Where the unit is written.</param>
/// <param name="UnitContent">The unit.</param>
/// <param name="Install">Commands run after the unit is written.</param>
/// <param name="Uninstall">Commands run before the unit is removed.</param>
/// <param name="AfterUninstall">Commands run after the unit is removed.</param>
public sealed record ServicePlan(
    string Platform,
    string Label,
    string UnitPath,
    string UnitContent,
    IReadOnlyList<IReadOnlyList<string>> Install,
    IReadOnlyList<IReadOnlyList<string>> Uninstall,
    IReadOnlyList<IReadOnlyList<string>> AfterUninstall);

/// <summary>
/// Installs the daemon as a per-user service that starts at login and restarts after failure: a launchd agent on macOS, a systemd
/// user unit on Linux, a logon task in Task Scheduler on Windows. Nothing needs administrator rights; the unit lives in the user's
/// own configuration, never in a repository, and names this machine's paths because that is what a service manager needs.
/// </summary>
public static class ServiceInstaller
{
    /// <summary>The plan for an estate on this machine, with the running executable.</summary>
    public static ServicePlan PlanHere(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        EstateManifest manifest = EstateManifest.Load(root, findings) ?? throw new InvalidOperationException(string.Join("; ", findings.Select(f => f.Message)));
        string platform = VericairnHome.Platform();
        uint? user = platform is "macos" or "linux" ? GetUid() : null;
        return Plan(platform, root, manifest.Estate, CurrentLauncher(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), VericairnHome.Path(), user);
    }

    /// <summary>The plan for a platform, pure: no file is touched and no command runs.</summary>
    public static ServicePlan Plan(string platform, string root, string estateId, IReadOnlyList<string> launcher, string userHome, string vericairnHome, uint? userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(estateId);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(userHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(vericairnHome);
        if (launcher.Count == 0)
        {
            throw new ArgumentException("A launcher names at least the executable.", nameof(launcher));
        }

        string[] arguments = [.. launcher, "--estate", root, "runtime", "serve"];
        switch (platform)
        {
            case "macos":
            {
                string label = "org.vericairn.runtime." + estateId.ToLowerInvariant();
                string log = Path.Combine(vericairnHome, "logs", label + ".log");
                string unitPath = Path.Combine(userHome, "Library", "LaunchAgents", label + ".plist");
                var plist = new StringBuilder();
                plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
                plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
                plist.Append("<plist version=\"1.0\">\n<dict>\n");
                plist.Append("  <key>Label</key><string>").Append(SecurityElement.Escape(label)).Append("</string>\n");
                plist.Append("  <key>ProgramArguments</key>\n  <array>\n");
                foreach (string argument in arguments)
                {
                    plist.Append("    <string>").Append(SecurityElement.Escape(argument)).Append("</string>\n");
                }

                plist.Append("  </array>\n");
                plist.Append("  <key>RunAtLoad</key><true/>\n");
                plist.Append("  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>\n");
                plist.Append("  <key>StandardOutPath</key><string>").Append(SecurityElement.Escape(log)).Append("</string>\n");
                plist.Append("  <key>StandardErrorPath</key><string>").Append(SecurityElement.Escape(log)).Append("</string>\n");
                plist.Append("</dict>\n</plist>\n");
                string domain = "gui/" + (userId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "$UID");
                return new ServicePlan(
                    platform,
                    label,
                    unitPath,
                    plist.ToString(),
                    [["launchctl", "bootstrap", domain, unitPath]],
                    [["launchctl", "bootout", domain + "/" + label]],
                    []);
            }

            case "linux":
            {
                string label = "vericairn-runtime-" + estateId.ToLowerInvariant();
                string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                string unitPath = Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(userHome, ".config") : xdg, "systemd", "user", label + ".service");
                string exec = string.Join(' ', arguments.Select(SystemdQuote));
                string unit = $"[Unit]\nDescription=Vericairn runtime for estate {estateId}\n\n[Service]\nExecStart={exec}\nRestart=on-failure\nRestartSec=5\n\n[Install]\nWantedBy=default.target\n";
                return new ServicePlan(
                    platform,
                    label,
                    unitPath,
                    unit,
                    [["systemctl", "--user", "daemon-reload"], ["systemctl", "--user", "enable", "--now", label + ".service"]],
                    [["systemctl", "--user", "disable", "--now", label + ".service"]],
                    [["systemctl", "--user", "daemon-reload"]]);
            }

            case "windows":
            {
                string label = "Vericairn Runtime " + estateId;
                string unitPath = Path.Combine(vericairnHome, "services", "vericairn-runtime-" + estateId.ToLowerInvariant() + ".xml");
                string command = arguments[0];
                string argumentText = string.Join(' ', arguments.Skip(1).Select(WindowsQuote));
                string task = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                    + "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                    + $"  <RegistrationInfo><Description>Vericairn runtime for estate {SecurityElement.Escape(estateId)}</Description></RegistrationInfo>\n"
                    + "  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>\n"
                    + "  <Settings>\n    <MultipleInstances>IgnoreNew</MultipleInstances>\n    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n"
                    + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n    <StartWhenAvailable>true</StartWhenAvailable>\n"
                    + "    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>\n    <Hidden>true</Hidden>\n  </Settings>\n"
                    + "  <Actions Context=\"Author\">\n"
                    + $"    <Exec><Command>{SecurityElement.Escape(command)}</Command><Arguments>{SecurityElement.Escape(argumentText)}</Arguments></Exec>\n"
                    + "  </Actions>\n</Task>\n";
                return new ServicePlan(
                    platform,
                    label,
                    unitPath,
                    task,
                    [["schtasks", "/Create", "/F", "/TN", label, "/XML", unitPath], ["schtasks", "/Run", "/TN", label]],
                    [["schtasks", "/End", "/TN", label], ["schtasks", "/Delete", "/F", "/TN", label]],
                    []);
            }

            default:
                throw new PlatformNotSupportedException($"Installing the runtime as a service is not supported on {platform}.");
        }
    }

    /// <summary>Writes the unit and registers the service; throws with the command's output when a command fails.</summary>
    public static IReadOnlyList<string> Install(ServicePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.UnitPath)!);
        File.WriteAllText(plan.UnitPath, plan.UnitContent, plan.Platform == "windows" ? Encoding.Unicode : new UTF8Encoding(false));
        var log = new List<string> { $"wrote {plan.UnitPath}" };
        foreach (IReadOnlyList<string> command in plan.Install)
        {
            log.Add(Run(command));
        }

        return log;
    }

    /// <summary>Unregisters the service and removes the unit; a missing service is not an error.</summary>
    public static IReadOnlyList<string> Uninstall(ServicePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var log = new List<string>();
        foreach (IReadOnlyList<string> command in plan.Uninstall)
        {
            try
            {
                log.Add(Run(command));
            }
            catch (InvalidOperationException exception)
            {
                log.Add(exception.Message);
            }
        }

        if (File.Exists(plan.UnitPath))
        {
            File.Delete(plan.UnitPath);
            log.Add($"removed {plan.UnitPath}");
        }

        foreach (IReadOnlyList<string> command in plan.AfterUninstall)
        {
            log.Add(Run(command));
        }

        return log;
    }

    /// <summary>How to start this very program again: the executable, or the dotnet host plus the entry assembly when running from source.</summary>
    public static IReadOnlyList<string> CurrentLauncher()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The running executable cannot be located.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? entry = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entry))
            {
                return [executable, entry];
            }
        }

        return [executable];
    }

    private static string Run(IReadOnlyList<string> command)
    {
        var info = new ProcessStartInfo(command[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"{command[0]} did not start.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        string line = string.Join(' ', command);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{line} exited {process.ExitCode}: {(error + output).Trim()}");
        }

        return $"ran {line}";
    }

    private static string SystemdQuote(string argument) => "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string WindowsQuote(string argument) => argument.Contains(' ', StringComparison.Ordinal) || argument.Contains('"', StringComparison.Ordinal) ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : argument;

    /// <summary>The user's numeric id from the system's own tool; launchd addresses per-user services by it.</summary>
    private static uint? GetUid()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("id") { ArgumentList = { "-u" }, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 && uint.TryParse(output, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint uid) ? uid : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
