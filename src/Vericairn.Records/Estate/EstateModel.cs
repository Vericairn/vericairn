// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>All records of an estate, indexed, with the findings produced while loading them.</summary>
public sealed class EstateModel
{
    private ILookup<string, RecordEnvelope> recordsByType;
    private Lazy<ILookup<(string Type, string? Subject), RecordEnvelope>> recordsBySubject;
    private Lazy<ILookup<string, (int Position, RecordEnvelope Record)>> decisionsByAnswer;
    private readonly object trustGate = new();
    private EstateTrust? applied;
    private EstateTrust? judged;
    private bool judging;
    private IReadOnlySet<string> notCounting = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Creates a model.</summary>
    public EstateModel(string root, IReadOnlyList<RecordEnvelope> records, IReadOnlyDictionary<string, RecordEnvelope> byId, IReadOnlyDictionary<string, string> supersededBy, FindingList findings)
    {
        Root = root;
        Records = records;
        // Projections ask for a few types repeatedly. Index once while loading so a Home report does not walk unrelated
        // history for every empty section. Lookup preserves each type's original path order and all historical revisions.
        (recordsByType, recordsBySubject, decisionsByAnswer) = Index(records);
        ById = byId;
        SupersededBy = supersededBy;
        Findings = findings;
        CountingRecords = records;
    }

    private static (ILookup<string, RecordEnvelope>, Lazy<ILookup<(string Type, string? Subject), RecordEnvelope>>, Lazy<ILookup<string, (int Position, RecordEnvelope Record)>>) Index(IReadOnlyList<RecordEnvelope> records)
    {
        ILookup<string, RecordEnvelope> byType = records.ToLookup(r => r.Type, StringComparer.Ordinal);
        return (byType,
            new(() => byType.SelectMany(group => group).ToLookup(r => (r.Type, r.Subject?.Id))),
            new(() => byType["decision"].Select((record, position) => (Position: position, Record: record))
                .SelectMany(item => AnswerIds(item.Record).Distinct(StringComparer.Ordinal).Select(id => (Id: id, Item: item)))
                .ToLookup(x => x.Id, x => x.Item, StringComparer.Ordinal)));
    }

    /// <summary>Repository root.</summary>
    public string Root { get; }

    /// <summary>All parsed records in path order, those that do not count among them: they are kept as evidence and reported.</summary>
    public IReadOnlyList<RecordEnvelope> Records { get; }

    /// <summary>
    /// The records that count, in path order (design/signed-history.md § 6). Type and subject lookups, currency and supersession read
    /// only these, so no rule that admits, authorizes, accepts, clears or holds reads a record that does not count.
    /// </summary>
    public IReadOnlyList<RecordEnvelope> CountingRecords { get; private set; }

    /// <summary>Records by id, those that do not count among them.</summary>
    public IReadOnlyDictionary<string, RecordEnvelope> ById { get; }

    /// <summary>Superseded record id to the id of its counting successor; a record that does not count supersedes nothing.</summary>
    public IReadOnlyDictionary<string, string> SupersededBy { get; private set; }

    /// <summary>Reads an object of the estate by content hash, or null when it is not held: what checkpoint inventories are read through.</summary>
    internal Func<string, byte[]?>? ObjectReader { get; set; }

    /// <summary>The repository paths the loader read, for telling an unreadable witnessed file from an absent one.</summary>
    internal IReadOnlySet<string>? FilePaths { get; set; }

    /// <summary>
    /// Whether a record counts: a record that does not, such as an unanchored unsigned record under required signatures in an estate
    /// that has adopted history checkpoints, a record whose signature does not verify, or a witnessed record present with another hash,
    /// is reported and kept, and grants nothing.
    /// </summary>
    public bool Counts(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return applied is null ? Trust.Counts(record) : !notCounting.Contains(record.Id) && applied.Counts(record);
    }

    /// <summary>The judgment of this model's signatures, anchors and history, made once.</summary>
    internal EstateTrust Trust
    {
        get
        {
            if (applied is not null) return applied;
            lock (trustGate)
            {
                if (judged is not null) return judged;
                if (judging) throw new InvalidOperationException("The estate's trust is read while it is being judged.");
                judging = true;
                try
                {
                    judged = EstateTrust.Build(this, Manifest, ObjectReader, FilePaths);
                }
                finally
                {
                    judging = false;
                }

                return judged;
            }
        }
    }

    /// <summary>
    /// Keeps a judgment made from every record and, when some records do not count, rebuilds the type and subject lookups and the
    /// supersession map from those that do. The loader calls it before any rule reads the model.
    /// </summary>
    internal void ApplyCounting(EstateTrust trust, IReadOnlySet<string> excluded, IReadOnlyDictionary<string, string> countingSupersededBy)
    {
        applied = trust;
        if (excluded.Count == 0) return;
        notCounting = excluded;
        CountingRecords = Records.Where(r => !excluded.Contains(r.Id)).ToList();
        (recordsByType, recordsBySubject, decisionsByAnswer) = Index(CountingRecords);
        SupersededBy = countingSupersededBy;
    }

    /// <summary>Findings from loading and validating records.</summary>
    public FindingList Findings { get; }

