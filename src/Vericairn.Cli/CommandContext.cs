// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>
/// The options, output and API lifetime shared by a command tree. An estate operation uses its daemon when available,
/// otherwise the embedded runtime; both are disposed after the handler returns.
/// </summary>
internal sealed class CommandContext(TextWriter? output = null, TextWriter? error = null, CancellationToken cancellationToken = default)
{
    private ClientBoundary? boundary;
    private readonly HashSet<string> daemonWarnings = new(StringComparer.Ordinal);
    internal IEstateApi? BorrowedApi { get; init; }
    internal JsonElement? ReleaseNoticePin { get; set; }

    public bool UsingDaemon { get; private set; }

    // The daemon this invocation's estate operation used and the health it answered; null when the operation ran embedded.
    private (string Root, DaemonEndpoint Endpoint, HealthResponse Health)? served;

    /// <summary>The daemon this invocation's estate operation used, against the estate's pin; null when it ran embedded (OPS-05).</summary>
    internal ServingRuntime? Serving() => served is { } used ? ServingRuntime.Of(used.Root, used.Endpoint, used.Health) : null;

    // The health the last liveness read answered, so a command can name the serving release without asking again.
    private HealthResponse? lastHealth;

    public CancellationToken CancellationToken => boundary?.Token ?? cancellationToken;

    public int Invoke(Func<int> action)
    {
        daemonWarnings.Clear();
        served = null;
        using var invocation = new ClientBoundary(Error, cancellationToken);
        boundary = invocation;
        try { return invocation.Invoke(action); }
        finally { boundary = null; }
    }

    public IDisposable HandleInterrupt(Action action) =>
        (boundary ?? throw new InvalidOperationException("No command is running.")).HandleInterrupt(action);

    public bool IsAlive(DaemonEndpoint endpoint)
    {
        using var client = new VericairnClient(endpoint);
        return IsAlive(client);
    }

    /// <summary>Whether the daemon at an endpoint answers this API, with the health it answered when it does.</summary>
    public bool IsAlive(DaemonEndpoint endpoint, out HealthResponse? health)
    {
        lastHealth = null;
        bool alive = IsAlive(endpoint);
        health = alive ? lastHealth : null;
        return alive;
    }

    private bool IsAlive(VericairnClient client)
    {
        try
        {
            HealthResponse health = client.HealthAsync(CancellationToken).GetAwaiter().GetResult();
            lastHealth = health;
            if (client.DaemonVersionWarning is { } warning && daemonWarnings.Add(warning))
                Error.WriteLine("warning: " + warning);
            return health.Api == ApiVersion.Current;
        }
        // An unreachable or unanswering daemon is not alive, as before the client boundary wrapped transport failures: the command
        // falls back to the embedded runtime. A person's cancellation still propagates.
        catch (Exception exception) when (exception is HttpRequestException or IOException or System.Net.Sockets.SocketException or JsonException or UnauthorizedAccessException
            or ClientUnavailableException or ClientTimeoutException)
        {
            return false;
        }
    }

    internal const int Clean = 0;
    internal const int HasErrors = 1;
    internal const int Failed = 2;

    public Option<string> EstateOption { get; } = new("--estate", "-e")
    {
        Description = "Path to the estate repository root.",
        DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        Recursive = true,
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Machine-readable output.",
        Recursive = true,
    };

    // Registry clients supply the server's inputs before its positional command arguments.
    // Share this option with mcp serve; it is not a global acting identity for other commands.
    public Option<string> McpPrincipalOption { get; } = new("--as")
    {
        Description = "Principal id for mcp serve; its admitted device on this machine signs the writes. Without it only the read-only tools are listed.",
    };

    public PresentationOptions Presentation { get; } = new();

    // Resolve the console writers at execution time, including when a caller captures output after building the tree.
    public TextWriter Output => output ?? Console.Out;
    public TextWriter Error => error ?? Console.Error;

    /// <summary>
    /// Runs an operation against the estate's API, retaining the existing ownership and failure boundary. An operation that may write
    /// shows the presence prompts it asks for unless <paramref name="presence"/> is false, as for a server that runs for a session.
    /// </summary>
    public int WithApi(string root, Func<IEstateApi, int> operation, bool readOnly = false, bool upgrade = false, bool presence = true)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            // In-process journeys supply an isolated service with explicit time and fixture providers.
            // Its owner retains its lifetime; ordinary invocations still discover their runtime below.
            if (BorrowedApi is not null)
            {
                if (BorrowedApi is VericairnClient borrowedClient) _ = IsAlive(borrowedClient);
                return Showing(new CancellationBoundApi(BorrowedApi, CancellationToken), operation, readOnly || !presence);
            }

