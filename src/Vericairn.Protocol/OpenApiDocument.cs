// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Protocol;

/// <summary>The OpenAPI description of API version 1: the single definition of routes that the daemon serves, the SDK calls and the contract tests check.</summary>
public static class OpenApiDocument
{
    /// <summary>Every route of version 1 with its method; the contract tests assert the daemon maps exactly these.</summary>
    public static IReadOnlyList<(string Method, string Path)> Routes { get; } =
    [
        ("GET", "/v1/health"),
        ("GET", "/v1/status"),
        ("GET", "/v1/validate"),
        ("GET", "/v1/check"),
        ("GET", "/v1/records/{id}"),
        ("POST", "/v1/records"),
        ("GET", "/v1/decisions"),
        ("GET", "/v1/decisions/{id}"),
        ("GET", "/v1/work/{key}/brief"),
        ("GET", "/v1/work/{key}"),
        ("POST", "/v1/work/assign"),
        ("POST", "/v1/work/attempt"),
        ("POST", "/v1/work/result"),
        ("POST", "/v1/work/amend"),
        ("GET", "/v1/devices"),
        ("POST", "/v1/devices"),
        ("POST", "/v1/devices/admit"),
        ("POST", "/v1/devices/revoke"),
        ("GET", "/v1/releases"),
        ("POST", "/v1/estate/upgrade"),
        ("POST", "/v1/estate/export"),
        ("GET", "/v1/reconcile"),
        ("GET", "/v1/recover/inspect"),
        ("GET", "/v1/objects"),
        ("POST", "/v1/objects"),
        ("GET", "/v1/objects/verify"),
        ("GET", "/v1/objects/{hash}"),
        ("POST", "/v1/shutdown"),
    ];

    /// <summary>The path of the document itself.</summary>
    public const string Path = "/openapi/v1.json";

    /// <summary>The document as JSON.</summary>
    public static string Json { get; } = Build();

