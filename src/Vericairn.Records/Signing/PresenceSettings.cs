// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Records.Signing;

/// <summary>
/// A device owner's presence setting on the machine holding its key (design/presence-sessions.md § 10): per-signature presence, or
/// per-action presence sessions with a limit, never longer than 300 seconds and never under 60.
/// </summary>
/// <param name="PerSignature">Whether every signature asks its own check.</param>
/// <param name="LimitSeconds">The owner's limit for a session, when set.</param>
/// <param name="Problem">Why the setting could not be read, when it could not; it then reads as per-signature, the stricter.</param>
internal sealed record PresenceSetting(bool PerSignature, int? LimitSeconds, string? Problem = null)
{
    /// <summary>The default: per-action presence sessions, at the protocol's limit.</summary>
    internal static PresenceSetting Default { get; } = new(false, null);
}

/// <summary>
/// From 0.9.4 (ID-16): the device owners' presence settings, kept owner-only in this user's machine state, never in a record, an export
/// or a support bundle. A process of the same operating-system user can change them; nothing in the hardware enforces them. The
/// command that sets them is <c>vericairn devices presence</c>; a file that cannot be read reads as per-signature, so a damaged setting
/// never loosens presence.
/// </summary>
internal static class PresenceSettings
{
    /// <summary>The settings file's name under the Vericairn home.</summary>
    internal const string FileName = "presence.json";

    /// <summary>The settings file's format.</summary>
    internal const string Format = "vericairn.presence-settings/1";

    /// <summary>The settings file under a home, this user's by default.</summary>
    internal static string PathOf(string? home = null) => Path.Combine(home ?? VericairnHome.Path(), FileName);

    /// <summary>
    /// A device's setting: the default when the file or its directory is absent, per-signature when either cannot be read or the file's
    /// form is not this one, so a setting that cannot be read never loosens presence.
    /// </summary>
    internal static PresenceSetting Read(string device, string? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        string path = PathOf(home);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return PresenceSetting.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file is there or may be, and cannot be read, its directory not searchable among them: it never loosens presence.
            return Unreadable("it cannot be read");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.GetString() != Format
                || !root.TryGetProperty("devices", out JsonElement devices) || devices.ValueKind != JsonValueKind.Object)
                return Unreadable("its form is not " + Format);
            if (!devices.TryGetProperty(device, out JsonElement entry)) return PresenceSetting.Default;
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("presence", out JsonElement presence) || presence.GetString() is not ("per-signature" or "per-action"))
                return Unreadable("the device's entry names neither per-signature nor per-action");
            int? limit = null;
            if (entry.TryGetProperty("limitSeconds", out JsonElement value))
            {
                if (!value.TryGetInt32(out int seconds) || seconds is < PresenceSessionProtocol.MinLimitSeconds or > PresenceSessionProtocol.MaxLimitSeconds)
                    return Unreadable("the device's limit is not 60 to 300 seconds");
                limit = seconds;
            }

            return new PresenceSetting(presence.GetString() == "per-signature", limit);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Unreadable("it cannot be read");
        }
    }

    /// <summary>
    /// Keeps a device's setting, owner-only; a limit is 60 to 300 seconds, or none for the protocol's. From 0.9.4 (CODE-80) a settings file
    /// that does not read as this format, which counts as per-signature for every device, is never replaced: writing one device's setting
    /// over it would loosen every other device's. <see cref="UnreadableSettingsException"/> says which file and how to clear it, and the
    /// file is left as it was. Another device's entry the file keeps as it stands, whatever it holds.
    /// </summary>
    internal static void Write(string device, bool perSignature, int? limitSeconds = null, string? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        if (limitSeconds is < PresenceSessionProtocol.MinLimitSeconds or > PresenceSessionProtocol.MaxLimitSeconds)
            throw new ArgumentOutOfRangeException(nameof(limitSeconds), "A session's limit is 60 to 300 seconds.");
        string path = PathOf(home);
        OwnerOnlyState.CreateDirectory(Path.GetDirectoryName(path)!, home ?? VericairnHome.Path());
        JsonObject root;
        if (File.Exists(path))
        {
            string? problem;
            JsonObject? existing;
            try
            {
                existing = JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject;
                problem = existing is null ? "it is not a JSON object"
                    : existing["format"] is not JsonValue format || !format.TryGetValue(out string? name) || name != Format ? "its form is not " + Format
                    : existing["devices"] is not JsonObject ? "it holds no devices object"
                    : null;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                existing = null;
                problem = "it is not well-formed JSON";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                existing = null;
                problem = "it cannot be read";
            }

            if (problem is not null) throw new UnreadableSettingsException(path, problem);
            root = existing!;
        }
        else
        {
            root = new JsonObject();
        }

        root["format"] = Format;
        JsonObject devices = root["devices"] as JsonObject ?? new JsonObject();
        root["devices"] = devices;
        var entry = new JsonObject { ["presence"] = perSignature ? "per-signature" : "per-action" };
        if (limitSeconds is int limit) entry["limitSeconds"] = limit;
        devices[device] = entry;
        OwnerOnlyState.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(root, Indented));
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static PresenceSetting Unreadable(string why) => new(true, null, $"The presence settings in {FileName} cannot be used ({why}), so every signature asks its own check.");
}

/// <summary>
/// From 0.9.4 (CODE-80): the device owners' presence settings file is there and does not read as its format, so every device counts as
/// per-signature, and no setting is written over it.
/// </summary>
internal sealed class UnreadableSettingsException(string path, string problem)
    : InvalidOperationException($"The presence settings in {path} cannot be used ({problem}), so every device on this machine counts as per-signature and nothing was written. "
        + $"Repair the file to the form {PresenceSettings.Format}, or remove it to return every device on this machine to the default, per-action presence sessions, then set each device again.")
{
    /// <summary>The settings file.</summary>
    internal string Path { get; } = path;
}
