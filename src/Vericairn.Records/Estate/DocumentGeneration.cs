// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>A generator's installed contract, independent of the record format and the interface version.</summary>
public sealed record DocumentGeneratorDescriptor(
    string Id, int ContractVersion, string Implementation, string ImplementationHash,
    ImmutableDictionary<string, ImmutableArray<string>> Inputs, ImmutableArray<string> RecordTypes,
    ImmutableArray<string> SourceKinds, ImmutableArray<string> Selections, string MediaType,
    string Ordering, string Audience, string PathMapping, int MaxMemberBytes = 1048576)
{
    /// <summary>Refuses incomplete descriptors before registration; implementations cannot silently replace each other.</summary>
    public void Validate(string id)
    {
        if (Id != id || !Patterns.Contribution.IsMatch(Id) || ContractVersion < 1
            || string.IsNullOrWhiteSpace(Implementation) || !ContentHash.IsValid(ImplementationHash)
            || Inputs is null || RecordTypes.IsDefault || SourceKinds.IsDefaultOrEmpty || Selections.IsDefaultOrEmpty
            || MediaType != "text/markdown" || string.IsNullOrWhiteSpace(Ordering) || Audience != "Internal"
            || PathMapping is not ("single" or "record-revision") || MaxMemberBytes is < 1 or > 1048576
            || RecordTypes.Distinct(StringComparer.Ordinal).Count() != RecordTypes.Length
            || RecordTypes.Any(t => RecordTypeRegistry.Find(t) is null)
            || SourceKinds.Distinct(StringComparer.Ordinal).Count() != SourceKinds.Length
            || Selections.Distinct(StringComparer.Ordinal).Count() != Selections.Length
            || Inputs.Any(p => !Patterns.Slug.IsMatch(p.Key) || p.Value.IsDefaultOrEmpty || p.Value.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("The generator descriptor is incomplete, unsupported or cyclic.", nameof(id));
        }
    }
}

/// <summary>A selected immutable source. File bytes keep their original hashes; record bodies retain exact revision identities.</summary>
public sealed record DocumentSourceSnapshot(string Kind, string Identity, string Hash, string? Repository,
    string? Path, ImmutableArray<byte> Bytes, string? RecordType, string? At, string? Title,
    string? Supersedes, RecordReference? Subject, JsonElement? Body)
{
    /// <summary>The exact recorded author, never a joined current name.</summary>
    public string? Author { get; init; }
    /// <summary>The source record's own format, retained independently of the rendering contract.</summary>
    public long? RecordFormat { get; init; }
    /// <summary>Captures a verified record without giving the renderer access to an estate, clock or filesystem.</summary>
    public static DocumentSourceSnapshot Record(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new("record", record.Id, record.ComputedHash, null, record.ExpectedPath, [], record.Type,
            record.At, EstateModel.BodyString(record, "title") ?? EstateModel.BodyString(record, "key") ?? EstateModel.BodyString(record, "token") ?? "item",
            record.Supersedes, record.Subject, record.Body.Clone()) { Author = record.By, RecordFormat = record.Format };
    }

    /// <summary>Captures a file or object, verifying the advertised exact bytes before any renderer sees them.</summary>
    public static DocumentSourceSnapshot Content(string kind, string identity, string repository, string path, string hash, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!DocumentPatterns.IsSafePath(path) || ContentHash.Sha256(bytes) != hash)
            throw new ArgumentException("The source path or content hash is invalid.", nameof(bytes));
        return new(kind, identity, hash, repository, path, [.. bytes], null, null, null, null, null, null);
    }
}

/// <summary>Interface version 1: the complete authorized selection and explicit parameters, with no ambient access.</summary>
public sealed record DocumentGenerationContext(int InterfaceVersion, DocumentGeneratorDescriptor Generator,
    ImmutableDictionary<string, string> Parameters, string Selection, string? Region,
    ImmutableArray<DocumentSourceSnapshot> Sources)
{
    /// <summary>A digest of this member's own source identities and parameters, independent of its enclosing output commit.</summary>
    public string InputDigest => ContentHash.Sha256(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(JsonSerializer.SerializeToElement(new
    {
        contract = Generator.ContractVersion,
        generator = Generator.Id,
        implementation = Generator.Implementation,
        implementationHash = Generator.ImplementationHash,
        parameters = Parameters,
        selection = Selection,
        region = Region,
        sources = Sources.OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.Identity, StringComparer.Ordinal).Select(s => new
        {
            kind = s.Kind, identity = s.Identity, hash = s.Hash, repository = s.Repository, path = s.Path,
            bytes = s.Kind == "record" ? (int?)null : s.Bytes.Length, type = s.RecordType, supersedes = s.Supersedes, subject = s.Subject,
        }),
    }))));
}

