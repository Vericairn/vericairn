// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>
/// The daemon serving an estate on this machine, as its endpoint file and its health describe it, against the release the estate's
/// manifest pins (OPS-05). It is machine-local knowledge: canonical status never carries it.
/// </summary>
internal sealed class ServingRuntime
{
    /// <summary>How long one health read of a daemon may take here; a local daemon answers at once.</summary>
    internal static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(10);

    private ServingRuntime(string root, DaemonEndpoint endpoint, HealthResponse? health, string? refused, EstateManifest? manifest)
    {
        Root = Path.GetFullPath(root);
        Endpoint = endpoint;
        Health = health;
        Refused = refused;
        Manifest = manifest;
        Relation = Relate();
    }

    /// <summary>The estate root asked about.</summary>
    public string Root { get; }

    /// <summary>The endpoint the daemon published beside this estate's live store.</summary>
    public DaemonEndpoint Endpoint { get; }

    /// <summary>What the daemon said of itself, or null when it refused this user's connection.</summary>
    public HealthResponse? Health { get; }

    /// <summary>Why the daemon could not be read, when it refused this user's connection.</summary>
    public string? Refused { get; }

    /// <summary>The estate's manifest as it is now, or null when it cannot be read.</summary>
    public EstateManifest? Manifest { get; }

    /// <summary>
    /// How the serving runtime stands against the pin: <c>pinned</c>, <c>older</c>, <c>newer</c>, <c>unordered</c> (a release that
    /// cannot be ordered against the pin), <c>no-release-pin</c> (the estate pins a source commit, which names no release, or its manifest
    /// cannot be read),
    /// <c>other-estate</c> (the daemon answering this estate's endpoint serves another estate or root) or <c>unreadable</c>.
    /// </summary>
    public string Relation { get; }

    /// <summary>The daemon's process id, as it reports it or as it published it.</summary>
    public int ProcessId => Health?.ProcessId ?? Endpoint.ProcessId;

    /// <summary>The daemon's release, without build metadata; null when it could not be read.</summary>
    public string? Release => Health is null ? null : ReleaseOf(Health.Vericairn);

    /// <summary>The release the estate pins; null for a source pin or an unreadable manifest.</summary>
    public string? PinnedRelease => Manifest is { Mode: "release", Version: { } version } ? version : null;

    /// <summary>Reads the daemon serving the estate on this machine, or null when none answers there.</summary>
    public static ServingRuntime? Read(string root, CancellationToken cancellationToken)
    {
        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        if (endpoint is null) return null;
        (HealthResponse? health, string? refused) = Probe(endpoint, cancellationToken);
        return health is null && refused is null ? null : new ServingRuntime(root, endpoint, health, refused, LoadManifest(root));
    }

    /// <summary>The daemon a command has just read, from the health it answered.</summary>
    public static ServingRuntime Of(string root, DaemonEndpoint endpoint, HealthResponse health) =>
        new(root, endpoint, health, null, LoadManifest(root));

    /// <summary>The estate's manifest as it is now, or null when it cannot be read.</summary>
    internal static EstateManifest? Pin(string root) => LoadManifest(root);

    /// <summary>Reads a daemon's health: what it said, or why it refused this user; both null when nothing answers at its address.</summary>
    internal static (HealthResponse? Health, string? Refused) Probe(DaemonEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new VericairnClient(endpoint) { OperationTimeout = _ => ProbeDeadline };
            return (client.HealthAsync(cancellationToken).GetAwaiter().GetResult(), null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or IOException or SocketException or UnauthorizedAccessException or TimeoutException or JsonException)
        {
            return (null, Denied(exception) ? Reason(exception) : null);
        }
    }

    /// <summary>One line naming the daemon, its release and the pin.</summary>
    public string Describe() => Relation == "unreadable"
        ? $"daemon {ProcessId} published this estate's endpoint and refused this user's connection: {Refused}"
        : $"daemon {ProcessId}, {ReleaseLine()}";

    /// <summary>The daemon's release against the pin, as one phrase.</summary>
    public string ReleaseLine() => Relation switch
    {
        "unreadable" => "release unknown",
        "pinned" => $"Vericairn {Release}, the release the estate pins",
        "other-estate" => $"Vericairn {Release}, serving estate {Health!.Estate} at {Health.Root}, not this estate",
        "no-release-pin" => $"Vericairn {Release}; " + (Manifest is null ? "the estate's manifest cannot be read" : $"the estate pins {Manifest.Describe()}, which names no release"),
        _ => $"Vericairn {Release}; the estate pins release {PinnedRelease}",
    };