    /// <summary>The manifest, when it loaded.</summary>
    public EstateManifest? Manifest
    {
        get => manifest;
        set
        {
            manifest = value;
            // The record format the manifest states decides which admissions, and so which records, count.
            lock (trustGate)
            {
                if (!ReferenceEquals(judged?.Manifest, value)) judged = null;
            }
        }
    }

    private EstateManifest? manifest;

    /// <summary>True when the record counts and no later counting record supersedes it.</summary>
    public bool IsCurrent(RecordEnvelope record) => record is not null && !notCounting.Contains(record.Id) && !SupersededBy.ContainsKey(record.Id);

    /// <summary>All records of a type.</summary>
    public IEnumerable<RecordEnvelope> OfType(string type) => recordsByType[type];

    /// <summary>Current records of a type.</summary>
    public IEnumerable<RecordEnvelope> Current(string type) => OfType(type).Where(IsCurrent);

    /// <summary>The estate's first charter, the one every later revision supersedes (the lowest id wins when several do; validators flag that).</summary>
    public RecordEnvelope? FirstEstate => applied is not null ? applied.FirstEstate : RawFirstEstate();

    /// <summary>The first charter among every record read, before any is judged: the root of trust.</summary>
    internal RecordEnvelope? RawFirstEstate() => OfType("estate").Where(r => r.Supersedes is null).OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The root operator: the principal the first charter names. A later revision cannot change it.</summary>
    public string? RootOperator => FirstEstate is { } first ? BodyString(first, "operator") : null;

    /// <summary>
    /// The current estate charter: the latest revision along the first charter's line that counts. While the charter a revision
    /// supersedes requires signatures, the revision counts only when the root operator signs it with an admitted device, so an unsigned
    /// or foreign revision changes nothing (<see cref="SignatureChecks.CharterRevisionCounts"/>). An estate without a first charter
    /// falls back to the lowest current one, which validators flag.
    /// </summary>
    public RecordEnvelope? CurrentEstate => Trust.Charter;

    /// <summary>Follows the supersedes chain from a record to its latest successor.</summary>
    public RecordEnvelope Latest(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        RecordEnvelope current = record;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (SupersededBy.TryGetValue(current.Id, out string? next) && ById.TryGetValue(next, out RecordEnvelope? successor) && seen.Add(successor.Id))
        {
            current = successor;
        }

        return current;
    }

    /// <summary>All record ids in this record's supersedes chain, earliest first, ending with the record itself.</summary>
    public IReadOnlyList<string> Chain(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var ids = new List<string> { record.Id };
        var seen = new HashSet<string>(StringComparer.Ordinal) { record.Id };
        RecordEnvelope current = record;
        while (current.Supersedes is not null && ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && seen.Add(previous.Id))
        {
            ids.Insert(0, previous.Id);
            current = previous;
        }

        return ids;
    }

    /// <summary>The latest principal record describing a principal (principals are referenced by their first record's id).</summary>
    public RecordEnvelope? CurrentPrincipal(string? id)
    {
        if (id is null || !ById.TryGetValue(id, out RecordEnvelope? record) || record.Type != "principal")
        {
            return null;
        }

        return Latest(record);
    }

    /// <summary>Roles the principal currently holds.</summary>
    public IReadOnlyList<string> RolesOf(string? id)
    {
        RecordEnvelope? principal = CurrentPrincipal(id);
        if (principal is null || !principal.Body.TryGetProperty("roles", out JsonElement roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToList();
    }

    /// <summary>True when the principal currently holds the role.</summary>
    public bool HasRole(string? id, string role) => RolesOf(id).Contains(role, StringComparer.Ordinal);

    /// <summary>True when a current authority record grants the principal the scope, and, at a given time, has not expired by then.</summary>
    public bool HasScope(string? id, string scope, DateTimeOffset? asOf = null) =>
        id is not null && Current("authority").Any(a =>
            a.Body.TryGetProperty("grantee", out JsonElement grantee) && grantee.GetString() == id &&
            a.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
            scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope) &&
            (asOf is null || !Expired(a, asOf.Value)));

    /// <summary>True when a grant states an expiry (record format 2) at or before the time.</summary>
    public static bool Expired(RecordEnvelope grant, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return BodyString(grant, "expires") is { } expires &&
            DateTimeOffset.TryParseExact(expires, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset end) &&
            at >= end;
    }

    /// <summary>Records of a type whose subject is the given record id.</summary>
    public IEnumerable<RecordEnvelope> About(string subjectId, string type) => recordsBySubject.Value[(type, subjectId)];

    // An answer may name several revisions of the same ask. Keep each decision occurrence once, in source order,
    // and retain the current-record filter at query time, just as the scan did.
    internal IEnumerable<RecordEnvelope> DecisionsAnswering(IEnumerable<string> asks) => asks
        .SelectMany(id => decisionsByAnswer.Value[id]).DistinctBy(item => item.Position).OrderBy(item => item.Position)
        .Select(item => item.Record).Where(IsCurrent);

    private static IEnumerable<string> AnswerIds(RecordEnvelope decision) =>
        decision.Body.TryGetProperty("answers", out JsonElement answers) && answers.ValueKind == JsonValueKind.Array
            ? answers.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!) : [];

    /// <summary>Reads a string field from a body, or null.</summary>
    public static string? BodyString(RecordEnvelope record, string name) =>
        record is not null && record.Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
