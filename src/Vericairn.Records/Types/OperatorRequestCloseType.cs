// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A signed done or dismissed outcome for one exact operator request.</summary>
public sealed class OperatorRequestCloseType : IRecordType
{
    /// <inheritdoc />
    public string Name => "operator-request-close";
    /// <inheritdoc />
    public string Summary => "A signed close of an exact request as done or dismissed. An agent preserves and cites the addressed operator's own words. Closure grants no acceptance or publication authority.";
    /// <inheritdoc />
    public string? SubjectType => "operator-request";
    /// <inheritdoc />
    public bool SubjectHashRequired => true;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("outcome", "done|dismissed", true, "The operator's disposition.", 3),
        new("reason", "string", false, "Required when dismissed.", 3),
        new("link", "string", false, "Optional evidence or observed destination; required for a completed publication.", 3),
        new("evidence", "object[]", false, "Evidence with summary and optional hash and location.", 3),
        new("operatorWords", "object", false, "Required for an agent: by (the operator), statement (verbatim), source (citation) and at (when said). The agent remains the record's author.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? outcome = context.Body.Enum("outcome", true, "done", "dismissed");
        string? reason = context.Body.Text("reason", outcome == "dismissed");
        if (outcome == "dismissed" && reason is not null && string.IsNullOrWhiteSpace(reason))
            context.Error("operator-request.reason", "A dismissal needs the operator's reason, not blank text.");
        RecordEnvelope? request = context.Find(context.Record.Subject?.Id);
        context.Body.Text("link", outcome == "done" && request is not null && EstateModel.BodyString(request, "kind") == "publication", 2000);
        foreach (BodyReader evidence in context.Body.NestedArray("evidence", false, allowEmpty: false) ?? [])
        {
            evidence.Text("summary", true, 2000);
            evidence.Hash("hash", false);
            evidence.Text("location", false, 2000);
            evidence.NoOtherKeys();
        }
        string? to = request is null ? null : EstateModel.BodyString(request, "to");
        bool direct = context.Record.By == to;
        BodyReader? words = context.Body.Nested("operatorWords", !direct);
        if (words is not null)
        {
            if (words.Ulid("by", true) != to) context.Error("operator-request.words", "The cited words must be the addressed operator's.");
            string? statement = words.Text("statement", true, 20000);
            string? source = words.Text("source", true, 2000);
            if (statement is not null && string.IsNullOrWhiteSpace(statement) || source is not null && string.IsNullOrWhiteSpace(source))
                context.Error("operator-request.words", "The operator's words and their citation cannot be blank.");
            string? at = words.Timestamp("at", true);
            if (at is not null && (string.CompareOrdinal(at, context.Record.At) > 0 || request is not null && string.CompareOrdinal(at, request.At) < 0))
                context.Error("operator-request.words", "The operator's words follow the exact request and precede their recording.");
            words.NoOtherKeys();
        }
        context.Body.NoOtherKeys();
        if (!direct && (context.Principal(context.Record.By) is not { } agent || EstateModel.BodyString(agent, "kind") != "agent"))
            context.Error("operator-request.author", "Only the addressed operator or an agent citing that operator's words records a close.");
        OperatorRequestRules.Closure(context);
    }
}

internal static class OperatorRequestRules
{
    internal static void Closure(ValidationContext context)
    {
        RecordEnvelope? request = context.Find(context.Record.Subject?.Id);
        if (request is not { Type: "operator-request" } || context.Record.Subject != request.Reference || !context.Model.IsCurrent(request))
            context.Error("operator-request.target", "A close pins the current exact request revision.");
        if (context.Record.Supersedes is not null)
            context.Error("operator-request.close-revision", "A request's outcome is retained; a closure cannot be superseded.");
        if (request is not null)
        {
            if (string.CompareOrdinal(context.Record.At, request.At) < 0)
                context.Error("operator-request.time", "A close follows its recorded request.");
            if (OperatorRequests.Closures(context.Model, request).Any(r => r.Id != context.Record.Id))
                context.Error("operator-request.closed", "The request already has an outcome; a second close is refused.");
        }
        if (context.Record.Signatures.Count == 0)
            context.Error("signature.required", "An operator request's outcome requires its author's admitted-device signature.");
    }

    internal static void Observation(ValidationContext context, BodyReader? facts, string? asOf)
    {
        Closure(context);
        RecordEnvelope? request = context.Find(context.Record.Subject?.Id);
        if (facts is null) return;
        facts.Enum("outcome", true, "done");
        string? destination = facts.Text("destination", true, 1000);
        facts.Text("location", true, 2000);
        facts.Hash("evidenceHash", true);
        if (request is not null && (destination != EstateModel.BodyString(request, "destination") ||
            asOf is not null && string.CompareOrdinal(asOf, request.At) < 0))
            context.Error("operator-request.observation", "The observation proves the effect at the requested destination after its exact intent was recorded.");
        if (asOf is not null && string.CompareOrdinal(asOf, context.Record.At) > 0)
            context.Error("observation.future", "An observation cannot report a future effect.");
    }
}
