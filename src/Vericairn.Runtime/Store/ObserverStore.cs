// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Store;

/// <summary>
/// From 0.9.4 (OPS-20): the automated observers declared on this machine for one clone of an estate, each with its latest report. Kept
/// beside the clone's live store under the user's Vericairn home, never in the estate and never in the live store itself, so that
/// rebuilding the derived cache never forgets a declaration and lets its silence pass unseen. It holds no machine path.
/// </summary>
internal sealed class ObserverStore
{
    /// <summary>The most observers one clone declares.</summary>
    internal const int MaxObservers = 64;

    private const long MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>One declared observer and its latest report.</summary>
    internal sealed record Entry(string Name, string Kind, int IntervalSeconds, int GraceSeconds, string? Summary, string DeclaredAt, string DeclaredBy, Report? LastReport);

    /// <summary>A report as this machine keeps it.</summary>
    internal sealed record Report(string At, string Outcome, string? Reason, string By, string? Detail, string ReceivedAt);

    /// <summary>Everything declared for one clone.</summary>
    internal sealed record Declared(string Estate, IReadOnlyList<Entry> Observers);

    private readonly string estate;

    /// <summary>The observers of one clone of an estate on this machine, under a given Vericairn home or the user's.</summary>
    internal ObserverStore(string root, string estate, string profile, string? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(estate);
        this.estate = estate;
        FilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(EstateLocations.LiveStorePath(root, estate, profile, home ?? VericairnHome.Path()))!, FileName);
        string full = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (System.IO.Path.GetFullPath(FilePath).StartsWith(full, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("This machine's observers of an estate are kept outside the estate; choose a Vericairn home outside it.");
    }

    /// <summary>The file's name beside the clone's live store.</summary>
    internal const string FileName = "observers.json";

    /// <summary>Where the declarations are kept.</summary>
    internal string FilePath { get; }

    /// <summary>The kind of failure, in words that name no path.</summary>
    internal static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "access to it was denied",
        JsonException => "it is malformed or larger than its limit",
        IOException => "it is not an ordinary file this user can read and write, or it lies inside the estate",
        _ => "it could not be read",
    };

    /// <summary>Whether an exception means the declarations cannot be read or kept here.</summary>
    internal static bool Unavailable(Exception exception) => exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException;

    /// <summary>What is declared, empty when nothing is.</summary>
    internal IReadOnlyList<Entry> Read()
    {
        if (!File.Exists(FilePath))
        {
            if (Directory.Exists(FilePath)) throw new IOException("This machine's observers must be kept in an ordinary file.");
            return [];
        }

        var info = new FileInfo(FilePath);
        if (info.LinkTarget is not null) throw new IOException("This machine's observers cannot be kept in a link.");
        if (info.Length > MaxBytes) throw new JsonException("This machine's observers exceed their size limit.");
        Declared declared = JsonSerializer.Deserialize<Declared>(File.ReadAllBytes(FilePath), Json) ?? throw new JsonException("This machine's observers are empty.");
        if (declared.Estate != estate || declared.Observers is null || declared.Observers.Any(o => o is null))
            throw new JsonException("This machine's observers name another estate or are malformed.");
        return declared.Observers;
    }

    /// <summary>Keeps the declarations, replacing them whole.</summary>
    internal void Write(IReadOnlyList<Entry> observers)
    {
        ArgumentNullException.ThrowIfNull(observers);
        string directory = System.IO.Path.GetDirectoryName(FilePath)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (new FileInfo(FilePath).LinkTarget is not null) throw new IOException("This machine's observers cannot be kept in a link.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Declared(estate, [.. observers.OrderBy(o => o.Name, StringComparer.Ordinal)]), Json);
        if (bytes.LongLength > MaxBytes) throw new JsonException("This machine's observers would exceed their size limit.");
        string temporary = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
