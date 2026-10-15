// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
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
    public static readonly string[] Statuses = ["not-started", "dispatched", "observed-success", "known-failure", "canceled", "uncertain"];

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
        new("provider", "string", true, "The provider the operation goes through, for example github-releases."),
        new("action", "string", true, "The declared operation, for example dispatch-release-workflow."),
        new("identity", "string", true, "The original identity the effect is reconciled under; unique among current operations."),
        new("intent", "string", true, "The intended effect, in a sentence."),
        new("target", "object", true, "Where the effect lands: repository, workflow, ref, version, product; string values only."),
        new("status", string.Join('|', Statuses), true, "not-started, dispatched, observed-success, known-failure, canceled or uncertain."),
        new("dispatchedAt", "timestamp", false, "When the dispatch was made; required once dispatched."),
        new("observed", "object", false, "summary, at, optional url and reference: what was observed; required for observed-success and known-failure."),
        new("reconciliation", "string", false, "How an uncertain or failed outcome was settled before a retry; required when a retry is dispatched."),
        new("evidence", "object[]", true, "summary, optional hash and location; may be empty."),
        new("cap", "object", false, "For an agent-run: what the run may spend, recorded before it starts; wallMinutes is required. Units: " + Caps.Units + ".", 2),
        new("usage", "object", false, "For an agent-run once settled: what it spent as the harness reported it, and the units it did not report (unreported), which are charged at the cap.", 2),
    ];

    /// <summary>The operation kind of a coding agent's run on an assignment, from record format 2.</summary>
    public const string AgentRun = "agent-run";

    /// <summary>An agent run states its cap before it starts and its observed usage once settled; other operations carry neither.</summary>
    private static void CheckAgentRun(ValidationContext context, string? action, string? status, BodyReader? cap, BodyReader? usage)
    {
        if (action != AgentRun)
        {
            if (cap is not null || usage is not null)
            {
                context.Error("operation.cap", "A cap and usage describe agent runs (action agent-run).");
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

        foreach (string key in new[] { "assignment", "principal", "workspace" })
        {
            if (!context.Record.Body.TryGetProperty("target", out JsonElement target) || target.ValueKind != JsonValueKind.Object || !target.TryGetProperty(key, out _))
            {
                context.Error("operation.agent-run", $"An agent run's target names its {key}.");
            }
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
        string? dispatchedAt = body.Timestamp("dispatchedAt", status is "dispatched" or "observed-success" or "uncertain");
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
        body.NoOtherKeys();
        if (context.Format >= 2)
        {
            CheckAgentRun(context, EstateModel.BodyString(context.Record, "action"), status, cap, usage);
        }

        if (status is null || identity is null)
        {
            return;
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("operation.author", $"Principal {context.Record.By} has no principal record.");
        }
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.LocalEngineering))
        {
            context.Error("operation.author", "External operations are recorded by the operator or under the 'local-engineering' scope.");
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
            if (previous.Type != Name || EstateModel.BodyString(previous, "identity") != identity || EstateModel.BodyString(previous, "provider") != EstateModel.BodyString(context.Record, "provider"))
            {
                context.Error("operation.identity", "A revision keeps the operation's identity and provider; a different effect is a different operation.");
            }
            else if (!Transitions.TryGetValue(previousStatus, out string[]? allowed) || Array.IndexOf(allowed, status) < 0)
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
