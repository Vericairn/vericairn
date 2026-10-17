// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Updates;

internal sealed record ReleaseCheckState(DateTimeOffset? LastAttemptAt = null, DateTimeOffset? LastSuccessAt = null, string? Tag = null)
{
    internal PublishedRelease? Release => PublishedRelease.FromTag(Tag);
}

internal sealed record ReleaseCheckSettings([property: JsonRequired] bool Enabled);

// Shared by clients and read without network by estate check. No estate identity or machine path is stored.
internal sealed class ReleaseUpdateStore(string? home = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    internal bool Enabled
    {
        get
        {
            try { return Read<ReleaseCheckSettings>("settings.json")?.Enabled ?? true; }
            catch (Exception exception) when (Unavailable(exception)) { return false; }
        }
    }

    internal PublishedRelease? CachedRelease()
    {
        try { return ReadState().Release; }
        catch (Exception exception) when (Unavailable(exception)) { return null; }
    }

    internal ReleaseCheckState ReadState() => Read<ReleaseCheckState>("check.json") ?? new();

    // A caller must hold this lock while rereading state, reserving an attempt and updating its answer.
    // Failure to acquire it skips the check; it never waits on a second client or sends a second request.
    internal FileStream Acquire()
    {
        string path = FilePath("check.lock");
        string directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    internal void Configure(bool enabled)
    {
        using FileStream held = Acquire();
        Write("settings.json", new ReleaseCheckSettings(enabled));
    }

    internal void WriteState(ReleaseCheckState state) => Write("check.json", state);

    internal static bool Unavailable(Exception exception) => exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;

    private T? Read<T>(string name)
    {
        string path = FilePath(name);
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 4096) throw new JsonException("Release check state exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json) ?? throw new JsonException("Release check state is empty.");
    }

    private void Write<T>(string name, T value)
    {
        string path = FilePath(name);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".tmp");
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

    private string FilePath(string name)
    {
        string directory = Path.Combine(home ?? VericairnHome.Path(), "updates");
        string suffix = "";
        int links = 0;
        // Resolve linked ancestors too, including the platform's temporary directory, before checking location.
        for (DirectoryInfo? parent = new(Path.GetFullPath(directory)); parent is not null; parent = parent.Parent)
        {
            if (parent.LinkTarget is not null)
            {
                if (++links > 40) throw new IOException("Release check state has too many linked ancestors.");
                parent = new DirectoryInfo(parent.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
            }
            if (Directory.Exists(Path.Combine(parent.FullName, ".git")) || File.Exists(Path.Combine(parent.FullName, ".git"))
                || File.Exists(Path.Combine(parent.FullName, "estate", "manifest.json"))
                || (File.Exists(Path.Combine(parent.FullName, "HEAD")) && Directory.Exists(Path.Combine(parent.FullName, "objects"))))
                throw new IOException("Release check state must be outside repositories and estates; choose a machine-local Vericairn home.");
            if (parent.Parent is null) directory = Path.Combine(parent.FullName, suffix);
            suffix = Path.Combine(parent.Name, suffix);
        }
        string path = Path.Combine(directory, name);
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Release check files cannot be symbolic links.");
        return path;
    }
}
