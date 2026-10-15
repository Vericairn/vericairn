// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>The discharge of an obligation a decision left, from record format 2 (the estate's design/estate-model.md § 10).</summary>
public sealed class DischargeType : IRecordType
{
    /// <inheritdoc />
    public string Name => "discharge";

    /// <inheritdoc />
    public string Summary => "The discharge of one obligation a decision left: which obligation, by its key, and the evidence that discharges it; recorded by the obligation's owner or the operator. An obligation stays open until a discharge names it.";

    /// <inheritdoc />
    public string? SubjectType => "decision";

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("obligation", "slug", true, "The key of the obligation in the decision's obligations.", 2),
        new("summary", "string", true, "How it was discharged.", 2),
        new("evidence", "object[]", true, "summary, optional hash and location: what shows it is discharged; at least one.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? key = body.Text("obligation", true, 60, Patterns.Slug, "an obligation key");
        body.Text("summary", true, 4000);
        foreach (BodyReader evidence in body.NestedArray("evidence", true, allowEmpty: false) ?? [])
        {
            evidence.Text("summary", true, 2000);
            evidence.Hash("hash", false);
            evidence.Text("location", false, 500);
            evidence.NoOtherKeys();
        }

        body.NoOtherKeys();
        RecordEnvelope? decision = context.Find(context.Record.Subject?.Id);
        if (key is null || decision is null || decision.Type != "decision")
        {
            return;
        }

        RecordEnvelope latest = context.Model.Latest(decision);
        JsonElement? obligation = null;
        if (latest.Body.TryGetProperty("obligations", out JsonElement obligations) && obligations.ValueKind == JsonValueKind.Array)
        {
            obligation = obligations.EnumerateArray().Cast<JsonElement?>().FirstOrDefault(o => o!.Value.ValueKind == JsonValueKind.Object && o.Value.TryGetProperty("key", out JsonElement k) && k.GetString() == key);
        }

        if (obligation is null)
        {
            context.Error("discharge.obligation", $"Decision {decision.Id} leaves no obligation '{key}'.");
            return;
        }

        string? owner = obligation.Value.TryGetProperty("owner", out JsonElement o) ? o.GetString() : null;
        if (context.Record.By != owner && !context.HasRole(context.Record.By, Roles.Operator))
        {
            context.Error("discharge.author", "An obligation is discharged by its owner or the operator.");
        }

        string root = context.Model.Chain(decision)[0];
        if (context.Model.IsCurrent(context.Record) && context.Model.Current(Name).Any(other => other.Id != context.Record.Id && other.Subject is not null && context.Find(other.Subject.Id) is { } otherDecision && context.Model.Chain(otherDecision)[0] == root && EstateModel.BodyString(other, "obligation") == key))
        {
            context.Error("discharge.duplicate", $"Obligation '{key}' already has a current discharge; a correction supersedes it.");
        }
    }
}
