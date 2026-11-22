// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// A retrieval run's sources (RUN-04): each invocation of one of the run's admitted web tools, and the result the harness gave the agent
/// for it, retained as an untrusted original in the run's custody, the run's owner-only directory on the machine that runs it. The text
/// is kept exactly as the harness reported it, named by its content hash; it never enters a record, a repository or the agent's
/// instructions, and the worker fetches nothing itself. Settlement records each source by its address or query, its time, its content
/// hash and size. The log is not tamper-evident: a harness that does not sandbox the agent runs as the same user and could change it.
/// </summary>
internal static class RunSources
{
    private const long MaximumLogBytes = 16L * 1024 * 1024;
    private const long MaximumSourceBytes = 8L * 1024 * 1024;
    private const long MaximumRetainedBytes = 64L * 1024 * 1024;
    private const int MaximumQuery = 500;

    /// <summary>The run's admitted web tools: the inventory's tools whose route is public-web.</summary>
    internal static HashSet<string> Tools(JsonElement? execution)
    {
        var tools = new HashSet<string>(StringComparer.Ordinal);
        if (execution is not { ValueKind: JsonValueKind.Object } selection || !selection.TryGetProperty("retrieval", out _)
            || !selection.TryGetProperty("environment", out JsonElement environment) || !environment.TryGetProperty("inventory", out JsonElement inventory)
            || inventory.ValueKind != JsonValueKind.Array)
        {
            return tools;
        }

        foreach (JsonElement component in inventory.EnumerateArray())
        {
            if (component.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "tool"
                && component.TryGetProperty("destinations", out JsonElement destinations) && destinations.ValueKind == JsonValueKind.Array
                && destinations.EnumerateArray().Any(d => d.GetString() == RetrievalAdmission.Destination))
            {
                tools.Add(component.GetProperty("id").GetString()!);
            }
        }

        return tools;
    }

    /// <summary>The address a web tool invocation names, when it names one.</summary>
    internal static string? Address(HarnessAction action) => Text(action.Input, "url") ?? Text(action.Input, "uri");

