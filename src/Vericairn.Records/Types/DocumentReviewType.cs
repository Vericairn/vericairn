// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Types;

/// <summary>
/// The clearance of a living document, from record format 2 (the estate's design/living-documents.md § 7): a review of the document's
/// exact content, against the declaration at its exact hash, saying whether it still holds, was revised, or needs a change.
/// </summary>
public sealed class DocumentReviewType : IRecordType
{
    /// <summary>current: the content holds; revised: it was changed for the trigger; needs-change: it is stale and stays due.</summary>
    public static readonly ImmutableArray<string> Outcomes = ["current", "revised", "needs-change"];

    /// <summary>How independent the reviewer is of the document's content, as for a review.</summary>
    public static readonly ImmutableArray<string> Independences = ["self", "independent-agent", "operator"];

    /// <summary>The outcomes of a check run at the clearance.</summary>
    public static readonly ImmutableArray<string> CheckOutcomes = ["passed", "failed", "skipped", "not-performed"];

    /// <inheritdoc />
    public string Name => "document-review";

    /// <inheritdoc />
    public string Summary => "Clears a due living document, or finds it stale: the reviewed content by hash (and commit for another repository), the covered paths' identity, the triggers it answers, the checks that ran, and the reviewer's independence of the content.";

    /// <inheritdoc />
    public string? SubjectType => "living-document";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("outcome", string.Join('|', Outcomes), true, "current when the content holds; revised when it was changed for the trigger; needs-change when it is stale and was not changed, so it stays due.", 2),
        new("content", "object", true, "The reviewed bytes: hash (sha256) and bytes, and commit for a document of another repository.", 2),
        new("covered", "hash", false, "The covered paths' content identity when reviewed, for a document with a covers trigger; required to clear one.", 2),
        new("answers", "object[]", true, "The triggers this review answers: on, and the record that fired it for release, upgrade, decision and accepted; may be empty.", 2),
        new("independence", string.Join('|', Independences), true, "self when the reviewer revised the content, or, for a document of another repository, states that it wrote it; independent-agent for another agent; operator for the operator.", 2),
        new("summary", "string", true, "What was checked.", 2),
        new("findings", "object[]", true, "severity (error|warning|info), summary, optional location; may be empty.", 2),
        new("checks", "object[]", true, "The declared checks that ran: name, outcome (passed|failed|skipped|not-performed), summary; a failed check refuses the clearance; may be empty.", 2),
        new("sections", "object[]", false, "Exact section reviews: id, hash, sourceDigest, outcome, fidelity (sketch|draft|final), sources (id, kind, identity, hash, bytes, status and exact records), checks and per-rule cohesion verdicts. Evaluator judgments name their exact evidence hashes and require independence. A section review clears no other section.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        TemplateRecords.Review(context);
        string? outcome = body.Enum("outcome", true, Outcomes);
        BodyReader? content = body.Nested("content", true);
        string? covered = body.Hash("covered", false);
        IReadOnlyList<BodyReader>? answers = body.NestedArray("answers", true, allowEmpty: true);
        string? independence = body.Enum("independence", true, Independences);
        body.Text("summary", true, 4000);
        foreach (BodyReader finding in body.NestedArray("findings", true, allowEmpty: true) ?? [])
        {
            finding.Enum("severity", true, "error", "warning", "info");
            finding.Text("summary", true, 2000);
            finding.Text("location", false, 500);
            finding.NoOtherKeys();
        }

        var failed = new List<string>();
        foreach (BodyReader check in body.NestedArray("checks", true, allowEmpty: true) ?? [])
        {
            string? name = check.Text("name", true, 200);
            if (check.Enum("outcome", true, CheckOutcomes) == "failed" && name is not null)
            {
                failed.Add(name);
            }

            check.Text("summary", true, 2000);
            check.NoOtherKeys();
        }

        string? commit = null;
        if (content is not null)
        {
            content.Hash("hash", true);
            content.Number("bytes", true, 0, 1L << 40);
            commit = content.Text("commit", false, 40, Patterns.Commit, "a 40-character commit");
            content.NoOtherKeys();
        }

        body.NoOtherKeys();
        RecordEnvelope? reviewer = context.Principal(context.Record.By);
        if (reviewer is null)
        {
            context.Error("document.author", $"Reviewing principal {context.Record.By} has no principal record.");
            return;
        }

        RecordEnvelope? declaration = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (declaration is null || declaration.Type != "living-document" || outcome is null)
        {
            return;
        }

        if (GeneratedDocumentContract.IsStrict(context.Model, declaration))
            context.Error("document.generated-clearance", "A whole generated document is verified by regeneration and takes no authored clearance record.");

        bool clears = outcome != "needs-change";
        if (clears && failed.Count > 0)
        {
            context.Error("document.check-failed", $"A failed check refuses the clearance ({string.Join(", ", failed)}); record needs-change, or fix the document and review it again.");
        }

        bool elsewhere = EstateModel.BodyString(declaration, "repository") is not null;
        if (elsewhere && content is not null && commit is null)
        {
            content.Error("document.commit", "commit", "A document of another repository is reviewed at a commit.");
        }
        else if (!elsewhere && commit is not null)
        {
            content!.Error("document.commit", "commit", "An estate file is reviewed in the estate's working tree, not at a commit.");
        }

        IReadOnlyList<string> on = Triggers(declaration);
        if (clears && covered is null && on.Contains("covers"))
        {
            body.Error("document.covered", "covered", "A covers trigger is cleared only by a review that records the covered paths' identity.");
        }

        foreach (BodyReader answer in answers ?? [])
        {
            string? trigger = answer.Text("on", true, 200);
            // Own-file changes are always covered, including declarations with only record triggers. Their content pin
            // supplies that clearance; the separate covered pin remains required only for explicitly declared paths.
            if (trigger is not null && trigger != "covers" && !(trigger == "sections" && TemplateCatalog.Pin(declaration) is not null) && !on.Contains(trigger))
            {
                answer.Error("document.answers", "on", $"The declaration has no '{trigger}' trigger.");
            }

            if (answer.Has("record"))
            {
                RecordReference? reference = RecordParser.ParseReference(answer.Element.GetProperty("record"), answer.Location + ".record", context.Findings);
                answer.Allow("record");
                if (reference is not null)
                {
                    context.RequireReference("answers.record", reference.Id, reference.Type);
                }
            }

            answer.NoOtherKeys();
        }

        Independence(context, declaration, reviewer, outcome, independence);
    }

