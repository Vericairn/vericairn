// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

/// <summary>The outcome of a tool call: text for the agent, whether it is an error, or that no such tool exists.</summary>
public sealed record ToolCallResult(string Text, bool IsError, bool Unknown = false);

/// <summary>One tool: a name, a description that describes and never directs, an input schema, and the call it makes through the API.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonObject InputSchema, bool Writes, Func<IEstateApi, JsonObject?, ToolCallResult> Invoke)
{
    /// <summary>The tool as tools/list presents it.</summary>
    public JsonObject Describe() => new() { ["name"] = Name, ["description"] = Description, ["inputSchema"] = InputSchema.DeepClone() };
}

/// <summary>
/// The tools an estate offers an agent. Reads are always visible. Writes are visible only when the acting principal has an
/// admitted device whose key this machine holds, because that is what the runtime will sign with; a tool the connection
/// cannot use is absent, not refused.
/// </summary>
public sealed class ToolRegistry
{
    private readonly IEstateApi api;
    private readonly string? actingPrincipal;
    private readonly List<ToolDefinition> tools;

    /// <summary>A registry over an open estate for an acting principal (null for a read-only connection).</summary>
    public ToolRegistry(IEstateApi api, string? actingPrincipal)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.actingPrincipal = string.IsNullOrWhiteSpace(actingPrincipal) ? null : actingPrincipal;
        tools = Define();
    }

    /// <summary>The principal writes are authored by, or null for a read-only connection.</summary>
    public string? ActingPrincipal => actingPrincipal;

    /// <summary>Every tool the registry knows, visible or not.</summary>
    public IReadOnlyList<ToolDefinition> All => tools;

    /// <summary>Whether write tools are visible now: the acting principal has an admitted device held on this machine.</summary>
    public bool WritesVisible()
    {
        if (actingPrincipal is null)
        {
            return false;
        }

        try
        {
            return api.Devices().Any(d => d.Principal == actingPrincipal && d.HeldHere && d.Admission.StartsWith("admitted", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            return false;
        }
    }

    /// <summary>The tools this connection may use.</summary>
    public IReadOnlyList<ToolDefinition> Visible()
    {
        bool writes = WritesVisible();
        return tools.Where(t => !t.Writes || writes).ToList();
    }

    /// <summary>Calls a visible tool; an invisible or unknown tool is unknown to the caller.</summary>
    public ToolCallResult Call(string name, JsonObject? arguments)
    {
        ToolDefinition? tool = Visible().FirstOrDefault(t => t.Name == name);
        if (tool is null)
        {
            return new ToolCallResult(string.Empty, true, Unknown: true);
        }

        try
        {
            return tool.Invoke(api, arguments);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException or UnauthorizedAccessException)
        {
            return new ToolCallResult(exception.Message, true);
        }
    }

    private List<ToolDefinition> Define()
    {
        string by = actingPrincipal ?? string.Empty;
        return
        [
            Read("estate_health", "The runtime's identity and what it serves: API version, Vericairn version, estate, profile and record format.", NoArguments(),
                (api, _) => Json(api.Health())),
            Read("estate_status", "The estate's current state derived from its records, as the canonical status document: work orders, releases, assignments, operations and findings. Identical on every machine for the same records.", NoArguments(),
                (api, _) => new ToolCallResult(api.Status().Json.GetRawText(), false)),
            Read("estate_check", "Validation of every record plus the repository cleanliness and portability checks: the findings with their counts.", NoArguments(),
                (api, _) => Json(api.Check())),
            Read("work_brief", "The brief of a work order, by key or record id: everything a contributor needs, from the records alone, as Markdown; includes the latest result's next step when one exists.", Arguments(("workOrder", "Work-order key, for example M2-API, or its record id.")), "workOrder",
                (api, a) => { BriefResponse? b = api.Brief(Arg(a, "workOrder")); return b is null ? NotFound("work order", Arg(a, "workOrder")) : new ToolCallResult(b.Markdown, false); }),
            Read("work_observe", "What an observer sees of a work order: its assignments, attempts, results, amendments, reviews and acceptances.", Arguments(("workOrder", "Work-order key or record id.")), "workOrder",
                (api, a) => { ObserveResponse? o = api.Observe(Arg(a, "workOrder")); return o is null ? NotFound("work order", Arg(a, "workOrder")) : new ToolCallResult(o.Text, false); }),
            Read("record_get", "One record by id: its envelope with type, author, subject, hash, signatures and body, and whether it is current.", Arguments(("id", "Record id (ULID).")), "id",
                (api, a) => { RecordResponse? r = api.Record(Arg(a, "id")); return r is null ? NotFound("record", Arg(a, "id")) : Json(r); }),
            Read("decisions_list", "The estate's current decisions, rendered as a list.", NoArguments(),
                (api, _) => new ToolCallResult(api.Decisions().Text, false)),
            Read("decision_get", "One decision by the id of any of its revisions: the operator's statement, the summary, the synthesis and the effects.", Arguments(("id", "Decision record id.")), "id",
                (api, a) => { DecisionResponse? d = api.Decision(Arg(a, "id")); return d is null ? NotFound("decision", Arg(a, "id")) : new ToolCallResult(d.Text, false); }),
            Read("releases_list", "The current release records: version, status, commit, id and signing state.", NoArguments(),
                (api, _) => Json(api.Releases())),
            Read("devices_list", "The devices known to the estate: principal, name, platform, custody, admission state and whether this machine holds their key.", NoArguments(),
                (api, _) => Json(api.Devices())),
            Write("work_attempt", "Records the start of an attempt by the acting principal on an assignment it holds: where the work happens, named without machine paths, and what the contributor intends. The runtime signs the record with the acting principal's admitted device on this machine and refuses it when the estate would be invalid.",
                Arguments(("assignment", "Assignment record id."), ("workspace", "Where the work happens: a repository and branch, a clone, a tool; never a machine path."), ("note", "What the contributor intends to do.")), ["assignment", "workspace", "note"],
                (api, a) => Outcome(api.Attempt(new AttemptRequest(Arg(a, "assignment"), by, Arg(a, "workspace"), Arg(a, "note"))))),
            Write("work_result", "Submits the result of an attempt: outcome (completed, partial, blocked, abandoned), summary, outputs by hash, evidence, checks, limitations, unresolved effects and the next step. A claim to inspect; acceptance is a separate record by someone with that authority.",
                Arguments(("attempt", "Attempt record id."), ("body", "The result body as an object: outcome, summary, outputs, evidence, checks, limitations, unresolvedEffects, nextStep.")), ["attempt", "body"],
                (api, a) => Outcome(api.Result(new ResultRequest(Arg(a, "attempt"), by, Body(a, "body")))), ["body"]),
            Write("work_amend", "Requests a change to an assignment's outcome, scope, target, budget or permitted effects. The request authorizes nothing; a decision admits or declines it.",
                Arguments(("assignment", "Assignment record id."), ("body", "The amendment body as an object.")), ["assignment", "body"],
                (api, a) => Outcome(api.Amend(new AmendRequest(Arg(a, "assignment"), by, Body(a, "body")))), ["body"]),
        ];
    }

    private static ToolDefinition Read(string name, string description, JsonObject schema, Func<IEstateApi, JsonObject?, ToolCallResult> invoke) => new(name, description, schema, false, invoke);

    private static ToolDefinition Read(string name, string description, JsonObject schema, string required, Func<IEstateApi, JsonObject?, ToolCallResult> invoke)
    {
        schema["required"] = new JsonArray(required);
        return new ToolDefinition(name, description, schema, false, invoke);
    }

    private static ToolDefinition Write(string name, string description, JsonObject schema, string[] required, Func<IEstateApi, JsonObject?, ToolCallResult> invoke, string[]? objectProperties = null)
    {
        schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        foreach (string property in objectProperties ?? [])
        {
            ((JsonObject)schema["properties"]![property]!)["type"] = "object";
        }

        return new ToolDefinition(name, description, schema, true, invoke);
    }

    private static JsonObject NoArguments() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };

    private static JsonObject Arguments(params (string Name, string Description)[] arguments)
    {
        var properties = new JsonObject();
        foreach ((string name, string description) in arguments)
        {
            properties[name] = new JsonObject { ["type"] = "string", ["description"] = description };
        }

        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
    }

    private static string Arg(JsonObject? arguments, string name)
    {
        if (arguments?[name] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw new ArgumentException($"'{name}' is required.");
    }

    private static JsonElement Body(JsonObject? arguments, string name)
    {
        if (arguments?[name] is JsonObject body)
        {
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return document.RootElement.Clone();
        }

        throw new ArgumentException($"'{name}' must be an object.");
    }

    private static ToolCallResult Json<T>(T value) => new(JsonSerializer.Serialize(value, ProtocolJson.Options), false);

    private static ToolCallResult NotFound(string what, string id) => new($"No {what} '{id}' in this estate.", true);

    private static ToolCallResult Outcome(WriteOutcome outcome) => new(JsonSerializer.Serialize(outcome, ProtocolJson.Options), !outcome.Ok);
}
