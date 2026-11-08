// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

/// <summary>What an upgrade found serving the estate and what it did about it (OPS-05).</summary>
/// <param name="Outcome">none, pinned, kept, replaced, stopped, not-stopped, unreachable or unchecked.</param>
/// <param name="Served">The daemon found serving, when one answered.</param>
/// <param name="Started">The pinned release's daemon started in its place.</param>
/// <param name="Done">What was found or done, when it needs nothing more.</param>
/// <param name="Warning">What still stands and exactly what to run, when something does.</param>
internal sealed record RuntimeReplacement(string Outcome, ServingRuntime? Served, ServingRuntime? Started, string? Done, string? Warning);

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
            if (existing is not null && context.IsAlive(existing))
            {
                return context.Fail($"Daemon {existing.ProcessId} already serves this estate; one process owns an estate's live state.");
            }

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            using IDisposable interrupt = context.HandleInterrupt(stop.Cancel);
            using PosixSignalRegistration? hangup = IgnoreHangup();
            var options = new DaemonOptions { Log = line => context.Output.WriteLine($"{RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)} {line}") };
            DaemonHost host = DaemonHost.StartAsync(root, loopback: loopback, options: options, cancellationToken: context.CancellationToken).GetAwaiter().GetResult();
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

            if (host.Endpoint.Mcp is string mcp)
            {
                context.Output.WriteLine($"  mcp  {mcp} on {(host.Endpoint.LocalNetwork is null ? "loopback" : "loopback and the local network")}, for agents' and services' devices");
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
        catch (Exception exception) when (detached && ClientFailure.FromException(exception) is null)
        {
            // Nobody reads a detached daemon's streams: an unexpected failure goes to its log, where 'runtime start' finds it.
            return context.Fail($"the daemon stopped on an unexpected {exception.GetType().Name}: {exception.Message}");
        }
    }

    public int RuntimeStart(string root, bool json)
    {
        bool launched = false;
        try
        {
            if (!IsEstate(root))
            {
                return context.Fail(NotAnEstate(root));
            }

            DaemonEndpoint? existing = DaemonHost.Discover(root);
            if (existing is not null && context.IsAlive(existing))
            {
                return RuntimeStatus(root, json);
            }

            string? failure = Launch(root, () => launched = true);
            return failure is null ? RuntimeStatus(root, json) : context.Fail(failure);
        }
        catch (OperationCanceledException exception) when (launched)
        {
            throw new ClientCancelledException(true, context.CancellationToken, exception);
        }
        catch (TimeoutException exception) when (launched)
        {
            throw new ClientTimeoutException(true, exception);
        }
        catch (Exception exception) when (launched && exception is (IOException or InvalidOperationException or System.ComponentModel.Win32Exception))
        {
            throw new ClientUnavailableException(true, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return context.Fail(exception.Message);
        }
    }

    /// <summary>
    /// Starts this command line's daemon for the estate in the background and waits until it answers: null once it serves, otherwise
    /// why it did not. <paramref name="launched"/> is called once its process exists; a timeout after that throws.
    /// </summary>
    private string? Launch(string root, Action launched)
    {
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
            return $"The daemon's log {log} cannot be written, so no daemon was started: {exception.Message}";
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
            return "The daemon process did not start.";
        }
        launched();

        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null && endpoint.ProcessId == process.Id && context.IsAlive(endpoint))
            {
                return null;
            }

            if (process.HasExited)
            {
                return $"The daemon exited with code {process.ExitCode} before serving{LogSince(log, logStart)}. Its log is {log}.";
            }

            Task.Delay(200, context.CancellationToken).GetAwaiter().GetResult();
        }

        throw new ClientTimeoutException(true);
    }

    public int RuntimeStop(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return context.Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        if (endpoint is null || !context.IsAlive(endpoint))
        {
            context.Output.WriteLine(json ? "{\"stopped\":null}" : "no daemon is serving this estate");
            return Clean;
        }

        bool asked = false;
        try
        {
            using (var client = new VericairnClient(endpoint))
            {
                client.ShutdownAsync(context.CancellationToken).GetAwaiter().GetResult();
                asked = true;
            }

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && context.IsAlive(endpoint))
            {
                Task.Delay(200, context.CancellationToken).GetAwaiter().GetResult();
            }

            bool stopped = !context.IsAlive(endpoint);
            context.Output.WriteLine(json ? JsonSerializer.Serialize(new { stopped = stopped ? endpoint.ProcessId : (int?)null, process = endpoint.ProcessId }) : $"asked daemon {endpoint.ProcessId} to stop; {(stopped ? "stopped" : "still answering")}");
            return stopped ? Clean : HasErrors;
        }
        catch (OperationCanceledException exception) when (asked)
        {
            throw new ClientCancelledException(true, context.CancellationToken, exception);
        }
        catch (TimeoutException exception) when (asked)
        {
            throw new ClientTimeoutException(true, exception);
        }
        catch (Exception exception) when ((exception is HttpRequestException or IOException or InvalidOperationException) && exception is not IClientOperationFailure)
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
        HealthResponse? health = null;
        bool alive = endpoint is not null && context.IsAlive(endpoint, out health);
        ServingRuntime? serving = alive && health is not null ? ServingRuntime.Of(root, endpoint!, health) : null;
        EstateManifest? pin = serving is null ? ServingRuntime.Pin(root) : serving.Manifest;
        string? configured = null;
        bool mcpConfigured = false;
        try
        {
            RuntimeSettings settings = RuntimeSettings.Load(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!);
            configured = settings.LocalNetwork;
            mcpConfigured = settings.Mcp;
        }
        catch (InvalidOperationException exception)
        {
            context.Error.WriteLine("warning: " + exception.Message);
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new
            {
                mode = alive ? "daemon" : "embedded",
                daemon = alive ? endpoint : null,
                localNetwork = configured,
                mcp = mcpConfigured,
                servingRelease = serving?.Release,
                pin = pin?.Describe(),
                pinnedRelease = pin is { Mode: "release" } ? pin.Version : null,
                mismatch = serving?.Mismatch(),
            }, ProtocolJson.Options));
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

            if (endpoint.Mcp is string mcp)
            {
                context.Output.WriteLine($"  mcp  {mcp} on {(endpoint.LocalNetwork is null ? "loopback" : "loopback and the local network")}, Streamable HTTP for agents' and services' devices, every request signed");
            }

            if (serving is not null)
            {
                context.Output.WriteLine($"  release  {serving.ReleaseLine()}");
                if (serving.Mismatch() is { } mismatch)
                {
                    context.Output.WriteLine($"  mismatch: {mismatch}");
                }
            }
        }
        else
        {
            context.Output.WriteLine("no daemon is serving this estate; commands run the embedded runtime");
            if (configured is not null)
            {
                context.Output.WriteLine($"  the local-network profile is configured at {configured} and served from the runtime's next start");
            }

            if (mcpConfigured)
            {
                context.Output.WriteLine("  network MCP at /mcp is on, served from the runtime's next start");
            }
        }

        return Clean;
    }

    /// <summary>How a stop request reaches a daemon; a test stands in for a daemon that refuses or ignores it.</summary>
    internal static Func<VericairnClient, CancellationToken, Task> AskToStop { get; set; } = (client, token) => client.ShutdownAsync(token);

    /// <summary>How long a daemon asked to stop may keep answering before it counts as still running; a test shortens it.</summary>
    internal static TimeSpan StopWait { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// After an upgrade (OPS-05): when a daemon of a release older than the estate's pin serves the estate on this machine, stops it
    /// and starts the pinned release's in its place from this command line, when this command line is that release's build (its
    /// version and, where the pinned release record or dependency observation names one, its source commit); otherwise it says
    /// exactly what to run. A daemon of another estate or root, of the pinned release or of a newer one is never touched, and
    /// none is started where none served. It runs once the upgrade's runtime has answered or closed, so neither the upgrade nor
    /// the daemon it stops holds the working tree's write lock while the new daemon opens the estate. The upgrade's records and
    /// pin are the same whatever it finds.
    /// </summary>
    internal RuntimeReplacement ReplaceOlderRuntime(string root, Func<string?>? pinnedCommit = null)
    {
        ServingRuntime? serving = ServingRuntime.Read(root, context.CancellationToken);
        if (serving is null)
        {
            return new("none", null, null, null, null);
        }

        switch (serving.Relation)
        {
            case "pinned":
                return new("pinned", serving, null, $"daemon {serving.ProcessId} serves this estate with Vericairn {serving.Release}, the release it pins", null);
            case "unreadable":
                return new("unreachable", serving, null, null, serving.Mismatch());
            case "older":
                break;
            default:
                return new("kept", serving, null, null, serving.Mismatch());
        }

        string pinned = serving.PinnedRelease!;
        string stop = ServingRuntime.StopCommand(root);
        string start = ServingRuntime.StartCommand(root, pinned);
        string older = $"daemon {serving.ProcessId} of Vericairn {serving.Release}, older than the estate's pin, release {pinned}";
        if (StopServing(serving.Endpoint) is { } refused)
        {
            return new("not-stopped", serving, null, null,
                $"{older}, still serves this estate: it could not be stopped ({refused}). The upgrade is applied. As the user that started it, run '{stop}' "
                + $"with Vericairn {serving.Release}'s command line or this one; if it still answers, end process {serving.ProcessId}. Then start the pinned release's runtime with {start}.");
        }

        string stopped = "stopped " + older;
        string identity = VersionText();
        string client = ServingRuntime.ReleaseOf(identity);
        string? commit = identity.Split('+') is [_, string built] && ReleaseBuild.IsCommit(built) ? built : null;
        string? pinnedSource = client == pinned ? pinnedCommit?.Invoke() : null;
        if (client != pinned || pinnedSource is not null && commit != pinnedSource)
        {
            string which = client != pinned
                ? $"Vericairn {client}, not the pinned release {pinned}"
                : $"a build of Vericairn {client} from commit {commit ?? "unknown"}, not the pinned release's commit {pinnedSource}";
            return new("stopped", serving, null, stopped,
                $"this command line is {which}, so it started no runtime in place of the one it stopped. Start the pinned release's runtime with {start}.");
        }

        string? failure;
        try
        {
            failure = Launch(root, () => { });
        }
        catch (Exception exception) when (!context.CancellationToken.IsCancellationRequested
            && exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            failure = ServingRuntime.Reason(exception);
        }

        ServingRuntime? started = failure is null ? ServingRuntime.Read(root, context.CancellationToken) : null;
        if (started is null)
        {
            return new("stopped", serving, null, stopped,
                $"the pinned release's runtime did not start in place of the one stopped: {Sentence(failure ?? "it stopped answering as soon as it started")} Start it with {start}.");
        }

        return new("replaced", serving, started, $"{stopped}; started daemon {started.ProcessId} of Vericairn {started.Release}", null);
    }

    /// <summary>Asks a daemon to stop and waits until it has: null once it stopped, otherwise why it is still running.</summary>
    private string? StopServing(DaemonEndpoint endpoint)
    {
        string? refused = null;
        try
        {
            using var client = new VericairnClient(endpoint) { OperationTimeout = _ => ServingRuntime.ProbeDeadline };
            AskToStop(client, context.CancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (!context.CancellationToken.IsCancellationRequested
            && exception is HttpRequestException or IOException or System.Net.Sockets.SocketException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or JsonException)
        {
            refused = ServingRuntime.Reason(exception);
        }

        var deadline = DateTimeOffset.UtcNow + StopWait;
        while (!Stopped(endpoint))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return refused ?? $"it still answered {(StopWait.TotalSeconds < 2 ? "a second" : $"{StopWait.TotalSeconds:0} seconds")} after it was asked to stop";
            }

            Task.Delay(200, context.CancellationToken).GetAwaiter().GetResult();
        }

        return null;
    }

    /// <summary>
    /// Whether a daemon asked to stop has: nothing answers at its address, and its socket file, which it deletes last, is gone or its
    /// process has ended, so the daemon started next cannot lose its own socket to the one stopping.
    /// </summary>
    private bool Stopped(DaemonEndpoint endpoint)
    {
        (HealthResponse? health, string? refused) = ServingRuntime.Probe(endpoint, context.CancellationToken);
        if (health is not null || refused is not null)
        {
            return false;
        }

        return endpoint.Kind != "unix" || !File.Exists(endpoint.Address) || !Runs(endpoint.ProcessId);
    }

    private static string Sentence(string text) => text.TrimEnd().EndsWith('.') ? text.TrimEnd() : text.TrimEnd() + ".";

    private static bool Runs(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
