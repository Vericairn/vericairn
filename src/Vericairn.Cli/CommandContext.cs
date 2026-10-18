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

    public bool UsingDaemon { get; private set; }

    public CancellationToken CancellationToken => boundary?.Token ?? cancellationToken;

    public int Invoke(Func<int> action)
    {
        using var invocation = new ClientBoundary(Error, cancellationToken);
        boundary = invocation;
        try { return invocation.Invoke(action); }
        finally { boundary = null; }
    }

    public IDisposable HandleInterrupt(Action action) =>
        (boundary ?? throw new InvalidOperationException("No command is running.")).HandleInterrupt(action);

    public bool IsAlive(DaemonEndpoint endpoint)
    {
        try
        {
            using var client = new VericairnClient(endpoint);
            return client.HealthAsync(CancellationToken).GetAwaiter().GetResult().Api == ApiVersion.Current;
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

    public PresentationOptions Presentation { get; } = new();

    // Resolve the console writers at execution time, including when a caller captures output after building the tree.
    public TextWriter Output => output ?? Console.Out;
    public TextWriter Error => error ?? Console.Error;

    /// <summary>Runs an operation against the estate's API, retaining the existing ownership and failure boundary.</summary>
    public int WithApi(string root, Func<IEstateApi, int> operation)
    {
        try
        {
            if (!IsEstate(root))
            {
                return Fail(NotAnEstate(root));
            }

            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            if (endpoint is not null && IsAlive(endpoint))
            {
                using var client = new VericairnClient(endpoint);
                UsingDaemon = true;
                return operation(new CancellationBoundApi(client, CancellationToken));
            }

            using EstateService service = EstateService.Open(root);
            UsingDaemon = false;
            return operation(new CancellationBoundApi(service, CancellationToken));
        }
        catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException) && exception is not IClientOperationFailure)
        {
            return Fail(exception.Message);
        }
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
            if (endpoint is null || !IsAlive(endpoint))
            {
                return Fail($"No daemon is serving this estate, and {purpose} needs one: configure the local-network profile with 'vericairn runtime configure --local-network <address:port>' and start the runtime with 'vericairn runtime start'.");
            }

            using var client = new VericairnClient(endpoint);
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
            Output.WriteLine(json ? JsonSerializer.Serialize(new { path = written.Path, id = written.Id, hash = written.Hash }) : $"{written.Path}  {written.Id}  {written.Hash}");
            return Clean;
        }

        Problem problem = outcome.Problem ?? new Problem("refused", "refused", []);
        if (problem.Code == "rejected")
        {
            Error.WriteLine(problem.Message);
            return ReportFindings(new FindingsResponse("rejected", problem.Findings.Count(f => f.Severity == "error"), problem.Findings.Count(f => f.Severity == "warning"), problem.Findings), json);
        }

        return Fail(problem.Message);
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
            }));
        }
        else
        {
            foreach (FindingDto finding in findings.Findings)
            {
                Output.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Location}: {finding.Message}");
            }

            Output.WriteLine($"{findings.Summary}: {findings.Errors} errors, {findings.Warnings} warnings");
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
