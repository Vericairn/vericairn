// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>All records of an estate, indexed, with the findings produced while loading them.</summary>
public sealed class EstateModel
{
    private ILookup<string, RecordEnvelope> recordsByType;
    private Lazy<ILookup<(string Type, string? Subject), RecordEnvelope>> recordsBySubject;
    private Lazy<ILookup<string, (int Position, RecordEnvelope Record)>> decisionsByAnswer;
    private Lazy<ILookup<string, (int Position, RecordEnvelope Record)>> workOrdersByInput;
    private readonly object trustGate = new();
    private EstateTrust? applied;
    private EstateTrust? judged;
    private bool judging;
    private IReadOnlySet<string> notCounting = new HashSet<string>(StringComparer.Ordinal);
    private AuthorityReplay? authority;
    private Dictionary<string, CurrentUse>? currentWorkOrderKeys;
    private Dictionary<string, CurrentUse>? currentOperationIdentities;
    private IReadOnlyList<LivingDocuments.State>? livingDocuments;
    private Lazy<RecordEnvelope?>? resultAcceptancesBegan;
    private Lazy<RecordEnvelope?>? overlapCheckBegan;
    private Lazy<IReadOnlyList<AssignmentOverlaps.Overlap>>? recordOverlaps;
    private ILookup<string, RecordEnvelope>? workOrdersByCarrier;

    /// <summary>Creates a model.</summary>
    public EstateModel(string root, IReadOnlyList<RecordEnvelope> records, IReadOnlyDictionary<string, RecordEnvelope> byId, IReadOnlyDictionary<string, string> supersededBy, FindingList findings)
    {
        Root = root;
        Records = records;
        // Projections ask for a few types repeatedly. Index once while loading so a Home report does not walk unrelated
        // history for every empty section. Lookup preserves each type's original path order and all historical revisions.
        (recordsByType, recordsBySubject, decisionsByAnswer, workOrdersByInput) = Index(records);
        ById = byId;
        SupersededBy = supersededBy;
        Findings = findings;
        CountingRecords = records;
    }

