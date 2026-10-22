// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Protocol;

/// <summary>The OpenAPI description of API version 1: the single definition of routes that the daemon serves, the SDK calls and the contract tests check.</summary>
public static class OpenApiDocument
{
    private static readonly string[] CommunicationParameterNames = ["recipient", "assignment", "progress", "status", "decision", "result", "handoff"];
    private static readonly string[] PolicyParameterNames = ["asOf", "release"];

    /// <summary>Every route of version 1 with its method; the contract tests assert the daemon maps exactly these.</summary>
    public static IReadOnlyList<(string Method, string Path)> Routes { get; } =
    [
        ("GET", "/v1/companions"),
        ("GET", "/v1/companions/render"),
        ("GET", "/v1/companions/sources"),
        ("GET", "/v1/companions/compare"),
        ("GET", "/v1/estate-policies"),
        ("GET", "/v1/estate-policies/kinds"),
        ("POST", "/v1/estate-policies/hash"),
        ("GET", "/v1/estate-policies/evaluate"),
        ("GET", "/v1/estate-policies/{key}/view"),
        ("GET", "/v1/writing-exceptions"),
        ("GET", "/v1/writing-exceptions/{id}"),
        ("GET", "/v1/estate-policies/access"),
        ("POST", "/v1/estate-policies"),
        ("POST", "/v1/estate-policies/{key}/revisions"),
        ("POST", "/v1/estate-policies/{key}/retire"),
        ("POST", "/v1/estate-policies/{key}/exceptions"),
        ("POST", "/v1/estate-policies/{key}/exceptions/{id}/revoke"),
        ("POST", "/v1/writing-exceptions"),
        ("POST", "/v1/writing-exceptions/{id}/remove"),
        ("POST", "/v1/work/{key}/placement"),
        ("GET", "/v1/health"),
        ("GET", "/v1/communication"),
        ("POST", "/v1/communication"),
        ("POST", "/v1/communication/delegations"),
        ("GET", "/v1/status"),
        ("POST", "/v1/status/observations"),
        ("GET", "/v1/progress"),
        ("GET", "/v1/handoff"),
        ("POST", "/v1/resume"),
        ("GET", "/v1/context/work/{key}"),
        ("GET", "/v1/context/work/{key}/brief"),
        ("GET", "/v1/context/original/{pin}"),
        ("GET", "/v1/context/{manifest}/sources/{source}"),
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
        ("POST", "/v1/work/progress"),
        ("POST", "/v1/work/result"),
        ("POST", "/v1/work/review"),
        ("GET", "/v1/release-bases/{hash}"),
        ("POST", "/v1/work/amend"),
        ("POST", "/v1/work/takeover"),
        ("GET", "/v1/devices"),
        ("POST", "/v1/devices"),
        ("POST", "/v1/devices/admit"),
        ("POST", "/v1/devices/rename"),
        ("POST", "/v1/devices/rotate"),
        ("POST", "/v1/devices/revoke"),
        ("POST", "/v1/devices/recovery"),
        ("GET", "/v1/releases"),
        ("GET", "/v1/documents"),
        ("POST", "/v1/documents/pin"),
        ("POST", "/v1/documents/{id}/reviews"),
        ("GET", "/v1/asks"),
        ("GET", "/v1/operator/actions"),
        ("POST", "/v1/operator/record"),
        ("GET", "/v1/goals"),
        ("POST", "/v1/goals"),
        ("GET", "/v1/goals/{id}"),
        ("GET", "/v1/goals/{id}/next"),
        ("POST", "/v1/goals/{id}/step"),
        ("POST", "/v1/goals/{id}/activate"),
        ("POST", "/v1/goals/{id}/pause"),
        ("POST", "/v1/goals/{id}/resume"),
        ("POST", "/v1/goals/{id}/stop"),
        ("POST", "/v1/goals/{id}/evaluate"),
        ("POST", "/v1/goals/{id}/accept"),
        ("GET", "/v1/providers"),
        ("GET", "/v1/launchers"),
        ("POST", "/v1/harness-qualifications"),
        ("GET", "/v1/runs"),
        ("POST", "/v1/runs"),
        ("POST", "/v1/runs/{id}/stop"),
        ("POST", "/v1/work/{key}/move-plan"),
        ("POST", "/v1/releases/withdraw"),
        ("POST", "/v1/releases/submit"),
        ("POST", "/v1/releases/observe"),
        ("POST", "/v1/changes/push"),
        ("POST", "/v1/changes/open"),
        ("POST", "/v1/changes/merge"),
        ("POST", "/v1/changes/observe"),
        ("POST", "/v1/packages/observe"),
        ("POST", "/v1/packages/unlist"),
        ("POST", "/v1/packages/deprecate"),

        ("POST", "/v1/estate/upgrade"),
        ("POST", "/v1/estate/export"),
        ("GET", "/v1/reconcile"),
        ("GET", "/v1/recover/inspect"),
        ("GET", "/v1/objects"),
        ("POST", "/v1/objects"),
        ("GET", "/v1/objects/verify"),
        ("GET", "/v1/objects/{hash}"),
        ("POST", "/v1/shutdown"),
        ("POST", "/v1/pairings"),
        ("POST", "/v1/pairing/hello"),
        ("POST", "/v1/pairing/confirm"),
        ("GET", "/v1/runtime/identity"),
        ("POST", "/v1/runtime/certificate"),
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
            if (path.StartsWith("/v1/companions/", StringComparison.Ordinal))
            {
                string[] names = path.EndsWith("/compare", StringComparison.Ordinal) ? ["selection", "commit", "audience"]
                    : ["generator", "selection", "revision", "snapshot", "page", "field", "index", "audience", "contract", "declaration", "commit"];
                operation["parameters"] = new JsonArray(names.Select(name => (JsonNode)new JsonObject
                {
                    ["name"] = name, ["in"] = "query", ["required"] = name is "generator" or "selection",
                    ["schema"] = name is "page" or "contract" ? new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["default"] = 1 }
                        : new JsonObject { ["type"] = "string" },
                    ["description"] = name switch
                    {
                        "snapshot" => "Exact record or manifest snapshot returned by coverage. Refresh and select again if unavailable.",
                        "revision" => "Exact immutable record revision; never follows a successor.",
                        "audience" => "Internal by default. A requested audience grants no access; unsupported audiences are unavailable.",
                        "commit" => "Exact retained source commit for a declared repository item; no clone-head substitution.",
                        "selection" => "item or nested list at a revision; current or all-revisions at a snapshot. Comparison selects a declaration or destination.",
                        _ => "Explicit reader input; inspect the installed contract through coverage.",
                    },
                }).ToArray());
            }
            if (method == "GET" && path is "/v1/communication" or "/v1/work/{key}/brief" or "/v1/context/work/{key}/brief" or "/v1/operator/actions")
            {
                operation["parameters"] = new JsonArray(CommunicationParameterNames
                    .Select(name => (JsonNode)new JsonObject { ["name"] = name, ["in"] = "query", ["required"] = false, ["schema"] = new JsonObject { ["type"] = "string" } }).ToArray());
            }