            served = null;
            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null)
            {
                using var client = new VericairnClient(endpoint);
                if (IsAlive(client))
                {
                    UsingDaemon = true;
                    served = (root, endpoint, lastHealth!);
                    return Showing(new CancellationBoundApi(client, CancellationToken), operation, readOnly || !presence);
                }
            }

            using EstateService service = readOnly ? EstateService.OpenReadOnly(root) : upgrade ? EstateService.OpenForUpgrade(root) : EstateService.Open(root);
            UsingDaemon = false;
            return Showing(new CancellationBoundApi(service, CancellationToken), operation, readOnly || !presence);
        }
        catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException) && exception is not IClientOperationFailure)
        {
            return Fail(exception.Message);
        }
    }

    /// <summary>
    /// Runs an operation, showing on standard error, for one that may write, the presence prompts its operator action asks for on the
    /// runtime's machine: the announcement before the first and "Signature k of n" at each (FIX-124). A read-only operation shows none.
    /// </summary>
    private int Showing(IEstateApi api, Func<IEstateApi, int> operation, bool quiet)
    {
        if (quiet) return operation(api);
        using PresenceDisplay display = PresenceDisplay.Start(api, Error);
        return operation(api);
    }

    /// <summary>Runs an operation that only a running daemon serves, such as pairing; refused, with the way on, when none answers.</summary>
    public int WithDaemon(string root, string purpose, Func<VericairnClient, int> operation)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            using var client = endpoint is null ? null : new VericairnClient(endpoint);
            if (client is null || !IsAlive(client))
            {
                return Fail($"No daemon is serving this estate, and {purpose} needs one: configure the local-network profile with 'vericairn runtime configure --local-network <address:port>' and start the runtime with 'vericairn runtime start'.");
            }

            return operation(client);
        }
        catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException) && exception is not IClientOperationFailure)
        {
            return Fail(exception.Message);
        }
    }

    /// <summary>A bare records directory without a manifest is still validated from its files; no live state is involved.</summary>
    public int WithFiles(string root, Func<EstateModel, int> operation)
    {
        try
        {
            return operation(EstateLoader.Load(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
    }

    public int WithBody(string bodyPath, Func<JsonElement, int> operation)
    {
        try
        {
            using JsonDocument body = CanonicalJson.Parse(bodyPath == "-" ? ReadStandardInput() : File.ReadAllBytes(bodyPath));
            return operation(body.RootElement.Clone());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
        {
            return Fail(exception.Message);
        }
    }

    public int ReportOutcome(WriteOutcome outcome, bool json)
    {
        if (outcome.Written is RecordWritten written)
        {
            if (json && outcome.Checkpoint is { } triggered)
                Output.WriteLine(JsonSerializer.Serialize(new { path = written.Path, id = written.Id, hash = written.Hash, checkpoint = triggered }, ProtocolJson.Options));
            else
                Output.WriteLine(json ? JsonSerializer.Serialize(new { path = written.Path, id = written.Id, hash = written.Hash }) : $"{written.Path}  {written.Id}  {written.Hash}");
            if (!json) ReportCheckpoint(outcome.Checkpoint);
            else if (outcome.Checkpoint?.Skipped is { } skipped) Error.WriteLine("warning: " + skipped);
            if (outcome.Problem is { } interrupted) return Fail(interrupted.Message);
            return Clean;
        }

        Problem problem = outcome.Problem ?? new Problem("refused", "refused", []);
        // A closing stored without its act (OPS-07) is a record written all the same: it is shown before the failure.
        if (outcome.Checkpoint is { Written: not null } stored)
        {
            if (json) Output.WriteLine(JsonSerializer.Serialize(new { code = problem.Code, checkpoint = stored }, ProtocolJson.Options));
            else ReportCheckpoint(stored);
        }

        if (problem.Code == "rejected")
        {
            Error.WriteLine(problem.Message);
            return ReportFindings(new FindingsResponse("rejected", problem.Findings.Count(f => f.Severity == "error"), problem.Findings.Count(f => f.Severity == "warning"), problem.Findings), json);
        }

        return Fail(problem.Message);
    }

    /// <summary>
    /// The history checkpoint a trigger wrote with an act (0.9.2): its path, id and hash with its trigger, or, as a warning, why none was
    /// written; nothing when no trigger applied.
    /// </summary>
    public void ReportCheckpoint(TriggeredCheckpoint? checkpoint)
    {
        if (checkpoint is null) return;
        if (checkpoint.Written is { } written) Output.WriteLine($"{written.Path}  {written.Id}  {written.Hash}  (history checkpoint, trigger {checkpoint.Trigger})");
        else if (checkpoint.Skipped is { } skipped) Error.WriteLine("warning: " + skipped);
    }

    public int ReportFindings(FindingsResponse findings, bool json)
    {
        if (json)
        {
            Output.WriteLine(JsonSerializer.Serialize(new
            {
                summary = findings.Summary,
                errors = findings.Errors,
                warnings = findings.Warnings,
                findings = findings.Findings.Select(f => new { severity = f.Severity, f.Code, f.Location, f.Message }),
                policies = JsonSerializer.SerializeToElement(findings.Policies, ProtocolJson.Options),
                writingExceptions = JsonSerializer.SerializeToElement(findings.WritingExceptions, ProtocolJson.Options),
            }));
        }
        else
        {
            foreach (FindingDto finding in findings.Findings)
            {
                Output.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Location}: {finding.Message}");
            }

            Output.WriteLine($"{findings.Summary}: {findings.Errors} errors, {findings.Warnings} warnings");
            if (findings.Policies is { } policies) Output.Write(PolicyPresentation.Render(policies, includeDiagnostics: false));
            if (findings.WritingExceptions is { } writing) Output.Write(PolicyPresentation.Render(writing, includeDiagnostics: false));
        }

        return findings.Errors > 0 ? HasErrors : Clean;
    }

    public int Fail(string message)
    {
        Error.WriteLine("error: " + message);
        return Failed;
    }

    public static EstateModel LoadCopy(string root)
    {
        if (!IsEstate(root))
        {
            return EstateLoader.Load(root);
        }

        using EstateSession session = EstateSession.Open(root);
        return session.Load();
    }

    public static string VersionText() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static bool IsEstate(string root) => File.Exists(Path.Combine(root, EstateLayout.ManifestPath));

    public static string NotAnEstate(string root) => $"{Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath}).";

    public static FindingsResponse ToFindings(FindingList findings, string summary) =>
        new(summary, findings.Errors, findings.Warnings, findings.Sorted().Select(f => new FindingDto(f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message)).ToList());

    private static byte[] ReadStandardInput()
    {
        using Stream input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }
}
