// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class DaemonLifecycle(CommandContext context)
{
    internal const string DaemonLogName = "daemon.log";

    public int RuntimeServe(string root, bool loopback, bool detached = false)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            if (detached)
            {
                // Started by 'runtime start': whoever started that command may be reading its output to the end, so the daemon
                // writes to its own log beside the live store and never to the streams it was given, which that command closes.
                // The log is opened before anything else can fail, so every failure is written where 'runtime start' reads it.
                string log = DaemonLogPath(root);
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                var writer = new StreamWriter(new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }

            DaemonEndpoint? existing = DaemonHost.Discover(root);
            if (existing is not null && VericairnClient.IsAlive(existing))
            {
                return context.Fail($"Daemon {existing.ProcessId} already serves this estate; one process owns an estate's live state.");
            }

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };
            using PosixSignalRegistration? hangup = IgnoreHangup();
            var options = new DaemonOptions { Log = line => context.Output.WriteLine($"{RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)} {line}") };
            DaemonHost host = DaemonHost.StartAsync(root, loopback: loopback, options: options).GetAwaiter().GetResult();
            context.Output.WriteLine($"vericairnd {CommandContext.VersionText()} serving {host.Service.Root}");
            context.Output.WriteLine($"  {host.Endpoint.Kind}  {host.Endpoint.Address}");
            if (host.Endpoint.Loopback is LoopbackEndpoint published)
            {
                context.Output.WriteLine($"  loopback  https://{published.Host}:{published.Port} pinned to {published.Certificate}");
            }

            if (host.Endpoint.LocalNetwork is LocalNetworkEndpoint network)
            {
                context.Output.WriteLine($"  local network  {RuntimeEndpointText.Address(network)}, mutual TLS with paired devices; runtime identity {network.Identity}");
            }

            context.Output.WriteLine($"  process {host.Endpoint.ProcessId}; stop with Ctrl+C or 'vericairn runtime stop'");
            host.WaitForShutdownAsync(stop.Token).GetAwaiter().GetResult();
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            context.Output.WriteLine("vericairnd stopped");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return context.Fail(exception.Message);
        }
        catch (Exception exception) when (detached)
        {
            // Nobody reads a detached daemon's streams: an unexpected failure goes to its log, where 'runtime start' finds it.
            return context.Fail($"the daemon stopped on an unexpected {exception.GetType().Name}: {exception.Message}");
        }
    }

    public int RuntimeStart(string root, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            DaemonEndpoint? existing = DaemonHost.Discover(root);
            if (existing is not null && VericairnClient.IsAlive(existing))
            {
                return RuntimeStatus(root, json);
            }

            // The daemon's log is opened here first: a log that cannot be written stops the start with its reason, and a daemon
            // that fails before serving is explained from what it wrote there since this point.
            string log = DaemonLogPath(root);
            long logStart;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                using var probe = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                logStart = probe.Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return context.Fail($"The daemon's log {log} cannot be written, so no daemon was started: {exception.Message}");
            }

            // The daemon gets streams of its own and writes to its log: it never inherits this command's output, which a caller
            // (Workbench, a script capturing the output) may be reading to the end while the daemon runs for as long as it serves.
            IReadOnlyList<string> launcher = ServiceInstaller.CurrentLauncher();
            var info = new ProcessStartInfo(launcher[0]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in launcher.Skip(1).Concat(["--estate", Path.GetFullPath(root), "runtime", "serve", "--detached"]))
            {
                info.ArgumentList.Add(argument);
            }

            NativeMethods.KeepStandardHandlesFromChildren();
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return context.Fail("The daemon process did not start.");
            }

            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                DaemonEndpoint? endpoint = DaemonHost.Discover(root);
                if (endpoint is not null && endpoint.ProcessId == process.Id && VericairnClient.IsAlive(endpoint))
                {
                    return RuntimeStatus(root, json);
                }

                if (process.HasExited)
                {
                    return context.Fail($"The daemon exited with code {process.ExitCode} before serving{LogSince(log, logStart)}. Its log is {log}.");
                }

                Thread.Sleep(200);
            }

            return context.Fail($"The daemon did not start serving within 30 seconds{LogSince(log, logStart)}. Its log is {log}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return context.Fail(exception.Message);
        }
    }

    public int RuntimeStop(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return context.Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        if (endpoint is null || !VericairnClient.IsAlive(endpoint))
        {
            context.Output.WriteLine(json ? "{\"stopped\":null}" : "no daemon is serving this estate");
            return Clean;
        }

        try
        {
            using (var client = new VericairnClient(endpoint))
            {
                client.Shutdown();
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && VericairnClient.IsAlive(endpoint))
            {
                Thread.Sleep(200);
            }

            bool stopped = !VericairnClient.IsAlive(endpoint);
            context.Output.WriteLine(json ? JsonSerializer.Serialize(new { stopped = stopped ? endpoint.ProcessId : (int?)null, process = endpoint.ProcessId }) : $"asked daemon {endpoint.ProcessId} to stop; {(stopped ? "stopped" : "still answering")}");
            return stopped ? Clean : HasErrors;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int RuntimeStatus(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return context.Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        bool alive = endpoint is not null && VericairnClient.IsAlive(endpoint);
        string? configured = null;
        try
        {
            configured = RuntimeSettings.Load(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!).LocalNetwork;
        }
        catch (InvalidOperationException exception)
        {
            context.Error.WriteLine("warning: " + exception.Message);
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new { mode = alive ? "daemon" : "embedded", daemon = alive ? endpoint : null, localNetwork = configured }, ProtocolJson.Options));
        }
        else if (alive)
        {
            context.Output.WriteLine($"daemon {endpoint!.ProcessId} serves {endpoint.Root} since {endpoint.StartedAt}");
            context.Output.WriteLine($"  {endpoint.Kind}  {endpoint.Address}");
            if (endpoint.Loopback is LoopbackEndpoint published)
            {
                context.Output.WriteLine($"  loopback  https://{published.Host}:{published.Port} pinned to {published.Certificate}");
            }

            if (endpoint.LocalNetwork is LocalNetworkEndpoint network)
            {
                context.Output.WriteLine($"  local network  {RuntimeEndpointText.Address(network)}, mutual TLS with paired devices; runtime identity {network.Identity}");
            }
        }
        else
        {
            context.Output.WriteLine("no daemon is serving this estate; commands run the embedded runtime");
            if (configured is not null)
            {
                context.Output.WriteLine($"  the local-network profile is configured at {configured} and served from the runtime's next start");
            }
        }

        return Clean;
    }

    public int RuntimeInstall(string root, bool planOnly, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            ServicePlan plan = ServiceInstaller.PlanHere(root);
            if (planOnly)
            {
                PrintPlan(plan, json);
                return Clean;
            }

            IReadOnlyList<string> log = ServiceInstaller.Install(plan);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { installed = plan.Label, unit = plan.UnitPath, log }));
            }
            else
            {
                foreach (string line in log)
                {
                    context.Output.WriteLine(line);
                }

                context.Output.WriteLine($"installed {plan.Label}: the runtime starts at login and restarts after failure; remove it with 'vericairn runtime uninstall'.");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return context.Fail(exception.Message);
        }
    }

    public int RuntimeUninstall(string root, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            ServicePlan plan = ServiceInstaller.PlanHere(root);
            IReadOnlyList<string> log = ServiceInstaller.Uninstall(plan);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { uninstalled = plan.Label, log }));
            }
            else
            {
                foreach (string line in log)
                {
                    context.Output.WriteLine(line);
                }

                context.Output.WriteLine($"uninstalled {plan.Label}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return context.Fail(exception.Message);
        }
    }



    /// <summary>Where a daemon started by 'runtime start' writes its output and its failures for an estate on this machine.</summary>
    internal static string DaemonLogPath(string root) => Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!, DaemonLogName);

    /// <summary>What the daemon wrote to its log after an offset, quoted briefly for a failed start, or nothing.</summary>
    private static string LogSince(string log, long start)
    {
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= start)
            {
                return string.Empty;
            }

            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string said = string.Join(" ", lines.TakeLast(5).Select(line => line.StartsWith("error: ", StringComparison.Ordinal) ? line["error: ".Length..] : line)).TrimEnd('.', ' ');
            return said.Length == 0 ? string.Empty : ": " + (said.Length > 1000 ? "..." + said[^1000..] : said);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static PosixSignalRegistration? IgnoreHangup()
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private void PrintPlan(ServicePlan plan, bool json)
    {
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(plan, ProtocolJson.Options));
            return;
        }

        context.Output.WriteLine($"{plan.Platform}: {plan.Label}");
        context.Output.WriteLine($"unit {plan.UnitPath}:");
        context.Output.Write(plan.UnitContent);
        foreach (IReadOnlyList<string> command in plan.Install)
        {
            context.Output.WriteLine("install: " + string.Join(' ', command));
        }

        foreach (IReadOnlyList<string> command in plan.Uninstall.Concat(plan.AfterUninstall))
        {
            context.Output.WriteLine("uninstall: " + string.Join(' ', command));
        }
    }
}