            if (method == "GET" && path == "/v1/estate-policies/evaluate")
            {
                operation["parameters"] = new JsonArray(PolicyParameterNames.Select(name => (JsonNode)new JsonObject
                {
                    ["name"] = name, ["in"] = "query", ["required"] = false, ["schema"] = new JsonObject { ["type"] = "string" },
                    ["description"] = name == "asOf" ? "Explicit canonical UTC preview time. Never authorizes submission." : "Exact release record id selecting its product, version and commit.",
                }).ToArray());
            }

            if (method == "GET" && path == "/v1/records")
            {
                operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "type", ["in"] = "query", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" }, ["description"] = "A record type, for example work-order." });
            }

            if (method == "GET" && path == "/v1/estate-policies/access")
                operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "principal", ["in"] = "query", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" }, ["description"] = "Acting principal whose current policy action admission is inspected; exact bounds are rechecked for every write." });

            if (method == "GET" && path == RuntimeIdentityScheme.Path)
            {
                operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "nonce", ["in"] = "query", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" }, ["description"] = "32 random bytes, base64url, fresh for each connection." });
            }

            if (method == "GET" && path is "/v1/handoff" or "/v1/context/work/{key}" or "/v1/context/work/{key}/brief")
            {
                JsonArray parameters = operation["parameters"] is JsonArray existing ? (JsonArray)existing.DeepClone() : new JsonArray();
                parameters.Add(new JsonObject { ["name"] = "maxBytes", ["in"] = "query", ["required"] = false, ["schema"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 }, ["description"] = "Optional context allocation in canonical payload bytes, separate from a run spending cap. Mandatory originals, manifest and accounting must fit or the package is refused." });
                operation["parameters"] = parameters;
            }

            if (method == "GET" && path == "/v1/asks")
            {
                operation["parameters"] = new JsonArray(new JsonObject { ["name"] = "to", ["in"] = "query", ["required"] = false, ["schema"] = new JsonObject { ["type"] = "string" }, ["description"] = "The principal asked, by id; every principal when omitted." });
            }