    /// <summary>Starts a run's empty source log, so settlement can tell a run that retrieved nothing from a log that went missing.</summary>
    internal static void Start(RunDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using var created = new FileStream(directory.SourcesFile, options);
        }
        catch (IOException) when (File.Exists(directory.SourcesFile))
        {
            // Already started.
        }
    }

    /// <summary>Logs an invocation of an admitted web tool, with the address or query it names; false when it could not be logged.</summary>
    internal static bool Invoked(RunDirectory directory, HarnessAction action, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(action);
        var line = new JsonObject { ["event"] = "invoked", ["id"] = action.Id, ["tool"] = action.Tool, ["at"] = RecordParser.FormatTimestamp(at) };
        if (Address(action) is string address) line["address"] = address;
        else if (Text(action.Input, "query") is string query) line["query"] = query;
        return Append(directory, line);
    }

    /// <summary>
    /// Retains a web tool's result as the harness gave it to the agent, named by its content hash, and logs it; a result beyond the
    /// per-source or per-run size is logged by hash and size but not retained. False when it could not be retained or logged.
    /// </summary>
    internal static bool Retrieved(RunDirectory directory, HarnessToolResult result, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(result);
        byte[] content = Encoding.UTF8.GetBytes(result.Content);
        string hash = ContentHash.Sha256(content);
        bool retained = false;
        bool kept = true;
        if (content.LongLength <= MaximumSourceBytes && RetainedBytes(directory) + content.LongLength <= MaximumRetainedBytes)
        {
            try
            {
                Directory.CreateDirectory(directory.SourcesDirectory);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory.SourcesDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                string path = Path.Combine(directory.SourcesDirectory, hash["sha256:".Length..] + ".txt");
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                try
                {
                    using var stream = new FileStream(path, options);
                    stream.Write(content);
                }
                catch (IOException) when (File.Exists(path) && new FileInfo(path).LinkTarget is null && ContentHash.Sha256File(path) == hash)
                {
                    // The same original was retained already.
                }

                retained = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                kept = false;
            }
        }

        var line = new JsonObject
        {
            ["event"] = "retrieved", ["id"] = result.Id, ["at"] = RecordParser.FormatTimestamp(at), ["hash"] = hash, ["bytes"] = content.LongLength,
            ["retained"] = retained, ["error"] = result.Error,
        };
        return Append(directory, line) && kept;
    }

    /// <summary>Marks the run's sources incomplete, for a source the worker could not retain or log.</summary>
    internal static void MarkIncomplete(RunDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        try { File.WriteAllText(directory.SourcesFile + ".incomplete", "incomplete"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// What settlement records for a retrieval run: each source, in the order it was invoked, by its tool, its address (an absolute http
    /// or https address with no user information that passes the estate's content check) or its query (likewise), or withheld, its
    /// time, whether its result was retained and its content hash and size; at most 100 in full, the count and digest of all, and
    /// whether the log was complete. Null when this machine does not hold the run's directory.
    /// </summary>
    internal static JsonObject? Read(RunDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!directory.Exists) return null;
        var info = new FileInfo(directory.SourcesFile);
        if (!info.Exists) return Incomplete();
        bool complete = info.LinkTarget is null && info.Length <= MaximumLogBytes && !File.Exists(directory.SourcesFile + ".incomplete");
        var order = new List<string>();
        var invoked = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var retrieved = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (info.LinkTarget is null && info.Length <= MaximumLogBytes)
        {
            foreach (string text in File.ReadLines(directory.SourcesFile))
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                try
                {
                    JsonObject line = JsonNode.Parse(text)!.AsObject();
                    string id = line["id"]!.GetValue<string>();
                    switch (line["event"]?.GetValue<string>())
                    {
                        case "invoked" when invoked.TryAdd(id, line):
                            order.Add(id);
                            break;
                        case "retrieved" when invoked.ContainsKey(id) && retrieved.TryAdd(id, line):
                            break;
                        default:
                            complete = false;
                            break;
                    }
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or NullReferenceException or FormatException)
                {
                    complete = false;
                }
            }
        }

        var entries = new List<JsonObject>();
        foreach (string id in order)
        {
            JsonObject call = invoked[id];
            JsonObject? result = retrieved.GetValueOrDefault(id);
            var entry = new JsonObject { ["tool"] = Identity(call["tool"]?.GetValue<string>()) };
            if (call["address"]?.GetValue<string>() is string address && RecordableAddress(address)) entry["address"] = address;
            else if (call["query"]?.GetValue<string>() is string query && RecordableQuery(query)) entry["query"] = query;
            else entry["withheld"] = true;
            entry["at"] = (result ?? call)["at"]!.GetValue<string>();
            entry["retained"] = result?["retained"]?.GetValue<bool>() == true;
            if (result is not null)
            {
                entry["hash"] = result["hash"]!.GetValue<string>();
                entry["bytes"] = result["bytes"]!.GetValue<long>();
            }

            entries.Add(entry);
        }

        return new JsonObject
        {
            ["sources"] = new JsonArray([.. entries.Take(ExecutionRecord.MaxRecordedSources).Select(e => (JsonNode)e.DeepClone())]),
            ["count"] = entries.Count,
            ["complete"] = complete,
            ["digest"] = Digest(entries),
        };
    }

    /// <summary>What settlement records when the log cannot be read at all: no source, and a list that says it is incomplete.</summary>
    internal static JsonObject Incomplete() => new()
    {
        ["sources"] = new JsonArray(),
        ["count"] = 0,
        ["complete"] = false,
        ["digest"] = Digest([]),
    };

    internal static string Digest(IReadOnlyList<JsonObject> entries) =>
        ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new JsonArray([.. entries.Select(e => (JsonNode)e.DeepClone())]))));

    private static bool RecordableAddress(string address) => address.Length <= 2000 && Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && Clean(address);

    private static bool RecordableQuery(string query) => query.Length is > 0 and <= MaximumQuery && !query.Any(char.IsControl) && Clean(query);

    // The estate's content check refuses secrets and machine paths in records; a source that would fail it is withheld from the record.
    private static bool Clean(string text) => !EstateChecks.Content("source.txt", Encoding.UTF8.GetBytes(text)).HasErrors;

    private static string Identity(string? tool) => tool is not null && ExecutionPolicy.IsIdentity(tool) ? tool : "unknown";

    private static long RetainedBytes(RunDirectory directory)
    {
        try
        {
            return Directory.Exists(directory.SourcesDirectory) ? new DirectoryInfo(directory.SourcesDirectory).EnumerateFiles().Sum(f => f.Length) : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return long.MaxValue / 2;
        }
    }

    private static bool Append(RunDirectory directory, JsonObject line)
    {
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            if (new FileInfo(directory.SourcesFile) is { Exists: true } existing && (existing.LinkTarget is not null || existing.Length > MaximumLogBytes)) return false;
            byte[] bytes = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(JsonSerializer.SerializeToElement(line)) + "\n");
            using var stream = new FileStream(directory.SourcesFile, options);
            stream.Write(bytes);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CanonicalJsonException)
        {
            return false;
        }
    }

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found)
        && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
}
