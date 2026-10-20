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
    public string Summary => "Pins a design or docs file at an exact content hash and revision number; editing the file requires a new revision record.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("path", "path", true, "Repository-relative path under design/ or docs/."),
        new("title", "string", true, "Document title."),
        new("hash", "hash", true, "sha256 of the file bytes."),
        new("bytes", "integer", true, "File size in bytes."),
        new("mediaType", "string", true, "For example text/markdown."),
        new("revision", "integer", true, "1 for the first revision; later revisions supersede the previous record."),
        new("summary", "string", true, "What this revision is or changes."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? path = body.Text("path", true, 500, Patterns.DocumentPath, "a repository-relative path under design/ or docs/");
        body.Text("title", true, 300);
        body.Hash("hash", true);
        body.Number("bytes", true, 0, 1L << 40);
        body.Text("mediaType", true, 100);
        long? revision = body.Number("revision", true, 1, 1_000_000);
        body.Text("summary", true, 4000);
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
            string? previousPath = previous.Body.TryGetProperty("path", out System.Text.Json.JsonElement p) ? p.GetString() : null;
            long previousRevision = previous.Body.TryGetProperty("revision", out System.Text.Json.JsonElement r) && r.TryGetInt64(out long value) ? value : 0;
            if (previousPath != path)
            {
                context.Error("document.path-changed", "A document revision supersedes a revision of the same path.");
            }

            if (revision != previousRevision + 1)
            {
                context.Error("document.revision", $"Revision must be {previousRevision + 1}, following the superseded record.");
            }
        }
        else if (revision != 1)
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
