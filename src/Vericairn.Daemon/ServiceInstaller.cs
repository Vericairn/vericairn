// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Processes;
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
    IReadOnlyList<IReadOnlyList<string>> AfterUninstall)
{
    /// <summary>The absolute Vericairn home captured by the service unit.</summary>
    public string? VericairnHome { get; init; }

    internal IReadOnlyList<string> Lookup { get; init; } = [];
}

/// <summary>
/// Installs the daemon as a per-user service that starts at login and restarts after failure: a launchd agent on macOS, a systemd
/// user unit on Linux, a logon task in Task Scheduler on Windows. Nothing needs administrator rights; the unit lives in the user's
/// own configuration, never in a repository, and names this machine's paths because that is what a service manager needs.
/// </summary>
public static class ServiceInstaller
{
    internal const string ServiceHomeOption = "--service-home";

    /// <summary>The plan for an estate on this machine, with the running executable.</summary>
    public static ServicePlan PlanHere(string root) => PlanHere(root, legacy: false);

    internal static ServicePlan PlanHereForInstall(string root) => PlanHere(root, legacy: false, createHome: true);

    // A previous installation had one label per estate and ran on the platform default home.
    internal static ServicePlan? LegacyPlanHere(string root) =>
        VericairnHome.SamePath(VericairnHome.Path(), VericairnHome.DefaultPath()) ? PlanHere(root, legacy: true) : null;

    private static ServicePlan PlanHere(string root, bool legacy, bool createHome = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var findings = new FindingList();
        EstateManifest manifest = EstateManifest.Load(root, findings) ?? throw new InvalidOperationException(string.Join("; ", findings.Select(f => f.Message)));
        string platform = VericairnHome.Platform();
        uint? user = platform is "macos" or "linux" ? GetUid() : null;
        string home = Path.GetFullPath(VericairnHome.Path());
        if (createHome && platform is "macos" or "linux" or "windows" && !Directory.Exists(home))
        {
            ValidateServiceHome(home);
            // Concurrent first installs through spelling aliases now plan against the same directory.
            OwnerOnlyState.CreateDirectory(home, home);
        }

        return Plan(platform, root, manifest.Estate, CurrentLauncher(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), home, user,
            PresenceSessionBuild.Current.RuntimeEnvironment, legacy);
    }

    /// <summary>The plan for a platform. On this platform it resolves the home's identity. No files are written and no commands run.</summary>
    public static ServicePlan Plan(string platform, string root, string estateId, IReadOnlyList<string> launcher, string userHome, string vericairnHome, uint? userId) =>
        Plan(platform, root, estateId, launcher, userHome, vericairnHome, userId, new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// From 0.9.4 (ID-16): the plan for a platform with the environment the service's runtime starts with. A release build passes the
    /// .NET diagnostics channel off (DOTNET_EnableDiagnostics=0), which .NET reads only from a process's environment at its start; launchd
    /// and systemd carry it, and Windows, which opens no presence session, does not.
    /// </summary>
    public static ServicePlan Plan(string platform, string root, string estateId, IReadOnlyList<string> launcher, string userHome, string vericairnHome, uint? userId,
        IReadOnlyDictionary<string, string> environment) => Plan(platform, root, estateId, launcher, userHome, vericairnHome, userId, environment, legacy: false);

    private static ServicePlan Plan(string platform, string root, string estateId, IReadOnlyList<string> launcher, string userHome, string vericairnHome, uint? userId,
        IReadOnlyDictionary<string, string> environment, bool legacy)
    {
        ArgumentNullException.ThrowIfNull(environment);
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

        ValidateServiceHome(vericairnHome);

        string suffix = string.Empty;
        if (!legacy)
        {
            string homeKey = platform == VericairnHome.Platform() && Path.IsPathFullyQualified(vericairnHome)
                ? VericairnHome.CanonicalIdentity(vericairnHome)
                : platform == "windows" ? Path.TrimEndingDirectorySeparator(vericairnHome).ToUpperInvariant()
                : Path.TrimEndingDirectorySeparator(vericairnHome);
            suffix = "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(homeKey)));
        }

        var settings = new Dictionary<string, string>(environment, StringComparer.Ordinal);
        if (!legacy)
            settings[VericairnHome.EnvironmentVariable] = vericairnHome;

        string[] arguments = [.. launcher, "--estate", root, "runtime", "serve"];
        switch (platform)
        {
            case "macos":
            {
                string label = "org.vericairn.runtime." + estateId.ToLowerInvariant() + suffix;
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
                if (settings.Count > 0)
                {
                    plist.Append("  <key>EnvironmentVariables</key>\n  <dict>\n");
                    foreach ((string name, string value) in settings.OrderBy(e => e.Key, StringComparer.Ordinal))
                        plist.Append("    <key>").Append(SecurityElement.Escape(name)).Append("</key><string>").Append(SecurityElement.Escape(value)).Append("</string>\n");
                    plist.Append("  </dict>\n");
                }

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
                    [])
                {
                    VericairnHome = vericairnHome,
                    Lookup = ["launchctl", "print", domain + "/" + label],
                };
            }

            case "linux":
            {
                string label = "vericairn-runtime-" + estateId.ToLowerInvariant() + suffix;
                string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                string unitPath = Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(userHome, ".config") : xdg, "systemd", "user", label + ".service");
                // A colon disables ExecStart's environment expansion. The paths are literal arguments.
                string prefix = arguments.Any(argument => argument.Contains('$', StringComparison.Ordinal)) ? ":" : string.Empty;
                string exec = prefix + string.Join(' ', arguments.Select(SystemdQuote));
                string variables = string.Concat(settings.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"Environment={SystemdQuote(e.Key + "=" + e.Value)}\n"));
                string unit = $"[Unit]\nDescription=Vericairn runtime for estate {estateId}\n\n[Service]\n{variables}ExecStart={exec}\nRestart=on-failure\nRestartSec=5\n\n[Install]\nWantedBy=default.target\n";
                return new ServicePlan(
                    platform,
                    label,
                    unitPath,
                    unit,
                    [["systemctl", "--user", "daemon-reload"], ["systemctl", "--user", "enable", "--now", label + ".service"]],
                    [["systemctl", "--user", "disable", "--now", label + ".service"]],
                    [["systemctl", "--user", "daemon-reload"]])
                {
                    VericairnHome = vericairnHome,
                    Lookup = ["systemctl", "--user", "show", "--property=LoadState", "--property=ActiveState", label + ".service"],
                };
            }

