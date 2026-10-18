// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An approved policy definition and its append-only lifecycle.</summary>
public sealed class EstatePolicyType : IRecordType
{
    /// <inheritdoc />
    public string Name => "estate-policy";
    /// <inheritdoc />
    public string Summary => "An owned, signed policy with explicit selection, versioned rules, level, activation and bounded exceptions; behavioral changes need exact operator approval.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("key", "slug", true, "Stable estate-unique identity key, retained after retirement.", 3),
        new("title", "string", true, "Human title.", 3),
        new("owner", "ulid", true, "Responsible principal; ownership grants no authority.", 3),
        new("reason", "string", true, "The declaration or change reason.", 3),
        new("basis", "object[]", true, "Exact decision references, optional requirement keys and the exact authority grant used by a delegate.", 3),
        new("state", "active|retired", true, "Retirement is terminal for this identity.", 3),
        new("appliesTo", "object", true, "Tagged estate, product, work-orders or repository selection.", 3),
        new("rules", "object[]", true, "Unique stable key, namespaced kind, positive version and parameters for each rule.", 3),
        new("level", "remind|warn|refuse", true, "Level of every rule.", 3),
        new("startsAt", "timestamp", true, "Activation no earlier than the first declaration.", 3),
        new("maxExceptionDays", "integer", true, "Positive maximum duration of any grant from its authoring time.", 3),
        new("predecessor", "reference", false, "Exact retired policy succeeded by a new identity and distinct key.", 3),
        new("retirement", "object", false, "Required only when retired: reason, findings disposition and optional successor identity.", 3),
        new("dutyDispositions", "object[]", false, "Exact already-due occurrence inventory ended-unmet by this approved revision, with reasons.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? key = PolicyRecords.Key(body);
        body.Text("title", true, 200);
        context.RequireReference("owner", body.Ulid("owner", true), "principal");
        body.Text("reason", true, 4000);
        string? state = body.Enum("state", true, "active", "retired");
        BodyReader? selection = body.Nested("appliesTo", true);
        PolicyRecords.Selection(context, selection);
        IReadOnlyList<BodyReader> rules = body.NestedArray("rules", true, false) ?? [];
        PolicyRecords.Unique(body, rules.Select(r => PolicyRecords.Text(r.Element, "key")), "rules");
        foreach (BodyReader rule in rules)
        {
            PolicyRecords.Key(rule);
            string? kind = rule.Text("kind", true, 200, Patterns.Contribution);
            long? version = rule.Number("version", true, 1, int.MaxValue);
            BodyReader? parameters = rule.Nested("parameters", true);
            if (kind == DutyRecords.Kind && version == 1 && parameters is not null) DutyRecords.Parameters(context, parameters, selection);
            // An unknown contribution remains intact for the evaluator to report unavailable; it is never an empty pass.
            rule.NoOtherKeys();
        }
        body.Enum("level", true, PolicyRecords.Levels);
        string? starts = body.Timestamp("startsAt", true);
        body.Number("maxExceptionDays", true, 1, 36500);
        RecordEnvelope? predecessor = PolicyRecords.Reference(context, body.Nested("predecessor", false), true, "estate-policy");
        BodyReader? retirement = body.Nested("retirement", state == "retired");
        if (retirement is not null)
        {
            retirement.Text("reason", true, 4000);
            retirement.Text("findings", true, 8000);
            string? successor = retirement.Ulid("successor", false);
            if (successor is not null) context.RequireReference("retirement.successor", successor, "estate-policy");
            retirement.NoOtherKeys();
            if (state != "retired") body.Error("policy.retirement", "retirement", "An active policy has no retirement.");
        }
        RecordEnvelope? previous = context.Find(context.Record.Supersedes);
        string action = previous is null ? "declare" : state == "retired" ? "retire" : "revise";
        bool behavior = previous is null || !PolicyRecords.SameFields(previous, context.Record, "rules", "appliesTo", "state", "level", "startsAt", "maxExceptionDays", "predecessor", "retirement") || body.Has("dutyDispositions");
        PolicyRequirements.Basis(context, action, previous, behavior);
        DutyRecords.Dispositions(context, previous);
        body.NoOtherKeys();

        string identity = PolicyRecords.Identity(context.Model, context.Record);
        RecordEnvelope first = context.Model.ById[identity];
        if (starts is not null && string.CompareOrdinal(starts, first.At) < 0) body.Error("policy.activation", "startsAt", "Activation cannot precede the declaration.");
        if (context.Model.OfType(Name).Any(p => p.Id != context.Record.Id && PolicyRecords.Identity(context.Model, p) != identity && EstateModel.BodyString(p, "key") == key)) body.Error("policy.duplicate-key", "key", "A key belongs to one identity, including after retirement.");
        if (previous is not null)
        {
            if (EstateModel.BodyString(previous, "state") == "retired") context.Error("policy.lifecycle", "Retirement is terminal; a renewed requirement needs a new identity.");
            if (!PolicyRecords.SameFields(previous, context.Record, "key", "predecessor")) context.Error("policy.identity", "The policy key and predecessor are fixed through its lineage.");
        }
        else if (state != "active") context.Error("policy.lifecycle", "A declaration starts active.");
        if (predecessor is not null && (EstateModel.BodyString(predecessor, "state") != "retired" || EstateModel.BodyString(predecessor, "key") == key)) body.Error("policy.predecessor", "predecessor", "A predecessor is an exact retired policy with a different key.");
    }
}