    /// <summary>The mismatch between the serving runtime and the pin with the way to settle it, or null when it serves the pinned release or the pin names none.</summary>
    public string? Mismatch() => Relation switch
    {
        "older" => $"the serving runtime is Vericairn {Release}, older than the estate's pin, release {PinnedRelease}, so the estate is served with {Release}'s operations and rules. "
            + $"Stop it with '{StopCommand(Root)}', then start the pinned release's runtime with {StartCommand(Root, PinnedRelease!)}.",
        "newer" => $"the serving runtime is Vericairn {Release}, newer than the estate's pin, release {PinnedRelease}. Upgrade the estate to that release, "
            + $"or stop it with '{StopCommand(Root)}' and start the pinned release's runtime with {StartCommand(Root, PinnedRelease!)}.",
        "unordered" => $"the serving runtime reports Vericairn {Release}, which cannot be ordered against the estate's pin, release {PinnedRelease}. "
            + $"Stop it with '{StopCommand(Root)}' and start the pinned release's runtime with {StartCommand(Root, PinnedRelease!)}.",
        "other-estate" => $"daemon {ProcessId} answers this estate's endpoint and serves estate {Health!.Estate} at {Health.Root}; it was left running. "
            + $"See 'vericairn --estate {Quote(Health.Root)} runtime status'.",
        "unreadable" => $"daemon {ProcessId} refused this user's connection ({Refused}), so its release is unknown. As the user that started it, run "
            + $"'{StopCommand(Root)}', then start the pinned release's runtime with "
            + (PinnedRelease is { } pinned ? StartCommand(Root, pinned) : $"'vericairn --estate {Quote(Root)} runtime start'") + ".",
        _ => null,
    };

    /// <summary>A release without its build metadata, as releases are compared.</summary>
    internal static string ReleaseOf(string? version) => version?.Split('+')[0] ?? "unknown";

    /// <summary>The command that stops the daemon serving an estate, with any release's command line.</summary>
    internal static string StopCommand(string root) => $"vericairn --estate {Quote(Path.GetFullPath(root))} runtime stop";

    /// <summary>How to start the pinned release's runtime: the estate's bootstrap, which runs exactly the pinned release, or that release's command line.</summary>
    internal static string StartCommand(string root, string release)
    {
        string direct = $"'vericairn --estate {Quote(Path.GetFullPath(root))} runtime start' with Vericairn {release}'s command line";
        string bootstrap = OperatingSystem.IsWindows() ? "bootstrap.ps1" : "bootstrap";
        return File.Exists(Path.Combine(root, bootstrap))
            ? $"'{(OperatingSystem.IsWindows() ? @".\bootstrap.ps1" : "./bootstrap")} runtime start' in the estate's directory, which runs release {release}, or {direct}"
            : direct;
    }

    /// <summary>A path quoted for a command line when it holds a space, a quote or a shell metacharacter.</summary>
    internal static string Quote(string value) =>
        value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '$' or '`' or '&' or ';' or '|' or '(' or ')' or '<' or '>')
            ? value : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>The most specific message of a failure, which names the cause rather than the wrapper.</summary>
    internal static string Reason(Exception exception)
    {
        Exception innermost = exception;
        while (innermost.InnerException is { } inner) innermost = inner;
        string text = innermost.Message.Trim().TrimEnd('.');
        return text.Length == 0 ? exception.Message.Trim().TrimEnd('.') : text;
    }

    private static bool Denied(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException || current is SocketException { SocketErrorCode: SocketError.AccessDenied }
                || current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                return true;
        }

        return false;
    }

    private static EstateManifest? LoadManifest(string root)
    {
        try
        {
            return EstateManifest.Load(Path.GetFullPath(root), new FindingList());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string Relate()
    {
        if (Health is null) return "unreadable";
        if (!SamePath(Health.Root, Root) || Manifest is not null && Health.Estate != Manifest.Estate) return "other-estate";
        if (PinnedRelease is not { } pinned) return "no-release-pin";
        string release = Release!;
        if (release == pinned) return "pinned";
        if (ReleaseFollowing.IsNewer(pinned, release)) return "older";
        return ReleaseFollowing.IsNewer(release, pinned) ? "newer" : "unordered";
    }

    private static bool SamePath(string left, string right)
    {
        static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(Normal(left), Normal(right), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
