// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json.Nodes;

namespace Vericairn.Protocol;

internal static class Mcp20260728
{
    public const string Version = "2026-07-28";
    public const string LegacyVersion = "2025-11-25";
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    public const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";
    public const string ProtocolVersionHeader = "MCP-Protocol-Version";
    public const string MethodHeader = "Mcp-Method";
    public const string NameHeader = "Mcp-Name";
    public const string ListenMethod = "subscriptions/listen";
    public const string Acknowledged = "notifications/subscriptions/acknowledged";

    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public static bool IsStateless(string method, JsonNode? parameters) =>
        method is "server/discover" or ListenMethod ||
        parameters is JsonObject p && p["_meta"] is JsonObject meta &&
        (meta.ContainsKey(ProtocolVersion) || meta.ContainsKey(ClientCapabilities) || meta.ContainsKey("io.modelcontextprotocol/clientInfo") ||
            meta.ContainsKey("io.modelcontextprotocol/logLevel"));

    public static string? RequestedVersion(JsonNode? parameters) =>
        parameters is JsonObject p && p["_meta"] is JsonObject meta ? Text(meta[ProtocolVersion]) : null;

    public static string? Name(string method, JsonNode? parameters) => parameters is JsonObject p
        ? method switch { "tools/call" or "prompts/get" => Text(p["name"]), "resources/read" => Text(p["uri"]), _ => null }
        : null;

    public static string EncodeHeader(string value) =>
        // HTTP strips surrounding whitespace from a plain header value.
        value.All(c => c is >= ' ' and <= '~') && !value.StartsWith(' ') && !value.EndsWith(' ')
            && !value.StartsWith("=?base64?", StringComparison.Ordinal)
            ? value : "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";

    public static bool MatchesHeader(string header, string value)
    {
        if (!header.StartsWith("=?base64?", StringComparison.Ordinal))
            return header.All(c => c is >= ' ' and <= '~') && string.Equals(header, value, StringComparison.Ordinal);
        if (!header.EndsWith("?=", StringComparison.Ordinal) || header.Length < 11) return false;
        try
        {
            byte[] decoded = Convert.FromBase64String(header[9..^2]);
            return decoded.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(value));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static JsonObject Error(JsonNode? id, int code, string message, JsonObject? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null) error["data"] = data;
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = error };
    }

    public static JsonObject Unsupported(JsonNode? id, string requested, IReadOnlyList<string> supported) =>
        Error(id, -32022, "Unsupported protocol version", new JsonObject
        {
            ["supported"] = new JsonArray(supported.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["requested"] = requested,
        });

    public static JsonObject? Validate(JsonNode? id, JsonNode? parameters, IReadOnlyList<string> supported)
    {
        string? requested = RequestedVersion(parameters);
        if (string.IsNullOrEmpty(requested))
            return Error(id, -32602, "Invalid params: request metadata must include a protocol version.");
        if (!supported.Contains(requested, StringComparer.Ordinal))
            return Unsupported(id, requested, supported);
        if (((JsonObject)((JsonObject)parameters!)["_meta"]!)[ClientCapabilities] is not JsonObject)
            return Error(id, -32602, "Invalid params: request metadata must include client capabilities.");
        return null;
    }
}
