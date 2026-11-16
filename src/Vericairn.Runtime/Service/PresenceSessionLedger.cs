// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>
/// One line of the presence session ledger: what happened to one session, and when.
/// </summary>
/// <param name="At">When the runtime kept it, as a record timestamp.</param>
/// <param name="Session">The session's id, as its terms state it.</param>
/// <param name="Event">announced, prompting, opened, signed, refused or ended.</param>
internal sealed record PresenceSessionEvent(string At, string Session, string Event)
{
    /// <summary>For announced, the action's name from the vocabulary.</summary>
    public string? Action { get; init; }

    /// <summary>For announced, the action as the runtime names it, such as "release submit 0.9.4".</summary>
    public string? Named { get; init; }

    /// <summary>For announced, the principal every record is by.</summary>
    public string? Principal { get; init; }

    /// <summary>For announced, the device whose key signs.</summary>
    public string? Device { get; init; }

    /// <summary>For announced, the terms' compact canonical text, which hold types, hashes and counts and never a record's bytes.</summary>
    public string? Terms { get; init; }

    /// <summary>For announced, why the list is announced: a renewal's reason, or none for the action's first list.</summary>
    public string? Why { get; init; }

    /// <summary>For prompting, the helper's nonce.</summary>
    public string? Nonce { get; init; }

    /// <summary>For prompting, the fingerprint the helper wrote into its dialog.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>For opened, the helper's wall clock at opening, as a record timestamp.</summary>
    public string? OpenedAt { get; init; }

    /// <summary>For announced and opened, the session's limit in seconds.</summary>
    public int? LimitSeconds { get; init; }

    /// <summary>For signed, the record's id; for refused, the refused record's, when the refusal was of a record.</summary>
    public string? Record { get; init; }

    /// <summary>For signed and refused, the record's type.</summary>
    public string? Type { get; init; }

    /// <summary>For signed and refused, the record's content hash.</summary>
    public string? Hash { get; init; }

    /// <summary>For signed, the entry of the list it was signed under, from 0.</summary>
    public int? Entry { get; init; }

    /// <summary>For refused, the helper's refusal code (design/presence-sessions.md § 6).</summary>
    public string? Code { get; init; }

    /// <summary>For refused, the helper's detail, never record content; for ended, why in words.</summary>
    public string? Detail { get; init; }

    /// <summary>For ended, closed, limit, lock, sleep, refusal, parent, input, declined or silent.</summary>
    public string? Reason { get; init; }
}

/// <summary>A signature the ledger kept for a session: the record's id, type and content hash, and its entry.</summary>
internal sealed record PresenceSessionSigned(string At, string Record, string? Type, string? Hash, int? Entry);

/// <summary>A refusal the ledger kept for a session: its code and detail, and the record refused when it was one.</summary>
internal sealed record PresenceSessionRefused(string At, string? Code, string? Detail, string? Type, string? Hash);

/// <summary>
/// One session as <c>vericairn devices sessions</c> lists it from the ledger: its terms, nonce and fingerprint, when and why it opened and
/// ended, each signed record and each refusal.
/// </summary>
internal sealed record PresenceSessionEntry(string Session, string AnnouncedAt, string? Action, string? Named, string? Principal, string? Device, string? Why,
    string? Terms, int? LimitSeconds, string? Nonce, string? Fingerprint, string? OpenedAt, IReadOnlyList<PresenceSessionSigned> Signed,
    IReadOnlyList<PresenceSessionRefused> Refused, string? EndedAt, string? Ended, string? EndedWhy);

/// <summary>
/// From 0.9.4 (CODE-80): the presence session ledger (design/presence-sessions.md § 7): each session this runtime opened or tried to open,
/// appended one JSON object per line to <c>presence-sessions.jsonl</c> beside the estate's live store, in this user's machine state and
/// readable by its owner only: its terms, nonce and fingerprint, when and why it opened and ended, each signed record's id and hash, and
/// each refusal. It is a diagnostic, never a record, and never in an export or a support bundle; a process of the same operating-system
/// user could alter it. A line that cannot be kept is dropped: the ledger never stops an action.
/// </summary>
internal sealed class PresenceSessionLedger
{
    /// <summary>The ledger's file name, beside the live store.</summary>
    internal const string FileName = "presence-sessions.jsonl";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly string path;
    private readonly object gate = new();

