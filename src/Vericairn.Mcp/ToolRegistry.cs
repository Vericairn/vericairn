// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

/// <summary>The outcome of a tool call: text for the agent, whether it is an error, or that no such tool exists.</summary>
public sealed record ToolCallResult(string Text, bool IsError, bool Unknown = false)
{
    /// <summary>A serving daemon release warning, separate from the tool's unchanged result text.</summary>
    public string? Warning { get; init; }
}

/// <summary>One tool: a name, a description that describes and never directs, an input schema, and the call it makes through the API.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonObject InputSchema, bool Writes, Func<IEstateApi, JsonObject?, ToolCallResult> Invoke)
{
    /// <summary>The asynchronous call, including the caller's cancellation token.</summary>
    public Func<IEstateApi, JsonObject?, CancellationToken, Task<ToolCallResult>> InvokeAsync { get; init; } =
        (api, arguments, token) => ClientOperation.RunSynchronousAsync(Name, () => Invoke(api, arguments), Writes, token);

    /// <summary>A policy action the server must admit for this acting principal.</summary>
    public string? PolicyAction { get; init; }

    /// <summary>A judgment tool visible only to the current operator, never to a worker principal.</summary>
    public bool OperatorOnly { get; init; }

    /// <summary>The tool as tools/list presents it.</summary>
    public JsonObject Describe() => new() { ["name"] = Name, ["description"] = Description, ["inputSchema"] = InputSchema.DeepClone() };
}