            case "windows":
            {
                string label = "Vericairn Runtime " + estateId + suffix;
                string unitPath = Path.Combine(vericairnHome, "services", "vericairn-runtime-" + estateId.ToLowerInvariant() + suffix + ".xml");
                // An Exec action has no environment block. Encode the path so the scheduler cannot expand a literal %NAME% in it.
                if (!legacy)
                    arguments = [.. arguments, ServiceHomeOption, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(vericairnHome))];
                string command = arguments[0];
                string argumentText = string.Join(' ', arguments.Skip(1).Select(WindowsQuote));
                string task = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                    + "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                    + $"  <RegistrationInfo><Description>Vericairn runtime for estate {SecurityElement.Escape(estateId)} using {SecurityElement.Escape(vericairnHome)}</Description></RegistrationInfo>\n"
                    + "  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>\n"
                    + "  <Settings>\n    <MultipleInstances>IgnoreNew</MultipleInstances>\n    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n"
                    + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n    <StartWhenAvailable>true</StartWhenAvailable>\n"
                    + "    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>\n    <Hidden>true</Hidden>\n  </Settings>\n"
                    // Task Scheduler requires a matching Principal id for Actions/@Context.
                    + "  <Actions>\n"
                    + $"    <Exec><Command>{SecurityElement.Escape(command)}</Command><Arguments>{SecurityElement.Escape(argumentText)}</Arguments></Exec>\n"
                    + "  </Actions>\n</Task>\n";
                return new ServicePlan(
                    platform,
                    label,
                    unitPath,
                    task,
                    [["schtasks", "/Create", "/F", "/TN", label, "/XML", unitPath], ["schtasks", "/Run", "/TN", label]],
                    [["schtasks", "/End", "/TN", label], ["schtasks", "/Delete", "/F", "/TN", label]],
                    [])
                {
                    VericairnHome = vericairnHome,
                    Lookup = ["schtasks", "/Query", "/TN", label, "/HRESULT"],
                };
            }

            default:
                throw new PlatformNotSupportedException($"Installing the runtime as a service is not supported on {platform}.");
        }
    }

    private static void ValidateServiceHome(string home)
    {
        if (home.Any(char.IsControl))
            throw new ArgumentException("The Vericairn home contains a control character the service unit cannot carry.");
    }

    /// <summary>Writes the unit and registers the service; throws with the command's output when a command fails.</summary>
    public static IReadOnlyList<string> Install(ServicePlan plan) => Install(plan, legacy: null);

    internal static IReadOnlyList<string> Install(ServicePlan plan, ServicePlan? legacy, Func<IReadOnlyList<string>, ServiceCommandResult>? execute = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        execute ??= Execute;
        // A Windows task can outlive its XML, and XML left behind is not a registered task.
        if (legacy is not null && (legacy.Platform != "windows" && File.Exists(legacy.UnitPath) || IsRegistered(legacy, execute)))
            throw new InvalidOperationException($"The legacy runtime service '{legacy.Label}' is installed for the default Vericairn home. Run 'vericairn runtime uninstall' for this estate and home before installing again.");

        if (plan.Platform == "macos" && plan.VericairnHome is string home)
            OwnerOnlyState.CreateDirectory(Path.Combine(home, "logs"), home);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.UnitPath)!);
        File.WriteAllText(plan.UnitPath, plan.UnitContent, plan.Platform == "windows" ? Encoding.Unicode : new UTF8Encoding(false));
        var log = new List<string> { $"wrote {plan.UnitPath}" };
        foreach (IReadOnlyList<string> command in plan.Install)
        {
            log.Add(Run(command, execute));
        }

        return log;
    }

    /// <summary>Unregisters the service and removes the unit; a missing service is not an error.</summary>
    public static IReadOnlyList<string> Uninstall(ServicePlan plan)
    {
        var result = Uninstall(plan, legacy: null);
        return result.Error is null ? result.Log : throw new InvalidOperationException(result.Error);
    }

    internal static (IReadOnlyList<string> Labels, IReadOnlyList<string> Log, string? Error) Uninstall(ServicePlan plan, ServicePlan? legacy,
        Func<IReadOnlyList<string>, ServiceCommandResult>? execute = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        execute ??= Execute;
        var log = new List<string>();
        var labels = new List<string>();
        try
        {
            Remove(plan, labels, log, execute);
            if (legacy is not null)
                Remove(legacy, labels, log, execute);
            return (labels, log, null);
        }
        catch (Exception exception) when (labels.Count > 0 && exception is (IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception))
        {
            // Keep the report of completed removals if a later step fails.
            return (labels, log, exception.Message);
        }
    }

    private static bool IsRegistered(ServicePlan plan, Func<IReadOnlyList<string>, ServiceCommandResult> execute)
    {
        ServiceCommandResult result = execute(plan.Lookup);
        if (plan.Platform == "linux")
        {
            string[] properties = result.Output.Split('\n', StringSplitOptions.TrimEntries);
            if (properties.Contains("LoadState=not-found", StringComparer.Ordinal) &&
                properties.Contains("ActiveState=inactive", StringComparer.Ordinal))
                return false;
        }

        if (result.ExitCode == 0)
            return true;

        const int LaunchdServiceNotFound = 113;
        const int WindowsFileNotFound = unchecked((int)0x80070002);
        if (plan.Platform == "macos" && result.ExitCode == LaunchdServiceNotFound ||
            plan.Platform == "windows" && result.ExitCode == WindowsFileNotFound)
            return false;

        throw new InvalidOperationException($"Could not check whether the runtime service '{plan.Label}' is registered. {result.Failure(plan.Lookup)}");
    }

    private static void Remove(ServicePlan plan, List<string> labels, List<string> log, Func<IReadOnlyList<string>, ServiceCommandResult> execute)
    {
        // The last command removes the registration. Windows first asks the running task to end.
        bool unregistered = false;
        for (int index = 0; index < plan.Uninstall.Count; index++)
        {
            IReadOnlyList<string> command = plan.Uninstall[index];
            try
            {
                log.Add(Run(command, execute));
                unregistered = true;
            }
            catch (InvalidOperationException exception)
            {
                if (index == plan.Uninstall.Count - 1 && plan.Lookup.Count > 0 && IsRegistered(plan, execute))
                    throw;
                log.Add(exception.Message);
                unregistered = false;
            }
        }

        if (plan.Platform == "windows" && unregistered)
            labels.Add(plan.Label);

        bool removedUnit = File.Exists(plan.UnitPath);
        if (removedUnit)
        {
            File.Delete(plan.UnitPath);
            log.Add($"removed {plan.UnitPath}");
        }

        if (plan.Platform != "windows" && (unregistered || removedUnit))
            labels.Add(plan.Label);

        foreach (IReadOnlyList<string> command in plan.AfterUninstall)
        {
            log.Add(Run(command, execute));
        }
    }

    /// <summary>How to start this very program again: the executable, or the dotnet host plus the entry assembly when running from source.</summary>
    public static IReadOnlyList<string> CurrentLauncher()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The running executable cannot be located.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Running from source through the dotnet host: the entry assembly sits beside the application base.
            string? name = Assembly.GetEntryAssembly()?.GetName().Name;
            string entry = name is null ? string.Empty : Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (name is not null && File.Exists(entry))
            {
                return [executable, entry];
            }
        }

        return [executable];
    }

    private static string Run(IReadOnlyList<string> command, Func<IReadOnlyList<string>, ServiceCommandResult> execute)
    {
        ServiceCommandResult result = execute(command);
        return result.ExitCode == 0 ? $"ran {string.Join(' ', command)}" : throw new InvalidOperationException(result.Failure(command));
    }

    private static ServiceCommandResult Execute(IReadOnlyList<string> command)
    {
        var info = new ProcessStartInfo(command[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = ProgramPath.Start(info) ?? throw new InvalidOperationException($"{command[0]} did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ServiceCommandResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static string SystemdQuote(string argument) => "\"" + argument.Replace("%", "%%", StringComparison.Ordinal).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string WindowsQuote(string argument)
    {
        if (argument.Length != 0 && !argument.Any(c => c is ' ' or '\t' or '"'))
            return argument;

        var quoted = new StringBuilder().Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // A quote needs its own escape and doubles the backslashes immediately before it.
            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            quoted.Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>The user's numeric id from the system's own tool; launchd addresses per-user services by it.</summary>
    private static uint? GetUid()
    {
        try
        {
            using Process? process = ProgramPath.Start(new ProcessStartInfo("id") { ArgumentList = { "-u" }, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
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

internal readonly record struct ServiceCommandResult(int ExitCode, string Output = "", string Error = "")
{
    internal string Failure(IReadOnlyList<string> command) => $"{string.Join(' ', command)} exited {ExitCode}: {(Error + Output).Trim()}";
}