    /// <summary>The ledger in a directory, the estate's live store's.</summary>
    internal PresenceSessionLedger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Folder = directory;
        path = Path.Combine(directory, FileName);
    }

    /// <summary>The directory the ledger is kept in.</summary>
    internal string Folder { get; }

    /// <summary>The ledger of an estate's clone on this machine, beside its live store under a Vericairn home.</summary>
    internal static string DirectoryOf(string root, string estate, string profile, string home) =>
        Path.GetDirectoryName(EstateLocations.LiveStorePath(root, estate, profile, home))!;

    /// <summary>The ledger's directory for the estate at a root, under this user's Vericairn home, as a client finds it.</summary>
    internal static string DirectoryOf(string root) => Path.GetDirectoryName(EstateLocations.EndpointFilePath(root))!;

    /// <summary>Appends one line, flushed before it returns; the file is created readable and writable by its owner only.</summary>
    internal void Append(PresenceSessionEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Json) + "\n");
        lock (gate)
        {
            try
            {
                Records.Signing.OwnerOnlyState.CreateDirectory(Folder);
                var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.Read, Options = FileOptions.WriteThrough };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var stream = new FileStream(path, options);
                // A torn tail stays its own ignored line, never a prefix of this one.
                if (stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
                }

                stream.Seek(0, SeekOrigin.End);
                stream.Write(line);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The ledger is a diagnostic: a line it cannot keep is dropped, and the action goes on.
            }
        }
    }

    /// <summary>The time now as the ledger writes it.</summary>
    internal static string Now(TimeProvider clock) => RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()));

    /// <summary>Every line of the ledger in a directory, oldest first; none when there is no ledger. A line cut short is skipped.</summary>
    internal static IReadOnlyList<PresenceSessionEvent> Events(string directory)
    {
        string file = Path.Combine(directory, FileName);
        if (!File.Exists(file)) return [];
        var events = new List<PresenceSessionEvent>();
        foreach (string line in File.ReadLines(file).Where(l => l.Length > 0))
        {
            try
            {
                if (JsonSerializer.Deserialize<PresenceSessionEvent>(line, Json) is { Session: not null, Event: not null } entry) events.Add(entry);
            }
            catch (JsonException)
            {
                // A line cut short by a crash is no event.
            }
        }

        return events;
    }

    /// <summary>The sessions the ledger in a directory holds, oldest first, each with everything kept of it.</summary>
    internal static IReadOnlyList<PresenceSessionEntry> Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var sessions = new List<PresenceSessionEntry>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PresenceSessionEvent e in Events(directory))
        {
            if (!index.TryGetValue(e.Session, out int at))
            {
                index[e.Session] = at = sessions.Count;
                sessions.Add(new PresenceSessionEntry(e.Session, e.At, null, null, null, null, null, null, null, null, null, null, [], [], null, null, null));
            }

            PresenceSessionEntry s = sessions[at];
            sessions[at] = e.Event switch
            {
                "announced" => s with { AnnouncedAt = e.At, Action = e.Action, Named = e.Named, Principal = e.Principal, Device = e.Device, Why = e.Why, Terms = e.Terms, LimitSeconds = e.LimitSeconds },
                "prompting" => s with { Nonce = e.Nonce, Fingerprint = e.Fingerprint },
                "opened" => s with { OpenedAt = e.OpenedAt, LimitSeconds = e.LimitSeconds ?? s.LimitSeconds },
                "signed" when e.Record is not null => s with { Signed = [.. s.Signed, new PresenceSessionSigned(e.At, e.Record, e.Type, e.Hash, e.Entry)] },
                "refused" => s with { Refused = [.. s.Refused, new PresenceSessionRefused(e.At, e.Code, e.Detail, e.Type, e.Hash)] },
                "ended" => s with { EndedAt = e.At, Ended = e.Reason, EndedWhy = e.Detail },
                _ => s,
            };
        }

        return sessions;
    }
}
