// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>Reads only turn-context facts from the fresh run home, never assistant prose or ambient sessions.</summary>
internal static class CodexRollout
{
    internal static IReadOnlyList<HarnessExecution> Read(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        try
        {
            string home = Path.Combine(runDirectory, "codex");
            string sessions = Path.Combine(home, "sessions");
            if (!Directory.Exists(sessions) || Linked(home) || Linked(sessions)) return [];
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
            string[] entries = Directory.EnumerateFileSystemEntries(sessions, "*", options).Take(4097).ToArray();
            if (entries.Length > 4096) return [];
            var reports = new List<HarnessExecution>();
            long remaining = 64L * 1024 * 1024;
            foreach (string path in entries.Where(p => Path.GetFileName(p).StartsWith("rollout-", StringComparison.Ordinal) && p.EndsWith(".jsonl", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                // A malformed or unreadable file invalidates this scan; a partial scan must not look like a match.
                if (Linked(path) || Directory.Exists(path)) return [];
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length > remaining) return [];
                remaining -= file.Length;
                using var reader = new StreamReader(file, new UTF8Encoding(false, true));
                while (reader.ReadLine() is string line)
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "turn_context") continue;
                    if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object) return [];
                    if (payload.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() > 1)) return [];
                    var report = new HarnessExecution(Model: Text(payload, "model"), Effort: Text(payload, "effort"));
                    if (report.Model is null && report.Effort is null) continue;
                    if (!reports.Contains(report)) reports.Add(report);
                    if (reports.Count > 128) return [];
                }
            }
            return reports;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            return [];
        }
    }

    private static bool Linked(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
