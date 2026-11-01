// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

/// <summary>
/// What support needs, gathered without record bodies: codes, counts, versions and settings, never titles, text or names. Every string
/// in it then passes through the redactor, which removes what the estate check calls a credential, a secret-named value or an absolute
/// machine path, and the known local directories by name.
/// </summary>
internal static partial class SupportBundle
{
    internal const string Format = "vericairn.support/1";
    private const int ErrorLines = 30;

    internal static JsonObject Gather(CommandContext context, string root)
    {
        var bundle = new JsonObject
        {
            ["bundle"] = Format,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["vericairn"] = VersionText(),
            ["platform"] = new JsonObject
            {
                ["os"] = RuntimeInformation.OSDescription,
                ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["rid"] = RuntimeInformation.RuntimeIdentifier,
                ["platform"] = VericairnHome.Platform(),
            },
            ["withheld"] = new JsonArray("record bodies, titles and text", "the estate's name and the principals' names", "network addresses", "credentials and secret-named values"),
        };

        if (IsEstate(root))
        {
            bundle["estate"] = Estate(context, root);
            bundle["recentErrors"] = RecentErrors(root);
            bundle["configuration"] = Configuration(root);
        }
        else
        {
            bundle["estate"] = "not an estate root; only the version and platform are gathered";
        }

        return bundle;
    }

