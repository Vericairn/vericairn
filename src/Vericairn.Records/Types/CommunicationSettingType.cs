// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An attributable presentation profile; an empty values object resets its scope to inheritance.</summary>
public sealed class CommunicationSettingType : IRecordType
{
    private static readonly string[] ScopeKeys = ["scope", "recipient", "assignment", "grantee"];

    /// <inheritdoc />
    public string Name => "communication-setting";
    /// <inheritdoc />
    public string Summary => "Estate defaults or a person's estate or assignment communication values, with an append-only revision history.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 2;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("scope", "estate|person|assignment", true, "The scope being revised."),
        new("recipient", "ulid", false, "The person whose choices these are; required outside estate defaults."),
        new("assignment", "ulid", false, "Required only for an assignment override."),
        new("values", "object", true, "Optional progress (none|completion-and-need|milestones|verbose), status, decision, result and handoff (short|standard|comprehensive). Omission inherits. From 0.9.4, additive in format 3 and on a person's own scope only: availability (state available, or away with until and an optional from) and quietHours (zone, an IANA time zone; windows, 1 to 14 of days, start and end as HH:MM; breakthrough, the attention categories delivered inside a window, incident when omitted); a reader before 0.9.4 refuses them as unknown fields."),
        new("authority", "ulid", false, "Explicit communication-delegation for a change by another principal."),
        new("authorityHash", "hash", false, "Exact delegation revision used."),
        new("basis", "ulid", false, "Existing decision explicitly adopted; never inferred from conversation."),
        new("basisHash", "hash", false, "Exact decision revision adopted."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? scope = body.Enum("scope", true, ["estate", "person", "assignment"]);
        string? recipient = body.Ulid("recipient", scope is "person" or "assignment");
        string? assignment = body.Ulid("assignment", scope == "assignment");
        BodyReader? values = body.Nested("values", true);
        if (values is not null)
        {
            values.Enum("progress", false, ["none", "completion-and-need", "milestones", "verbose"]);
            foreach (string field in new[] { "status", "decision", "result", "handoff" })
            {
                values.Enum(field, false, ["short", "standard", "comprehensive"]);
            }

            // From 0.9.4 (GOAL-11), additive in record format 3: a person declares availability and quiet hours on their own scope.
            bool availability = values.Since(3, "availability") && Availability(values);
            bool quiet = values.Since(3, "quietHours") && QuietHours(values);
            if ((availability || quiet) && scope is not null and not "person")
            {
                values.Error("communication.availability", availability ? "availability" : "quietHours", "Availability and quiet hours are a person's own, on their person scope; estate defaults and assignment overrides carry neither.");
            }

            values.NoOtherKeys();
        }

        string? authority = body.Ulid("authority", false);
        string? authorityHash = body.Hash("authorityHash", authority is not null);
        string? basis = body.Ulid("basis", false);
        string? basisHash = body.Hash("basisHash", basis is not null);
        body.NoOtherKeys();
        if ((scope == "estate" && recipient is not null) || (scope != "assignment" && assignment is not null))
        {
            context.BodyError("communication.scope", "Estate defaults have no recipient; only assignment overrides name an assignment.");
        }

        if (recipient is not null && context.Principal(recipient) is { } person && EstateModel.BodyString(person, "kind") != "human")
        {
            context.Error("communication.person", "Communication values belong to a person, not an agent.");
        }

        context.RequireReference("recipient", recipient, "principal");
        context.RequireReference("assignment", assignment, "assignment");
        RecordEnvelope? decision = context.RequireReference("basis", basis, "decision");
        if ((decision is not null && (decision.ComputedHash != basisHash || EstateModel.BodyString(decision, "decidedBy") != recipient)) || (basis is null && basisHash is not null))
        {
            context.Error("communication.basis", "Adoption pins the exact decision of the recipient.");
        }

        if (scope == "estate")
        {
            if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer))
            {
                context.Error("communication.authority", "Only an estate administrator sets estate defaults.");
            }
        }
        else if (context.Record.By != recipient && !CommunicationDelegationType.Authorizes(context, authority, authorityHash))
        {
            context.Error("communication.authority", "A change belongs to its recipient or an explicit, unexpired delegate for this exact scope; being an operator or assignment author does not imply consent.");
        }
        else if (authority is not null && !CommunicationDelegationType.Authorizes(context, authority, authorityHash))
        {
            context.Error("communication.authority", "The named delegation does not authorize this change.");
        }

        if (authority is null && authorityHash is not null)
        {
            context.BodyError("communication.authority", "An authority hash requires its delegation id.");
        }

        ValidateRevision(context, Name, ["scope", "recipient", "assignment"]);
    }

    /// <summary>The attention categories quiet hours may let through.</summary>
    private static readonly string[] Categories = ["decision-needed", "blocked", "uncertain-effect", "failed-check", "incident", "information"];

    private static readonly string[] Days = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    private static readonly System.Text.RegularExpressions.Regex Clock = new(@"\A(?:[01][0-9]|2[0-3]):[0-5][0-9]\z|\A24:00\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex Zone = new(@"\A[A-Za-z][A-Za-z0-9_+\-]{0,63}(?:/[A-Za-z0-9_+\-]{1,64}){0,3}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Reads availability: available, or away with until and an optional earlier from. True when present.</summary>
    private static bool Availability(BodyReader values)
    {
        if (values.Nested("availability", false) is not BodyReader availability)
        {
            return false;
        }

        string? state = availability.Enum("state", true, ["available", "away"]);
        string? until = availability.Timestamp("until", state == "away");
        string? from = availability.Timestamp("from", false);
        availability.NoOtherKeys();
        if (state == "available" && (until is not null || from is not null))
        {
            availability.Error("communication.availability", "state", "An available person names no until or from; away names until, when the person is back.");
        }
        else if (from is not null && until is not null && string.CompareOrdinal(from, until) >= 0)
        {
            availability.Error("communication.availability", "from", "Away's from comes before its until.");
        }

        return true;
    }

    /// <summary>Reads quiet hours: an IANA zone, 1 to 14 weekly windows and the categories that break through. True when present.</summary>
    private static bool QuietHours(BodyReader values)
    {
        if (values.Nested("quietHours", false) is not BodyReader quiet)
        {
            return false;
        }

        quiet.Text("zone", true, 200, Zone, "an IANA time zone, such as Europe/Berlin or UTC");
        IReadOnlyList<BodyReader>? windows = quiet.NestedArray("windows", true, allowEmpty: false);
        if (windows is { Count: > 14 })
        {
            quiet.Error("communication.quiet-hours", "windows", "Quiet hours hold at most 14 weekly windows.");
        }

        foreach (BodyReader window in windows ?? [])
        {
            IReadOnlyList<string>? days = window.StringArray("days", true, allowEmpty: false, maxItemLength: 3);
            if (days is not null && (days.Any(d => !Days.Contains(d, StringComparer.Ordinal)) || days.Distinct(StringComparer.Ordinal).Count() != days.Count))
            {
                window.Error("communication.quiet-hours", "days", "A window names the days it starts on, each once: mon, tue, wed, thu, fri, sat or sun.");
            }

            string? start = window.Text("start", true, 5, Clock, "HH:MM");
            string? end = window.Text("end", true, 5, Clock, "HH:MM, 24:00 for midnight at the day's end");
            window.NoOtherKeys();
            if (start == "24:00" || (start is not null && start == end))
            {
                window.Error("communication.quiet-hours", "start", "A window starts before 24:00, and its start and end differ; one whose end is not after its start runs past midnight.");
            }
        }

        IReadOnlyList<string>? breakthrough = quiet.StringArray("breakthrough", false, allowEmpty: true, maxItemLength: 40);
        if (breakthrough is not null && breakthrough.Any(c => !Categories.Contains(c, StringComparer.Ordinal)))
        {
            quiet.Error("communication.quiet-hours", "breakthrough", "Breakthrough names attention categories: decision-needed, blocked, uncertain-effect, failed-check, incident or information.");
        }

        quiet.NoOtherKeys();
        return true;
    }

    internal static void ValidateRevision(ValidationContext context, string type, IReadOnlyList<string> keys)
    {
        bool Same(RecordEnvelope record) => keys.All(k => EstateModel.BodyString(record, k) == EstateModel.BodyString(context.Record, k));
        if (context.Record.Supersedes is string prior && context.Find(prior) is { } previous && !Same(previous))
        {
            context.Error("communication.scope", "A revision keeps its recipient and scope; reset the old scope separately.");
        }

        if (context.Model.IsCurrent(context.Record) && context.Model.Current(type).Any(r => r.Id != context.Record.Id && Same(r)))
        {
            context.Error("communication.conflict", "Several current revisions exist for this communication scope; reconcile them explicitly, never select the last arrival.");
        }
    }

    internal static bool SameScope(RecordEnvelope left, RecordEnvelope right) => left.Type == right.Type &&
        ScopeKeys.All(key => EstateModel.BodyString(left, key) == EstateModel.BodyString(right, key));
}
