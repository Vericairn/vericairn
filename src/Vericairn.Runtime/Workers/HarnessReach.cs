// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>What a process standing in the harness's place reached: the planted marker file and the test host.</summary>
/// <param name="OutsideFile">readable, blocked or unknown.</param>
/// <param name="TestHost">reachable, blocked or unknown.</param>
internal sealed record ReachMeasurement(string OutsideFile, string TestHost)
{
    /// <summary>Neither was measured.</summary>
    internal static ReachMeasurement Unknown { get; } = new("unknown", "unknown");
}

/// <summary>A secret-bearing path the worker's operating-system user can open for reading, named by its kind and path; its content is never read.</summary>
/// <param name="Kind">The kind of secret the path holds by convention, one of <see cref="HarnessReach.Kinds"/>.</param>
/// <param name="Path">The full path on this machine, kept only in the run's own custody.</param>
internal sealed record SecretPath(string Kind, string Path);

/// <summary>
/// What a worker run can reach outside its workspace (CODE-82; decision 01M7DH4TWF4GC4VSCYH1WC0VJ5). Neither native launcher confines
/// its harness process: it runs as the worker's operating-system user, and Codex's workspace-write sandbox confines only its shell
/// commands' writes and network, never its reads or the harness process itself. So the worker measures instead of assuming: it plants a
/// marker file outside the workspace and the run's temporary directory and starts a test host on the loopback interface, then starts a
/// probe in the harness's place (the same operating-system user, environment and working directory the harness gets) and records whether
/// the probe read each. The probe is the vericairn executable itself, with a private argument; it executes nothing else. Before a run
/// starts the worker also lists the secret-bearing paths the user can open for reading, by path and kind, never their content, into the
/// run's own custody; the run's record carries only their count and kinds. Unknown is never reported as blocked.
/// </summary>
internal static class HarnessReach
{
    /// <summary>The private argument that makes the vericairn executable the reach probe.</summary>
    internal const string Marker = "--vericairn-worker-reach-probe";

    /// <summary>The kinds of secret-bearing path the inventory knows, in the order the record lists them.</summary>
    internal static IReadOnlyList<string> Kinds => Records.Types.ExecutionRecord.SecretKinds;

    /// <summary>The most paths one inventory lists; a longer inventory says it is incomplete.</summary>
    internal const int MaxPaths = (int)Records.Types.ExecutionRecord.MaxSecretPaths;

    private const int MaxDirectoryEntries = 256;
    private const int TokenBytes = 18;
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConnectDeadline = TimeSpan.FromSeconds(5);
    private const int ProbeOutputLimit = 4096;
    private static readonly TimeSpan InventoryDeadline = TimeSpan.FromSeconds(10);

