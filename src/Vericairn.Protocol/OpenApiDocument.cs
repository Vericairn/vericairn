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
        ("GET", "/v1/records"),
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
        ("POST", "/v1/devices/rename"),
        ("POST", "/v1/devices/rotate"),
        ("POST", "/v1/devices/revoke"),
        ("POST", "/v1/devices/recovery"),
        ("GET", "/v1/releases"),
        ("GET", "/v1/documents"),
        ("POST", "/v1/documents/{id}/reviews"),
        ("GET", "/v1/goals"),
        ("POST", "/v1/goals"),
        ("GET", "/v1/goals/{id}"),
        ("POST", "/v1/goals/{id}/activate"),
        ("POST", "/v1/goals/{id}/pause"),
        ("POST", "/v1/goals/{id}/resume"),
        ("POST", "/v1/goals/{id}/stop"),
        ("POST", "/v1/goals/{id}/evaluate"),
        ("POST", "/v1/goals/{id}/accept"),
        ("GET", "/v1/providers"),
        ("POST", "/v1/releases/submit"),
        ("POST", "/v1/releases/observe"),
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
            if (method == "GET" && path == "/v1/records")
            {
                operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "type", ["in"] = "query", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" }, ["description"] = "A record type, for example work-order." });
            }

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
                    + "Every response is what the runtime observed, never what a client assumed. Problems carry a stable code (refused, rejected, not-found, invalid, denied, provider; conflict is reserved and nothing answers with it yet), "
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

        if (method == "POST" && path.StartsWith("/v1/releases/", StringComparison.Ordinal))
        {
            responses["200"] = new JsonObject { ["description"] = "ReleaseResponse: what was recorded and what the provider showed; a problem inside when nothing could proceed." };
        }
        else if (method == "POST" && path != "/v1/shutdown" && path != "/v1/objects" && path != "/v1/estate/upgrade" && path != "/v1/estate/export")
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
            ["DeviceCreateRequest"] = Object(("principal", "string"), ("name", "string"), ("custody", "string"), ("profiles", "array"), ("presence", "boolean")),
            ["AdmitRequest"] = Object(("device", "string"), ("by", "string"), ("note", "string"), ("recovery", "string"), ("passphrase", "string")),
            ["DeviceRenameRequest"] = Object(("device", "string"), ("name", "string")),
            ["DeviceRotateRequest"] = Object(("device", "string"), ("custody", "string"), ("presence", "boolean")),
            ["RevokeRequest"] = Object(("device", "string"), ("by", "string"), ("reason", "string"), ("recovery", "string"), ("passphrase", "string")),
            ["DocumentReviewRequest"] = Object(("by", "string"), ("outcome", "string"), ("summary", "string"), ("findings", "array"), ("commit", "string"), ("independence", "string")),
            ["GoalProposeRequest"] = Object(("by", "string"), ("body", "object"), ("supersedes", "string")),
            ["GoalActionRequest"] = Object(("by", "string"), ("note", "string")),
            ["GoalEvaluateRequest"] = Object(("by", "string"), ("summary", "string"), ("verdicts", "array")),
            ["GoalVerdict"] = Object(("condition", "integer"), ("verdict", "string"), ("reason", "string"), ("read", "array")),
            ["GoalAcceptRequest"] = Object(("by", "string"), ("statement", "string")),
            ["DeviceRecoveryRequest"] = Object(("principal", "string"), ("name", "string"), ("output", "string"), ("passphrase", "string")),
            ["ReleaseSubmitRequest"] = Object(("product", "string"), ("version", "string"), ("repository", "string"), ("by", "string"), ("ref", "string"), ("workflow", "string"), ("reconciliation", "string"), ("provider", "string"), ("settings", "object")),
            ["ReleaseObserveRequest"] = Object(("version", "string"), ("repository", "string"), ("by", "string"), ("qualified", "array"), ("limitations", "array"), ("signing", "string"), ("cancel", "string"), ("product", "string"), ("provider", "string")),
            ["ProviderRow"] = Object(("provider", "string"), ("kind", "string"), ("implementation", "string"), ("contract", "string"), ("status", "string"), ("operations", "array"), ("limitations", "array"), ("settings", "array"), ("destination", "string"), ("locations", "array")),
            ["ReleaseResponse"] = Object(("identity", "string"), ("status", "string"), ("message", "string"), ("ok", "boolean"), ("operation", "object"), ("candidate", "object"), ("release", "object"), ("releaseId", "string"), ("run", "string"), ("pending", "string"), ("publish", "string"), ("publishedAt", "string"), ("url", "string"), ("problem", "object")),
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
        "/v1/devices/rename" => "DeviceRenameRequest",
        "/v1/devices/rotate" => "DeviceRotateRequest",
        "/v1/devices/recovery" => "DeviceRecoveryRequest",
        "/v1/documents/{id}/reviews" => "DocumentReviewRequest",
        "/v1/goals" => "GoalProposeRequest",
        "/v1/goals/{id}/activate" or "/v1/goals/{id}/pause" or "/v1/goals/{id}/resume" or "/v1/goals/{id}/stop" => "GoalActionRequest",
        "/v1/goals/{id}/evaluate" => "GoalEvaluateRequest",
        "/v1/goals/{id}/accept" => "GoalAcceptRequest",
        "/v1/devices/revoke" => "RevokeRequest",
        "/v1/releases/submit" => "ReleaseSubmitRequest",
        "/v1/releases/observe" => "ReleaseObserveRequest",
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
        ("GET", "/v1/records") => "The current records of one type, oldest first.",
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
        ("POST", "/v1/devices/rename") => "Rename a device, keeping its identity.",
        ("POST", "/v1/devices/rotate") => "Give a device a new key, keeping its identity.",
        ("POST", "/v1/devices/revoke") => "Revoke a device's admission from now on.",
        ("POST", "/v1/devices/recovery") => "Make the operator's recovery device: its key is written once, encrypted, for offline keeping, and an admitted device of the operator admits it.",
        ("GET", "/v1/releases") => "Current release records.",
        ("GET", "/v1/documents") => "The declared living documents with their derived state: current, due with every trigger newer than the latest clearance, or unavailable with the contributions no installed pack provides.",
        ("POST", "/v1/documents/{id}/reviews") => "Review a living document by its declaration: the runtime reads the content, records the covered paths' identity and the triggers the review answers, runs the declared checks, and refuses a clearance a failed check or the declaration's clearedBy forbids.",
        ("GET", "/v1/goals") => "The estate's current goals: each one's current revision, title, status and sponsor.",
        ("POST", "/v1/goals") => "Propose a goal, or a revision of one (supersedes), from the goal record's body; a proposal authorizes and funds nothing, and a revision starts again from its sponsor's activation.",
        ("GET", "/v1/goals/{id}") => "A goal by the id of any of its revisions: its objective, sponsor, status and granted budget, each condition's verdict (met, not-met or unknown) with the records it read beside its current evaluation's verdict, its steps, and what stands between it and its acceptance.",
        ("POST", "/v1/goals/{id}/activate") => "Activate a proposed goal: its sponsor, a person who holds the operator role or a grant of the spending scope, grants its budget; an agent never activates a goal.",
        ("POST", "/v1/goals/{id}/pause") => "Pause an active goal; its sponsor's.",
        ("POST", "/v1/goals/{id}/resume") => "Resume a paused, budget-limited or blocked goal, which grants its budget again; its sponsor's, as activation is.",
        ("POST", "/v1/goals/{id}/stop") => "Stop a goal: its sponsor ends it as abandoned, and nothing follows; no step runs before the worker contract exists.",
        ("POST", "/v1/goals/{id}/evaluate") => "Record a goal's evaluation, condition by condition: the runtime's verdict for each record predicate and the evaluator's for each evidence statement, with the records each read, by a principal other than any step's author.",
        ("POST", "/v1/goals/{id}/accept") => "Accept a goal at its current revision: by its sponsor when the sponsor is a person and by an operator when an agent sponsors it, never by its evaluator or a step's author, once its current evaluation finds every condition met and every record predicate holds.",
        ("GET", "/v1/providers") => "The provider and adapter implementations available to the runtime on its machine, with their declared settings, destinations and served locations.",
        ("POST", "/v1/releases/submit") => "Submit a release as intent before effect: the operation record is written, then the build is dispatched through the provider the request names or the product declares; nothing is published, and a provider with no implementation here is refused.",
        ("POST", "/v1/releases/observe") => "Observe a release's run, draft and publication and record what settled (the operation revision, the candidate, the observed publication); or cancel an intent that had no effect.",
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