    /// <summary>The bundle as shown and as written: indented JSON after redaction, with what was redacted counted.</summary>
    internal static string Render(JsonObject bundle)
    {
        var redactor = new Redactor(KnownDirectories());
        JsonNode redacted = redactor.Redact(bundle)!;
        redacted["redacted"] = new JsonObject { ["secrets"] = redactor.Secrets, ["paths"] = redactor.Paths };
        return redacted.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    private static JsonObject Estate(CommandContext context, string root)
    {
        var estate = new JsonObject();
        int code = context.WithApi(root, api =>
        {
            estate["runtime"] = context.UsingDaemon ? "daemon" : "embedded";
            JsonElement status = api.Status().Json;
            estate["pin"] = status.TryGetProperty("vericairn", out JsonElement pin) ? JsonNode.Parse(pin.GetRawText()) : null;
            estate["records"] = status.TryGetProperty("records", out JsonElement records) ? records.GetInt32() : null;
            estate["workOrders"] = Counts(status, "workOrders", "status");
            estate["assignments"] = Counts(status, "assignments", "state");
            estate["releases"] = Counts(status, "releases", "status");
            estate["operations"] = Counts(status, "operations", "status");
            estate["runs"] = CountsOf(api.Runs().Runs.Select(r => r.Status));
            FindingsResponse check = api.Check();
            estate["check"] = new JsonObject
            {
                ["errors"] = check.Errors,
                ["warnings"] = check.Warnings,
                // Codes and locations only: a finding's message may quote what a record says.
                ["findings"] = new JsonArray([.. check.Findings.Where(f => f.Severity != "info").Take(200)
                    .Select(f => (JsonNode)new JsonObject { ["severity"] = f.Severity, ["code"] = f.Code, ["location"] = f.Location })]),
            };
            return Clean;
        }, readOnly: true);
        if (code != Clean)
        {
            estate["problem"] = "the estate could not be read; its check and status are missing";
        }

        return estate;
    }

    private static JsonArray RecentErrors(string root)
    {
        string log = DaemonLifecycle.DaemonLogPath(root);
        if (!File.Exists(log))
        {
            return [];
        }

        string[] lines;
        try { lines = File.ReadLines(log).TakeLast(2000).ToArray(); }
        catch (IOException) { return ["the runtime's log could not be read"]; }
        catch (UnauthorizedAccessException) { return ["the runtime's log could not be read"]; }
        return [.. lines.Where(l => ErrorLine().IsMatch(l)).TakeLast(ErrorLines).Select(l => (JsonNode)(l.Length > 300 ? l[..300] + "…" : l))];
    }

    private static JsonObject Configuration(string root)
    {
        RuntimeSettings settings = RuntimeSettings.Load(Path.GetDirectoryName(DaemonHost.EndpointFilePath(root))!);
        var store = new WorkerConfigurationStore();
        var launchers = new JsonObject();
        foreach (IAgentLauncher launcher in ProviderRegistry.Current.Launchers)
        {
            // Worker configuration exists only for the native launchers.
            if (launcher.Launcher is not ("codex" or "claude-code")) continue;
            WorkerConfiguration? configured = null;
            try { configured = store.Read(launcher); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ProviderRefusedException) { }
            launchers[launcher.Launcher] = configured is null ? "not configured" : new JsonObject { ["model"] = configured.Model, ["effort"] = configured.Effort };
        }

        return new JsonObject
        {
            ["localNetwork"] = settings.LocalNetwork is null ? "off" : settings.AllowPublic ? "on, public address allowed" : "on",
            ["workspaceRoot"] = store.ReadWorkspaceRoot() is null ? "default" : "configured",
            ["launchers"] = launchers,
        };
    }

    private static JsonObject Counts(JsonElement status, string name, string field) =>
        CountsOf(status.TryGetProperty(name, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().Select(r => r.TryGetProperty(field, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "?" : "?")
            : []);

    private static JsonObject CountsOf(IEnumerable<string> values) =>
        new(values.GroupBy(v => v, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count())));

    // The directories a bundle names by role rather than by path, longest first so a nested one wins.
    private static IReadOnlyList<(string Path, string Name)> KnownDirectories()
    {
        var known = new List<(string, string)>();
        void Add(string? path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 2) return;
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            known.Add((full, name));
        }

        Add(VericairnHome.Path(), "<vericairn-home>");
        Add(Environment.CurrentDirectory, "<working-directory>");
        Add(Path.GetTempPath(), "<temp>");
        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<home>");
        return [.. known.OrderByDescending(k => k.Item1.Length)];
    }

    [GeneratedRegex(@"error|fail|exception|refused|denied", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();

    /// <summary>Removes credentials, secret-named values and machine paths from every string of a JSON tree, counting what it removed.</summary>
    internal sealed partial class Redactor(IReadOnlyList<(string Path, string Name)> known)
    {
        public int Secrets { get; private set; }

        public int Paths { get; private set; }

        public JsonNode? Redact(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var copy = new JsonObject();
                    foreach ((string name, JsonNode? value) in obj)
                    {
                        if (SecretShapes.SecretKeyName().IsMatch(name) && value is not null)
                        {
                            Secrets++;
                            copy[name] = "<secret>";
                        }
                        else
                        {
                            copy[name] = Redact(value);
                        }
                    }

                    return copy;
                case JsonArray array:
                    return new JsonArray([.. array.Select(Redact)]);
                case JsonValue value when value.TryGetValue(out string? text):
                    return JsonValue.Create(Text(text));
                default:
                    return node?.DeepClone();
            }
        }

        public string Text(string text)
        {
            string result = text;
            foreach ((string path, string name) in known)
            {
                if (result.Contains(path, StringComparison.OrdinalIgnoreCase))
                {
                    Paths++;
                    result = result.Replace(path, name, StringComparison.OrdinalIgnoreCase);
                }
            }

            result = SecretShapes.Secret().Replace(result, _ => { Secrets++; return "<secret>"; });
            result = PathTail().Replace(result, match => SecretShapes.AbsolutePath().IsMatch(match.Value) || TempPath().IsMatch(match.Value) ? Count(match) : match.Value);
            return result;
        }

        private string Count(Match match)
        {
            Paths++;
            return "<path>";
        }

        // A whole path token: from its start to the next space or quote.
        [GeneratedRegex(@"(?:[A-Za-z]:\\|/)[^\s""'`<>|]*")]
        private static partial Regex PathTail();

        [GeneratedRegex(@"^/(?:tmp|var/folders|private/tmp)/")]
        private static partial Regex TempPath();
    }
}
