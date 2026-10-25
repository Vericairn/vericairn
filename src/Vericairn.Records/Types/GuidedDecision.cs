// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static class GuidedDecision
{
    internal static void Validate(ValidationContext context, IReadOnlyList<string>? answers, IReadOnlyList<string>? documents)
    {
        BodyReader body = context.Body;
        BodyReader? answer = body.Since(2, "answer") ? body.Nested("answer", false) : null;
        BodyReader? change = body.Since(2, "changeSet") ? body.Nested("changeSet", false) : null;
        if (answer is null && change is null) return;
        if (answer is null || change is null)
            context.Error("decision.guided", "A guided decision keeps both its answer basis and its document change set.");
        if (answer is not null)
        {
            string? ask = answer.Ulid("ask", true);
            string? hash = answer.Hash("hash", true);
            string? at = answer.Timestamp("at", true);
            answer.NoOtherKeys();
            RecordEnvelope? source = context.RequireReference("answer.ask", ask, "ask");
            if (source is not null && (source.ComputedHash != hash || answers is not { Count: 1 } || answers[0] != ask))
                context.Error("decision.answer", "The answer pins exactly the ask revision named in answers.");
            string? decided = EstateModel.BodyString(context.Record, "decidedAt");
            if (at is not null && (string.CompareOrdinal(at, context.Record.At) > 0
                || RecordParser.IsTimestamp(decided) && string.CompareOrdinal(decided, at) > 0))
                context.Error("decision.time", "The decision time cannot follow the answer, and the answer cannot follow its recording.");
            if (source is not null && at is not null && string.CompareOrdinal(at, context.Model.ById[context.Model.Chain(source)[0]].At) < 0)
                context.Error("decision.time", "The answer time cannot precede the ask.");
        }
        if (change is null) return;
        change.Text("baseCommit", true, 40, Patterns.Commit, "a full commit id");
        change.Text("head", true, 40, Patterns.Commit, "a full commit id");
        IReadOnlyList<BodyReader>? pins = change.NestedArray("pins", true, allowEmpty: true);
        change.NoOtherKeys();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader pin in pins ?? [])
        {
            string? path = pin.Text("path", true, 500, Patterns.DocumentPath, "a design file path");
            string? id = pin.Ulid("id", true);
            string? hash = pin.Hash("hash", true);
            pin.NoOtherKeys();
            if (path is not null && (!path.StartsWith("design/", StringComparison.Ordinal) || !DocumentPatterns.IsSafePath(path) || !paths.Add(path)))
                context.Error("decision.documents", "Guided document pins name each changed design path exactly once.");
            RecordEnvelope? revision = context.RequireReference("changeSet.pins", id, "document-revision");
            if (revision is not null && (revision.ComputedHash != hash || EstateModel.BodyString(revision, "path") != path))
                context.Error("decision.pin", "Each document pin must match the exact revision record and document path.");
        }
        if (!paths.SetEquals(documents ?? []) || paths.Count != (documents?.Count ?? 0))
            context.Error("decision.documents", "The decision's documents must equal its complete pinned change set.");
    }
}