/// <summary>Success requires complete selection; unavailable and error never carry partial rendered output.</summary>
public enum DocumentGenerationStatus
{
    /// <summary>All required inputs were selected and rendered.</summary>
    Success,
    /// <summary>A required source or implementation cannot be obtained.</summary>
    Unavailable,
    /// <summary>The request, implementation or generated result violates its contract.</summary>
    Error,
}

/// <summary>One rendition, or a safe reason why it could not be produced.</summary>
public sealed record DocumentRenderResult(DocumentGenerationStatus Status, string? Content, string? Reason)
{
    /// <summary>A complete rendition.</summary>
    public static DocumentRenderResult Success(string content) => new(DocumentGenerationStatus.Success, content, null);
    /// <summary>A missing capability or source, never an empty successful list.</summary>
    public static DocumentRenderResult Unavailable(string reason) => new(DocumentGenerationStatus.Unavailable, null, reason);
    /// <summary>A failed generation with no output.</summary>
    public static DocumentRenderResult Error(string reason) => new(DocumentGenerationStatus.Error, null, reason);
}

/// <summary>A complete authorized snapshot from an adapter, including the exact repository commit inspected.</summary>
public sealed record DocumentSourceSelection(DocumentGenerationStatus Status, ImmutableArray<DocumentSourceSnapshot> Sources, string? Commit, string? Reason)
{
    /// <summary>Selects all revisions of only the descriptor's declared record types; callers authorize before this boundary.</summary>
    public static DocumentSourceSelection Records(EstateModel model, DocumentGeneratorDescriptor descriptor, string? commit = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (model.Findings.HasErrors)
            return new(DocumentGenerationStatus.Unavailable, [], commit, "The selected estate records could not be verified.");
        if (descriptor.SourceKinds.Any(k => k != "record"))
            return new(DocumentGenerationStatus.Unavailable, [], commit, "A required source adapter is not installed.");
        return new(DocumentGenerationStatus.Success,
            [.. model.Records.Where(r => descriptor.RecordTypes.Contains(r.Type)).OrderBy(r => r.Type, StringComparer.Ordinal)
                .ThenBy(r => r.Id, StringComparer.Ordinal).Select(DocumentSourceSnapshot.Record)], commit, null);
    }
}

/// <summary>One expected member with its exact source basis; this is derived response data, never another ledger.</summary>
public sealed record DocumentMember(string Path, DocumentGenerationContext Context);

/// <summary>A complete member inventory or an explicit refusal, with no partial inventory on failure.</summary>
public sealed record DocumentMemberSelection(DocumentGenerationStatus Status, ImmutableArray<DocumentMember> Members, string? Commit, string? Reason);

/// <summary>Validates declared inputs and enumerates expected paths from complete selected source snapshots.</summary>
public static class DocumentGeneration
{
    private static readonly string[] TitleFields = ["title", "key", "token"];
    /// <summary>The version of the immutable renderer interface.</summary>
    public const int InterfaceVersion = 1;