    private static (ILookup<string, RecordEnvelope>, Lazy<ILookup<(string Type, string? Subject), RecordEnvelope>>, Lazy<ILookup<string, (int Position, RecordEnvelope Record)>>,
        Lazy<ILookup<string, (int Position, RecordEnvelope Record)>>) Index(IReadOnlyList<RecordEnvelope> records)
    {
        ILookup<string, RecordEnvelope> byType = records.ToLookup(r => r.Type, StringComparer.Ordinal);
        return (byType,
            new(() => byType.SelectMany(group => group).ToLookup(r => (r.Type, r.Subject?.Id))),
            new(() => byType["decision"].Select((record, position) => (Position: position, Record: record))
                .SelectMany(item => AnswerIds(item.Record).Distinct(StringComparer.Ordinal).Select(id => (Id: id, Item: item)))
                .ToLookup(x => x.Id, x => x.Item, StringComparer.Ordinal)),
            new(() => byType["work-order"].Select((record, position) => (Position: position, Record: record))
                .SelectMany(item => GoalRules.Inputs(item.Record).Distinct(StringComparer.Ordinal).Select(id => (Id: id, Item: item)))
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

    /// <summary>
    /// From 0.9.4 (FIX-148): the loader's use of the signature tables while it judged this model, naming the tables and the context its
    /// verifications are counted in; null for a model judged outside a loader, which reads the process's tables uncounted.
    /// </summary>
    internal Signing.SignatureTables.Judgment? Signatures { get; init; }

    /// <summary>The repository paths the loader read, for telling an unreadable witnessed file from an absent one.</summary>
    internal IReadOnlySet<string>? FilePaths { get; set; }

    /// <summary>
    /// Whether an estate that has not adopted history checkpoints reads every record's roles and grants at the record's own time, as an
    /// adopted estate reads the records no adoption witnesses: what adoption's check compares with the reading of today to list what the
    /// at-time reading changes (design/signed-history.md § 10). Never set on a model a rule or a write reads.
    /// </summary>
    internal bool AuthorityAtRecordTime { get; init; }

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
        authority = null;
        currentWorkOrderKeys = null;
        currentOperationIdentities = null;
        livingDocuments = null;
        resultAcceptancesBegan = null;
        overlapCheckBegan = null;
        recordOverlaps = null;
        workOrdersByCarrier = null;
        if (excluded.Count == 0) return;
        notCounting = excluded;
        CountingRecords = Records.Where(r => !excluded.Contains(r.Id)).ToList();
        (recordsByType, recordsBySubject, decisionsByAnswer, workOrdersByInput) = Index(CountingRecords);
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
            livingDocuments = null;
            resultAcceptancesBegan = null;
            overlapCheckBegan = null;
            recordOverlaps = null;
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
    public IEnumerable<RecordEnvelope> OfType(string type)
    {
        Interlocked.Increment(ref typeReads.GetOrAdd(type, static _ => new StrongBox<long>()).Value);
        return recordsByType[type];
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, StrongBox<long>> typeReads = new(StringComparer.Ordinal);

    /// <summary>
    /// How many times each record type has been read whole from this model (<see cref="OfType"/>, <see cref="Current"/>): a
    /// deterministic count of the whole-estate passes a judgment and the views derived from it make, for cost tests (FIX-136).
    /// </summary>
    internal IReadOnlyDictionary<string, long> TypeReads => typeReads.ToDictionary(t => t.Key, t => Interlocked.Read(ref t.Value.Value), StringComparer.Ordinal);

    /// <summary>Current records of a type.</summary>
    public IEnumerable<RecordEnvelope> Current(string type) => OfType(type).Where(IsCurrent);

    /// <summary>
    /// Whether a current work order with another id than <paramref name="record"/>'s uses <paramref name="key"/> (FIX-107): read from a
    /// map of the current work orders' keys, built in one pass the first time it is asked, so judging every work order's key costs time
    /// in proportion to their number.
    /// </summary>
    internal bool AnotherCurrentWorkOrderUses(string key, RecordEnvelope record) => AnotherCurrentUses(ref currentWorkOrderKeys, "work-order", "key", key, record);

    /// <summary>
    /// Whether a current operation with another id than <paramref name="record"/>'s has <paramref name="identity"/> (FIX-136): read from a
    /// map of the current operations' identities built in one pass, as <see cref="AnotherCurrentWorkOrderUses"/> reads keys.
    /// </summary>
    internal bool AnotherCurrentOperationUses(string identity, RecordEnvelope record) => AnotherCurrentUses(ref currentOperationIdentities, "operation", "identity", identity, record);

    private bool AnotherCurrentUses(ref Dictionary<string, CurrentUse>? map, string type, string field, string value, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(record);
        Dictionary<string, CurrentUse> uses = LazyInitializer.EnsureInitialized(ref map, () =>
        {
            var built = new Dictionary<string, CurrentUse>(StringComparer.Ordinal);
            foreach (RecordEnvelope current in Current(type))
            {
                if (!current.Body.TryGetProperty(field, out JsonElement named) || named.ValueKind != JsonValueKind.String) continue;
                string text = named.GetString()!;
                if (!built.TryGetValue(text, out CurrentUse seen)) built[text] = new(current.Id, false);
                else if (!seen.Shared && seen.First != current.Id) built[text] = seen with { Shared = true };
            }

            return built;
        });
        return uses.TryGetValue(value, out CurrentUse found) && (found.Shared || found.First != record.Id);
    }

    /// <summary>
    /// Every living document's state (<see cref="LivingDocuments.Derive"/>), derived the first time it is asked once the model is loaded
    /// and kept with it (FIX-136): status, its checks and each flow predicate that reads a document's state read the same states instead
    /// of deriving every document again.
    /// </summary>
    internal IReadOnlyList<LivingDocuments.State> LivingDocumentStates(Func<EstateModel, IReadOnlyList<LivingDocuments.State>> derive) =>
        LazyInitializer.EnsureInitialized(ref livingDocuments, () =>
        {
            Interlocked.Increment(ref livingDocumentDerivations);
            return derive(this);
        });

    private long livingDocumentDerivations;

    /// <summary>How many times every living document's state has been derived for this model: a deterministic count for cost tests.</summary>
    internal long LivingDocumentDerivations => Interlocked.Read(ref livingDocumentDerivations);

    /// <summary>
    /// The acceptance that marks when acceptances began to name a result's exact hash (<see cref="EstateStatus.ResultAcceptancesBegan"/>),
    /// found the first time it is asked once the model is loaded and kept with it, as the living documents' states are.
    /// </summary>
    internal RecordEnvelope? ResultAcceptancesBegan(Func<EstateModel, RecordEnvelope?> find) =>
        LazyInitializer.EnsureInitialized(ref resultAcceptancesBegan, () => new Lazy<RecordEnvelope?>(() => find(this))).Value;

    /// <summary>
    /// The first record the overlap check's build wrote (<see cref="AssignmentOverlaps.Began"/>), found the first time it is asked once
    /// the model is loaded and kept with it.
    /// </summary>
    internal RecordEnvelope? OverlapCheckBegan(Func<EstateModel, RecordEnvelope?> find) =>
        LazyInitializer.EnsureInitialized(ref overlapCheckBegan, () => new Lazy<RecordEnvelope?>(() => find(this))).Value;

    /// <summary>
    /// The overlaps among active assignments as the records alone show them (<see cref="AssignmentOverlaps.Overlaps(EstateModel, IRepositoryView?)"/>
    /// with no clone), derived the first time status or progress asks once the model is loaded and kept with it.
    /// </summary>
    internal IReadOnlyList<AssignmentOverlaps.Overlap> RecordOverlaps(Func<EstateModel, IReadOnlyList<AssignmentOverlaps.Overlap>> derive) =>
        LazyInitializer.EnsureInitialized(ref recordOverlaps, () => new Lazy<IReadOnlyList<AssignmentOverlaps.Overlap>>(() => derive(this))).Value;

    /// <summary>The first current record using a value, and whether one with another id uses it too.</summary>
    private readonly record struct CurrentUse(string First, bool Shared);

    /// <summary>
    /// The estate's first charter, the one every later revision supersedes: the charter the manifest names, never displaced by another that
    /// supersedes nothing (validators flag that one); without a manifest, the first such charter by id.
    /// </summary>
    public RecordEnvelope? FirstEstate => Trust.FirstEstate;

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

    /// <summary>
    /// From 0.9.4 (FIX-149): the current work orders placed in a release carrier, which a placement names by its identity, in path
    /// order, the carrier itself never among them. Read from one index of the current work orders by their placement's carrier, built once
    /// for a judged model, so counting each carrier's placements reads the work orders once whatever the number of carriers.
    /// </summary>
    internal IEnumerable<RecordEnvelope> PlacedIn(RecordEnvelope carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ILookup<string, RecordEnvelope> index = LazyInitializer.EnsureInitialized(ref workOrdersByCarrier, () => Current("work-order")
            .Select(order => (Order: order, Carrier: CarrierOf(order)))
            .Where(placed => placed.Carrier is not null && placed.Carrier != Chain(placed.Order)[0])
            .ToLookup(placed => placed.Carrier!, placed => placed.Order, StringComparer.Ordinal));
        return index[Chain(carrier)[0]];

        string? CarrierOf(RecordEnvelope order) =>
            order.Body.TryGetProperty("placement", out JsonElement placement) && placement.ValueKind == JsonValueKind.Object
                && placement.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "release"
                && placement.TryGetProperty("carrier", out JsonElement named) && named.ValueKind == JsonValueKind.String
                ? LivingDocuments.ProductIdentity(this, named.GetString()!)
                : null;
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

    /// <summary>The latest counting principal record describing a principal (principals are referenced by their first record's id); null when none counts.</summary>
    public RecordEnvelope? CurrentPrincipal(string? id)
    {
        if (id is null || !ById.TryGetValue(id, out RecordEnvelope? record) || record.Type != "principal")
        {
            return null;
        }

        // The newest revision of the line that counts and that its author may write (Authority); a line with none describes no principal.
        return Authority.Current(Chain(record)[0]);
    }

    /// <summary>
    /// Which principal and authority records that count give roles and scopes: read in time order from the root operator, each only when
    /// its author may write it at that point (<see cref="AuthorityReplay"/>), so no record authorizes itself.
    /// </summary>
    internal AuthorityReplay Authority => authority ??= AuthorityReplay.Run(OfType("principal"), OfType("authority"), RootOperator, r => Chain(r)[0],
        id => ById.TryGetValue(id, out RecordEnvelope? found) ? found : null);

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

    /// <summary>
    /// The time a record's authority is read at (design/signed-history.md § 8): in an estate that has adopted history checkpoints, the
    /// record's own time, or the adoption's for a record the adoption checkpoint witnesses, since such a record keeps the reading it had
    /// then; null in an estate that has not adopted, where roles and grants are read as they are now.
    /// </summary>
    internal DateTimeOffset? AuthorityTime(RecordEnvelope record)
    {
        EstateTrust trust = Trust;
        return trust.Adopted ? trust.History.AdoptedAt(record) ?? record.AtTime : AuthorityAtRecordTime ? record.AtTime : null;
    }

    /// <summary>
    /// The principal record describing a principal as it stood at a record's place: in an estate that has adopted history checkpoints,
    /// the latest revision that counts dated at or before the record's authority time, and none before the principal's first record;
    /// otherwise the current one.
    /// </summary>
    public RecordEnvelope? PrincipalAt(string? id, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (AuthorityTime(record) is not { } at) return CurrentPrincipal(id);
        return id is not null && ById.TryGetValue(id, out RecordEnvelope? principal) && principal.Type == "principal" ? RevisionAt(principal, at) : null;
    }

    /// <summary>Roles a principal held at a record's place (<see cref="PrincipalAt"/>).</summary>
    public IReadOnlyList<string> RolesAt(string? id, RecordEnvelope record)
    {
        RecordEnvelope? principal = PrincipalAt(id, record);
        return principal is not null && principal.Body.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array
            ? roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToList()
            : [];
    }

    /// <summary>
    /// True when the principal held the role at a record's place: as it stood at the record's authority time in an estate that has adopted
    /// history checkpoints, so a later revision neither gives nor takes a role for an earlier record; as it is now in any other estate.
    /// </summary>
    public bool HasRoleAt(string? id, string role, RecordEnvelope record) => RolesAt(id, record).Contains(role, StringComparer.Ordinal);

    /// <summary>
    /// True when a grant gave the principal the scope at a record's place. In an estate that has adopted history checkpoints each grant is
    /// read as it stood at the record's authority time: the latest revision that counts dated at or before it, unexpired when the record
    /// was authored, and not closed by a closing that does not witness the record; so a later grant authorizes nothing earlier and a
    /// later narrowing withdraws nothing already written. In any other estate, as <see cref="HasScope"/> reads it at the record's time.
    /// </summary>
    public bool HasScopeAt(string? id, string scope, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (AuthorityTime(record) is not { } at) return HasScope(id, scope, record.AtTime);
        if (id is null) return false;
        EstateTrust trust = Trust;
        string time = RecordParser.FormatTimestamp(at);
        foreach (string line in Authority.GrantLines)
        {
            if (Authority.At(line, time) is not { } grant || BodyString(grant, "grantee") != id) continue;
            if (grant.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
                scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope) &&
                !Expired(grant, record.AtTime) && !trust.GrantClosedFor(grant, record, anchorsCount: true))
                return true;
        }

        return false;
    }

    /// <summary>The revision of a line in force at a time: its latest record that its author may write (Authority) dated at or before it.</summary>
    private RecordEnvelope? RevisionAt(RecordEnvelope record, DateTimeOffset at) => Authority.At(Chain(record)[0], RecordParser.FormatTimestamp(at));

    /// <summary>
    /// True when the latest authority record of a line that its author may write (<see cref="Authority"/>) grants the principal the scope,
    /// and, at a given time, has not expired by then.
    /// </summary>
    public bool HasScope(string? id, string scope, DateTimeOffset? asOf = null) =>
        id is not null && Authority.GrantLines.Select(Authority.Current).OfType<RecordEnvelope>().Any(a =>
            a.Body.TryGetProperty("grantee", out JsonElement grantee) && grantee.GetString() == id &&
            a.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
            scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope) &&
            (asOf is null || !Expired(a, asOf.Value)));

    /// <summary>
    /// The principal record describing a principal just before a record, in time order with ids breaking ties: its latest revision that
    /// its author may write (<see cref="Authority"/>) read before the record; null when none is.
    /// </summary>
    internal RecordEnvelope? PrincipalBefore(string? id, RecordEnvelope record) =>
        id is not null && ById.TryGetValue(id, out RecordEnvelope? principal) && principal.Type == "principal" ? Authority.Before(Chain(principal)[0], record) : null;

    /// <summary>
    /// True when a grant its author may write (<see cref="Authority"/>), as its line stood just before a record in time order with ids
    /// breaking ties, gave the principal the scope and had not expired when the record was authored: a later grant authorizes nothing
    /// earlier, and a revision read after the record withdraws nothing from it.
    /// </summary>
    internal bool ScopeBefore(string? id, string scope, RecordEnvelope record) =>
        id is not null && Authority.GrantLines.Select(line => Authority.Before(line, record)).OfType<RecordEnvelope>().Any(grant =>
            BodyString(grant, "grantee") == id && grant.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
            scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope) && !Expired(grant, record.AtTime));

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

    /// <summary>
    /// The work orders that count of which a revision names one of <paramref name="ids"/> among its inputs, each revision once, in path
    /// order (FIX-136): read from an index of every work order's inputs built once per model, so finding a goal's work does not read every
    /// work order again for each goal and each assignment judged.
    /// </summary>
    internal IEnumerable<RecordEnvelope> WorkOrdersNaming(IEnumerable<string> ids) => ids
        .SelectMany(id => workOrdersByInput.Value[id]).DistinctBy(item => item.Position).OrderBy(item => item.Position)
        .Select(item => item.Record);

    private static IEnumerable<string> AnswerIds(RecordEnvelope decision) =>
        decision.Body.TryGetProperty("answers", out JsonElement answers) && answers.ValueKind == JsonValueKind.Array
            ? answers.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!) : [];

    /// <summary>Reads a string field from a body, or null.</summary>
    public static string? BodyString(RecordEnvelope record, string name) =>
        record is not null && record.Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
