// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Daemon;

/// <summary>
/// How the operator configured the runtime of an estate on this machine, kept owner-only beside its live store: machine
/// configuration, never a record. The local-network profile is off unless it names an address, and listens on a wildcard or
/// public address only when the operator allowed that address explicitly.
/// </summary>
/// <param name="LocalNetwork">The address and port the local-network profile listens on, for example 192.168.1.20:7443; null when off.</param>
/// <param name="AllowPublic">The operator allowed an address that is a wildcard or reachable from other networks (--allow-public).</param>
public sealed record RuntimeSettings(string? LocalNetwork = null, bool AllowPublic = false)
{
    /// <summary>File name beside the live store.</summary>
    public const string FileName = "runtime.json";

    private const string Format = "vericairn.runtime-settings/1";

    /// <summary>The settings kept in a directory; the defaults, with the local network off, when there are none.</summary>
    public static RuntimeSettings Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return new RuntimeSettings();
        }

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            string? address = node?["localNetwork"]?.GetValue<string>();
            bool allowPublic = node?["allowPublic"]?.GetValue<bool>() ?? false;
            return new RuntimeSettings(string.IsNullOrWhiteSpace(address) ? null : address, allowPublic);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException($"The runtime settings in {path} cannot be read: {exception.Message} Set them again with 'vericairn runtime configure'.", exception);
        }
    }

    /// <summary>Keeps the settings in a directory, readable by their owner only.</summary>
    public void Save(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        var node = new JsonObject { ["format"] = Format, ["localNetwork"] = LocalNetwork, ["allowPublic"] = AllowPublic };
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// The configured local-network address, or null when the profile is off; refused when it is a wildcard or public address the
    /// operator did not allow, as when the settings were edited by hand.
    /// </summary>
    public IPEndPoint? LocalNetworkEndPoint()
    {
        if (LocalNetwork is null)
        {
            return null;
        }

        if (!TryParseAddress(LocalNetwork, out IPEndPoint? endpoint, out string? problem))
        {
            throw new InvalidOperationException(problem);
        }

        string? exposure = Exposure(endpoint!.Address);
        if (exposure is not null && !AllowPublic)
        {
            throw new InvalidOperationException($"The runtime settings name {LocalNetwork} for the local-network profile, which is {exposure}, and the operator did not allow that: give a private address with 'vericairn runtime configure --local-network <address:port>', or allow this one with --allow-public.");
        }

        return endpoint;
    }

    /// <summary>
    /// What makes an address unfit for the local-network profile without the operator's explicit leave, or null when it is private:
    /// loopback, 10/8, 172.16/12, 192.168/16 or link-local for IPv4, and loopback, link-local or unique local (fc00::/7) for IPv6.
    /// </summary>
    public static string? Exposure(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return "a wildcard address, every interface of this machine and every network they reach";
        }

        if (IPAddress.IsLoopback(address))
        {
            return null;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            bool privateRange = bytes[0] == 10 || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
            return privateRange ? null : "an address other networks may reach";
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && (address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal))
        {
            return null;
        }

        return "an address other networks may reach";
    }

    /// <summary>Reads an address the local-network profile can listen on: an IP address and a port, 192.168.1.20:7443 or [fe80::1]:7443; port 0 asks for any free port.</summary>
    public static bool TryParseAddress(string? text, out IPEndPoint? endpoint, out string? problem)
    {
        endpoint = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(text) || !IPEndPoint.TryParse(text.Trim(), out IPEndPoint? parsed))
        {
            problem = $"'{text}' is not an address to listen on: give an IP address of this machine and a port, for example 192.168.1.20:7443.";
            return false;
        }

        // IPEndPoint.TryParse accepts an address without a port, as port 0; the profile needs one written out.
        string trimmed = text.Trim();
        int colon = trimmed.LastIndexOf(':');
        bool hasPort = colon > 0 && (parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6 || trimmed.StartsWith('[')) && int.TryParse(trimmed[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _);
        if (!hasPort)
        {
            problem = $"'{text}' names no port: give one, for example {parsed.Address}:7443.";
            return false;
        }

        endpoint = parsed;
        return true;
    }
}