    /// <summary>Canonical selection order, independent of adapters and filesystem enumeration.</summary>
    public static ImmutableArray<DocumentSourceSnapshot> Order(ImmutableArray<DocumentSourceSnapshot> sources) =>
        [.. sources.OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.RecordType, StringComparer.Ordinal)
            .ThenBy(s => s.At, StringComparer.Ordinal).ThenBy(s => s.Identity, StringComparer.Ordinal)];

    /// <summary>Validates exact identities and file hashes before the selected content crosses into a renderer.</summary>
    public static string? ValidateSources(DocumentGeneratorDescriptor descriptor, ImmutableArray<DocumentSourceSnapshot> sources)
    {
        if (sources.IsDefault || sources.Any(s => !descriptor.SourceKinds.Contains(s.Kind) || !ContentHash.IsValid(s.Hash)
            || string.IsNullOrWhiteSpace(s.Identity) || s.Bytes.IsDefault
            || (s.Kind == "record" ? !descriptor.RecordTypes.Contains(s.RecordType ?? string.Empty) || !ValidRecord(s)
                : s.Path is null || !DocumentPatterns.IsSafePath(s.Path) || ContentHash.Sha256(s.Bytes.AsSpan()) != s.Hash))
            || sources.Select(s => (s.Kind, s.Identity)).Distinct().Count() != sources.Length)
            return "The source selection is inconsistent with the declared contract.";
        return null;
    }

    private static bool ValidRecord(DocumentSourceSnapshot source)
    {
        if (source.Body is not { ValueKind: JsonValueKind.Object } body || source.RecordFormat is not (>= 1 and <= RecordFormats.Current)
            || !Identity.Ulid.IsValid(source.Identity) || !Identity.Ulid.IsValid(source.Author) || !RecordParser.IsTimestamp(source.At)
            || source.Repository is not null || !source.Bytes.IsEmpty) return false;
        var basis = new JsonObject
        {
            ["format"] = RecordFormats.Identifier(source.RecordFormat.Value), ["type"] = source.RecordType,
            ["id"] = source.Identity, ["at"] = source.At, ["by"] = source.Author, ["body"] = JsonNode.Parse(body.GetRawText()),
        };
        if (source.Supersedes is not null) basis["supersedes"] = source.Supersedes;
        if (source.Subject is { } subject)
        {
            var reference = new JsonObject { ["type"] = subject.Type, ["id"] = subject.Id };
            if (subject.Hash is not null) reference["hash"] = subject.Hash;
            basis["subject"] = reference;
        }
        string title = TitleFields.Select(k => body.TryGetProperty(k, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null).FirstOrDefault(t => t is not null) ?? "item";
        return title == source.Title && source.Path == "records/" + source.RecordType + "/" + source.At![..7] + "/" + source.Identity + ".json"
            && ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(basis))) == source.Hash;
    }

    /// <summary>Enumerates a concrete file or a per-revision register without looking at output files or their pins.</summary>
    public static DocumentMemberSelection Members(RecordEnvelope declaration, DocumentSourceSelection sources)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Status != DocumentGenerationStatus.Success) return new(sources.Status, [], sources.Commit, sources.Reason);
        if (DocumentChecks.Generator(declaration) is not (string id, IReadOnlyList<string> inputs)
            || DocumentGenerators.Find(id)?.Descriptor is not { } descriptor)
            return Unavailable("The versioned generator is not installed.");

        var parameters = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (string input in inputs)
        {
            int equal = input.IndexOf('=', StringComparison.Ordinal);
            if (equal < 1 || !parameters.TryAdd(input[..equal], input[(equal + 1)..]))
                return Error("Inputs must be unique named values.");
        }

        if (!parameters.TryGetValue("format", out string? version)
            || version != descriptor.ContractVersion.ToString(CultureInfo.InvariantCulture))
            return Unavailable("The requested output contract version is unavailable.");
        foreach ((string name, string value) in parameters)
        {
            if (name == "format") continue;
            if (!descriptor.Inputs.TryGetValue(name, out ImmutableArray<string> allowed) || !allowed.Contains(value))
                return Error("An input name or value is not supported by this generator contract.");
        }

        if (descriptor.Inputs.Keys.Any(k => !parameters.ContainsKey(k))) return Error("A required named input is missing.");
        string selection = parameters.GetValueOrDefault("selection", "all-revisions");
        if (!descriptor.Selections.Contains(selection)) return Unavailable("The requested selection is unavailable.");
        if (parameters.GetValueOrDefault("audience") != descriptor.Audience) return Unavailable("The requested audience selection is unavailable.");
        if (ValidateSources(descriptor, sources.Sources) is { } error) return Error(error);
        if (sources.Sources.Any(s => s.RecordType is "document-revision" or "document-review" or "living-document" or "verification"))
            return Error("A committed rendition cannot depend on output pins, declarations, clearance or check results.");

        string? pattern = EstateModel.BodyString(declaration, "pathPattern");
        var context = new DocumentGenerationContext(InterfaceVersion, descriptor, parameters.ToImmutable(), selection, null, Order(sources.Sources));
        if (pattern is null)
        {
            string? path = EstateModel.BodyString(declaration, "path");
            return path is null || !DocumentPatterns.IsSafePath(path) ? Error("A safe output path is required.")
                : new(DocumentGenerationStatus.Success, [new(path, context)], sources.Commit, null);
        }

        if (!DocumentPatterns.IsValid(pattern) || descriptor.PathMapping != "record-revision" || selection != "all-revisions")
            return Error("This generator selection does not support the declared path mapping.");
        var members = ImmutableArray.CreateBuilder<DocumentMember>();
        foreach (DocumentSourceSnapshot source in sources.Sources)
        {
            if (source.Kind != "record" || source.At is null || source.Title is null || !RecordParser.IsTimestamp(source.At))
                return Error("Each register member requires an exact record revision, UTC time and title.");
            string path = DocumentPatterns.Member(pattern, source.Identity,
                DateTimeOffset.ParseExact(source.At, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), source.Title);
            members.Add(new(path, context with { Selection = "item", Sources = [source] }));
        }

        if (members.Select(m => m.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
            return Error("Register members collide on a portable filesystem.");
        return new(DocumentGenerationStatus.Success, [.. members.OrderBy(m => m.Path, StringComparer.Ordinal)], sources.Commit, null);

        DocumentMemberSelection Error(string reason) => new(DocumentGenerationStatus.Error, [], sources.Commit, reason);
        DocumentMemberSelection Unavailable(string reason) => new(DocumentGenerationStatus.Unavailable, [], sources.Commit, reason);
    }
}