/// <summary>
/// The tools an estate offers an agent. Reads are always visible. Writes are visible only when the acting principal has an
/// admitted device whose key this machine holds, because that is what the runtime will sign with; a tool the connection
/// cannot use is absent, not refused.
/// </summary>
public sealed partial class ToolRegistry
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
        try { return WritesVisibleAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException or OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Checks current admission without blocking; interrupted checks are never permission to write.</summary>
    public async Task<bool> WritesVisibleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return actingPrincipal is not null && (await api.DevicesAsync(cancellationToken).ConfigureAwait(false))
            .Any(d => d.Principal == actingPrincipal && d.HeldHere && d.Admission.StartsWith("admitted", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The tools this connection may use.</summary>
    public IReadOnlyList<ToolDefinition> Visible()
    {
        try { return VisibleAsync().GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException or OperationCanceledException or TimeoutException)
        { return tools.Where(t => !t.Writes).ToList(); }
    }

    /// <summary>The tools this connection may use, after a cancellable admission check.</summary>
    public async Task<IReadOnlyList<ToolDefinition>> VisibleAsync(CancellationToken cancellationToken = default)
    {
        bool writes = await WritesVisibleAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> policyActions = writes ? await api.PolicyAccessAsync(actingPrincipal!, cancellationToken).ConfigureAwait(false) : [];
        bool isOperator = writes && await IsOperatorAsync(cancellationToken).ConfigureAwait(false);
        return tools.Where(t => !t.Writes || writes && (!t.OperatorOnly || isOperator) && (t.PolicyAction is null || policyActions.Contains(t.PolicyAction, StringComparer.Ordinal))).ToList();
    }

    /// <summary>Calls a visible tool; an invisible or unknown tool is unknown to the caller.</summary>
    public ToolCallResult Call(string name, JsonObject? arguments) => CallAsync(name, arguments).GetAwaiter().GetResult();

    /// <summary>Calls a tool through the asynchronous API, preserving timeout, cancellation and uncertain effects.</summary>
    public async Task<ToolCallResult> CallAsync(string name, JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToolDefinition? tool = tools.FirstOrDefault(t => t.Name == name);
            if (tool is null || (tool.Writes && (!await WritesVisibleAsync(cancellationToken).ConfigureAwait(false) ||
                tool.OperatorOnly && !await IsOperatorAsync(cancellationToken).ConfigureAwait(false) ||
                tool.PolicyAction is { } action && !(await api.PolicyAccessAsync(actingPrincipal!, cancellationToken).ConfigureAwait(false)).Contains(action, StringComparer.Ordinal))))
            {
                return new ToolCallResult(string.Empty, true, Unknown: true);
            }

            ToolCallResult result = await tool.InvokeAsync(api, arguments, cancellationToken).ConfigureAwait(false);
            return result with { Warning = (api as IDaemonVersion)?.DaemonVersionWarning };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException or UnauthorizedAccessException or OperationCanceledException or TimeoutException)
        {
            return new ToolCallResult(ClientFailure.FromException(exception)?.Message ?? exception.Message, true)
                { Warning = (api as IDaemonVersion)?.DaemonVersionWarning };
        }
    }

    private async Task<bool> IsOperatorAsync(CancellationToken cancellationToken)
    {
        // Principals are named by their first revision. Follow retained lineage rather than
        // trusting the roles on that old record or a role supplied by the caller.
        List<RecordResponse> matching = [];
        foreach (RecordResponse current in await api.RecordsAsync("principal", cancellationToken).ConfigureAwait(false))
        {
            RecordResponse? revision = current;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (revision is { Type: "principal" } && seen.Add(revision.Id))
            {
                if (revision.Id == actingPrincipal) { matching.Add(current); break; }
                revision = revision.Envelope.TryGetProperty("supersedes", out JsonElement prior) && prior.ValueKind == JsonValueKind.String
                    ? await api.RecordAsync(prior.GetString()!, cancellationToken).ConfigureAwait(false) : null;
            }
        }
        return matching.Count == 1 && matching[0].Envelope.GetProperty("body").TryGetProperty("roles", out JsonElement roles) &&
            roles.ValueKind == JsonValueKind.Array && roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == "operator");
    }

    private List<ToolDefinition> Define()
    {
        string by = actingPrincipal ?? string.Empty;
        return
        [
            .. CompanionTools(),
            Read("policies_list", "Policy declarations, exception history and findings at recorded time, with the explicit basis; reads create no record.", NoArguments(),
                async (api, a, token) => Json(await api.PoliciesAsync(token).ConfigureAwait(false))),
            Read("policy_kinds", "Installed policy kind versions, implementation hashes, selections, sources and bounds.", NoArguments(),
                async (api, a, token) => Json(await api.PolicyKindsAsync(token).ConfigureAwait(false))),
            Read("policy_access", "Policy write actions currently admitted for the connected principal. Exact bounds are rechecked for every write.", NoArguments(),
                async (api, a, token) => Json(await api.PolicyAccessAsync(by, token).ConfigureAwait(false))),
            Read("policy_show", "One policy by key or record id, including duty deadlines, exact evidence and exception history.", Arguments(("key", "Policy key or record id.")), "key",
                async (api, a, token) => await api.PolicyAsync(Arg(a, "key"), token).ConfigureAwait(false) is { } policy ? Json(policy) : NotFound("policy", Arg(a, "key"))),
            Read("release_basis_get", "Read the retained exact release basis, including duty evidence, sources, findings and document comparisons. Historical evidence grants no new submission authority.", Arguments(("hash", "Release-basis object hash from operation evidence.")), "hash",
                async (api, a, token) => await api.ReleaseBasisAsync(Arg(a, "hash"), token).ConfigureAwait(false) is { } basis ? Json(basis) : NotFound("release basis", Arg(a, "hash"))),
            Read("policy_evaluate", "Evaluate policies with a recorded-time basis or explicit preview. A preview cannot authorize submission.", Arguments(("asOf", "Optional canonical UTC preview time."), ("release", "Optional release record id selecting its exact product, version and commit.")),
                async (api, a, token) => { PolicyResponse result = await api.PolicyEvaluateAsync(OptionalArg(a, "asOf"), OptionalArg(a, "release"), token).ConfigureAwait(false); return new ToolCallResult(JsonSerializer.Serialize(result, ProtocolJson.Options), result.Problem is not null); }),
            Read("writing_exceptions_list", "Writing entries, removal history, match counts and exact excepted occurrences, with their basis. Reads create no record.", NoArguments(),
                async (api, a, token) => Json(await api.WritingExceptionsAsync(token).ConfigureAwait(false))),
            Read("writing_exception_show", "One writing entry by id, its removal history and current match evidence.", Arguments(("id", "Writing-exception record id.")), "id",
                async (api, a, token) => await api.WritingExceptionAsync(Arg(a, "id"), token).ConfigureAwait(false) is { } entry ? Json(entry) : NotFound("writing exception", Arg(a, "id"))),
            Read("estate_health", "The runtime's identity and what it serves: API version, Vericairn version, estate, profile and record format.", NoArguments(),
                async (api, _, token) => Json((await api.HealthAsync(cancellationToken: token).ConfigureAwait(false)))),
            Read("communication_get", "The recipient's effective communication values, every contributing revision, conflicts and freshness; shipped and estate defaults when no recipient is named. Read-only; no permission or preference is inferred.", Arguments(("recipient", "Recipient principal id; omit for estate defaults."), ("assignment", "Exact assignment id for a personal override.")),
                async (api, a, token) => Json((await api.CommunicationAsync(OptionalArg(a, "recipient"), OptionalArg(a, "assignment"), cancellationToken: token).ConfigureAwait(false)))),
            Read("estate_status", "The estate's current state derived from its records, as the canonical status document: work orders, releases, assignments, operations and findings. Identical on every machine for the same records.", NoArguments(),
                async (api, _, token) => new ToolCallResult((await api.StatusAsync(cancellationToken: token).ConfigureAwait(false)).Json.GetRawText(), false)),
            Read("estate_check", "Validation of every record plus the repository cleanliness and portability checks: the findings with their counts.", NoArguments(),
                async (api, _, token) => Json((await api.CheckAsync(cancellationToken: token).ConfigureAwait(false)))),
            Read("estate_progress", "The progress report, with record-derived sections and separately labelled latest local agent claims: where the estate stands, the work orders by state, each published release and what it contained, what comes next by release, the next eligible work and what waits on the operator, each line naming the records it rests on. As text, or as the canonical JSON when form is json. Reading it changes nothing.", Arguments(("form", "text (the default) or json.")),
                async (api, a, token) => OptionalArg(a, "form") switch
                {
                    null or "text" => new ToolCallResult((await api.ProgressAsync(cancellationToken: token).ConfigureAwait(false)).Text, false),
                    "json" => new ToolCallResult((await api.ProgressAsync(cancellationToken: token).ConfigureAwait(false)).Json.GetRawText(), false),
                    string other => new ToolCallResult($"form is text or json, not '{other}'.", true),
                }),
            Read("work_brief", "The measured brief of a work order, by key or record id: mandatory originals, the complete withheld list and the latest result's next step. Named optional sources stay withheld unless maxBytes is given and they fit. Recipient settings control the lead; reads write nothing.", Arguments(("workOrder", "Work-order key, for example M2-API, or its record id."), ("maxBytes", "Optional positive context byte allocation, as a decimal string."), ("recipient", "The person addressed by the contributor; omitted means estate defaults only."), ("assignment", "The exact assignment for the recipient override.")), "workOrder",
                async (api, a, token) => { BriefResponse? b = (await api.BriefContextAsync(Arg(a, "workOrder"), ContextLimit(a), OptionalArg(a, "recipient"), OptionalArg(a, "assignment"), cancellationToken: token).ConfigureAwait(false)); return b is null ? NotFound("work order", Arg(a, "workOrder")) : new ToolCallResult(b.Markdown, b.Problem is not null); }),
            Read("work_observe", "What an observer sees of a work order: its assignments, attempts, results, amendments, reviews and acceptances.", Arguments(("workOrder", "Work-order key or record id.")), "workOrder",
                async (api, a, token) => { ObserveResponse? o = (await api.ObserveAsync(Arg(a, "workOrder"), cancellationToken: token).ConfigureAwait(false)); return o is null ? NotFound("work order", Arg(a, "workOrder")) : new ToolCallResult(o.Text, false); }),
            Read("context_prepare", "Compute a measured deterministic context package for a work order, with exact original identities and a complete withheld list. Optional maxBytes is a context allocation, separate from a run spending cap; mandatory overflow refuses. Without maxBytes, optional bodies are withheld. Tokens remain unknown. Writes nothing.", Arguments(("workOrder", "Work-order key or record id."), ("maxBytes", "Optional positive context byte allocation, as a decimal string.")), "workOrder",
                async (api, a, token) => { ContextResponse c = (await api.ContextAsync(Arg(a, "workOrder"), ContextLimit(a), cancellationToken: token).ConfigureAwait(false)); return new ToolCallResult(c.Text, c.Problem is not null); }),
            Read("context_read", "Read the pinned original by its self-contained identity, never the latest revision. Rechecks access and verifies its hashes; returns original bytes as base64 with the same identity as the CLI and API.", ContextReadArguments(),
                async (api, a, token) => { ContextReadResponse c = (await api.ContextReadAsync(Arg(a, "pin"), cancellationToken: token).ConfigureAwait(false)); return new ToolCallResult(JsonSerializer.Serialize(c, ProtocolJson.Options), c.Problem is not null); }),
            Read("record_get", "One record by id: its envelope with type, author, subject, hash, signatures and body, and whether it is current.", Arguments(("id", "Record id (ULID).")), "id",
                async (api, a, token) => { RecordResponse? r = (await api.RecordAsync(Arg(a, "id"), cancellationToken: token).ConfigureAwait(false)); return r is null ? NotFound("record", Arg(a, "id")) : Json(r); }),
            Read("records_list", "The current records of one type, oldest first, as compact rows: id, type, hash and path; record_get fetches one in full.", Arguments(("type", "Record type, for example work-order, decision or release.")), "type",
                async (api, a, token) => Json((await api.RecordsAsync(Arg(a, "type"), cancellationToken: token).ConfigureAwait(false)).Select(r => new { r.Id, r.Type, r.Hash, r.Path }).ToList())),
            Read("decisions_list", "The estate's current decisions, rendered as a list.", NoArguments(),
                async (api, _, token) => new ToolCallResult((await api.DecisionsAsync(cancellationToken: token).ConfigureAwait(false)).Text, false)),
            Read("decision_get", "One decision by the id of any of its revisions: the operator's statement, the summary, the synthesis and the effects.", Arguments(("id", "Decision record id.")), "id",
                async (api, a, token) => { DecisionResponse? d = (await api.DecisionAsync(Arg(a, "id"), cancellationToken: token).ConfigureAwait(false)); return d is null ? NotFound("decision", Arg(a, "id")) : new ToolCallResult(d.Text, false); }),
            Read("releases_list", "The current release records: version, status, commit, id and signing state.", NoArguments(),
                async (api, _, token) => Json((await api.ReleasesAsync(cancellationToken: token).ConfigureAwait(false)))),
            Read("devices_list", "The devices known to the estate: principal, name, platform, custody, admission state and whether this machine holds their key.", NoArguments(),
                async (api, _, token) => Json((await api.DevicesAsync(cancellationToken: token).ConfigureAwait(false)))),
            Read("documents_due", "The living documents that are due or unavailable: each with its level (remind, warn or refuse), every trigger newer than its latest clearance, and the contributions no installed pack provides. A document due at refuse holds its product's next release.", NoArguments(),
                async (api, _, token) => Json((await api.DocumentsAsync(cancellationToken: token).ConfigureAwait(false)).Where(d => d.State != "current").ToList())),
            Read("goal_get", "One goal by the id of any of its revisions, in the machine form of vericairn goal show: its objective, completion conditions, constraints, sponsor, budget and status, each condition's verdict with the records it read, its current evaluation and its steps; without an id, the estate's goals with their status. Reading a goal changes nothing.", Arguments(("goal", "Goal record id (ULID); omitted, the goals are listed.")),
                async (api, a, token) => OptionalArg(a, "goal") is string id ? (await api.GoalAsync(id, cancellationToken: token).ConfigureAwait(false)) is GoalResponse goal ? new ToolCallResult(goal.Json.GetRawText(), false) : NotFound("goal", id) : Json((await api.GoalsAsync(cancellationToken: token).ConfigureAwait(false)))),
            Read("goal_progress", "A goal's progress condition by condition, as vericairn goal show prints it: each record predicate's verdict (met, not-met or unknown) with its reason, each condition's verdict in the current evaluation, the steps, who accepts the goal and what still stands between it and its acceptance, which no tool records.", Arguments(("goal", "Goal record id (ULID), any revision.")), "goal",
                async (api, a, token) => (await api.GoalAsync(Arg(a, "goal"), cancellationToken: token).ConfigureAwait(false)) is GoalResponse goal ? new ToolCallResult(goal.Text, false) : NotFound("goal", Arg(a, "goal"))),
            Read("asks_list", "What waits on a principal, or on every principal when none is named: the asks still open or notified, oldest first, at most three in one delivery to each with the recommendation first and the consequence of leaving it unanswered, the rest waiting in order. As text, or as the canonical JSON when form is json. Reading them answers nothing: an ask is answered by a decision of the principal it is put to, which no tool records.", Arguments(("to", "The principal asked (ULID); omitted, every principal."), ("form", "text (the default) or json.")),
                async (api, a, token) => OptionalArg(a, "form") switch
                {
                    null or "text" => new ToolCallResult((await api.AsksAsync(OptionalArg(a, "to"), cancellationToken: token).ConfigureAwait(false)).Text, false),
                    "json" => new ToolCallResult((await api.AsksAsync(OptionalArg(a, "to"), cancellationToken: token).ConfigureAwait(false)).Json.GetRawText(), false),
                    string other => new ToolCallResult($"form is text or json, not '{other}'.", true),
                }),
            Write("policy_declare", "PolicyDeclare: validate expected hashes and sources and sign as the acting principal, within the exact authority bounds.",
                Arguments(("expected", "Expected current target hash, or canonical body hash for a creation."), ("body", "The complete prepared record body, including its exact authority basis.")), ["expected", "body"],
                async (api, a, token) => Outcome(await api.PolicyDeclareAsync(new PolicyChangeRequest(by, Body(a, "body"), Arg(a, "expected")), cancellationToken: token).ConfigureAwait(false)), ["body"]) with { PolicyAction = "declare" },
            Write("policy_revise", "PolicyRevise: validate expected hashes and sources and sign as the acting principal, within the exact authority bounds.",
                Arguments(("key", "Policy key or id."), ("expected", "Expected current target hash, or canonical body hash for a creation."), ("body", "The complete prepared record body, including its exact authority basis.")), ["key", "expected", "body"],
                async (api, a, token) => Outcome(await api.PolicyReviseAsync(Arg(a, "key"), new PolicyChangeRequest(by, Body(a, "body"), Arg(a, "expected")), cancellationToken: token).ConfigureAwait(false)), ["body"]) with { PolicyAction = "revise" },
            Write("policy_retire", "PolicyRetire: validate expected hashes and sources and sign as the acting principal, within the exact authority bounds.",
                Arguments(("key", "Policy key or id."), ("expected", "Expected current target hash, or canonical body hash for a creation."), ("body", "The complete prepared record body, including its exact authority basis.")), ["key", "expected", "body"],
                async (api, a, token) => Outcome(await api.PolicyRetireAsync(Arg(a, "key"), new PolicyChangeRequest(by, Body(a, "body"), Arg(a, "expected")), cancellationToken: token).ConfigureAwait(false)), ["body"]) with { PolicyAction = "retire" },
            Write("policy_except", "PolicyExcept: validate expected hashes and sources and sign as the acting principal, within the exact authority bounds.",
                Arguments(("key", "Policy key or id."), ("expected", "Expected current target hash, or canonical body hash for a creation."), ("body", "The complete prepared record body, including its exact authority basis.")), ["key", "expected", "body"],
                async (api, a, token) => Outcome(await api.PolicyExceptAsync(Arg(a, "key"), new PolicyChangeRequest(by, Body(a, "body"), Arg(a, "expected")), cancellationToken: token).ConfigureAwait(false)), ["body"]) with { PolicyAction = "except" },
            Write("policy_revoke_exception", "PolicyRevokeException: validate expected hashes and sources and sign as the acting principal, within the exact authority bounds.",
                Arguments(("key", "Policy key or id."), ("id", "Exception record id."), ("expected", "Expected current target hash, or canonical body hash for a creation."), ("body", "The complete prepared record body, including its exact authority basis.")), ["key", "id", "expected", "body"],
                async (api, a, token) => Outcome(await api.PolicyRevokeExceptionAsync(Arg(a, "key"), Arg(a, "id"), new PolicyChangeRequest(by, Body(a, "body"), Arg(a, "expected")), cancellationToken: token).ConfigureAwait(false)), ["body"]) with { PolicyAction = "revoke-exception" },
            Write("writing_exception_add", "WritingExceptionAdd: validate expected hashes and sources and sign as the acting principal, within the exact authority bounds.",
                Arguments(("expected", "Expected current target hash, or canonical body hash for a creation."), ("body", "The complete prepared record body, including its exact authority basis.")), ["expected", "body"],
                async (api, a, token) => Outcome(await api.WritingExceptionAddAsync(new PolicyChangeRequest(by, Body(a, "body"), Arg(a, "expected")), cancellationToken: token).ConfigureAwait(false)), ["body"]) with { PolicyAction = "writing-add" },
            Write("writing_exception_remove", "Remove one writing entry with its expected hash and a reason, signed as the acting principal under current authority.",
                Arguments(("id", "Writing-entry record id."), ("expected", "Expected current entry hash."), ("reason", "Why this entry is removed."), ("basis", "Optional exact authority basis for this removal.")), ["id", "expected", "reason"],
                async (api, a, token) => Outcome(await api.WritingExceptionRemoveAsync(Arg(a, "id"), new WritingExceptionRemoveRequest(by, Arg(a, "expected"), Arg(a, "reason"), a?["basis"] is null ? null : Body(a, "basis")), token).ConfigureAwait(false)), ["basis"]) with { PolicyAction = "writing-remove" },
            Write("work_attempt", "Records the start of an attempt by the acting principal on an assignment it holds: where the work happens, named without machine paths, and what the contributor intends. The runtime signs the record with the acting principal's admitted device on this machine and refuses it when the estate would be invalid.",
                Arguments(("assignment", "Assignment record id."), ("workspace", "Where the work happens: a repository and branch, a clone, a tool; never a machine path."), ("note", "What the contributor intends to do.")), ["assignment", "workspace", "note"],
                async (api, a, token) => Outcome((await api.AttemptAsync(new AttemptRequest(Arg(a, "assignment"), by, Arg(a, "workspace"), Arg(a, "note")), cancellationToken: token).ConfigureAwait(false)))),
            Write("work_progress", "Publishes the contributor's latest progress claim on its own attempt. Active notes replace live state without records; a completed phase is recorded once. Claims never establish liveness, scope or acceptance.",
                Arguments(("attempt", "Attempt record id."), ("phase", "Stable lower-case phase name."), ("note", "The contributor's progress claim, without secrets or machine paths."), ("state", "active or completed.")), ["attempt", "phase", "note", "state"],
                async (api, a, token) =>
                {
                    bool completed = Arg(a, "state") switch { "active" => false, "completed" => true, _ => throw new ArgumentException("state is active or completed.") };
                    WorkProgressResponse response = (await api.WorkProgressAsync(new WorkProgressRequest(Arg(a, "attempt"), by, Arg(a, "phase"), Arg(a, "note"), completed), cancellationToken: token).ConfigureAwait(false));
                    return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), !response.Ok);
                }),
            Write("work_result", "Submits the result of an attempt: outcome (completed, partial, blocked, abandoned), summary, outputs by hash, evidence, checks, limitations, unresolved effects and the next step. A claim to inspect; acceptance is a separate record by someone with that authority.",
                Arguments(("attempt", "Attempt record id."), ("expected", "Expected current attempt hash; required when duties are supplied."), ("body", "The complete result body: outcome, summary, outputs, evidence, checks, limitations, unresolvedEffects, nextStep and optional exact duties with scope and proof bindings.")), ["attempt", "body"],
                async (api, a, token) => Outcome((await api.ResultAsync(new ResultRequest(Arg(a, "attempt"), by, Body(a, "body"), OptionalArg(a, "expected")), cancellationToken: token).ConfigureAwait(false))), ["body"]),
            Write("work_review", "Record the operator's review of an exact result and its evidence read, signed as the acting principal. Not offered to worker principals. Independence and current evidence are validated; a review accepts neither work nor a release.",
                Arguments(("result", "Exact current result id."), ("expected", "Expected current result hash."), ("body", "The ordinary review body: independence, outcome, summary, findings and exact read references.")), ["result", "expected", "body"],
                async (api, a, token) => Outcome(await api.ReviewResultAsync(new ResultReviewRequest(Arg(a, "result"), by, Body(a, "body"), Arg(a, "expected")), token).ConfigureAwait(false)), ["body"]) with { OperatorOnly = true },
            Write("document_review", "Records the acting principal's review of a living document of the estate, by its path: current (it holds), revised (it was changed for the trigger) or needs-change (it is stale and stays due). The runtime reads the content, records the covered paths' identity and the triggers answered, runs the declared checks, and refuses a clearance that a failed check or the declaration's clearedBy forbids, such as an agent clearing what it revised where independence is asked.",
                Arguments(("path", "The document's repository-relative path."), ("outcome", "current, revised or needs-change."), ("summary", "What was checked.")), ["path", "outcome", "summary"],
                async (api, a, token) => (await api.DocumentsAsync(cancellationToken: token).ConfigureAwait(false)).FirstOrDefault(d => d.Path == Arg(a, "path") && d.Repository is null) is DocumentRow row
                    ? Outcome((await api.DocumentReviewAsync(row.Declaration, new DocumentReviewRequest(by, Arg(a, "outcome"), Arg(a, "summary")), cancellationToken: token).ConfigureAwait(false)))
                    : NotFound("living document", Arg(a, "path"))),
            Write("work_amend", "Requests a change to an assignment's outcome, scope, target, budget or permitted effects. The request authorizes nothing; a decision admits or declines it.",
                Arguments(("assignment", "Assignment record id."), ("body", "The amendment body as an object.")), ["assignment", "body"],
                async (api, a, token) => Outcome((await api.AmendAsync(new AmendRequest(Arg(a, "assignment"), by, Body(a, "body")), cancellationToken: token).ConfigureAwait(false))), ["body"]),
        ];
    }

    private static ToolDefinition Read(string name, string description, JsonObject schema, Func<IEstateApi, JsonObject?, CancellationToken, Task<ToolCallResult>> invoke) => Definition(name, description, schema, false, invoke);

    private static ToolDefinition Read(string name, string description, JsonObject schema, string required, Func<IEstateApi, JsonObject?, CancellationToken, Task<ToolCallResult>> invoke)
    {
        schema["required"] = new JsonArray(required);
        return Definition(name, description, schema, false, invoke);
    }

    private static ToolDefinition Write(string name, string description, JsonObject schema, string[] required, Func<IEstateApi, JsonObject?, CancellationToken, Task<ToolCallResult>> invoke, string[]? objectProperties = null)
    {
        schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        foreach (string property in objectProperties ?? [])
        {
            ((JsonObject)schema["properties"]![property]!)["type"] = "object";
        }

        return Definition(name, description, schema, true, invoke);
    }

    private static ToolDefinition Definition(string name, string description, JsonObject schema, bool writes,
        Func<IEstateApi, JsonObject?, CancellationToken, Task<ToolCallResult>> invoke) =>
        new(name, description, schema, writes, (api, arguments) => invoke(api, arguments, CancellationToken.None).GetAwaiter().GetResult()) { InvokeAsync = invoke };

    private static JsonObject NoArguments() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };

    private static JsonObject ContextReadArguments()
    {
        JsonObject schema = Arguments(("pin", "The self-contained vc1_ identity generated by the context package."));
        schema["required"] = new JsonArray("pin");
        return schema;
    }

    private static long? ContextLimit(JsonObject? arguments)
    {
        string? text = OptionalArg(arguments, "maxBytes");
        return text is null ? null : long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long limit)
            ? limit : throw new ArgumentException("maxBytes must be a positive decimal byte allocation.");
    }

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

    private static string? OptionalArg(JsonObject? arguments, string name) =>
        arguments?[name] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

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
