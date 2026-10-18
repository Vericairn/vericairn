// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>
/// Command implementations. Every operation on an open estate goes through the API: the daemon's when one serves the estate on
/// this machine, the embedded runtime otherwise, so that one owner holds the live state and one implementation evaluates
/// authority. Exit codes: 0 clean, 1 findings with errors, 2 usage or failure.
/// </summary>
internal static partial class Commands
{
    internal const int Clean = CommandContext.Clean;
    internal const int HasErrors = CommandContext.HasErrors;
    internal const int Failed = CommandContext.Failed;

    private static readonly CommandContext Context = new();


    public static int Status(string root, bool json, bool observe = false, string? by = null, CommunicationSelection? presentation = null)
    {
        if (observe || by is not null)
        {
            if (!observe || string.IsNullOrWhiteSpace(by))
            {
                return Fail("Recording an observation requires both --observe and --by with a human principal id.");
            }

            return WithApi(root, api =>
            {
                WriteOutcome written = api.ObserveEstate(new EstateObserveRequest(by));
                if (!written.Ok)
                {
                    return ReportOutcome(written, json);
                }

                StatusResponse observed = api.Status();
                Console.Out.Write(json ? CanonicalJson.SerializePretty(observed.Json) : CommandPresentation.Status(api, observed, presentation));
                return observed.HasErrors ? HasErrors : Clean;
            });
        }

        if (!IsEstate(root))
        {
            return WithFiles(root, model =>
            {
                FindingList checks = EstateChecks.Run(model);
                Console.Out.Write(json ? EstateStatus.RenderJson(model, checks) : EstateStatus.Render(model, checks));
                return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
            });
        }

        return WithApi(root, api =>
        {
            StatusResponse status = api.Status();
            Console.Out.Write(json ? CanonicalJson.SerializePretty(status.Json) : CommandPresentation.Status(api, status, presentation));
            return status.HasErrors ? HasErrors : Clean;
        });
    }

