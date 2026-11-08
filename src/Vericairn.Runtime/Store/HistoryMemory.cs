// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Store;

/// <summary>
/// What this machine remembers of an estate's history outside the records (design/signed-history.md §§ 7 and 9): the newest history
/// checkpoint it verified, with the main-line commit holding it, and the latest <c>estate check --expect</c> result. Kept beside the
/// clone's live store under the user's Vericairn home, never in the estate, and holding no machine path: a fresh machine has none,
/// which is the residual risk the design names.
/// </summary>
public sealed class HistoryMemory
{
    private const long MaxBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private readonly string path;

    /// <summary>The memory of one clone of an estate on this machine, under a given Vericairn home or the user's.</summary>
    public HistoryMemory(string root, string estate, string profile, string? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(estate);
        path = Path.Combine(Path.GetDirectoryName(EstateLocations.LiveStorePath(root, estate, profile, home ?? VericairnHome.Path()))!, "history.json");
        string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(path).StartsWith(full, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The machine's memory of an estate's history is kept outside the estate; choose a Vericairn home outside it.");
    }

    /// <summary>A checkpoint this machine verified, and the main-line commit that held it then.</summary>
    /// <param name="Id">The checkpoint's id.</param>
    /// <param name="Hash">Its content hash.</param>
    /// <param name="Path">Its repository-relative path.</param>
    /// <param name="Branch">The main line's branch.</param>
    /// <param name="Commit">The main line's tip that held it.</param>
    /// <param name="VerifiedAt">When this machine verified it.</param>
    public sealed record Verified(string Id, string Hash, string Path, string Branch, string Commit, string VerifiedAt);

    /// <summary>The latest <c>estate check --expect</c> on this machine.</summary>
    /// <param name="Expected">What was expected: an id or a hash.</param>
    /// <param name="Outcome">held or missing.</param>
    /// <param name="CheckedAt">When.</param>
    /// <param name="Checkpoint">The checkpoint the name matched, when held.</param>
    public sealed record Expectation(string Expected, string Outcome, string CheckedAt, string? Checkpoint);

    /// <summary>Everything remembered of one clone.</summary>
    /// <param name="Estate">The estate's id.</param>
    /// <param name="Checkpoint">The newest checkpoint verified, or null.</param>
    /// <param name="Expect">The latest expectation checked, or null.</param>
    public sealed record Remembered(string Estate, Verified? Checkpoint, Expectation? Expect);

    /// <summary>Whether an exception means the memory cannot be read or kept here, which a check reports and never fails on.</summary>
    public static bool Unavailable(Exception exception) => exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;

    /// <summary>What this machine remembers, or null when nothing is.</summary>
    public Remembered? Read()
    {
        if (!File.Exists(path))
        {
            if (Directory.Exists(path)) throw new IOException("The machine's memory of history must be an ordinary file.");
            return null;
        }

        if (new FileInfo(path).LinkTarget is not null) throw new IOException("The machine's memory of history cannot be a link.");
        if (new FileInfo(path).Length > MaxBytes) throw new JsonException("The machine's memory of history exceeds its size limit.");
        return JsonSerializer.Deserialize<Remembered>(File.ReadAllBytes(path), Json) ?? throw new JsonException("The machine's memory of history is empty.");
    }

    /// <summary>Keeps what this machine remembers, replacing it whole.</summary>
    public void Write(Remembered value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("The machine's memory of history cannot be a link.");
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                JsonSerializer.Serialize(stream, value, Json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>
    /// Whether the main line's tip holds a record at its path with the hash given: what tells a checkpoint the main line still contains
    /// from one a rewrite removed, whatever branch is checked out.
    /// </summary>
    public static bool Holds(ISourceAdapter git, string root, EstateMainLine main, string recordPath, string hash)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(main);
        byte[]? bytes = git.FileAtCommit(root, main.Commit, recordPath);
        return bytes is not null && RecordParser.Parse(bytes, recordPath, new FindingList(), checkFormatting: false)?.ComputedHash == hash;
    }

    /// <summary>
    /// Remembers an <c>estate check --expect</c> (design/signed-history.md § 9): its outcome, and, when the copy holds the expected
    /// checkpoint and the main line holds it too, that checkpoint as the newest this machine verified, replacing what it remembered, since
    /// a head known elsewhere is the operator's own reference. Returns a note when the memory could not be kept, else null.
    /// </summary>
    public static string? RememberExpected(string root, string estate, string profile, string expected, (string Id, string Hash, string Path)? held, ISourceAdapter? git,
        DateTimeOffset now, string? home = null)
    {
        try
        {
            var memory = new HistoryMemory(root, estate, profile, home);
            Remembered current = memory.Read() ?? new Remembered(estate, null, null);
            string at = RecordParser.FormatTimestamp(now);
            Verified? verified = current.Checkpoint;
            if (held is { } match && profile == "repository-synced" && git?.MainLine(root) is { } main && Holds(git, root, main, match.Path, match.Hash))
                verified = new Verified(match.Id, match.Hash, match.Path, main.Branch, main.Commit, at);
            memory.Write(current with { Estate = estate, Checkpoint = verified, Expect = new Expectation(expected.Trim(), held is null ? "missing" : "held", at, held?.Id) });
            return null;
        }
        catch (Exception exception) when (Unavailable(exception))
        {
            return "This machine could not remember the expectation: " + exception.Message;
        }
    }
}