            if (path.Contains('{', StringComparison.Ordinal))
            {
                var parameters = operation["parameters"] is JsonArray existingParameters ? (JsonArray)existingParameters.DeepClone() : new JsonArray();
                foreach (string name in path.Split('/').Where(s => s.StartsWith('{')).Select(s => s.Trim('{', '}')))
                {
                    parameters.Add(new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
                }

                if (operation["parameters"] is null)
                {
                    operation["parameters"] = parameters;
                }
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
                    + "Every response is what the runtime observed, never what a client assumed. Problems carry a stable code (refused, rejected, not-found, invalid, denied, provider, conflict, reconcile-required), "
                    + "a message with the permitted next action and, for rejected records, the findings. "
                    + "Transports: a local socket (Unix domain socket, or a named pipe on Windows) authenticated by operating-system identity, and loopback TLS pinned to the "
                    + "runtime certificate where every request is signed by an admitted device (headers Vericairn-Device, Vericairn-Signed-At, Vericairn-Signature; scheme vericairn-request/1). "
                    + "From 0.6.0, when the operator configures an address, the local network: mutual TLS, where a paired device presents a certificate its device key issued, has the runtime identity "
                    + "it pinned at pairing sign the runtime certificate's fingerprint for each connection (GET /v1/runtime/identity, scheme vericairn-runtime-identity/1), and signs every request; "
                    + "the device must be admitted now and permitted the local-network profile, and acts for its own principal. A device pairs with a code an administrator issues "
                    + "(POST /v1/pairings), over POST /v1/pairing/hello and POST /v1/pairing/confirm (scheme vericairn-pairing/1).",
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
                        ["description"] = "Required on loopback and the local network: an ECDSA P-256 signature by an admitted device over the request (scheme vericairn-request/1), with Vericairn-Device and Vericairn-Signed-At.",
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

        if (path == "/v1/companions" || path.StartsWith("/v1/companions/", StringComparison.Ordinal))
        {
            string schema = path == "/v1/companions" ? "CompanionsResponse" : path.EndsWith("/compare", StringComparison.Ordinal) ? "CompanionComparison" : "CompanionResponse";
            responses["200"] = new JsonObject
            {
                ["description"] = "A versioned read result with explicit unavailable, historical, current or different basis. Generation grants no authority or authored clearance.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/" + schema } } },
            };
        }
        else if (path is "/v1/resume")
        {
            responses["200"] = new JsonObject { ["description"] = "ResumeResponse: every difference, or an invalid-handoff problem. This POST is a read; it authors no record." };
        }
        else if (path == "/v1/estate-policies/hash")
        {
            responses["200"] = new JsonObject
            {
                ["description"] = "PolicyHashResponse: the definitionHash checked by policy approval, with only top-level basis omitted. This POST reads only its input and writes nothing.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/PolicyHashResponse" } } },
            };
            responses["422"] = new JsonObject
            {
                ["description"] = "PolicyHashResponse with an invalid problem and no definitionHash: supply a prepared policy or duty body, without a record envelope.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/PolicyHashResponse" } } },
            };
            responses["400"] = new JsonObject { ["description"] = "The request is not valid JSON or cannot bind to PolicyHashRequest." };
        }
        else if (path == "/v1/documents/pin")
        {
            foreach (string code in new[] { "200", "422" })
                responses[code] = new JsonObject
                {
                    ["description"] = code == "200" ? "DocumentPinResponse: the stored record receipt and revision. A problem with a receipt requires reconciliation before retrying."
                        : "DocumentPinResponse: no record or revision, with the refusal and findings.",
                    ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/DocumentPinResponse" } } },
                };
            responses["400"] = new JsonObject { ["description"] = "The request cannot bind to DocumentPinRequest." };
        }
        else if (path == "/v1/operator/actions")
        {
            responses["200"] = new JsonObject { ["description"] = "OperatorActionsResponse: text in the selected communication settings, complete canonical JSON (vericairn.operator-actions/1), and hasErrors. Items retain their sources, reasons, waitsOn, inspection command and record-authoring commands with argument arrays and required JSON bodies; null body fields require operator input. No signing or provider access occurs." };
        }
        else if (path == "/v1/operator/record")
        {
            responses["200"] = new JsonObject { ["description"] = "OperatorRecordResponse: the signed record, retained branch and head, last recorded change and exact recovery step. Only ok with completed means the pinned merge and target checks passed.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/OperatorRecordResponse" } } } };
        }
        else if (path is "/v1/pairings" or "/v1/pairing/hello" or "/v1/pairing/confirm" or "/v1/runtime/identity" or "/v1/runtime/certificate")
        {
            DaemonResponses(path, responses);
        }
        else if (method == "POST" && path.StartsWith("/v1/packages/", StringComparison.Ordinal))
        {
            responses["200"] = new JsonObject { ["description"] = "PackagesResponse: exact registry observations, or the original account-holder intent with its latest outcome and manual step.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/PackagesResponse" } } } };
        }
        else if (method == "POST" && path.StartsWith("/v1/changes/", StringComparison.Ordinal))
        {
            responses["200"] = new JsonObject { ["description"] = "ChangeResponse: recorded intent, observed effect and exact-head evidence; uncertainty never authorizes retry.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/ChangeResponse" } } } };
        }
        else if (method == "POST" && path is "/v1/releases/withdraw" or "/v1/work/{key}/move-plan")
        {
            string schema = path == "/v1/releases/withdraw" ? "ReleaseResponse" : "WorkMovePlanResponse";
            responses["200"] = new JsonObject
            {
                ["description"] = "The complete result, including refusal findings or receipts from an interrupted batch. No records are stored on a stale preparation or validation refusal.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/" + schema } } },
            };
        }
        else if (method == "POST" && path.StartsWith("/v1/releases/", StringComparison.Ordinal))
        {
            responses["200"] = new JsonObject { ["description"] = "ReleaseResponse: what was recorded and what the provider showed; a problem inside when nothing could proceed." };
        }
        else if (method == "POST" && path == "/v1/work/progress")
        {
            responses["200"] = new JsonObject { ["description"] = "WorkProgressResponse: the latest agent claim, a written record only for a completed phase, or a problem when refused." };
        }
        else if (method == "POST" && path.StartsWith("/v1/runs", StringComparison.Ordinal))
        {
            responses["200"] = new JsonObject { ["description"] = path == "/v1/runs" ? "RunResponse: what was recorded and launched; a problem inside when nothing was launched." : "RunStopResponse: the stop's facts as observed; a problem inside when no stop was asked." };
            responses.Remove("404");
        }
        else if (method == "POST" && path is "/v1/goals/{id}/step" or "/v1/work/takeover")
        {
            responses["200"] = new JsonObject { ["description"] = path == "/v1/work/takeover" ? "TakeoverResponse: the hand-over recorded and the stops asked; a problem inside when nothing was recorded." : "GoalStepResponse: what was recorded and started; a problem inside when nothing was dispatched." };
            responses.Remove("404");
        }
        else if (method == "POST" && path != "/v1/shutdown" && path != "/v1/objects" && path != "/v1/estate/upgrade" && path != "/v1/estate/export")
        {
            responses["200"] = new JsonObject
            {
                ["description"] = "WriteOutcome with written (path, id, hash): the record was stored. An interrupted companion publication also carries a reconcile-required problem with its recovery action; retain both and never resubmit the stored record.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = "#/components/schemas/WriteOutcome" } } },
            };
            responses["404"] = new JsonObject { ["description"] = "The subject named does not exist (WriteOutcome with a not-found problem)." };
            responses["422"] = new JsonObject { ["description"] = "Refused or rejected (WriteOutcome with written null and a problem; a rejection carries findings). A reconcile-required problem without written holds further writes until recovery." };
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

        responses["401"] ??= new JsonObject { ["description"] = "On loopback: the request is not signed by a device admitted now. On the local network: also when the device is not permitted that profile, the connection's client certificate is not one its key issued, the request names another principal, or it acts on the runtime's own machine (Problem with code denied)." };
        return responses;
    }

    /// <summary>The answers of the routes only a running daemon serves, from 0.6.0.</summary>
    private static void DaemonResponses(string path, JsonObject responses)
    {
        switch (path)
        {
            case "/v1/pairings":
                responses["200"] = new JsonObject { ["description"] = "PairingIssued: the code, shown once, with the principal, the profiles, the expiry, the attempts, the address and the runtime identity's fingerprint." };
                responses["409"] = new JsonObject { ["description"] = "No local-network profile is served, or too many codes wait (Problem)." };
                responses["422"] = new JsonObject { ["description"] = "Refused: an unknown principal or profile, no administrator's authority, or no admitted device of the administrator here to sign the admission (Problem)." };
                break;
            case "/v1/pairing/hello":
                responses["200"] = new JsonObject { ["description"] = "PairingChallenge: the runtime's identity, the certificate on this connection and the device record to sign, with the runtime's MAC; uses one of the code's attempts." };
                responses["401"] = new JsonObject { ["description"] = "The code is unknown, expired, used or out of attempts, or the request did not come over the local network (Problem with code denied)." };
                responses["422"] = new JsonObject { ["description"] = "A hello without a handle or a nonce (Problem)." };
                break;
            case "/v1/pairing/confirm":
                responses["200"] = new JsonObject { ["description"] = "PairingCompleted: the device record and the admission, with the runtime's MAC over the exchange." };
                responses["401"] = new JsonObject { ["description"] = "No such exchange is open, the code expired or was used, or the confirmation does not match the code (Problem with code denied); nothing is recorded." };
                responses["422"] = new JsonObject { ["description"] = "The device's signature does not verify, or the estate refused its records (Problem, with findings when rejected); nothing is recorded." };
                break;
            case "/v1/runtime/identity":
                responses["200"] = new JsonObject { ["description"] = "RuntimeIdentityProof: the identity key's signature over the estate, the certificate on this connection and the nonce." };
                responses["404"] = new JsonObject { ["description"] = "The runtime has no identity: it serves no local-network profile (Problem)." };
                responses["422"] = new JsonObject { ["description"] = "No nonce of 16 to 64 bytes (Problem)." };
                break;
            default:
                responses["200"] = new JsonObject { ["description"] = "CertificateRotated: the new certificate's fingerprint and expiry, the replaced one's, whether open connections were closed, and the new identity's fingerprint when it was replaced." };
                responses["409"] = new JsonObject { ["description"] = "The daemon serves no TLS profile, or no identity to replace (Problem)." };
                break;
        }
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

        JsonObject policyHashRequest = Object(("body", "object"));
        policyHashRequest["required"] = new JsonArray("body");
        JsonObject policyHashResponse = Object(("definitionHash", "string"), ("problem", "object"));
        policyHashResponse["required"] = new JsonArray("definitionHash", "problem");
        policyHashResponse["properties"]!["definitionHash"]!["nullable"] = true;
        policyHashResponse["properties"]!["problem"]!["nullable"] = true;

        JsonObject operatorRecordRequest = Object(("type", "string"), ("by", "string"), ("body", "object"), ("subject", "string"),
            ("supersedes", "string"), ("product", "string"), ("waitSeconds", "integer"), ("reconciliation", "string"));
        operatorRecordRequest["required"] = new JsonArray("type", "by", "body");
        operatorRecordRequest["properties"]!["waitSeconds"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 240, ["default"] = 120 };

        return new JsonObject
        {
            ["CompanionRequest"] = Object(("generator", "string"), ("selection", "string"), ("revision", "string"), ("snapshot", "string"), ("page", "integer"), ("field", "string"), ("index", "string"), ("audience", "string"), ("contract", "integer"), ("declaration", "string"), ("commit", "string")),
            ["CompanionCompareRequest"] = Object(("selection", "string"), ("commit", "string"), ("audience", "string")),
            ["CompanionsResponse"] = Object(("version", "integer"), ("status", "string"), ("snapshot", "string"), ("manifestSnapshot", "string"), ("coverage", "array"), ("generators", "array"), ("destinations", "array"), ("reason", "string")),
            ["CompanionResponse"] = Object(("version", "integer"), ("status", "string"), ("currentness", "string"), ("completeness", "string"), ("audience", "string"), ("snapshot", "string"), ("commit", "string"), ("generator", "object"), ("sources", "array"), ("inputDigest", "string"), ("contentHash", "string"), ("text", "string"), ("reason", "string")),
            ["CompanionComparison"] = Object(("version", "integer"), ("status", "string"), ("currentness", "string"), ("snapshot", "string"), ("commit", "string"), ("differences", "array"), ("problems", "array")),
            ["CompanionSource"] = Object(("kind", "string"), ("identity", "string"), ("hash", "string"), ("path", "string"), ("repository", "string"), ("recordType", "string"), ("at", "string"), ("current", "boolean"), ("bytes", "integer")),
            ["PolicyChangeRequest"] = Object(("by", "string"), ("body", "object"), ("expected", "string")),
            ["PolicyHashRequest"] = policyHashRequest,
            ["PolicyHashResponse"] = policyHashResponse,
            ["WritingExceptionRemoveRequest"] = Object(("by", "string"), ("expected", "string"), ("reason", "string"), ("basis", "object")),
            ["WorkPlaceRequest"] = Object(("by", "string"), ("expected", "string"), ("expectedSelection", "string"), ("releasePlan", "string"), ("backlog", "string")),
            ["CommunicationSetRequest"] = Object(("by", "string"), ("scope", "string"), ("values", "object"), ("recipient", "string"), ("assignment", "string"), ("expected", "string"), ("authority", "string"), ("basis", "string")),
            ["CommunicationDelegateRequest"] = Object(("by", "string"), ("recipient", "string"), ("grantee", "string"), ("scope", "string"), ("assignment", "string"), ("status", "string"), ("expected", "string"), ("expires", "string")),
            ["CommunicationProfile"] = Object(("recipient", "string"), ("assignment", "string"), ("fields", "object"), ("revisions", "array"), ("conflicts", "array"), ("observedAt", "string"), ("revisionHash", "string"), ("unavailable", "string")),
            ["EstateObserveRequest"] = Object(("by", "string")),
            ["HandoffPackage"] = Object(("hash", "string"), ("bytes", "integer"), ("payload", "object")),
            ["NewRecordRequest"] = Object(("type", "string"), ("by", "string"), ("body", "object"), ("subject", "string"), ("supersedes", "string"), ("at", "string"), ("id", "string"), ("expected", "string")),
            ["AssignRequest"] = Object(("workOrder", "string"), ("contributor", "string"), ("by", "string"), ("brief", "object"), ("note", "string")),
            ["AttemptRequest"] = Object(("assignment", "string"), ("by", "string"), ("workspace", "string"), ("note", "string"), ("at", "string")),
            ["WorkProgressRequest"] = Object(("attempt", "string"), ("by", "string"), ("phase", "string"), ("note", "string"), ("completed", "boolean")),
            ["ResultRequest"] = Object(("attempt", "string"), ("by", "string"), ("body", "object"), ("expected", "string")),
            ["ResultReviewRequest"] = Object(("result", "string"), ("by", "string"), ("body", "object"), ("expected", "string")),
            ["AmendRequest"] = Object(("assignment", "string"), ("by", "string"), ("body", "object")),
            ["DeviceCreateRequest"] = Object(("principal", "string"), ("name", "string"), ("custody", "string"), ("profiles", "array"), ("presence", "boolean")),
            ["AdmitRequest"] = Object(("device", "string"), ("by", "string"), ("note", "string"), ("recovery", "string"), ("passphrase", "string")),
            ["DeviceRenameRequest"] = Object(("device", "string"), ("name", "string")),
            ["DeviceRotateRequest"] = Object(("device", "string"), ("custody", "string"), ("presence", "boolean")),
            ["RevokeRequest"] = Object(("device", "string"), ("by", "string"), ("reason", "string"), ("recovery", "string"), ("passphrase", "string")),
            ["DocumentReviewRequest"] = Object(("by", "string"), ("outcome", "string"), ("summary", "string"), ("findings", "array"), ("commit", "string"), ("independence", "string")),
            ["DocumentPinRequest"] = new JsonObject
            {
                ["type"] = "object", ["required"] = new JsonArray("path", "by", "summary"),
                ["properties"] = new JsonObject
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Normalized file path under design/ or docs/ in the runtime's estate repository." },
                    ["by"] = new JsonObject { ["type"] = "string", ["description"] = "Authoring principal id." },
                    ["summary"] = new JsonObject { ["type"] = "string" },
                    ["title"] = new JsonObject { ["type"] = "string", ["nullable"] = true, ["description"] = "Defaults to the current title or the first pin's file name without extension." },
                    ["expected"] = new JsonObject { ["type"] = "string", ["nullable"] = true, ["description"] = "Expected current pin record id; a mismatch writes nothing." },
                },
            },
            ["DocumentPinResponse"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["written"] = new JsonObject { ["type"] = "object", ["nullable"] = true, ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject { ["type"] = "string" }, ["id"] = new JsonObject { ["type"] = "string" }, ["hash"] = new JsonObject { ["type"] = "string" },
                    } },
                    ["revision"] = new JsonObject { ["type"] = "integer", ["format"] = "int64", ["nullable"] = true },
                    ["problem"] = new JsonObject { ["type"] = "object", ["nullable"] = true, ["allOf"] = new JsonArray(new JsonObject { ["$ref"] = "#/components/schemas/Problem" }) },
                    ["ok"] = new JsonObject { ["type"] = "boolean" },
                },
            },
            ["GoalProposeRequest"] = Object(("by", "string"), ("body", "object"), ("supersedes", "string")),
            ["GoalActionRequest"] = Object(("by", "string"), ("note", "string")),
            ["GoalEvaluateRequest"] = Object(("by", "string"), ("summary", "string"), ("verdicts", "array")),
            ["GoalVerdict"] = Object(("condition", "integer"), ("verdict", "string"), ("reason", "string"), ("read", "array")),
            ["GoalAcceptRequest"] = Object(("by", "string"), ("statement", "string")),
            ["GoalStepRequest"] = Object(("by", "string"), ("repository", "string"), ("settings", "object"), ("wallMinutes", "integer"), ("branch", "string"), ("base", "string"), ("configuration", "string"), ("execution", "object"), ("allocation", "object"), ("contextPackage", "string")),
            ["GoalStepResponse"] = Object(("goal", "string"), ("outcome", "string"), ("message", "string"), ("ok", "boolean"), ("step", "string"), ("route", "string"), ("principal", "string"), ("assignment", "object"), ("ask", "object"), ("run", "object"), ("problem", "object")),
            ["TakeoverRequest"] = Object(("assignment", "string"), ("by", "string"), ("contributor", "string"), ("note", "string")),
            ["TakeoverResponse"] = Object(("message", "string"), ("ok", "boolean"), ("assignment", "object"), ("stops", "array"), ("problem", "object")),
            ["DeviceRecoveryRequest"] = Object(("principal", "string"), ("name", "string"), ("output", "string"), ("passphrase", "string")),
            ["ChangesPushRequest"] = Object(("product", "string"), ("branch", "string"), ("head", "string"), ("by", "string"), ("reconciliation", "string")),
            ["OperatorRecordRequest"] = operatorRecordRequest,
            ["OperatorRecordResponse"] = Object(("identity", "string"), ("status", "string"), ("message", "string"), ("ok", "boolean"), ("nextStep", "string"), ("record", "object"), ("branch", "string"), ("head", "string"), ("change", "object"), ("problem", "object")),
            ["ChangesOpenRequest"] = Object(("product", "string"), ("branch", "string"), ("head", "string"), ("title", "string"), ("body", "string"), ("by", "string"), ("reconciliation", "string")),
            ["ChangesMergeRequest"] = Object(("product", "string"), ("pullRequest", "string"), ("head", "string"), ("by", "string"), ("reconciliation", "string")),
            ["ChangesObserveRequest"] = Object(("operation", "string"), ("by", "string"), ("cancel", "string")),
            ["PackagesObserveRequest"] = Object(("by", "string"), ("product", "string"), ("version", "string"), ("operation", "string")),
            ["PackagesUnlistRequest"] = Object(("product", "string"), ("version", "string"), ("package", "string"), ("by", "string")),
            ["PackagesDeprecateRequest"] = Object(("product", "string"), ("version", "string"), ("package", "string"), ("by", "string"), ("reasons", "array"), ("message", "string"), ("alternatePackage", "string"), ("alternateRange", "string")),
            ["PackagesResponse"] = Object(("ok", "boolean"), ("status", "string"), ("message", "string"), ("packages", "array"), ("intent", "object"), ("operation", "object"), ("instruction", "string"), ("problem", "object")),
            ["ChangeResponse"] = Object(("identity", "string"), ("status", "string"), ("message", "string"), ("ok", "boolean"), ("operation", "object"), ("observation", "object"), ("ask", "object"), ("pullRequest", "string"), ("head", "string"), ("mergeCommit", "string"), ("checkStatus", "string"), ("problem", "object")),
            ["ReleaseWithdrawRequest"] = Object(("release", "string"), ("by", "string"), ("expected", "string"), ("provider", "string"), ("reason", "string"), ("settings", "object")),
            ["WorkMovePlanRequest"] = Object(("by", "string"), ("expected", "string"), ("expectedWork", "object"), ("to", "string"), ("expectedTarget", "string"), ("version", "string")),
            ["WorkMovePlanResponse"] = Object(("written", "array"), ("problem", "object"), ("ok", "boolean")),
            ["ReleaseSubmitRequest"] = Object(("product", "string"), ("version", "string"), ("repository", "string"), ("by", "string"), ("ref", "string"), ("workflow", "string"), ("reconciliation", "string"), ("provider", "string"), ("settings", "object"), ("commit", "string"), ("expected", "string")),
            ["ReleaseObserveRequest"] = Object(("version", "string"), ("repository", "string"), ("by", "string"), ("qualified", "array"), ("limitations", "array"), ("signing", "string"), ("cancel", "string"), ("product", "string"), ("provider", "string"), ("withdrawal", "string")),
            ["ProviderRow"] = Object(("provider", "string"), ("kind", "string"), ("implementation", "string"), ("contract", "string"), ("status", "string"), ("operations", "array"), ("limitations", "array"), ("settings", "array"), ("destination", "string"), ("locations", "array"), ("execution", "object")),
            ["RunStartRequest"] = Object(("assignment", "string"), ("launcher", "string"), ("agent", "string"), ("by", "string"), ("repository", "string"), ("goal", "string"), ("branch", "string"), ("base", "string"), ("wallMinutes", "integer"), ("settings", "object"), ("configuration", "string"), ("execution", "object"), ("allocation", "object"), ("contextPackage", "string")),
            ["RunStopRequest"] = Object(("by", "string"), ("reason", "string"), ("graceSeconds", "integer")),
            ["HarnessQualifyRequest"] = Object(("launcher", "string"), ("by", "string"), ("settings", "object")),
            ["RunResponse"] = Object(("run", "string"), ("status", "string"), ("message", "string"), ("ok", "boolean"), ("operation", "object"), ("cap", "object"), ("workspace", "string"), ("problem", "object")),
            ["RunStopResponse"] = Object(("run", "string"), ("state", "string"), ("message", "string"), ("ok", "boolean"), ("stop", "object"), ("problem", "object")),
            ["RunRow"] = Object(("run", "string"), ("identity", "string"), ("launcher", "string"), ("agent", "string"), ("assignment", "string"), ("goal", "string"), ("workspace", "string"), ("status", "string"), ("cap", "object"), ("usage", "object"), ("charged", "object"), ("stop", "object"), ("heartbeat", "string"), ("observed", "boolean"), ("generic", "boolean"), ("liveness", "object"), ("scope", "object"), ("signals", "array"), ("execution", "object"), ("executionObserved", "object")),
            ["RunsResponse"] = Object(("runs", "array"), ("goals", "array")),
            ["ReleaseResponse"] = Object(("identity", "string"), ("status", "string"), ("message", "string"), ("ok", "boolean"), ("operation", "object"), ("candidate", "object"), ("release", "object"), ("releaseId", "string"), ("run", "string"), ("pending", "string"), ("publish", "string"), ("publishedAt", "string"), ("url", "string"), ("problem", "object"), ("policies", "object"), ("basisEvidence", "array")),
            ["UpgradeRequest"] = Object(("version", "string"), ("by", "string"), ("check", "boolean")),
            ["ExportRequest"] = Object(("output", "string"), ("by", "string")),
            ["PairingIssueRequest"] = Object(("principal", "string"), ("by", "string"), ("profiles", "array")),
            ["PairingHello"] = Object(("handle", "string"), ("nonce", "string")),
            ["PairingConfirm"] = Object(("handle", "string"), ("runtimeNonce", "string"), ("device", "object"), ("signature", "string"), ("mac", "string")),
            ["CertificateRotateRequest"] = Object(("compromised", "boolean"), ("reason", "string"), ("identity", "boolean")),
            ["ObjectBytes"] = new JsonObject { ["type"] = "string", ["format"] = "binary" },
            ["WriteOutcome"] = Object(("written", "object"), ("problem", "object")),
            ["Problem"] = Object(("code", "string"), ("message", "string"), ("findings", "array")),
        };
    }

    private static string RequestSchema(string path) => path switch
    {
        "/v1/estate-policies" => "PolicyChangeRequest",
        "/v1/estate-policies/hash" => "PolicyHashRequest",
        "/v1/estate-policies/{key}/revisions" => "PolicyChangeRequest",
        "/v1/estate-policies/{key}/retire" => "PolicyChangeRequest",
        "/v1/estate-policies/{key}/exceptions" => "PolicyChangeRequest",
        "/v1/estate-policies/{key}/exceptions/{id}/revoke" => "PolicyChangeRequest",
        "/v1/writing-exceptions" => "PolicyChangeRequest",
        "/v1/writing-exceptions/{id}/remove" => "WritingExceptionRemoveRequest",
        "/v1/work/{key}/placement" => "WorkPlaceRequest",

        "/v1/communication" => "CommunicationSetRequest",
        "/v1/communication/delegations" => "CommunicationDelegateRequest",
        "/v1/status/observations" => "EstateObserveRequest",
        "/v1/resume" => "HandoffPackage",
        "/v1/records" => "NewRecordRequest",
        "/v1/work/assign" => "AssignRequest",
        "/v1/work/attempt" => "AttemptRequest",
        "/v1/work/progress" => "WorkProgressRequest",
        "/v1/work/result" => "ResultRequest",
        "/v1/work/review" => "ResultReviewRequest",
        "/v1/work/amend" => "AmendRequest",
        "/v1/work/takeover" => "TakeoverRequest",
        "/v1/goals/{id}/step" => "GoalStepRequest",
        "/v1/devices" => "DeviceCreateRequest",
        "/v1/devices/admit" => "AdmitRequest",
        "/v1/devices/rename" => "DeviceRenameRequest",
        "/v1/devices/rotate" => "DeviceRotateRequest",
        "/v1/devices/recovery" => "DeviceRecoveryRequest",
        "/v1/documents/{id}/reviews" => "DocumentReviewRequest",
        "/v1/documents/pin" => "DocumentPinRequest",
        "/v1/goals" => "GoalProposeRequest",
        "/v1/goals/{id}/activate" or "/v1/goals/{id}/pause" or "/v1/goals/{id}/resume" or "/v1/goals/{id}/stop" => "GoalActionRequest",
        "/v1/goals/{id}/evaluate" => "GoalEvaluateRequest",
        "/v1/goals/{id}/accept" => "GoalAcceptRequest",
        "/v1/devices/revoke" => "RevokeRequest",
        "/v1/runs" => "RunStartRequest",
        "/v1/harness-qualifications" => "HarnessQualifyRequest",
        "/v1/runs/{id}/stop" => "RunStopRequest",
        "/v1/work/{key}/move-plan" => "WorkMovePlanRequest",
        "/v1/releases/withdraw" => "ReleaseWithdrawRequest",
        "/v1/releases/submit" => "ReleaseSubmitRequest",
        "/v1/releases/observe" => "ReleaseObserveRequest",
        "/v1/changes/push" => "ChangesPushRequest",
        "/v1/operator/record" => "OperatorRecordRequest",
        "/v1/changes/open" => "ChangesOpenRequest",
        "/v1/changes/merge" => "ChangesMergeRequest",
        "/v1/changes/observe" => "ChangesObserveRequest",
        "/v1/packages/observe" => "PackagesObserveRequest",
        "/v1/packages/unlist" => "PackagesUnlistRequest",
        "/v1/packages/deprecate" => "PackagesDeprecateRequest",

        "/v1/estate/upgrade" => "UpgradeRequest",
        "/v1/estate/export" => "ExportRequest",
        "/v1/objects" => "ObjectBytes",
        "/v1/pairings" => "PairingIssueRequest",
        "/v1/pairing/hello" => "PairingHello",
        "/v1/pairing/confirm" => "PairingConfirm",
        "/v1/runtime/certificate" => "CertificateRotateRequest",
        _ => "Problem",
    };

    private static string OperationId(string method, string path) =>
        method.ToLowerInvariant() + string.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(s => char.ToUpperInvariant(s.Trim('{', '}')[0]) + s.Trim('{', '}')[1..]));

    private static string Summary(string method, string path) => (method, path) switch
    {
        ("POST", "/v1/documents/pin") => "Pin exact estate document bytes as the next revision, superseding the current pin. Unchanged bytes, generated documents, stale expected record ids and forked current pins refuse without writing. Does not edit the document.",
        ("GET", "/v1/companions") => "Installed readable coverage, exact collection snapshots and declared destinations; no adoption is inferred.",
        ("GET", "/v1/companions/render") => "Render one explicitly selected authorized item or collection; currentness describes its exact basis, not acceptance.",
        ("GET", "/v1/companions/sources") => "Read the exact permitted source basis without invoking a generator; local inspectors stay local.",
        ("GET", "/v1/companions/compare") => "Fresh byte and membership comparison without repair, generated pins or authored clearance.",
        ("GET", "/v1/estate-policies/access") => "PolicyAccess: admitted policy actions, with exact target bounds rechecked at submission.",
        ("POST", "/v1/estate-policies") => "PolicyDeclare: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/estate-policies/{key}/revisions") => "PolicyRevise: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/estate-policies/{key}/retire") => "PolicyRetire: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/estate-policies/{key}/exceptions") => "PolicyExcept: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/estate-policies/{key}/exceptions/{id}/revoke") => "PolicyRevokeException: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/writing-exceptions") => "WritingExceptionAdd: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/writing-exceptions/{id}/remove") => "WritingExceptionRemove: validate the expected hash and all source evidence, then sign as the acting principal.",
        ("POST", "/v1/work/{key}/move-plan") => "WorkMovePlan: compare the complete expected planned-work membership and all hashes, validate every successor, then store the batch. No writes on a conflict or validation refusal.",
        ("POST", "/v1/releases/withdraw") => "ReleaseWithdraw: validate the exact candidate withdrawal and draft-deletion intent together. Deletion remains the operator's step.",
        ("POST", "/v1/work/{key}/placement") => "WorkPlace: validate the expected hash and all source evidence, then sign as the acting principal.",

        ("GET", "/v1/estate-policies") => "PolicyResponse: declarations, exception history and findings at recorded time, with the explicit basis; creates no record.",
        ("GET", "/v1/estate-policies/{key}/view") => "PolicyResponse for a key or revision id, including recurring-duty deadlines, completion evidence and exceptions.",
        ("GET", "/v1/estate-policies/kinds") => "Installed kind versions, implementation hashes, supported selections, sources and bounds.",
        ("POST", "/v1/estate-policies/hash") => "PolicyHash: hash a prepared policy or duty body with only basis omitted. Reads only the supplied body, requires no write authority and writes nothing; grants no approval.",
        ("GET", "/v1/estate-policies/evaluate") => "PolicyResponse evaluated from server sources, with optional canonical UTC asOf and exact release record preview; never authorizes submission.",
        ("GET", "/v1/writing-exceptions") => "WritingExceptionsResponse: registry history, authors, reasons, scopes, counts and exact warning and excepted locations with the basis.",
        ("GET", "/v1/writing-exceptions/{id}") => "WritingExceptionsResponse for one entry identity, including removal history and current matches; creates no record.",
        ("GET", "/v1/communication") => "The effective recipient profile with every contributing revision, conflicts and freshness. Query values override this view only. No record is authored.",
        ("POST", "/v1/communication") => "Replace or reset one scoped communication profile with its expected prior hash. Only its owner or an explicit delegate may change it; stale edits return conflict.",
        ("POST", "/v1/communication/delegations") => "A person grants or revokes permission to change one scope of their communication setting, with the expected prior hash.",
        ("GET", "/v1/health") => "The daemon's identity and what it serves.",
        ("GET", "/v1/status") => "Current status: human text and canonical JSON, identical on every machine for the same records. Its compact attention references are derived from records alone.",
        ("POST", "/v1/status/observations") => "Record a person's explicit observation of estate status, with runtime-supplied timestamp and hashes of the record set, pin and status. Agents cannot author it; ordinary reads remain read-only.",
        ("POST", "/v1/resume") => "Compare a retained hand-over with current records, including every addition and supersession, missing or changed originals, the full estate pin and observed state. The world may have moved; grants no authority and restarts nothing.",
        ("GET", "/v1/handoff") => "HandoffResponse: a complete deterministic hand-over and measured package, or a problem naming every missing or invalid source. Read-only; all records retained; no synthesis or authority. Optional maxBytes refuses overflow.",
        ("GET", "/v1/context/work/{key}/brief") => "BriefResponse: measured agent brief; mandatory originals inline, named optional sources withheld unless a context allocation is given and they fit. Writes nothing.",
        ("GET", "/v1/context/work/{key}") => "ContextResponse: compute a deterministic work context package and exact originals; writes nothing.",
        ("GET", "/v1/context/original/{pin}") => "ContextReadResponse: self-contained pinned identity and exact bytes from immutable records or a matching working-tree document. Rechecks access and hashes; writes nothing.",
        ("GET", "/v1/context/{manifest}/sources/{source}") => "ContextReadResponse: legacy manifest handles return original-unavailable after access checking; prepare again for a self-contained pin.",
        ("GET", "/v1/progress") => "The progress report: where the estate stands, the work orders by state, each published release and what it contained, what comes next by release, the next eligible work and what waits on the operator; text and canonical JSON with compact current attention references. Record-derived sections agree for the same records; repository checks, latest local agent claims and worker observations are labelled separately and can differ by runtime.",
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
        ("POST", "/v1/work/progress") => "Publish the contributor's latest progress claim on its own attempt. Active notes replace local state; a completed phase is recorded once, and an identical completion retry returns its record. Claims never establish worker liveness, scope or acceptance.",
        ("POST", "/v1/work/result") => "Submit a result with exact duty proof bindings; a duty result requires the current attempt hash and an admitted signature.",
        ("GET", "/v1/release-bases/{hash}") => "Read retained exact release-admission inputs and findings; historical evidence grants no new authority.",
        ("POST", "/v1/work/review") => "Review the expected exact result revision with its evidence read, signed as the acting principal; no acceptance is recorded.",
        ("POST", "/v1/work/amend") => "Request an amendment; it authorizes nothing.",
        ("POST", "/v1/work/takeover") => "Hand a step an agent holds over to a person: a new assignment of the step to the person, carrying the agent's brief with the agent's assignment among its sources, which holds the step from then on; every run of the agent's assignment still in flight is asked to stop, each stop's facts reported as observed. Recorded by a person only, the one taking the step over, the sponsor of its goal or an operator, never by an agent.",
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
        ("GET", "/v1/asks") => "What waits on a principal (to), or on every principal: the asks still open or notified, oldest first, at most three delivered to each with the recommendation first and the consequence of leaving it unanswered, the rest waiting in order (vericairn.asks/1). Reading it answers nothing: an ask is answered by a decision of the principal it is put to that names it, never by waiting.",
        ("GET", "/v1/goals") => "The estate's current goals: each one's current revision, title, status and sponsor.",
        ("GET", "/v1/operator/actions") => "Every open operator ask, pending operator acceptance and recorded waiting reserved action, with reasons, dependencies, exact record-authoring commands and required JSON input (vericairn.operator-actions/1). Text follows communication settings, defaulting to the estate's root operator; JSON always contains the complete queue. Record errors suppress authoring commands. This read neither signs nor records, inspects keys or calls providers. Not exposed through MCP.",
        ("POST", "/v1/goals") => "Propose a goal, or a revision of one (supersedes), from the goal record's body; a proposal authorizes and funds nothing, and a revision starts again from its sponsor's activation. Anyone may propose a new goal; a revision is recorded only by the sponsor of the revision it supersedes when the sponsor is a person, and by an operator when an agent sponsors it. The body's limits.deadline and limits.consecutiveFailures are validated and recorded but not yet enforced.",
        ("GET", "/v1/goals/{id}") => "A goal by the id of any of its revisions: its objective, sponsor, status and granted budget, each condition's verdict (met, not-met or unknown) with the records it read beside its current evaluation's verdict, its steps, and what stands between it and its acceptance.",
        ("GET", "/v1/goals/{id}/next") => "A goal's steps routed from the runtime's machine (vericairn.next/1): each step's state, what holds each planned one, and for each eligible one the principal its role's roster reaches (an agent where a launcher for its harness, its admitted key and the goal's budget allow, a person where needed, a reserved action only to one who holds it) or why none is available, with whether the budget allows it and the custody guard for an agent; the goal's ledger and asks. Read-only; it never falls back to a principal the roster does not name.",
        ("POST", "/v1/goals/{id}/step") => "Take an active goal's next step, one at a time, as an operator, the goal's sponsor when a person, or a principal holding local-engineering: its first eligible step whose route acts, routed from the runtime's machine. An agent's step goes on with the assignment that holds it, or is assigned to the agent together with its run's intent after the worker's whole preflight, and its run starts through the worker; every refusal before the intent, custody, the budget, the repository, the launcher or the workspace, records nothing, the assignment included. A person's step is assigned to the person and asked of them, the recommendation first, the two recorded together; a step no principal is available for is asked of the sponsor, never given to a contributor the roster does not name. It activates, funds, accepts and answers nothing.",
        ("POST", "/v1/goals/{id}/activate") => "Activate a proposed goal, which grants its budget: by its sponsor, a person who holds the operator role or a grant of the spending scope, or by an operator when an agent sponsors it; an agent never activates a goal.",
        ("POST", "/v1/goals/{id}/pause") => "Pause an active goal; its sponsor's, or an operator's for a goal an agent sponsors. Prevents new runs but does not stop runs already in flight; request each run's stop separately.",
        ("POST", "/v1/goals/{id}/resume") => "Resume a paused, budget-limited or blocked goal, which grants its budget again; by whoever may activate it.",
        ("POST", "/v1/goals/{id}/stop") => "Stop a goal: its sponsor, or an operator for a goal an agent sponsors, ends it as abandoned and prevents new runs. Runs already in flight are not stopped; request each run's stop separately.",
        ("POST", "/v1/goals/{id}/evaluate") => "Record a goal's evaluation, condition by condition: the runtime's verdict for each record predicate and the evaluator's for each evidence statement, with the records each read, by a principal other than any step's author.",
        ("POST", "/v1/goals/{id}/accept") => "Accept a goal at its current revision: by its sponsor when the sponsor is a person, or the principal the sponsor delegated an ordinary goal's acceptance to, and by an operator, only an operator, when an agent sponsors it; never by its evaluator or a step's author, and only once its current evaluation finds every condition met and every record predicate holds.",
        ("GET", "/v1/providers") => "The provider and adapter implementations available to the runtime on its machine, with their declared settings, destinations and served locations: release providers, source adapters, coding-agent launchers and workspace providers.",
        ("GET", "/v1/launchers") => "The coding-agent launchers available to the runtime on its machine, providers of vericairn.launcher.agent/1, with their settings and limitations; a generic launcher is labeled as not complete agent support.",
        ("POST", "/v1/harness-qualifications") => "Machine-local only: an authorized human qualifies one exact installed native harness version from six local probe digests. Paired local-network devices are refused before probing; local socket and signed loopback retain their authority checks. No agent is launched and no raw probe output is recorded. Remote is not built.",
        ("GET", "/v1/runs") => "The agent runs, each with its cap recorded before it started, its observed usage once settled, what it is charged (its cap for every unit not reported, and while in flight), a stop's facts and worker-observed signals with severity and attention references; and the ledger of every goal with a run or active now.",
        ("POST", "/v1/runs") => "Start a coding agent's run on an assignment (vericairn.worker/1): refused while the goal is not active or its budget is spent, while a device of an operator or of the goal's sponsor signs without presence on the runtime's machine and the sponsor recorded no custody exception in force for the goal, and before anything is recorded when the launcher cannot run; otherwise the workspace is prepared, the intent recorded with the cap, and the harness launched, then observed and settled. The principal recording the run (by) is the operator, the goal's sponsor when the sponsor is a person, or a principal with the local-engineering scope. Nothing is accepted.",
        ("POST", "/v1/runs/{id}/stop") => "Ask a run to stop: the request, its transmission to the harness, the harness's acknowledgment, an escalation after the grace period and the observed containment are separate facts; a stop that is not observed is reported as requested.",
        ("POST", "/v1/releases/submit") => "Submit a release as intent before effect: the operation record is written, then the build is dispatched through the provider the request names or the product declares; nothing is published, and a provider with no implementation here is refused.",
        ("POST", "/v1/changes/push") => "Record intent, scan exactly the commits, then push the pinned head with a remote lease and record its observed outcome. Requires repository-write authority.",
        ("POST", "/v1/operator/record") => "Sign the operator's own record on their admitted device, commit that record alone on a new estate branch, and scan, push, open, wait and merge through recorded changes operations. Requires a clean repository-synced estate at record format 3 and declared checks. Identical input resumes the original action; uncertain effects are observed before retry. waitSeconds bounds each check phase from 0 to 240 (default 120). Synchronous only; not exposed through MCP.",
        ("POST", "/v1/changes/open") => "Record intent and open a pull request from an exact head with a recorded successful scanned push.",
        ("POST", "/v1/changes/merge") => "Record intent and checks, refuse missing or unsuccessful checks, merge pinned to the head, and observe the target run. A failure raises an operator ask.",
        ("POST", "/v1/changes/observe") => "Record an observation under an original change's provider and head; incomplete or uncertain outcomes remain uncertain.",
        ("POST", "/v1/packages/observe") => "Record nuget.org public API state and response digests for a product's packages or an original account-holder intent. An absent version is not found, never unlisted.",
        ("POST", "/v1/packages/unlist") => "Record unlisting intent and return the exact nuget.org step for the account holder. An identical rerun keeps the original intent; no registry write or API key is used.",
        ("POST", "/v1/packages/deprecate") => "Record exact deprecation intent and return the account holder's nuget.org step. Observe the original operation afterwards to record the outcome.",
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
        ("POST", "/v1/pairings") => "Issue a pairing code under an administrator's authority (the operator, or a principal with the administer scope whose admitted device here signs the admission) for a principal and the connectivity profiles its device will be permitted: 16 characters, 80 secret bits, for ten minutes, five attempts and one device. On the runtime's own machine only.",
        ("POST", "/v1/pairing/hello") => "Start a pairing over the local network with a code's handle and a nonce: the runtime answers with its identity, the certificate on this connection, the device record it will write, and its MAC with the code's runtime key.",
        ("POST", "/v1/pairing/confirm") => "Complete a pairing: the device's key, descriptive identity and signature of its record, with its MAC with the code's device key; the runtime records the device and its admission, and answers with its MAC over the whole exchange.",
        ("GET", "/v1/runtime/identity") => "The runtime identity's proof for a nonce: its signature over the estate, the certificate this connection presented and the nonce, which a paired device checks before any other request.",
        ("POST", "/v1/runtime/certificate") => "Replace the runtime certificate now: paired devices keep working, since the runtime identity signs the new one; for a compromise every other open connection is closed too, and identity replaces the runtime identity as well, after which every device pairs again. On the runtime's own machine only.",
        _ => string.Empty,
    };
}