    /// <summary>Recorded progress with labelled local claims and worker observations, through the API; exit 1 when the check it shows found errors, as status does.</summary>
    public static int Progress(string root, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        ProgressResponse progress = api.Progress();
        Console.Out.Write(json ? CanonicalJson.SerializePretty(progress.Json) : CommandPresentation.Protected(api, "status", "estate progress", progress.Text, presentation) + CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, presentation?.Recipient, () => progress)));
        return progress.HasErrors ? HasErrors : Clean;
    });

    public static int Resume(string root, string input, bool json) => WithBody(input, body => WithApi(root, api =>
    {
        HandoffPackage? package = body.Deserialize<HandoffPackage>(ProtocolJson.Options);
        if (package is null)
        {
            return Fail("A retained hand-over package is required.");
        }

        ResumeResponse response = api.ResumeHandoff(package);
        Console.Out.Write(json ? response.Json is JsonElement view ? CanonicalJson.SerializePretty(view) : JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
        return response.Problem is not null ? Failed : response.HasErrors ? HasErrors : Clean;
    }));

    public static int Handoff(string root, string? output, long? maxBytes, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        HandoffResponse response = api.Handoff(maxBytes);
        if (response.Package is null)
        {
            Console.Out.Write(json ? JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
            return Failed;
        }

        string package = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(response.Package, ProtocolJson.Options));
        if (output is not null)
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(Encoding.UTF8.GetBytes(package));
        }

        Console.Out.Write(json ? package : ContextTextMeasurement.Presentation(CommandPresentation.Protected(api, "handoff", response.Package.Hash, response.Text, presentation)).Text);
        return Clean;
    });

    /// <summary>What waits on a principal, or on every principal, through the API: at most three asks in one delivery to each, the recommendation first.</summary>
    public static int Ask(string root, string? to, bool json) => WithApi(root, api =>
    {
        if (to is not null && !Records.Identity.Ulid.IsValid(to))
        {
            return Fail($"--to names a principal by its ULID, not '{to}'.");
        }

        AsksResponse asks = api.Asks(to);
        Console.Out.Write(json ? CanonicalJson.SerializePretty(asks.Json) : asks.Text);
        return Clean;
    });










    /// <summary>What <c>vericairn goal propose</c> was given.</summary>







    /// <summary>What <c>vericairn worker run</c> was given.</summary>









    private static RunRow? Follow(IEstateApi api, string run, string by) => RunConsole.Follow(api, run, by);

    private static void PrintSettled(RunRow? row) => RunConsole.PrintSettled(Context, row);

    /// <summary>What <c>vericairn goal step</c> was given.</summary>






    private static void PrintRun(RunRow row) => RunConsole.PrintRun(Context, row);

    private static void PrintStop(RunStop stop) => RunConsole.PrintStop(Context, stop);

    private static string Units(JsonElement? caps) => RunConsole.Units(caps);


    public static int Decisions(string root, string? id, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        if (id is null)
        {
            DecisionsResponse decisions = api.Decisions();
            Console.Out.Write(json ? CanonicalJson.SerializePretty(decisions.Json) : CommandPresentation.Protected(api, "decision", "decision index", decisions.Text, presentation));
            return Clean;
        }

        DecisionResponse? decision = api.Decision(id);
        if (decision is null)
        {
            return Fail($"No decision has id {id}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(decision.Body) : CommandPresentation.Protected(api, "decision", id, decision.Text, presentation));
        return Clean;
    });

    public static int Doctor(string root)
    {
        Console.Out.WriteLine($"vericairn {VersionText()}");
        Console.Out.WriteLine($"runtime   .NET {Environment.Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Console.Out.WriteLine($"git       {Probe("git", "--version") ?? "not found on PATH"}");
        Console.Out.WriteLine($"dotnet    {Probe("dotnet", "--version") ?? "not found on PATH (needed only to build from source)"}");
        Console.Out.WriteLine(IsEstate(root) ? $"estate    {Path.GetFullPath(root)} (manifest present)" : $"estate    {Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath})");
        long? format = IsEstate(root) ? EstateManifest.Load(root, new FindingList())?.RecordFormat : null;
        Console.Out.WriteLine(format is < 2
            ? $"custody   {DeviceKeyStores.Default(enclave: false).Kind} (the default for this estate, at record format {format}, which cannot name the Secure Enclave; {DeviceKeyStores.Default().Kind} once it is at format 2); per-user state at {VericairnHome.Path()}"
            : $"custody   {DeviceKeyStores.Default().Kind} (default on this platform); per-user state at {VericairnHome.Path()}");
        if (OperatingSystem.IsMacOS())
        {
            Console.Out.WriteLine(SecureEnclaveKeyStore.Available()
                ? "enclave   Secure Enclave custody available (record format 2): a person's device asks for Touch ID or the login password at every signature unless it is created with --presence false; an agent's never asks"
                : $"enclave   Secure Enclave custody unavailable ({(SecureEnclaveCustody.HelperPath() is null ? $"no {SecureEnclaveCustody.HelperName} helper beside the command line" : "no Secure Enclave available to this process")})");
        }
        Console.Out.WriteLine($"records   reads record formats {string.Join(", ", RecordFormats.Reads)}; writes an estate's own format, at most {RecordFormats.Current}");
        if (IsEstate(root))
        {
            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            Console.Out.WriteLine(endpoint is not null && VericairnClient.IsAlive(endpoint)
                ? $"daemon    process {endpoint.ProcessId} serves this estate since {endpoint.StartedAt} ({endpoint.Kind}{(endpoint.Loopback is null ? string.Empty : ", loopback " + endpoint.Loopback.Port)})"
                : "daemon    none; commands run the embedded runtime");
        }

        return Clean;
    }

    public static int Version()
    {
        Console.Out.WriteLine(VersionText());
        return Clean;
    }

    public static int RuntimeServe(string root, bool loopback, bool detached = false)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
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
                return Fail($"Daemon {existing.ProcessId} already serves this estate; one process owns an estate's live state.");
            }

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };
            using PosixSignalRegistration? hangup = IgnoreHangup();
            var options = new DaemonOptions { Log = line => Console.Out.WriteLine($"{RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)} {line}") };
            DaemonHost host = DaemonHost.StartAsync(root, loopback: loopback, options: options).GetAwaiter().GetResult();
            Console.Out.WriteLine($"vericairnd {VersionText()} serving {host.Service.Root}");
            Console.Out.WriteLine($"  {host.Endpoint.Kind}  {host.Endpoint.Address}");
            if (host.Endpoint.Loopback is LoopbackEndpoint published)
            {
                Console.Out.WriteLine($"  loopback  https://{published.Host}:{published.Port} pinned to {published.Certificate}");
            }

            if (host.Endpoint.LocalNetwork is LocalNetworkEndpoint network)
            {
                Console.Out.WriteLine($"  local network  {LocalNetworkAddress(network)}, mutual TLS with paired devices; runtime identity {network.Identity}");
            }

            Console.Out.WriteLine($"  process {host.Endpoint.ProcessId}; stop with Ctrl+C or 'vericairn runtime stop'");
            host.WaitForShutdownAsync(stop.Token).GetAwaiter().GetResult();
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Console.Out.WriteLine("vericairnd stopped");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return Fail(exception.Message);
        }
        catch (Exception exception) when (detached)
        {
            // Nobody reads a detached daemon's streams: an unexpected failure goes to its log, where 'runtime start' finds it.
            return Fail($"the daemon stopped on an unexpected {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>The name of the daemon's log beside the estate's live store.</summary>
    internal const string DaemonLogName = "daemon.log";

    /// <summary>Where a daemon started by 'runtime start' writes its output and its failures for an estate on this machine.</summary>
    internal static string DaemonLogPath(string root) => Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!, DaemonLogName);

    public static int RuntimeStart(string root, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
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
                return Fail($"The daemon's log {log} cannot be written, so no daemon was started: {exception.Message}");
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
                return Fail("The daemon process did not start.");
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
                    return Fail($"The daemon exited with code {process.ExitCode} before serving{LogSince(log, logStart)}. Its log is {log}.");
                }

                Thread.Sleep(200);
            }

            return Fail($"The daemon did not start serving within 30 seconds{LogSince(log, logStart)}. Its log is {log}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Fail(exception.Message);
        }
    }

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

    public static int RuntimeStop(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return Fail(NotAnEstate(root));
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        if (endpoint is null || !VericairnClient.IsAlive(endpoint))
        {
            Console.Out.WriteLine(json ? "{\"stopped\":null}" : "no daemon is serving this estate");
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
            Console.Out.WriteLine(json ? JsonSerializer.Serialize(new { stopped = stopped ? endpoint.ProcessId : (int?)null, process = endpoint.ProcessId }) : $"asked daemon {endpoint.ProcessId} to stop; {(stopped ? "stopped" : "still answering")}");
            return stopped ? Clean : HasErrors;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeStatus(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return Fail(NotAnEstate(root));
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
            Console.Error.WriteLine("warning: " + exception.Message);
        }

        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new { mode = alive ? "daemon" : "embedded", daemon = alive ? endpoint : null, localNetwork = configured }, ProtocolJson.Options));
        }
        else if (alive)
        {
            Console.Out.WriteLine($"daemon {endpoint!.ProcessId} serves {endpoint.Root} since {endpoint.StartedAt}");
            Console.Out.WriteLine($"  {endpoint.Kind}  {endpoint.Address}");
            if (endpoint.Loopback is LoopbackEndpoint published)
            {
                Console.Out.WriteLine($"  loopback  https://{published.Host}:{published.Port} pinned to {published.Certificate}");
            }

            if (endpoint.LocalNetwork is LocalNetworkEndpoint network)
            {
                Console.Out.WriteLine($"  local network  {LocalNetworkAddress(network)}, mutual TLS with paired devices; runtime identity {network.Identity}");
            }
        }
        else
        {
            Console.Out.WriteLine("no daemon is serving this estate; commands run the embedded runtime");
            if (configured is not null)
            {
                Console.Out.WriteLine($"  the local-network profile is configured at {configured} and served from the runtime's next start");
            }
        }

        return Clean;
    }

    public static int RuntimeInstall(string root, bool planOnly, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
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
                Console.Out.WriteLine(JsonSerializer.Serialize(new { installed = plan.Label, unit = plan.UnitPath, log }));
            }
            else
            {
                foreach (string line in log)
                {
                    Console.Out.WriteLine(line);
                }

                Console.Out.WriteLine($"installed {plan.Label}: the runtime starts at login and restarts after failure; remove it with 'vericairn runtime uninstall'.");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeUninstall(string root, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            ServicePlan plan = ServiceInstaller.PlanHere(root);
            IReadOnlyList<string> log = ServiceInstaller.Uninstall(plan);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { uninstalled = plan.Label, log }));
            }
            else
            {
                foreach (string line in log)
                {
                    Console.Out.WriteLine(line);
                }

                Console.Out.WriteLine($"uninstalled {plan.Label}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return Fail(exception.Message);
        }
    }


























    public static int RuntimeConfigure(string root, string? localNetwork, bool allowPublic, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            if (allowPublic && localNetwork is null)
            {
                return Fail("--allow-public allows the address --local-network gives; give both.");
            }

            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            RuntimeSettings settings;
            string? exposure = null;
            if (localNetwork is null)
            {
                settings = RuntimeSettings.Load(directory);
            }
            else
            {
                string? address = null;
                if (!localNetwork.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    if (!RuntimeSettings.TryParseAddress(localNetwork, out System.Net.IPEndPoint? endpoint, out string? problem))
                    {
                        return Fail(problem!);
                    }

                    // The profile is for a private network: a wildcard or public address is the operator's explicit choice.
                    exposure = RuntimeSettings.Exposure(endpoint!.Address);
                    if (exposure is not null && !allowPublic)
                    {
                        return Fail($"{endpoint} is {exposure}, and the local-network profile is for a private network: give this machine's address on it (in 10/8, 172.16/12, 192.168/16, link-local or fc00::/7), or add --allow-public to listen there anyway. Nothing was changed.");
                    }

                    address = endpoint.ToString();
                }

                settings = new RuntimeSettings(address, AllowPublic: exposure is not null);
                settings.Save(directory);
            }

            DaemonEndpoint? running = DaemonHost.Discover(root);
            bool alive = running is not null && VericairnClient.IsAlive(running);
            string? served = running?.LocalNetwork is LocalNetworkEndpoint lan ? new System.Net.IPEndPoint(System.Net.IPAddress.Parse(lan.Host), lan.Port).ToString() : null;
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { localNetwork = settings.LocalNetwork, allowPublic = settings.AllowPublic, served = alive ? served : null, restart = alive && localNetwork is not null }, ProtocolJson.Options));
                return Clean;
            }

            Console.Out.WriteLine(settings.LocalNetwork is null
                ? "the local-network profile is off: the runtime listens on its local socket and loopback only"
                : $"the runtime serves the local-network profile at {settings.LocalNetwork}, to devices that pair with it, from its next start");
            if (settings.AllowPublic)
            {
                Console.Out.WriteLine("  allowed with --allow-public: a wildcard or public address, where every device that reaches it may try to pair");
            }
            if (alive)
            {
                Console.Out.WriteLine(served is null ? $"  daemon {running!.ProcessId} serves no local-network profile now" : $"  daemon {running!.ProcessId} serves it now at {served}");
                if (localNetwork is not null)
                {
                    Console.Out.WriteLine("  the running daemon keeps its listeners until it restarts: 'vericairn runtime stop', then 'vericairn runtime start'");
                }
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public static int RuntimeRotate(string root, bool compromised, bool identity, string? reason, bool json)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null && VericairnClient.IsAlive(endpoint))
            {
                using var client = new VericairnClient(endpoint);
                (CertificateRotated? rotated, Problem? problem) = client.RotateCertificate(new CertificateRotateRequest(compromised, reason, identity));
                if (rotated is null)
                {
                    return Fail(problem?.Message ?? "The certificate was not rotated.");
                }

                if (json)
                {
                    Console.Out.WriteLine(JsonSerializer.Serialize(rotated, ProtocolJson.Options));
                    return Clean;
                }

                Console.Out.WriteLine($"runtime certificate {rotated.Certificate}, until {rotated.NotAfter}, replaced {rotated.Previous}");
                Console.Out.WriteLine(rotated.Identity is null
                    ? "  paired devices keep working: they pin the runtime identity, which signs the certificate in use"
                    : $"  runtime identity replaced by {rotated.Identity}: every paired device pairs again");
                if (rotated.Compromised)
                {
                    Console.Out.WriteLine($"  after a compromise: {rotated.Closed} open connections were closed, and the old certificate is refused by every client");
                }

                return Clean;
            }

            // No daemon: the files are replaced now and the next start serves them; nothing is open to close.
            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!;
            string? replacedIdentity = null;
            if (identity)
            {
                if (!File.Exists(Path.Combine(directory, RuntimeIdentity.FileName)))
                {
                    return Fail("This runtime has no identity to replace: it is made when the local-network profile is first served.");
                }

                replacedIdentity = RuntimeIdentity.LoadOrCreate(directory).Replace(directory).Fingerprint;
            }

            using System.Security.Cryptography.X509Certificates.X509Certificate2 next = RuntimeCertificate.Replace(directory);
            string fingerprint = RuntimeCertificate.Fingerprint(next);
            if (json)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new { certificate = fingerprint, identity = replacedIdentity, served = false }, ProtocolJson.Options));
                return Clean;
            }

            Console.Out.WriteLine($"no daemon is serving this estate: the runtime certificate is now {fingerprint}, served from the next start");
            if (replacedIdentity is not null)
            {
                Console.Out.WriteLine($"  runtime identity replaced by {replacedIdentity}: every paired device pairs again");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or System.Security.Cryptography.CryptographicException)
        {
            return Fail(exception.Message);
        }
    }








    internal static ISourceAdapter? SourceAdapter
    {
        get => CommandProviders.SourceAdapter;
        set => CommandProviders.SourceAdapter = value;
    }

    internal static IReleaseProvider? ReleaseProvider
    {
        get => CommandProviders.ReleaseProvider;
        set => CommandProviders.ReleaseProvider = value;
    }

    private static ISourceAdapter? GitAdapter(out int failure) => CommandProviders.Git(context: Context, out failure);










    private static Dictionary<string, string>? ParseSettings(string[] settings, out string? problem) => CommandSettings.Parse(settings, out problem);



    private static JsonElement Element(Json.JsonObject body) => CommandJson.Element(body);

    /// <summary>Runs an operation against the estate's API: the daemon's when one answers for this estate, the embedded runtime otherwise.</summary>
    private static int WithApi(string root, Func<IEstateApi, int> operation) =>
        Context.WithApi(root, operation);

    private static string LocalNetworkAddress(LocalNetworkEndpoint network) =>
        "https://" + new System.Net.IPEndPoint(System.Net.IPAddress.Parse(network.Host), network.Port);

    /// <summary>A bare records directory without a manifest is still validated from its files; no live state is involved.</summary>
    private static int WithFiles(string root, Func<EstateModel, int> operation) =>
        Context.WithFiles(root, operation);

    private static int WithBody(string bodyPath, Func<JsonElement, int> operation) =>
        Context.WithBody(bodyPath, operation);

    internal static EstateModel LoadCopy(string root) => CommandContext.LoadCopy(root);

    private static bool IsEstate(string root) => CommandContext.IsEstate(root);

    private static string NotAnEstate(string root) => CommandContext.NotAnEstate(root);

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

    private static void PrintPlan(ServicePlan plan, bool json)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(plan, ProtocolJson.Options));
            return;
        }

        Console.Out.WriteLine($"{plan.Platform}: {plan.Label}");
        Console.Out.WriteLine($"unit {plan.UnitPath}:");
        Console.Out.Write(plan.UnitContent);
        foreach (IReadOnlyList<string> command in plan.Install)
        {
            Console.Out.WriteLine("install: " + string.Join(' ', command));
        }

        foreach (IReadOnlyList<string> command in plan.Uninstall.Concat(plan.AfterUninstall))
        {
            Console.Out.WriteLine("uninstall: " + string.Join(' ', command));
        }
    }


    private static string VersionText() => CommandContext.VersionText();

    private static string? Probe(string tool, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { argument }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output.Split('\n')[0] : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static FindingsResponse ToFindings(FindingList findings, string summary) =>
        CommandContext.ToFindings(findings, summary);

    private static int ReportOutcome(WriteOutcome outcome, bool json) =>
        Context.ReportOutcome(outcome, json);

    internal static int ReportFindings(FindingsResponse findings, bool json) =>
        Context.ReportFindings(findings, json);

    internal static int Fail(string message) =>
        Context.Fail(message);


}
