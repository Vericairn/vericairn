// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;

namespace Vericairn.Records.Types;

/// <summary>Pins a document in the repository (design or docs) at an exact hash and revision.</summary>
public sealed class DocumentRevisionType : IRecordType
{
    /// <inheritdoc />
    public string Name => "document-revision";

    /// <inheritdoc />
    public string Summary => "Pins a design or docs file at an exact content hash; authored-content pins generate their revision history from records.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("path", "path", true, "Repository-relative path under design/ or docs/; format 3 also admits README.md for the estate's template instance."),
        new("title", "string", true, "Document title."),
        new("hash", "hash", true, "sha256 of the bytes selected by hashMode; whole-file bytes when absent."),
        new("bytes", "integer", true, "Size of the bytes selected by hashMode."),
        new("mediaType", "string", true, "For example text/markdown."),
        new("revision", "integer", true, "Sequential for whole-file pins; advisory for authored-revisions-v1, whose displayed ordinal comes from recorded time and id."),
        new("summary", "string", true, "What this revision is or changes."),
        new("hashMode", "string", false, "authored-revisions-v1 excludes the generated revision payload, retaining its markers.", 3),
        new("decision", "ulid", false, "The exact decision carried by this revision.", 3),
        new("revisionHistory", "object[]", false, "Conversion's preserved lines, each bound to an old revision id and record hash.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? path = body.Text("path", true, 500, context.Record.Format >= 3 ? Patterns.TemplateDocumentPath : Patterns.DocumentPath,
            "a repository-relative path under design/ or docs/ (also README.md in format 3)");
        body.Text("title", true, 300);
        body.Hash("hash", true);
        body.Number("bytes", true, 0, 1L << 40);
        string? mediaType = body.Text("mediaType", true, 100);
        long? revision = body.Number("revision", true, 1, 1_000_000);
        body.Text("summary", true, 4000);
        bool authored = body.Since(3, "hashMode") && body.Enum("hashMode", false, RevisionLines.HashMode) == RevisionLines.HashMode;
        if (authored && mediaType != "text/markdown") context.Error("document.hash-mode", "The generated revision hash mode requires Markdown content.");
        if (body.Has("revisionHistory") && !authored) context.Error("document.history", "Preserved history belongs to an authored-content revision pin.");
        if (body.Since(3, "decision")) context.RequireReference("decision", body.Ulid("decision", false), "decision");
        if (body.Since(3, "revisionHistory"))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyReader entry in body.NestedArray("revisionHistory", false) ?? [])
            {
                string? id = entry.Ulid("id", true);
                string? hash = entry.Hash("hash", true);
                string? line = entry.Text("line", true, 16000);
                entry.NoOtherKeys();
                Model.RecordEnvelope? pin = context.RequireReference("revisionHistory.id", id, Name);
                if (!authored || pin is null || pin.Id == context.Record.Id || pin.AtTime > context.Record.AtTime
                    || EstateModel.BodyString(pin, "path") != path || pin.ComputedHash != hash || id is null || !seen.Add(id)
                    || line is null || !pin.Body.TryGetProperty("revision", out var number) || !number.TryGetInt64(out long value)
                    || !RevisionLines.ValidLegacyLine(line, value))
                    context.Error("document.history", "Each preserved revision line must name one exact earlier revision of this path and its original advisory number.");
            }
        }
        body.NoOtherKeys();
        if (path is null || revision is null)
        {
            return;
        }

        if (context.Model.Current("living-document").Any(d => EstateModel.BodyString(d, "repository") is null
            && GeneratedDocumentContract.IsStrict(context.Model, d) && !GeneratedDocumentContract.IsRetired(context.Model, d) && GeneratedDocumentContract.Owns(d, path)
            && !GeneratedDocumentContract.IsHistoricalPin(context.Model, d, context.Record)))
            context.Error("document.generated-pin", "A whole generated document is verified from its sources and takes no output pin.");

        if (path.Contains("/../", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal))
        {
            body.Error("body.pattern", "path", "The path must be normalized.");
        }

        Model.RecordEnvelope? previous = context.Record.Supersedes is null ? null : context.Find(context.Record.Supersedes);
        if (previous is not null && previous.Type == Name)
        {
            if (authored)
            {
                var ancestors = new HashSet<string>(StringComparer.Ordinal) { context.Record.Id };
                for (Model.RecordEnvelope? ancestor = previous; ancestor is not null; ancestor = context.Find(ancestor.Supersedes))
                {
                    if (ancestors.Add(ancestor.Id)) continue;
                    context.Error("document.cycle", "Document revision predecessors must form an acyclic history.");
                    break;
                }
            }
            string? previousPath = previous.Body.TryGetProperty("path", out System.Text.Json.JsonElement p) ? p.GetString() : null;
            long previousRevision = previous.Body.TryGetProperty("revision", out System.Text.Json.JsonElement r) && r.TryGetInt64(out long value) ? value : 0;
            if (previousPath != path)
            {
                context.Error("document.path-changed", "A document revision supersedes a revision of the same path.");
            }

            if (!authored && revision != previousRevision + 1)
            {
                context.Error("document.revision", $"Revision must be {previousRevision + 1}, following the superseded record.");
            }
        }
        else if (!authored && revision != 1)
        {
            context.Error("document.revision", "The first revision of a document is revision 1 and supersedes nothing.");
        }

        if (context.Model.IsCurrent(context.Record))
        {
            Model.RecordEnvelope[] pins = context.Model.Current(Name).Where(other => EstateModel.BodyString(other, "path") == path).ToArray();
            if (pins.Length > 1)
            {
                context.Error("document.multiple", MultipleMessage(path, pins));
            }
        }
    }

    /// <summary>The same reconciliation diagnostic for estate validation and document pinning.</summary>
    public static string MultipleMessage(string path, IReadOnlyList<Model.RecordEnvelope> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        return $"More than one current revision record exists for {path}. Reconcile the forked pins before pinning again: "
            + string.Join("; ", pins.OrderBy(p => p.Id, StringComparer.Ordinal)
                .Select(p => $"{p.Id} (supersedes {p.Supersedes ?? "none"})")) + ".";
    }
}
