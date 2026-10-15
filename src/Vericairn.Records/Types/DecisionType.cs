// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An operator decision, with the operator's words preserved verbatim.</summary>
public sealed class DecisionType : IRecordType
{
    /// <summary>How much rides on a decision, from record format 2.</summary>
    public static readonly string[] Stakes = ["low", "medium", "high", "critical"];

    /// <summary>How an obligation a decision leaves is resolved, from record format 2.</summary>
    public static readonly string[] Resolutions = ["action", "evidence", "review", "decision"];

    /// <inheritdoc />
    public string Name => "decision";

    /// <inheritdoc />
    public string Summary => "An operator decision: the verbatim statement, a summary, its effects and the work orders it affects.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("title", "string", true, "Short title."),
        new("statement", "string", true, "The operator's words, verbatim."),
        new("summary", "string", true, "What was decided, in one or two sentences, for lists."),
        new("synthesis", "string", false, "A clean, thorough restatement for display: context, what was decided, why, consequences and limits. Expected on every decision; the recorder writes it and it never alters the verbatim statement."),
        new("effects", "string[]", true, "Concrete effects on the estate."),
        new("affects", "ulid[]", true, "Work orders this decision changes; may be empty."),
        new("decidedBy", "ulid", true, "The operator principal who decided."),
        new("decidedAt", "timestamp", true, "When the operator decided."),
        new("stake", string.Join('|', Stakes), true, "How much rides on the decision; at medium and above the recorder asks with a recommendation first and at most three questions at a time.", 2),
        new("notAuthorized", "string[]", true, "What this answer does not authorize, so no one reads more into it; may be empty.", 2),
        new("answers", "ulid[]", false, "The asks this decision answers; an ask is answered by the principal it was put to, and never by waiting.", 2),
        new("obligations", "object[]", false, "What the answer leaves owed: key, summary, owner (ulid), resolution (action|evidence|review|decision), discharge (what discharges it) and optionally gates (the operation or work it holds); each stays open until a discharge record names it.", 2),
        new("documents", "string[]", false, "The living documents this decision changes, by path or by declaration id; each becomes due until it is cleared.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("title", true, 200);
        body.Text("statement", true, 20000);
        body.Text("summary", true, 4000);
        if (body.Text("synthesis", false, 40000) is null && !body.Has("synthesis") && context.Model.IsCurrent(context.Record))
        {
            body.Warning("decision.synthesis", "synthesis", "Every decision should carry a synthesis: a clean, thorough restatement for display alongside the verbatim statement.");
        }

        body.StringArray("effects", true, allowEmpty: false, maxItemLength: 2000);
        IReadOnlyList<string>? affects = body.StringArray("affects", true, allowEmpty: true, maxItemLength: Identity.Ulid.Length);
        string? decidedBy = body.Ulid("decidedBy", true);
        body.Timestamp("decidedAt", true);
        if (body.Since(2, "stake"))
        {
            body.Enum("stake", true, Stakes);
        }

        if (body.Since(2, "notAuthorized"))
        {
            body.StringArray("notAuthorized", true, allowEmpty: true, maxItemLength: 2000);
        }

        IReadOnlyList<string>? answers = body.Since(2, "answers") ? body.StringArray("answers", false, allowEmpty: false, maxItemLength: Identity.Ulid.Length) : null;
        IReadOnlyList<BodyReader>? obligations = body.Since(2, "obligations") ? body.NestedArray("obligations", false, allowEmpty: false) : null;
        IReadOnlyList<string>? documents = body.Since(2, "documents") ? body.StringArray("documents", false, allowEmpty: false, maxItemLength: 500) : null;
        body.NoOtherKeys();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader obligation in obligations ?? [])
        {
            string? key = obligation.Text("key", true, 60, Patterns.Slug, "a lower-case kebab-case key");
            obligation.Text("summary", true, 2000);
            string? owner = obligation.Ulid("owner", true);
            obligation.Enum("resolution", true, Resolutions);
            obligation.Text("discharge", true, 2000);
            obligation.Text("gates", false, 500);
            obligation.NoOtherKeys();
            if (key is not null && !keys.Add(key))
            {
                obligation.Error("decision.obligation", "key", $"Obligation '{key}' is listed twice.");
            }

            context.RequireReference("obligations.owner", owner, "principal");
        }
        // A document is named by its path or by its declaration; a path need not be declared yet, a declaration id must exist.
        foreach (string document in documents ?? [])
        {
            if (Identity.Ulid.IsValid(document))
            {
                context.RequireReference("documents", document, "living-document");
            }
            else if (!Patterns.RelativePath.IsMatch(document))
            {
                body.Error("body.pattern", "documents", $"'{document}' is neither a normalized repository-relative path nor a declaration id.");
            }
        }

        foreach (string id in answers ?? [])
        {
            if (context.RequireReference("answers", id, "ask") is not { } ask)
            {
                continue;
            }

            RecordEnvelope latest = context.Model.Latest(ask);
            if (EstateModel.BodyString(latest, "to") != decidedBy)
            {
                context.Error("decision.answers", $"Ask {id} was put to another principal; an ask is answered by the principal it was put to.");
            }
            else if (EstateModel.BodyString(latest, "state") == "withdrawn")
            {
                context.Error("decision.answers", $"Ask {id} was withdrawn before it was answered.");
            }
        }

        // From format 2 the operator's words are protected: a revision corrects everything around them, and new words are a new decision.
        if (context.Format >= 2 && context.Find(context.Record.Supersedes) is { Type: "decision" } previous && EstateModel.BodyString(previous, "statement") != EstateModel.BodyString(context.Record, "statement"))
        {
            context.Error("decision.statement", "A revision of a decision keeps the operator's words verbatim; different words are a new decision.");
        }
        if (affects is not null)
        {
            foreach (string id in affects)
            {
                context.RequireReference("affects", id, "work-order");
            }
        }

        if (decidedBy is not null && context.RequireReference("decidedBy", decidedBy, "principal") is not null && !context.HasRole(decidedBy, Roles.Operator))
        {
            context.Error("decision.operator", "Decisions are made by a principal holding the operator role; agents record them.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("decision.author", $"Recording principal {context.Record.By} has no principal record.");
        }
    }
}