    // Conventional locations under the user's home. A directory entry lists the files directly in it that the filter keeps.
    private static readonly (string Kind, string Path, Func<string, bool>? Directory)[] Candidates =
    [
        ("ssh-key", ".ssh", name => !name.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("known_hosts", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("authorized_keys", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("config", StringComparison.OrdinalIgnoreCase)),
        ("cloud-credentials", ".aws/credentials", null),
        ("cloud-credentials", ".aws/sso/cache", _ => true),
        ("cloud-credentials", ".azure/msal_token_cache.json", null),
        ("cloud-credentials", ".azure/msal_token_cache.bin", null),
        ("cloud-credentials", ".azure/accessTokens.json", null),
        ("cloud-credentials", ".config/gcloud/credentials.db", null),
        ("cloud-credentials", ".config/gcloud/access_tokens.db", null),
        ("cloud-credentials", ".config/gcloud/application_default_credentials.json", null),
        ("cloud-credentials", ".kube/config", null),
        ("registry-credentials", ".npmrc", null),
        ("registry-credentials", ".pypirc", null),
        ("registry-credentials", ".gem/credentials", null),
        ("registry-credentials", ".cargo/credentials", null),
        ("registry-credentials", ".cargo/credentials.toml", null),
        ("registry-credentials", ".docker/config.json", null),
        ("registry-credentials", ".nuget/NuGet/NuGet.Config", null),
        ("registry-credentials", ".config/NuGet/NuGet.Config", null),
        ("registry-credentials", "AppData/Roaming/NuGet/NuGet.Config", null),
        ("forge-credentials", ".git-credentials", null),
        ("forge-credentials", ".config/git/credentials", null),
        ("forge-credentials", ".config/gh/hosts.yml", null),
        ("forge-credentials", "AppData/Roaming/GitHub CLI/hosts.yml", null),
        ("forge-credentials", ".netrc", null),
        ("forge-credentials", "_netrc", null),
        ("signing-key", ".gnupg/private-keys-v1.d", _ => true),
        ("harness-login", ".claude/.credentials.json", null),
        ("harness-login", ".codex/auth.json", null),
        ("environment-file", ".env", null),
        ("keychain", "Library/Keychains", name => name.EndsWith(".keychain-db", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".keychain", StringComparison.OrdinalIgnoreCase)),
    ];

    /// <summary>Handles the probe's private invocation; null for an ordinary command line. Reads the marker and the test host, nothing else.</summary>
    internal static int? TryRun(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 3 || arguments[0] != Marker || !int.TryParse(arguments[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port)
            || port is <= 0 or > 65535)
        {
            return null;
        }

        Console.Out.Write(JsonSerializer.Serialize(new Dictionary<string, string?> { ["file"] = ReadFile(arguments[1]), ["host"] = ReadHost(port) }));
        return 0;
    }

    /// <summary>
    /// Plants the marker and the test host, starts the probe in the harness's place and judges what it reported: readable or reachable
    /// only when it returned the exact token, blocked when it was refused, unknown when the probe did not run or report.
    /// </summary>
    internal static ReachMeasurement Measure(string plantDirectory, string workspace, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> command,
        Action<string, TcpListener>? beforeProbe = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(plantDirectory);
        ArgumentException.ThrowIfNullOrEmpty(workspace);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0) return ReachMeasurement.Unknown;
        string fileToken = Token();
        string hostToken = Token();
        string marker = Path.Combine(plantDirectory, "reach-marker-" + Token()[..12].Replace('-', 'a').Replace('_', 'b'));
        TcpListener? listener = null;
        using var stop = new CancellationTokenSource();
        Task? serving = null;
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(marker, options))
            {
                stream.Write(Encoding.ASCII.GetBytes(fileToken));
            }

            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serving = Serve(listener, hostToken, stop.Token);
            beforeProbe?.Invoke(marker, listener);
            (string? File, string? Host)? reported = Probe(command, marker, port, workspace, environment);
            return reported is not { } seen ? ReachMeasurement.Unknown
                : new(Judge(seen.File, fileToken, "readable"), Judge(seen.Host, hostToken, "reachable"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SocketException or InvalidOperationException)
        {
            return ReachMeasurement.Unknown;
        }
        finally
        {
            stop.Cancel();
            listener?.Stop();
            try { serving?.Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { }
            try { File.Delete(marker); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The secret-bearing paths the user can open for reading: conventional locations under the harness's home, the harness logins its
    /// launcher imported for the run, and the device keys of the Vericairn home. Opening is the only test; no byte is read.
    /// </summary>
    internal static IReadOnlyList<SecretPath> SecretPaths(IReadOnlyDictionary<string, string> environment, out bool complete)
    {
        ArgumentNullException.ThrowIfNull(environment);
        complete = true;
        long started = Stopwatch.GetTimestamp();
        var found = new List<SecretPath>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? home = environment.GetValueOrDefault("HOME") ?? environment.GetValueOrDefault("USERPROFILE");
        if (home is null || !Path.IsPathFullyQualified(home))
        {
            complete = false;
        }
        else
        {
            foreach ((string kind, string relative, Func<string, bool>? filter) in Candidates)
            {
                complete &= Add(kind, Path.Combine([home, .. relative.Split('/')]), filter);
            }
        }

        foreach ((string variable, string file) in new[] { ("CLAUDE_CONFIG_DIR", ".credentials.json"), ("CODEX_HOME", "auth.json") })
        {
            if (environment.GetValueOrDefault(variable) is string directory && Path.IsPathFullyQualified(directory)) complete &= Add("harness-login", Path.Combine(directory, file), null);
        }

        string vericairn = environment.GetValueOrDefault(VericairnHome.EnvironmentVariable) is string named && Path.IsPathFullyQualified(named) ? named : VericairnHome.Path();
        complete &= Add("device-key", Path.Combine(vericairn, "keys"), _ => true);
        if (found.Count > MaxPaths)
        {
            complete = false;
            found.RemoveRange(MaxPaths, found.Count - MaxPaths);
        }

        return found;

        bool Add(string kind, string path, Func<string, bool>? filter)
        {
            // A slow file system cannot hold run admission: past the deadline the inventory stops and says it is incomplete.
            if (Stopwatch.GetElapsedTime(started) > InventoryDeadline) return false;
            try
            {
                if (filter is null)
                {
                    if (File.Exists(path) && Readable(path) && seen.Add(path)) found.Add(new(kind, path));
                    return true;
                }

                if (!Directory.Exists(path)) return true;
                int entries = 0;
                foreach (string entry in Directory.EnumerateFiles(path))
                {
                    if (++entries > MaxDirectoryEntries || Stopwatch.GetElapsedTime(started) > InventoryDeadline) return false;
                    if (filter(Path.GetFileName(entry)) && Readable(entry) && seen.Add(entry)) found.Add(new(kind, entry));
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A directory the user cannot list is not reachable through this inventory; one that failed otherwise leaves it incomplete.
                return exception is UnauthorizedAccessException;
            }
        }
    }

    /// <summary>What the run's record carries: the measurement, and the count and kinds of the secret-bearing paths, never a path.</summary>
    internal static JsonObject Record(ReachMeasurement measured, IReadOnlyList<SecretPath> paths, bool complete) => new()
    {
        ["outsideFile"] = measured.OutsideFile,
        ["testHost"] = measured.TestHost,
        ["secretPaths"] = paths.Count,
        ["secretKinds"] = new JsonArray(Kinds.Where(k => paths.Any(p => p.Kind == k)).Select(k => (JsonNode)k).ToArray()),
        ["secretPathsComplete"] = complete,
    };

    /// <summary>
    /// Measures a run's reach before it starts and keeps the paths in the run's custody, the run's own owner-only directory, which is
    /// outside the workspace and its temporary directory, so the marker is planted there. Returns what the run's record carries.
    /// </summary>
    internal static JsonObject Observe(RunDirectory directory, string workspace, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> command, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(clock);
        ReachMeasurement measured = Measure(directory.Path, workspace, environment, command);
        IReadOnlyList<SecretPath> paths = SecretPaths(environment, out bool complete);
        directory.RetainReach(new JsonObject
        {
            ["measuredAt"] = RecordParser.FormatTimestamp(clock.GetUtcNow()),
            ["outsideFile"] = measured.OutsideFile,
            ["testHost"] = measured.TestHost,
            ["secretPaths"] = new JsonArray(paths.Select(p => (JsonNode)new JsonObject { ["kind"] = p.Kind, ["path"] = p.Path }).ToArray()),
            ["complete"] = complete,
        });
        return Record(measured, paths, complete);
    }

    private static bool Readable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Opening for reading tests access and reads nothing. The open never blocks: a named pipe or device at a conventional path
            // would otherwise hold run admission until something wrote to it.
            int descriptor = open(path, ReadOnly | (OperatingSystem.IsMacOS() ? 0x0004 | 0x20000 : 0x0800 | 0x0100));
            if (descriptor < 0) return false;
            _ = close(descriptor);
            return true;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private const int ReadOnly = 0;

#pragma warning disable SYSLIB1054 // Runtime marshalling avoids requiring unsafe code in the project, as ContainedProcess does.
    // O_NONBLOCK and O_NOCTTY: 0x0004 and 0x20000 on macOS, 0x0800 and 0x0100 on Linux.
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int open(string path, int flags);

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int close(int descriptor);
#pragma warning restore SYSLIB1054

    private static string Judge(string? reported, string token, string reached) => reported is null ? "blocked" : reported == token ? reached : "unknown";

    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).Replace('+', '-').Replace('/', '_');

    private static async Task Serve(TcpListener listener, string token, CancellationToken stop)
    {
        byte[] answer = Encoding.ASCII.GetBytes(token);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
                await client.GetStream().WriteAsync(answer, stop).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                if (stop.IsCancellationRequested || exception is ObjectDisposedException or InvalidOperationException) return;
            }
        }
    }

    // The probe is the vericairn executable with its private argument and nothing else, in the bounded process primitive's containment:
    // the deadline covers its launch, output and exit, and its whole process group is ended with it.
    private static (string? File, string? Host)? Probe(IReadOnlyList<string> command, string marker, int port, string workspace, IReadOnlyDictionary<string, string> environment)
    {
        var info = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = workspace,
        };
        foreach (string argument in command.Skip(1).Concat([Marker, marker, port.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment.Clear();
        foreach ((string name, string value) in environment)
        {
            info.Environment[name] = value;
        }

        ProcessResult result = BoundedProcess.Run(info, ProbeDeadline, ProbeOutputLimit);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput.Trim());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2) return null;
            return (Text(root, "file"), Text(root, "host"));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name) => root.GetProperty(name) switch
        {
            { ValueKind: JsonValueKind.String } value => value.GetString(),
            { ValueKind: JsonValueKind.Null } => null,
            _ => throw new KeyNotFoundException(name),
        };
    }

    private static string? ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] buffer = new byte[64];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadHost(int port)
    {
        try
        {
            using var client = new TcpClient();
            if (!client.ConnectAsync(IPAddress.Loopback, port).Wait(ConnectDeadline)) return null;
            using NetworkStream stream = client.GetStream();
            stream.ReadTimeout = (int)ConnectDeadline.TotalMilliseconds;
            byte[] buffer = new byte[64];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Encoding.ASCII.GetString(buffer, 0, read);
        }
        catch (Exception exception) when (exception is IOException or SocketException or AggregateException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }
}
