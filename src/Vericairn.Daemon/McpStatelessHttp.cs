// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>Version routing and the header contract for stateless Streamable HTTP.</summary>
internal static class McpStatelessHttp
{
    internal static bool Selected(HttpRequest request, string? method, JsonNode? parameters) =>
        method is not null && Mcp20260728.IsStateless(method, parameters)
        || request.Headers.ContainsKey(Mcp20260728.MethodHeader)
        || request.Headers.TryGetValue(McpEndpoint.VersionHeader, out StringValues version)
            && version.Count == 1 && !McpEndpoint.Versions.Contains(version[0]!, StringComparer.Ordinal);

    internal static JsonObject? Validate(HttpRequest request, JsonObject message, JsonNode? id, string? method, JsonNode? parameters)
    {
        bool validId = id is JsonValue value && value.GetValueKind() is JsonValueKind.String or JsonValueKind.Number;
        if (message["jsonrpc"] is not JsonValue jsonrpc || !jsonrpc.TryGetValue(out string? rpcVersion) || rpcVersion != "2.0"
            || string.IsNullOrWhiteSpace(method) || message.ContainsKey("id") && !validId || message.ContainsKey("result") || message.ContainsKey("error"))
        {
            return Mcp20260728.Error(validId ? id : null, -32600, "Invalid request: send one JSON-RPC request or notification.");
        }

        string? requested = Mcp20260728.RequestedVersion(parameters);
        if (requested is null)
        {
            return Mcp20260728.Validate(id, parameters, McpEndpoint.DiscoveryVersions);
        }

        if (!request.Headers.TryGetValue(McpEndpoint.VersionHeader, out StringValues version) || version.Count != 1 || version[0] != requested)
        {
            return Mismatch(id, McpEndpoint.VersionHeader);
        }

        if (Mcp20260728.Validate(id, parameters, McpEndpoint.DiscoveryVersions) is JsonObject invalid)
        {
            return invalid;
        }

        if (requested != Mcp20260728.Version)
        {
            return Mcp20260728.Error(id, -32602, $"MCP {requested} uses the session-based transport; send initialize.");
        }

        if (!Matches(request, Mcp20260728.MethodHeader, method))
        {
            return Mismatch(id, Mcp20260728.MethodHeader);
        }

        if (method is "tools/call" or "resources/read" or "prompts/get")
        {
            string? name = Mcp20260728.Name(method, parameters);
            if (string.IsNullOrEmpty(name))
            {
                return Mcp20260728.Error(id, -32602, "Invalid params: the request must name the tool, resource or prompt.");
            }

            if (!Matches(request, Mcp20260728.NameHeader, name))
            {
                return Mismatch(id, Mcp20260728.NameHeader);
            }
        }

        return null;
    }

    private static bool Matches(HttpRequest request, string key, string value) => request.Headers.TryGetValue(key, out StringValues header)
        && header.Count == 1 && Mcp20260728.MatchesHeader(header[0]!, value);

    private static JsonObject Mismatch(JsonNode? id, string header) =>
        Mcp20260728.Error(id, -32020, $"Header mismatch: {header} must match the JSON-RPC message.");
}
