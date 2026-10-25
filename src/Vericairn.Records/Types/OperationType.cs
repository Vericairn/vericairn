// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// An intended external effect under an original identity: the intent is recorded before anything is dispatched, every change
/// of state is a superseding revision, and an uncertain outcome is reconciled under the same identity before any retry.
/// </summary>
public sealed class OperationType : IRecordType
{
    /// <summary>The states of an external operation, in the product's state language.</summary>
    public static readonly ImmutableArray<string> Statuses = ["not-started", "dispatched", "observed-success", "known-failure", "canceled", "uncertain"];

    private static readonly Dictionary<string, string[]> Transitions = new(StringComparer.Ordinal)
    {
        ["not-started"] = ["dispatched", "known-failure", "canceled", "uncertain"],
        ["dispatched"] = ["observed-success", "known-failure", "uncertain"],
        ["uncertain"] = ["observed-success", "known-failure", "dispatched", "canceled", "uncertain"],
        ["known-failure"] = ["dispatched", "uncertain", "known-failure", "canceled"],
        ["canceled"] = ["dispatched", "uncertain"],
        ["observed-success"] = [],
    };

    /// <inheritdoc />
    public string Name => "operation";

    /// <inheritdoc />
    public string Summary => "An intended external effect with an original identity: recorded as not-started before any dispatch, then dispatched, observed-success, known-failure, canceled or uncertain, each as a superseding revision. One current operation per identity; a retry after an uncertain or failed outcome names its reconciliation. The transport response is not the effect.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        SavedPrompts.UseField,
        new("provider", "string", true, "The provider the operation goes through, for example github-releases."),
        new("action", "string", true, "The declared operation, for example dispatch-release-workflow."),
        new("identity", "string", true, "The original identity the effect is reconciled under; unique among current operations."),
        new("intent", "string", true, "The intended effect, in a sentence."),
        new("target", "object", true, "Where the effect lands: repository, workflow, ref, version, product; for an agent run, its assignment, its principal, its workspace (a repository and branch, never a machine path), the goal it is charged to, the worker contract and, for a run launched under it, the sponsor's custody exception (custodyException); string values only. Format-3 package actions pin provider, product, productHash, package and version, with either release and releaseHash or reserved (the same version declared reserved in that exact product, with no release pins); the subject pins that release or product declaration."),
        new("status", string.Join('|', Statuses), true, "not-started, dispatched, observed-success, known-failure, canceled or uncertain."),
        new("dispatchedAt", "timestamp", false, "When the dispatch was made; required once dispatched. Account-holder package actions never claim a dispatch and omit this field."),
        new("observed", "object", false, "summary, at, optional url and reference: what was observed; required for observed-success and known-failure."),
        new("reconciliation", "string", false, "How an uncertain or failed outcome was settled before a retry; required when a retry is dispatched."),
        new("evidence", "object[]", true, "summary, optional hash and location; may be empty."),
        new("checks", "object[]", false, "For repository changes: name, head, status and optional conclusion for every reported check. A merge admits only successful checks of its exact head.", 3),
        new("requiredChecks", "string[]", false, "The exact check names from the pinned product declaration; none guessed.", 3),
        new("scan", "object", false, "For a push: tool, version, range, verdict and optional findingCount. Only aggregate scan facts; never matched text, findings or process output.", 3),
        new("cap", "object", false, "For an agent-run: what the run may spend, recorded before it starts and kept in every revision; wallMinutes is required. A run that names its goal starts only while the goal is active, with the units its budget grants, within what remains of them, its concurrency and its attempts per step. Units: " + Caps.Units + ".", 2),
        new("usage", "object", false, "For an agent-run once settled: what it spent as the harness reported it, and the units its cap states that were not reported (unreported), which are charged at the cap.", 2),
        new("scope", "object", false, "Worker scope audit at settlement: changed count, outsideCount, repository-relative outside paths (at most 1000), and optional problem when incomplete. Outside changes need attention, never acceptance.", 2),
        new("liveness", "object", false, "Worker-observed event-stream liveness: stallSeconds, lastEventAt, and optional attentionAt, attention (ask id), stoppedAt. Attention at one silence limit; stop at two.", 2),
        new("signals", "object[]", false, "Worker event-stream and settlement observations: code, severity (warning|critical), at, summary, optional fingerprint, count and attention (ask id). Raw invocation inputs never enter a record.", 2),
        new("execution", "object", false, "Immutable admitted model, effort, provenance, disabled Adapter, environment inventory and context allocation, recorded before dispatch.", 2),
        new("executionObserved", "object", false, "Harness reports at settlement, with discrepancies and facts still unknown; never inferred from the request.", 2),
        new("stop", "object", false, "For an agent-run that was stopped: requestedAt and requestedBy (a principal, or worker for a cap the run reached), optional reason, then each fact once observed: transmittedAt with transmission, acknowledgedAt with acknowledgment, escalatedAt with escalation, and containedAt, only in a settled revision. A stop not observed stays requested.", 2),
    ];

    /// <summary>The operation kind of a coding agent's run on an assignment, from record format 2.</summary>
    public const string AgentRun = "agent-run";

    /// <summary>
    /// An agent run states its cap before it starts and keeps it, and its observed usage once settled, unit by unit; its intent, when it
    /// names its goal, is within the goal's budget (the agent-run ledger, <see cref="AgentRuns"/>); a stop records its facts as they were
    /// observed. Other operations carry none of these.
    /// </summary>
    private static void CheckAgentRun(ValidationContext context, string? action, string? status, BodyReader? cap, BodyReader? usage, BodyReader? stop)
    {
        if (action != AgentRun)
        {
            if (cap is not null || usage is not null)
            {
                context.Error("operation.cap", "A cap and usage describe agent runs (action agent-run).");
            }

            if (stop is not null)
            {
                context.Error("operation.stop", "A stop's facts describe agent runs (action agent-run).");
            }

            return;
        }

        if (cap is null)
        {
            context.Error("operation.cap", "An agent run records its cap before it starts, in every revision.");
        }
        else
        {
            Caps.Read(cap, "wallMinutes");
        }

        if (usage is not null)
        {
            Caps.ReadUsage(usage);
        }
        else if (status is "observed-success" or "known-failure")
        {
            context.Error("operation.usage", "A settled agent run records its usage; what the harness did not report is listed as unreported and charged at the cap.");
        }

        JsonElement target = context.Record.Body.TryGetProperty("target", out JsonElement t) && t.ValueKind == JsonValueKind.Object ? t : default;
        foreach (string key in new[] { "assignment", "principal", "workspace" })
        {
            if (target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(key, out _))
            {
                context.Error("operation.agent-run", $"An agent run's target names its {key}.");
            }
        }

        if (cap is not null)
        {
            Kept(context, cap.Element);
            if (usage is not null)
            {
                Reported(context, cap.Element, usage.Element);
            }
        }

        if (context.Record.Supersedes is string priorId && context.Find(priorId) is RecordEnvelope prior && prior.Body.TryGetProperty("stop", out JsonElement priorStop) && priorStop.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty fact in priorStop.EnumerateObject())
            {
                if (stop is null || !stop.Element.TryGetProperty(fact.Name, out JsonElement kept) || CanonicalJson.Serialize(kept) != CanonicalJson.Serialize(fact.Value))
                {
                    context.Error("operation.stop-kept", $"A run keeps its recorded stop fact '{fact.Name}' unchanged in every revision.");
                }
            }
        }

        if (stop is not null)
        {
            Stop(context, status, stop);
        }

        // The intent of a run that names its goal: the goal active, and the cap within its budget, judged from the records before it.
        bool reserves = status is "not-started" or "dispatched" or "uncertain";
        RecordEnvelope? predecessor = context.Record.Supersedes is string previousId ? context.Find(previousId) : null;
        bool alreadyReserved = predecessor is not null && EstateModel.BodyString(predecessor, "action") == AgentRun
            && EstateModel.BodyString(predecessor, "status") is "not-started" or "dispatched" or "uncertain"
            && predecessor.Body.TryGetProperty("target", out JsonElement previousTarget)
            && target.ValueKind == JsonValueKind.Object && CanonicalJson.Serialize(previousTarget) == CanonicalJson.Serialize(target);
        if (reserves && !alreadyReserved && cap is not null && target.ValueKind == JsonValueKind.Object && target.TryGetProperty("goal", out JsonElement named) && named.ValueKind == JsonValueKind.String)
        {
            RecordEnvelope? goal = AgentRuns.GoalAt(context.Model, named.GetString()!, context.Record);
            if (goal is null)
            {
                context.Error("operation.goal", $"An agent run's target names goal {named.GetString()}, which is no goal of this estate written before the run.");
                return;
            }

            string? assignment = target.TryGetProperty("assignment", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            foreach ((string code, string message) in AgentRuns.StartRefusals(context.Model, goal, assignment, cap.Element, context.Record))
            {
                context.Error(code, message);
            }

            // A run launched under the sponsor's custody exception names one that covered the goal's revision when its intent was
            // recorded, judged as of then: neither expired nor replaced by a revision recorded before the intent.
            if (target.TryGetProperty("custodyException", out JsonElement excepted) && excepted.ValueKind == JsonValueKind.String
                && (context.Find(excepted.GetString()) is not RecordEnvelope exception || !CustodyExceptions.CoveredWhen(context.Model, exception, goal, context.Record)))
            {
                context.Error("operation.custody", $"The run names custody exception {excepted.GetString()}, which did not cover goal {goal.Id}'s revision when the run was recorded: an exception is recorded by who decides for the goal, about its exact revision, is in force until its expiry, and a revision of it replaces it.");
            }
        }
        else if (context.Record.Supersedes is null && target.ValueKind == JsonValueKind.Object && target.TryGetProperty("custodyException", out _))
        {
            context.Error("operation.custody", "A run names a custody exception only with the goal it is charged to, whose exception it is.");
        }
    }

    /// <summary>
    /// True for an agent run, from record format 2, recorded by who decides for the goal it is charged to when that is a person, its
    /// sponsor: taking a step of the goal under its activation, which granted the budget the run spends (design/goals.md §§ 6 and 9). The
    /// goal is judged at its revision when the record was written.
    /// </summary>
    private static bool SponsorsRun(ValidationContext context)
    {
        if (context.Format < 2 || EstateModel.BodyString(context.Record, "action") != AgentRun
            || !context.Record.Body.TryGetProperty("target", out JsonElement target) || target.ValueKind != JsonValueKind.Object
            || !target.TryGetProperty("goal", out JsonElement named) || named.ValueKind != JsonValueKind.String
            || AgentRuns.GoalAt(context.Model, named.GetString()!, context.Record) is not RecordEnvelope goal)
        {
            return false;
        }

        GoalRules.Accepter decider = GoalRules.AccepterOf(context.Model, goal, context.Record);
        return decider.Principal is not null && decider.Principal == context.Record.By;
    }

    /// <summary>A revision keeps its intent's cap and target: the allowance and what it funds are fixed before launch.</summary>
    private static void Kept(ValidationContext context, JsonElement cap)
    {
        if (context.Record.Supersedes is null)
        {
            return;
        }

        RecordEnvelope intent = context.Model.ById.TryGetValue(context.Model.Chain(context.Record)[0], out RecordEnvelope? first) ? first : context.Record;
        if (intent.Id != context.Record.Id && intent.Body.TryGetProperty("cap", out JsonElement recorded) && CanonicalJson.Serialize(recorded) != CanonicalJson.Serialize(cap))
        {
            context.Error("operation.cap", "An agent run keeps the cap its intent recorded in every revision; what a run may spend is fixed before it starts.");
        }

        if (intent.Id != context.Record.Id && intent.Body.TryGetProperty("target", out JsonElement target)
            && context.Record.Body.TryGetProperty("target", out JsonElement current) && CanonicalJson.Serialize(target) != CanonicalJson.Serialize(current))
        {
            context.Error("operation.agent-run-target-kept", "An agent run keeps its intent's target, including its goal and assignment; moving a reservation requires a new admitted intent.");
        }
    }

    /// <summary>A settled run can gain evidence and missing facts, but never replace the facts already recorded.</summary>
    private static void SettlementKept(ValidationContext context, RecordEnvelope previous)
    {
        foreach (JsonProperty fact in previous.Body.EnumerateObject())
        {
            // Stop facts are individually immutable, with additions checked for chronology by Stop.
            if (fact.Name == "stop")
            {
                continue;
            }

            bool kept = context.Record.Body.TryGetProperty(fact.Name, out JsonElement current);
            if (kept && fact.Name == "evidence" && fact.Value.ValueKind == JsonValueKind.Array && current.ValueKind == JsonValueKind.Array)
            {
                kept = current.GetArrayLength() >= fact.Value.GetArrayLength()
                    && fact.Value.EnumerateArray().Select(e => CanonicalJson.Serialize(e))
                        .SequenceEqual(current.EnumerateArray().Take(fact.Value.GetArrayLength()).Select(e => CanonicalJson.Serialize(e)), StringComparer.Ordinal);
            }
            else
            {
                kept = kept && CanonicalJson.Serialize(current) == CanonicalJson.Serialize(fact.Value);
            }

            if (!kept)
            {
                context.Error("operation.agent-run-settlement-kept", $"A settled agent run keeps its recorded '{fact.Name}' unchanged; evidence may only be appended and new facts must satisfy the record rules.");
            }
        }
    }

    /// <summary>A settled run's usage states, for each charged unit its cap states, what was spent or that it was not reported.</summary>
    private static void Reported(ValidationContext context, JsonElement cap, JsonElement usage)
    {
        HashSet<string> unreported = usage.TryGetProperty("unreported", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array
            ? listed.EnumerateArray().Where(u => u.ValueKind == JsonValueKind.String).Select(u => u.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
        foreach (string unit in AgentRuns.ChargedUnits.Where(u => AgentRuns.Amount(cap, u) is not null))
        {
            if (AgentRuns.Amount(usage, unit) is null && !unreported.Contains(unit))
            {
                context.Error("operation.usage", $"The run's cap states {unit}, so its usage states the {unit} spent or lists {unit} as unreported, which is charged at the cap.");
            }
        }

        if (AgentRuns.CurrencyUnit(usage) is string spent && AgentRuns.CurrencyUnit(cap) is string capped && spent != capped)
        {
            context.Error("operation.usage", $"The run's usage is in {spent}, and its cap in {capped}.");
        }
    }

    /// <summary>
    /// A stop's facts, each recorded only once observed and never before the request it answers: requested, transmitted, acknowledged,
    /// escalated, and contained, which is observed only when the run settles. A stop that was not observed stays requested.
    /// </summary>
    private static void Stop(ValidationContext context, string? status, BodyReader stop)
    {
        string? requestedAt = stop.Timestamp("requestedAt", true);
        string? requestedBy = stop.Text("requestedBy", true, 100);
        stop.Text("reason", false, 2000);
        string? transmittedAt = stop.Timestamp("transmittedAt", false);
        stop.Text("transmission", transmittedAt is not null, 2000);
        string? acknowledgedAt = stop.Timestamp("acknowledgedAt", false);
        stop.Text("acknowledgment", false, 2000);
        string? escalatedAt = stop.Timestamp("escalatedAt", false);
        stop.Text("escalation", escalatedAt is not null, 2000);
        string? containedAt = stop.Timestamp("containedAt", false);
        stop.NoOtherKeys();
        if (requestedBy is not null && requestedBy != "worker" && context.Principal(requestedBy) is null)
        {
            stop.Error("operation.stop", "requestedBy", "A stop is requested by a principal of this estate, or by the worker for a cap the run reached.");
        }

        foreach ((string name, string? at) in new[] { ("transmittedAt", transmittedAt), ("acknowledgedAt", acknowledgedAt), ("escalatedAt", escalatedAt), ("containedAt", containedAt) })
        {
            if (at is not null && requestedAt is not null && string.CompareOrdinal(at, requestedAt) < 0)
            {
                stop.Error("operation.stop", name, $"'{name}' records a fact observed after the stop was requested, at {requestedAt}.");
            }
        }

        if ((acknowledgedAt is not null || escalatedAt is not null) && transmittedAt is null)
        {
            context.Error("operation.stop", "A stop is acknowledged or escalated only once it was transmitted.");
        }

        foreach ((string name, string? at) in new[] { ("acknowledgedAt", acknowledgedAt), ("escalatedAt", escalatedAt) })
        {
            if (at is not null && transmittedAt is not null && string.CompareOrdinal(at, transmittedAt) < 0)
            {
                stop.Error("operation.stop-order", name, "Acknowledgment and escalation cannot precede transmission.");
            }
        }

        if (containedAt is not null)
        {
            foreach (string? earlier in new[] { requestedAt, transmittedAt, acknowledgedAt, escalatedAt })
            {
                if (earlier is not null && string.CompareOrdinal(containedAt, earlier) < 0)
                {
                    stop.Error("operation.stop-order", "containedAt", "Containment cannot precede any earlier stop fact.");
                }
            }

            if (context.Record.Body.TryGetProperty("observed", out JsonElement observed) && observed.ValueKind == JsonValueKind.Object
                && observed.TryGetProperty("at", out JsonElement settled) && settled.ValueKind == JsonValueKind.String
                && string.CompareOrdinal(containedAt, settled.GetString()) > 0)
            {
                stop.Error("operation.stop-order", "containedAt", "Containment cannot follow the observed settlement time.");
            }
        }

        if (containedAt is not null && status is not ("observed-success" or "known-failure"))
        {
            context.Error("operation.stop", "Containment is observed when the run settles; a run still in flight records no containment.");
        }
    }

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("provider", true, 100, Patterns.Slug, "a lower-case kebab-case provider id");
        body.Text("action", true, 100, Patterns.Slug, "a lower-case kebab-case action");
        string? identity = body.Text("identity", true, 500);
        body.Text("intent", true, 4000);
        BodyReader? target = body.Nested("target", true);
        if (target is not null)
        {
            foreach (JsonProperty property in target.Element.EnumerateObject())
            {
                target.Allow(property.Name);
                if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString()!.Length > 500)
                {
                    target.Error("body.type", property.Name, "Target values are strings of at most 500 characters.");
                }
            }

            target.NoOtherKeys();
        }

        string? status = body.Enum("status", true, Statuses);
        bool accountAction = PackageRecordRules.IsAction(context.Record);
        string? dispatchedAt = body.Timestamp("dispatchedAt", !accountAction && status is "dispatched" or "observed-success" or "uncertain");
        BodyReader? observed = body.Nested("observed", status is "observed-success" or "known-failure");
        if (observed is not null)
        {
            observed.Text("summary", true, 4000);
            observed.Timestamp("at", true);
            observed.Text("url", false, 500);
            observed.Text("reference", false, 500);
            observed.NoOtherKeys();
        }

        string? reconciliation = body.Text("reconciliation", false, 4000);
        foreach (BodyReader evidence in body.NestedArray("evidence", true, allowEmpty: true) ?? [])
        {
            evidence.Text("summary", true, 2000);
            evidence.Hash("hash", false);
            evidence.Text("location", false, 500);
            evidence.NoOtherKeys();
        }

        BodyReader? cap = body.Since(2, "cap") ? body.Nested("cap", false) : null;
        BodyReader? usage = body.Since(2, "usage") ? body.Nested("usage", false) : null;
        BodyReader? stop = body.Since(2, "stop") ? body.Nested("stop", false) : null;
        BodyReader? scope = body.Since(2, "scope") ? body.Nested("scope", false) : null;
        if (scope is not null)
        {
            long? changed = scope.Number("changed", true, 0, int.MaxValue);
            long? outside = scope.Number("outsideCount", true, 0, int.MaxValue);
            IReadOnlyList<string>? paths = scope.StringArray("outside", true, allowEmpty: true, maxItemLength: 600);
            scope.Text("problem", false, 2000);
            if (outside > changed || paths?.Count > outside || paths?.Count > 1000)
            {
                context.Error("operation.scope", "Scope counts cover every outside path, with at most one thousand names listed.");
            }

            if (EstateModel.BodyString(context.Record, "action") != AgentRun || status is not ("known-failure" or "observed-success"))
            {
                context.Error("operation.scope", "A scope audit describes a settled agent run.");
            }

            scope.NoOtherKeys();
        }

        BodyReader? liveness = body.Since(2, "liveness") ? body.Nested("liveness", false) : null;
        if (liveness is not null)
        {
            if (EstateModel.BodyString(context.Record, "action") != AgentRun)
            {
                context.Error("operation.liveness", "Event-stream liveness describes agent runs only.");
            }

            liveness.Number("stallSeconds", true, 1, 86400);
            liveness.Timestamp("lastEventAt", true);
            string? attentionAt = liveness.Timestamp("attentionAt", false);
            string? attention = liveness.Ulid("attention", false);
            string? stoppedAt = liveness.Timestamp("stoppedAt", false);
            context.RequireReference("liveness.attention", attention, "ask");
            if (stoppedAt is not null && (attentionAt is null || string.CompareOrdinal(stoppedAt, attentionAt) < 0))
            {
                context.Error("operation.liveness", "A stall stop follows its worker-observed attention fact.");
            }

            liveness.NoOtherKeys();
        }

        IReadOnlyList<BodyReader>? signals = body.Since(2, "signals") ? body.NestedArray("signals", false, allowEmpty: true) : null;
        var signalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader signal in signals ?? [])
        {
            string? code = signal.Enum("code", true, "network", "credentials", "scope", "scope-read", "audit-unverified", "burn-rate", "loop", "stall", "scope-settlement", "execution", "tool-inventory", "attempt-missing");
            string? severity = signal.Enum("severity", true, "warning", "critical");
            signal.Timestamp("at", true);
            signal.Text("summary", true, 2000);
            signal.Hash("fingerprint", false);
            signal.Number("count", false, 1, int.MaxValue);
            string? attention = signal.Ulid("attention", false);
            context.RequireReference("signals.attention", attention, "ask");
            if (context.Find(attention) is { Type: "ask" } ask && ask.Subject?.Id != context.Model.Chain(context.Record)[0])
            {
                // Settlement review asks pin the revision that produced them. Later evidence may supersede that operation
                // while retaining its immutable signals; require the pinned settlement to remain in this same run's chain.
                bool settlementReview = code is "attempt-missing" or "scope-settlement"
                    && ask.Subject is not null && context.Find(ask.Subject.Id) is { Type: "operation" } settledOperation
                    && ask.Subject == settledOperation.Reference && context.Model.Chain(context.Record).Contains(settledOperation.Id)
                    && EstateModel.BodyString(settledOperation, "status") is "observed-success" or "known-failure" or "canceled";
                if (!settlementReview) context.Error("operation.signals", "A signal's attention item names this run's intent, or for a settlement review, an exact settled revision of the same run.");
            }
            signal.NoOtherKeys();
            if (!signalKeys.Add(code + ":" + severity) || EstateModel.BodyString(context.Record, "action") != AgentRun)
            {
                context.Error("operation.signals", "Signals belong to agent runs and are recorded once per kind and severity.");
            }
        }

        ChangeOperationRules.Read(context);
        PackageRecordRules.Operation(context);
        ExecutionRecord.Read(context, status);
        SavedPrompts.ValidateUse(context);
        body.NoOtherKeys();
        if (context.Format >= 2)
        {
            CheckAgentRun(context, EstateModel.BodyString(context.Record, "action"), status, cap, usage, stop);
        }

        if (status is null || identity is null)
        {
            return;
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("operation.author", $"Principal {context.Record.By} has no principal record.");
        }
        else if (ChangeOperationRules.IsChange(context.Record))
        {
            if (!ChangeOperationRules.AuthorizedWhen(context))
                context.Error("operation.repository-write", "Repository changes require repository-write authority in force.");
        }
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.LocalEngineering) && !SponsorsRun(context))
        {
            context.Error("operation.author", "External operations are recorded by the operator or under the 'local-engineering' scope; an agent's run of a goal also by the goal's sponsor when the sponsor is a person.");
        }

        RecordEnvelope? previous = context.Record.Supersedes is null ? null : context.Find(context.Record.Supersedes);
        if (previous is null)
        {
            if (status != "not-started")
            {
                context.Error("operation.intent-first", "An operation starts as not-started: the intent is recorded before anything is dispatched; later states supersede it.");
            }
        }
        else
        {
            string previousStatus = EstateModel.BodyString(previous, "status") ?? "?";
            if (EstateModel.BodyString(previous, "action") == AgentRun && previousStatus is "known-failure" or "canceled" or "observed-success")
            {
                SettlementKept(context, previous);
            }

            if (EstateModel.BodyString(previous, "action") == AgentRun
                && (EstateModel.BodyString(context.Record, "action") != AgentRun
                    || (status == "dispatched" && previousStatus != "not-started")
                    || (previousStatus is "known-failure" or "canceled" or "observed-success" && status != previousStatus)))
            {
                context.Error("operation.agent-run-relaunch", "An agent-run identity is launched at most once and never reopens after settlement or cancellation; a retry needs a new intent and budget admission.");
            }

            if (EstateModel.BodyString(previous, "action") == AgentRun
                && (EstateModel.BodyString(previous, "dispatchedAt") is not null || previousStatus is "known-failure" or "canceled" or "observed-success")
                && dispatchedAt != EstateModel.BodyString(previous, "dispatchedAt"))
            {
                context.Error("operation.agent-run-dispatch-kept", "An agent run keeps its original dispatch timestamp; a retry requires a new admitted intent.");
            }

            if (previous.Type != Name || EstateModel.BodyString(previous, "identity") != identity || EstateModel.BodyString(previous, "provider") != EstateModel.BodyString(context.Record, "provider") || EstateModel.BodyString(previous, "action") != EstateModel.BodyString(context.Record, "action"))
            {
                context.Error("operation.identity", "A revision keeps the operation's identity and provider; a different effect is a different operation.");
            }
            else if (!(accountAction && previousStatus == "not-started" && status == "observed-success")
                && (!Transitions.TryGetValue(previousStatus, out string[]? allowed) || Array.IndexOf(allowed, status) < 0))
            {
                context.Error("operation.transition", $"An operation does not go from {previousStatus} to {status}.");
            }
            else if (previousStatus is "uncertain" or "known-failure" or "canceled" && (status == "dispatched" || (status == "uncertain" && previousStatus != "uncertain")) && string.IsNullOrWhiteSpace(reconciliation))
            {
                context.Error("operation.reconciliation", $"A retry after {previousStatus} names how the earlier outcome was reconciled under the original identity.");
            }

            if (dispatchedAt is not null && string.CompareOrdinal(dispatchedAt, previous.At) < 0 && previousStatus == "not-started")
            {
                context.Error("operation.intent-first", "The dispatch cannot precede the recorded intent.");
            }
        }

        if (context.Model.IsCurrent(context.Record) && context.Model.Current(Name).Any(other => other.Id != context.Record.Id && EstateModel.BodyString(other, "identity") == identity))
        {
            context.Error("operation.duplicate-identity", $"Another current operation already has identity {identity}; reconcile it under that identity instead of starting a second one.");
        }
    }
}