    /// <summary>The triggers a declaration names.</summary>
    public static IReadOnlyList<string> Triggers(RecordEnvelope declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return declaration.Body.TryGetProperty("triggers", out JsonElement triggers) && triggers.ValueKind == JsonValueKind.Array
            ? triggers.EnumerateArray().Select(t => t.TryGetProperty("on", out JsonElement on) ? on.GetString() : null).OfType<string>().ToList()
            : [];
    }

    /// <summary>
    /// The reviewer's independence of the content: self when this review revised it, or when the reviewer recorded the estate file's
    /// latest revision or the latest revising review before this review. The estate's records show who revised an estate file but
    /// not who wrote a document of another repository, so there a reviewer may also state self. And the declaration's clearedBy,
    /// which only a clearing review must meet: a self review does not clear a document that asks for independence.
    /// </summary>
    private static void Independence(ValidationContext context, RecordEnvelope declaration, RecordEnvelope reviewer, string outcome, string? independence)
    {
        string? path = EstateModel.BodyString(declaration, "path");
        bool elsewhere = EstateModel.BodyString(declaration, "repository") is not null;
        IReadOnlyList<string> chain = context.Model.Chain(context.Model.Latest(declaration));
        RecordEnvelope? reviser = elsewhere
            ? context.Model.OfType("document-review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && EstateModel.BodyString(r, "outcome") == "revised" && Before(r, context.Record)).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault()
            : context.Model.OfType("document-revision").Where(r => EstateModel.BodyString(r, "path") == path && Before(r, context.Record)).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        JsonElement[] sections = context.Record.Body.TryGetProperty("sections", out JsonElement selected) && selected.ValueKind == JsonValueKind.Array
            ? selected.EnumerateArray().ToArray() : [];
        bool revised = outcome == "revised" || (sections.Length == 0 ? reviser?.By == context.Record.By
            : sections.Any(s => TJson.Text(s, "outcome") == "revised" || TemplateDocuments.Writer(context.Model, declaration,
                TJson.Text(s, "id") ?? "", TJson.Text(s, "hash"), context.Record.At) == context.Record.By));
        bool self = revised || (elsewhere && independence == "self");
        string kind = EstateModel.BodyString(reviewer, "kind") ?? string.Empty;
        if (independence is not null)
        {
            if (revised && independence != "self")
            {
                context.Error("document.independence", "The reviewer revised this content, so independence must be 'self'.");
            }
            else if (!self && independence == "self")
            {
                context.Error("document.independence", "The reviewer did not revise this estate file, so independence cannot be 'self'.");
            }
            else if (independence == "operator" && !context.HasRole(context.Record.By, Roles.Operator))
            {
                context.Error("document.independence", "Only an operator-role principal records an operator review.");
            }
            else if (independence == "independent-agent" && kind != "agent")
            {
                context.Error("document.independence", $"'independent-agent' requires an agent principal; this reviewer is a {kind}.");
            }
        }

        if (outcome == "needs-change")
        {
            return;
        }

        switch (EstateModel.BodyString(declaration, "clearedBy") ?? "any")
        {
            case "independent" when self:
                context.Error("document.cleared-by", "This document is cleared by a principal other than the one that wrote or revised its content.");
                break;
            case "operator" when !context.HasRole(context.Record.By, Roles.Operator):
                context.Error("document.cleared-by", "This document is cleared by the operator.");
                break;
        }
    }

    /// <summary>Record order: by authoring time, and by identifier within the same second, as record format 2 orders records of one second.</summary>
    private static bool Before(RecordEnvelope a, RecordEnvelope b) =>
        string.CompareOrdinal(a.At, b.At) < 0 || (a.At == b.At && string.CompareOrdinal(a.Id, b.Id) < 0);
}