    private static string Build()
    {
        var paths = new JsonObject();
        foreach ((string method, string path) in Routes)
        {
            if (paths[path] is not JsonObject item)
            {
                item = [];
                paths[path] = item;
            }

            var operation = new JsonObject
            {
                ["operationId"] = OperationId(method, path),
                ["summary"] = Summary(method, path),
                ["responses"] = Responses(method, path),
            };
            if (path.Contains('{', StringComparison.Ordinal))
            {
                var parameters = new JsonArray();
                foreach (string name in path.Split('/').Where(s => s.StartsWith('{')).Select(s => s.Trim('{', '}')))
                {
                    parameters.Add(new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
                }

                operation["parameters"] = parameters;
            }

            if (method == "POST" && path != "/v1/shutdown")
            {
                operation["requestBody"] = new JsonObject
                {
                    ["required"] = true,
                    ["content"] = new JsonObject
                    {
                        [path == "/v1/objects" ? "application/octet-stream" : "application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/" + RequestSchema(path) } },
                    },
                };
            }

            item[method.ToLowerInvariant()] = operation;
        }

        var document = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = "Vericairn API",
                ["version"] = ApiVersion.Current,
                ["description"] = "One versioned API that every client uses. Authority is evaluated by the runtime behind it; a client presents, requests and observes. "
                    + "Every response is what the runtime observed, never what a client assumed. Problems carry a stable code (refused, rejected, not-found, invalid, conflict, denied), "
                    + "a message with the permitted next action and, for rejected records, the findings. "
                    + "Transports: a local socket (Unix domain socket, or a named pipe on Windows) authenticated by operating-system identity, and loopback TLS pinned to the "
                    + "runtime certificate where every request is signed by an admitted device (headers Vericairn-Device, Vericairn-Signed-At, Vericairn-Signature; scheme vericairn-request/1).",
            },
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["schemas"] = Schemas(),
                ["securitySchemes"] = new JsonObject
                {
                    ["deviceSignature"] = new JsonObject
                    {
                        ["type"] = "apiKey",
                        ["in"] = "header",
                        ["name"] = RequestSigning.SignatureHeader,
                        ["description"] = "Required on loopback: an ECDSA P-256 signature by an admitted device over the request (scheme vericairn-request/1), with Vericairn-Device and Vericairn-Signed-At.",
                    },
                },
            },
        };
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Responses(string method, string path)
    {
        var responses = new JsonObject { ["200"] = new JsonObject { ["description"] = "What the runtime observed." } };
        if (path.Contains('{', StringComparison.Ordinal))
        {
            responses["404"] = new JsonObject { ["description"] = "Nothing has that id, key or hash." };
        }

        if (method == "POST" && path != "/v1/shutdown" && path != "/v1/objects" && path != "/v1/estate/upgrade" && path != "/v1/estate/export")
        {
            responses["404"] = new JsonObject { ["description"] = "The subject named does not exist (WriteOutcome with a not-found problem)." };
            responses["422"] = new JsonObject { ["description"] = "Refused or rejected (WriteOutcome with a problem; a rejection carries findings)." };
        }

        if (path is "/v1/estate/upgrade" or "/v1/estate/export")
        {
            responses["409"] = new JsonObject { ["description"] = "Not carried out (Problem)." };
        }

        if (method == "POST" && path == "/v1/objects")
        {
            responses["413"] = new JsonObject { ["description"] = "Above the object size limit (Problem)." };
        }

        if (path == "/v1/shutdown")
        {
            responses["202"] = new JsonObject { ["description"] = "The daemon will stop." };
        }

        responses["401"] = new JsonObject { ["description"] = "On loopback: the request is not signed by a device admitted now (Problem with code denied)." };
        return responses;
    }

    private static JsonObject Schemas()
    {
        static JsonObject Object(params (string Name, string Type)[] properties)
        {
            var props = new JsonObject();
            foreach ((string name, string type) in properties)
            {
                props[name] = new JsonObject { ["type"] = type };
            }

            return new JsonObject { ["type"] = "object", ["properties"] = props };
        }

        return new JsonObject
        {
            ["NewRecordRequest"] = Object(("type", "string"), ("by", "string"), ("body", "object"), ("subject", "string"), ("supersedes", "string"), ("at", "string"), ("id", "string")),
            ["AssignRequest"] = Object(("workOrder", "string"), ("contributor", "string"), ("by", "string"), ("brief", "object"), ("note", "string")),
            ["AttemptRequest"] = Object(("assignment", "string"), ("by", "string"), ("workspace", "string"), ("note", "string"), ("at", "string")),
            ["ResultRequest"] = Object(("attempt", "string"), ("by", "string"), ("body", "object")),
            ["AmendRequest"] = Object(("assignment", "string"), ("by", "string"), ("body", "object")),
            ["DeviceCreateRequest"] = Object(("principal", "string"), ("name", "string"), ("custody", "string"), ("profiles", "array")),
            ["AdmitRequest"] = Object(("device", "string"), ("by", "string"), ("note", "string")),
            ["RevokeRequest"] = Object(("device", "string"), ("by", "string"), ("reason", "string")),
            ["UpgradeRequest"] = Object(("version", "string"), ("by", "string"), ("check", "boolean")),
            ["ExportRequest"] = Object(("output", "string"), ("by", "string")),
            ["ObjectBytes"] = new JsonObject { ["type"] = "string", ["format"] = "binary" },
            ["WriteOutcome"] = Object(("written", "object"), ("problem", "object")),
            ["Problem"] = Object(("code", "string"), ("message", "string"), ("findings", "array")),
        };
    }

    private static string RequestSchema(string path) => path switch
    {
        "/v1/records" => "NewRecordRequest",
        "/v1/work/assign" => "AssignRequest",
        "/v1/work/attempt" => "AttemptRequest",
        "/v1/work/result" => "ResultRequest",
        "/v1/work/amend" => "AmendRequest",
        "/v1/devices" => "DeviceCreateRequest",
        "/v1/devices/admit" => "AdmitRequest",
        "/v1/devices/revoke" => "RevokeRequest",
        "/v1/estate/upgrade" => "UpgradeRequest",
        "/v1/estate/export" => "ExportRequest",
        "/v1/objects" => "ObjectBytes",
        _ => "Problem",
    };

    private static string OperationId(string method, string path) =>
        method.ToLowerInvariant() + string.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(s => char.ToUpperInvariant(s.Trim('{', '}')[0]) + s.Trim('{', '}')[1..]));

    private static string Summary(string method, string path) => (method, path) switch
    {
        ("GET", "/v1/health") => "The daemon's identity and what it serves.",
        ("GET", "/v1/status") => "Current status: human text and the canonical JSON that is identical on every machine.",
        ("GET", "/v1/validate") => "Validate every record and the manifest.",
        ("GET", "/v1/check") => "Validate and run the repository checks.",
        ("GET", "/v1/records/{id}") => "A record by id.",
        ("POST", "/v1/records") => "Author a record; signed with the author's admitted device on the runtime's machine; rejected with findings when the estate would be invalid.",
        ("GET", "/v1/decisions") => "Current decisions.",
        ("GET", "/v1/decisions/{id}") => "One decision by the id of any of its revisions.",
        ("GET", "/v1/work/{key}/brief") => "The brief of a work order: everything a contributor needs, from the records alone.",
        ("GET", "/v1/work/{key}") => "What an observer sees of a work order.",
        ("POST", "/v1/work/assign") => "Delegate a work order to a contributor under a brief.",
        ("POST", "/v1/work/attempt") => "Record the start of an attempt.",
        ("POST", "/v1/work/result") => "Submit the result of an attempt: a claim to inspect.",
        ("POST", "/v1/work/amend") => "Request an amendment; it authorizes nothing.",
        ("GET", "/v1/devices") => "Devices known to the estate and whether this machine holds their keys.",
        ("POST", "/v1/devices") => "Create a device on the runtime's machine.",
        ("POST", "/v1/devices/admit") => "Admit a device.",
        ("POST", "/v1/devices/revoke") => "Revoke a device's admission from now on.",
        ("GET", "/v1/releases") => "Current release records.",
        ("POST", "/v1/estate/upgrade") => "Plan or apply an upgrade to a published release.",
        ("POST", "/v1/estate/export") => "Write a verified export to a path on the runtime's machine; no keys or authority travel with it.",
        ("GET", "/v1/reconcile") => "Reconcile tasks left by diverged copies.",
        ("GET", "/v1/recover/inspect") => "What this copy holds, for recovery.",
        ("GET", "/v1/objects") => "Objects held.",
        ("POST", "/v1/objects") => "Store bytes as an object named by their hash.",
        ("GET", "/v1/objects/verify") => "Verify every object against its name.",
        ("GET", "/v1/objects/{hash}") => "An object's verified bytes.",
        ("POST", "/v1/shutdown") => "Ask the daemon to stop; closing a client never stops it.",
        _ => string.Empty,
    };
}
