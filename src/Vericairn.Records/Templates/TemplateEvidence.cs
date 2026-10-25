// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Templates;

/// <summary>A complete source selection, exact revision references and payload bytes, or explicit unavailable evidence.</summary>
public sealed record TemplateSource(string Id, string Kind, string Identity, string Hash, long Bytes, string? Content, string Status, IReadOnlyList<RecordReference> Records);

/// <summary>One deterministic check or evaluator judgment. Unknown and not-judged outcomes never count as passed.</summary>
public sealed record TemplateCheckResult(string Id, string Outcome, string Summary);

/// <summary>The derived state of a section or typed field and the complete basis needed to inspect it.</summary>
public sealed record TemplateSectionState(string Id, string State, bool Required, string RequiredFidelity, string? ContentHash,
    string SourceDigest, string? Writer, string? Review, JsonElement Definition, IReadOnlyList<TemplateSource> Sources,
    IReadOnlyList<TemplateCheckResult> Checks, IReadOnlyList<TemplateCheckResult> Cohesion, IReadOnlyList<string> Reasons);

/// <summary>Exact source selection for sections; never retrieves external references or reads outside the declared repository.</summary>
internal static class TemplateEvidence
{
    internal static byte[]? Document(EstateModel model, RecordEnvelope declaration, string? commit, string? path = null)
    {
        path ??= EstateModel.BodyString(declaration, "path");
        if (path is null) return null;
        if (EstateModel.BodyString(declaration, "repository") is null)
        {
            string file = GeneratedDocumentVerification.SafeFile(model.Root, path);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(declaration, "source"));
        commit ??= source?.Head(model, declaration);
        if (source is null || commit is null || source.Resolve(model, declaration, commit) != commit) return null;
        JsonObject body = JsonNode.Parse(declaration.Body.GetRawText())!.AsObject();
        body["path"] = path;
        var selected = new RecordEnvelope(declaration.Type, declaration.Id, declaration.At, declaration.By, declaration.Subject, declaration.Supersedes,
            JsonSerializer.SerializeToElement(body), declaration.DeclaredHash, declaration.ComputedHash, declaration.Signatures, declaration.Format);
        return source.Read(model, selected, commit);
    }

    internal static IReadOnlyList<TemplateSource> Sources(EstateModel model, RecordEnvelope declaration, JsonElement section,
        IReadOnlyDictionary<string, TemplateSectionContent> parts, string? commit)
    {
        var results = new List<TemplateSource>();
        foreach (JsonElement source in TJson.Array(section, "sources"))
        {
            string id = TJson.Text(source, "id")!;
            string kind = TJson.Text(source, "kind")!;
            string? content = null;
            string identity = kind;
            RecordReference[] references = [];
            try
            {
                if (kind == "section")
                {
                    identity = TJson.Text(source, "section")!;
                    content = parts.GetValueOrDefault(identity)?.Content;
                }
                else if (kind is "record" or "records")
                {
                    RecordEnvelope[] records = kind == "record"
                        ? model.ById.TryGetValue(TJson.Text(source, "record")!, out RecordEnvelope? record) ? [model.Latest(record)] : []
                        : model.Current(TJson.Text(source, "type")!).Where(r =>
                            TJson.Text(TJson.Object(source, "where"), "field") is not { } field || Value(r.Body, field) == TJson.Text(TJson.Object(source, "where"), "equals"))
                            .OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
                    references = records.Select(r => r.Reference).ToArray();
                    identity = references.Length == 0 ? "No matching records." : string.Join(";", references.Select(r => r.ToString()));
                    if (kind == "records" || records.Length == 1)
                    {
                        string? field = TJson.Text(source, "field");
                        string?[] values = records.Select(r => field is null ? CanonicalJson.SerializePretty(r.Body) : Value(r.Body, field)).ToArray();
                        if (values.All(v => v is not null)) content = string.Join(kind == "record" ? "" : "\n", values);
                    }
                }
                else
                {
                    identity = TJson.Text(source, "path")!;
                    byte[]? bytes = Document(model, declaration, commit, identity);
                    if (bytes is not null && (kind != "external" || ContentHash.Sha256(bytes) == TJson.Text(source, "hash")))
                        content = new UTF8Encoding(false, true).GetString(bytes);
                    if (kind == "external") identity = TJson.Text(source, "reference") + " at " + identity;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or DecoderFallbackException)
            { content = null; }
            byte[] payload = content is null ? [] : Encoding.UTF8.GetBytes(content);
            results.Add(new(id, kind, identity, ContentHash.Sha256(payload), payload.LongLength, content, content is null ? "unavailable" : "available", references));
        }
        return results;
    }

    internal static string Digest(TemplateDefinition definition, IReadOnlyDictionary<string, string> options, JsonElement section, IReadOnlyList<TemplateSource> sources) =>
        ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(new
        {
            template = definition.Hash, options, section,
            sources = sources.Select(s => new { s.Id, s.Kind, s.Identity, s.Hash, s.Bytes, s.Status }),
        })));

    internal static string? Value(JsonElement body, string field)
    {
        foreach (string component in field.Split('.'))
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(component, out body)) return null;
        return body.ValueKind == JsonValueKind.String ? body.GetString() : CanonicalJson.Serialize(body);
    }

    internal static IReadOnlyList<string> Items(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
        .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("<!--", StringComparison.Ordinal))
        .Select(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) ? line[2..].Trim() : line)
        .Distinct(StringComparer.Ordinal).ToArray();

    internal static TemplateCheckResult FieldType(JsonElement section, string content)
    {
        string? type = TJson.Text(section, "type");
        bool valid = type switch
        {
            "number" => decimal.TryParse(content.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            "date" => DateOnly.TryParseExact(content.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "list" => content.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).All(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal)),
            "table" => content.Split('\n').Count(l => l.TrimStart().StartsWith('|')) >= 2,
            _ => true,
        };
        return new("field-type", valid ? "passed" : "failed", valid ? "The field has its declared representation." : "The field does not have its declared representation.");
    }
}
