// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Remote;

// A bounded reader, not an authority evaluator. Missing material makes the phone ineligible; the runtime judges the write.
internal sealed class RemoteDecisionReader(IEstateApi api)
{
    private const int MaterialLimit = 128;
    private const int TextLimit = 256 * 1024;
    private readonly Dictionary<string, RecordResponse> records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteDecisionEvidence> evidence = new(StringComparer.Ordinal);
    private readonly HashSet<string> reasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<RecordResponse>> current = new(StringComparer.Ordinal);
    private int bytes;

    internal async Task<RemoteDecisionReview> ReadAsync(string id, string principal, string asOf)
    {
        string? head = null;
        RecordResponse original = await RequiredAsync(id).ConfigureAwait(false);
        RecordResponse subject = await LatestAsync(original).ConfigureAwait(false);
        string root = await RootAsync(subject).ConfigureAwait(false);
        await AddAsync(subject).ConfigureAwait(false);
        StatusResponse status = await api.StatusAsync().ConfigureAwait(false);
        if (status.HasErrors) reasons.Add("The estate reports validation errors. Inspect them on the desktop before deciding.");
        RecordResponse person = await LatestAsync(await RequiredAsync(principal).ConfigureAwait(false)).ConfigureAwait(false);
        await AddAsync(person).ConfigureAwait(false);
        if (person.Type != "principal" || !Rows(Body(person), "roles").Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == "operator"))
            reasons.Add("This decision requires an operator. Inspect the authority and act on the desktop.");
        if (subject.Type == "ask")
        {
            AsksResponse asks = await api.AsksAsync(principal).ConfigureAwait(false);
            bool waiting = Rows(asks.Json, "principals").Where(p => Text(p, "id") == principal)
                .SelectMany(p => Rows(p, "delivered")).Any(a => Text(a, "revision") == subject.Id && Text(a, "hash") == subject.Hash);
            if (!waiting || Text(Body(subject), "to") != principal)
                reasons.Add("This exact ask is not in your current delivery. Refresh Attention; an answered, withdrawn or differently addressed ask cannot be answered here.");
            head = await BasisAsync().ConfigureAwait(false);
        }
        else if (subject.Type is "result" or "release")
        {
            if (subject.Type == "result" && !Rows(status.Json, "assignments").Any(a => Text(a, "latestResult") == subject.Id && Text(a, "state") == "awaiting review"))
                reasons.Add("This result is no longer awaiting review. Inspect its current assignment and outcome on the desktop.");
            if (subject.Type == "release" && Text(Body(subject), "status") != "candidate")
                reasons.Add("Only a current release candidate can be accepted here.");
            if ((await CurrentAsync("acceptance").ConfigureAwait(false)).Any(a => Pins(a, subject)))
                reasons.Add("An acceptance of this exact subject is already recorded. Inspect that outcome on the desktop.");
        }
        else reasons.Add("This subject's decision flow is available on the desktop.");

        var basis = new List<string>();
        foreach (RecordResponse review in (await CurrentAsync("review").ConfigureAwait(false)).Where(r => Pins(r, subject)))
        {
            basis.Add(review.Id);
            await AddAsync(review).ConfigureAwait(false);
        }
        foreach (RecordResponse verification in (await CurrentAsync("verification").ConfigureAwait(false)).Where(r => Pins(r, subject)))
            await AddAsync(verification).ConfigureAwait(false);
        return new(root, subject, asOf, records.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray(),
            evidence.Values.OrderBy(e => e.Hash, StringComparer.Ordinal).ToArray(), basis.Order(StringComparer.Ordinal).ToArray(), reasons.Order(StringComparer.Ordinal).ToArray())
        { Head = head };
    }

    // The runtime supplies the estate's basis: the phone cannot observe the repository, and never infers a commit from the software's pin.
    private async Task<string?> BasisAsync()
    {
        DecisionBasisResponse basis;
        try { basis = await api.DecisionBasisAsync().ConfigureAwait(false); }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null || exception is NotSupportedException or JsonException)
        {
            reasons.Add("This runtime did not supply the estate's current basis, which an answer needs. Update the runtime, or answer on the desktop.");
            return null;
        }
        if (!basis.Ok || basis.Head is not string head || !Patterns.Commit.IsMatch(head) || basis.BaseCommit != head || basis.Documents is null)
        {
            reasons.Add("The estate's current basis is unavailable" + (basis.Problem is null ? "." : ": " + basis.Problem.Message) + " An answer needs it; answer on the desktop.");
            return null;
        }
        if (basis.DocumentsChanged)
        {
            reasons.Add("Design documents changed since the estate's head " + head + ": " + string.Join(", ", basis.Documents.Select(d => d.Path)) +
                ". An answer with document changes is recorded on the desktop, which pins them.");
            return null;
        }
        return head;
    }

    private async Task AddAsync(RecordResponse record)
    {
        if (records.ContainsKey(record.Id)) return;
        if (records.Count >= MaterialLimit || bytes + Encoding.UTF8.GetByteCount(record.Envelope.GetRawText()) > TextLimit)
        {
            reasons.Add("The complete material exceeds this phone reader's capacity. Review it on the desktop; no shortened approval is offered.");
            return;
        }
        Validate(record);
        records.Add(record.Id, record);
        bytes += Encoding.UTF8.GetByteCount(record.Envelope.GetRawText());
        if (record.Type is "document-revision" or "resource" or "living-document" or "estate-policy" or "policy-exception")
            reasons.Add("The " + record.Type + " material needs its desktop reader. Its record alone is insufficient for a decision.");
        JsonElement subject = Property(record.Envelope, "subject");
        if (subject.ValueKind == JsonValueKind.Object) await ReferenceAsync(subject).ConfigureAwait(false);
        await ReferencesAsync(Body(record)).ConfigureAwait(false);
    }

    private async Task ReferencesAsync(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray()) await ReferencesAsync(item).ConfigureAwait(false);
            return;
        }
        if (value.ValueKind == JsonValueKind.String && Ulid.IsValid(value.GetString()))
        {
            await AddAsync(await LatestAsync(await RequiredAsync(value.GetString()!).ConfigureAwait(false)).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        if (Text(value, "id") is string id && Ulid.IsValid(id) && Text(value, "type") is not null)
        {
            await ReferenceAsync(value).ConfigureAwait(false);
            return;
        }
        // Evidence and outputs with hashes require readable bytes, not only a digest or an external location.
        if (Text(value, "hash") is string hash && Text(value, "ask") is null)
            await EvidenceAsync(hash).ConfigureAwait(false);
        if (Text(value, "location") is not null && Text(value, "hash") is null)
            reasons.Add("Some material is named only by a location. Inspect that evidence on the desktop.");
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Name is "sbom" or "notices" && property.Value.ValueKind == JsonValueKind.String)
                await EvidenceAsync(property.Value.GetString()!).ConfigureAwait(false);
            else if (property.Name != "hash") await ReferencesAsync(property.Value).ConfigureAwait(false);
        }
    }

    private async Task ReferenceAsync(JsonElement reference)
    {
        string id = Text(reference, "id") ?? throw new InvalidDataException("A material subject has no identity.");
        RecordResponse record = await RequiredAsync(id).ConfigureAwait(false);
        if (record.Type != Text(reference, "type") || Text(reference, "hash") is string hash && record.Hash != hash)
            throw new InvalidDataException("A material subject does not match its exact reference.");
        if (Text(reference, "hash") is null) record = await LatestAsync(record).ConfigureAwait(false);
        await AddAsync(record).ConfigureAwait(false);
        if (!record.Current)
        {
            reasons.Add("A named subject has been superseded. Review the original and current revisions on the desktop.");
            await AddAsync(await LatestAsync(record).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    private async Task EvidenceAsync(string hash)
    {
        if (evidence.ContainsKey(hash)) return;
        if (bytes >= TextLimit) { reasons.Add("The complete evidence needs the desktop reader."); return; }
        byte[]? content = await api.ObjectGetAsync(hash).ConfigureAwait(false);
        if (content is null || ContentHash.Sha256(content) != hash)
        { reasons.Add("Evidence " + hash + " is unavailable or does not match its hash. Review it on the desktop."); return; }
        if (bytes + content.Length > TextLimit || !System.Text.Unicode.Utf8.IsValid(content) || content.Any(b => b < 32 && b is not (9 or 10 or 13)))
        { reasons.Add("Evidence " + hash + " needs a reader unavailable on this phone. Review it on the desktop."); return; }
        bytes += content.Length;
        evidence.Add(hash, new(hash, Encoding.UTF8.GetString(content)));
    }

    private async Task<RecordResponse> RequiredAsync(string id) =>
        await api.RecordAsync(id).ConfigureAwait(false) ?? throw new InvalidDataException("A named subject is unavailable. Refresh or inspect it on the desktop.");

    private async Task<IReadOnlyList<RecordResponse>> CurrentAsync(string type)
    {
        if (!current.TryGetValue(type, out IReadOnlyList<RecordResponse>? result))
            current[type] = result = await api.RecordsAsync(type).ConfigureAwait(false);
        return result;
    }

    private async Task<RecordResponse> LatestAsync(RecordResponse record)
    {
        if (record.Current) return record;
        foreach (RecordResponse candidate in await CurrentAsync(record.Type).ConfigureAwait(false))
        {
            RecordResponse revision = candidate;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(revision.Id) && seen.Count <= MaterialLimit)
            {
                if (revision.Id == record.Id) return candidate;
                if (Text(revision.Envelope, "supersedes") is not string previous) break;
                revision = await RequiredAsync(previous).ConfigureAwait(false);
            }
        }
        throw new InvalidDataException("The current subject revision is unavailable. Inspect its history on the desktop.");
    }

    private async Task<string> RootAsync(RecordResponse record)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (Text(record.Envelope, "supersedes") is string previous)
        {
            if (!seen.Add(record.Id) || seen.Count >= MaterialLimit) throw new InvalidDataException("The full subject history needs the desktop reader.");
            record = await RequiredAsync(previous).ConfigureAwait(false);
        }
        return record.Id;
    }

    internal static void Validate(RecordResponse record)
    {
        var findings = new FindingList();
        RecordEnvelope? parsed = RecordParser.Parse(Encoding.UTF8.GetBytes(record.Envelope.GetRawText()), "phone review", findings, checkFormatting: false);
        if (parsed is null || findings.HasErrors || parsed.Id != record.Id || parsed.Type != record.Type || parsed.ComputedHash != record.Hash)
            throw new InvalidDataException("The observed record does not match its declared identity and hash.");
    }

    internal static bool Same(RemoteDecisionReview first, RemoteDecisionReview second) =>
        first.Root == second.Root && first.Subject.Id == second.Subject.Id && first.Subject.Hash == second.Subject.Hash &&
        first.Material.Select(r => (r.Id, r.Hash, r.Current)).SequenceEqual(second.Material.Select(r => (r.Id, r.Hash, r.Current))) &&
        first.Evidence.Select(e => e.Hash).SequenceEqual(second.Evidence.Select(e => e.Hash)) &&
        first.Basis.SequenceEqual(second.Basis) && first.Reasons.SequenceEqual(second.Reasons) && first.Head == second.Head;

    internal static string Describe(RemoteDecisionReview review) => string.Join("\n\n", review.Material.Select(r =>
        r.Type + " " + r.Id + "\n" + r.Hash + (r.Current ? "\nCurrent at observation" : "\nHistorical revision") + "\n" + Describe(r.Envelope))) +
        (review.Evidence.Count == 0 ? "" : "\n\nEvidence\n" + string.Join("\n\n", review.Evidence.Select(e => e.Hash + "\n" + e.Text)));

    internal static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => string.Join("\n", value.EnumerateObject().Select(p => p.Name + ": " + Describe(p.Value))),
        JsonValueKind.Array => value.GetArrayLength() == 0 ? "none" : string.Join("\n", value.EnumerateArray().Select(Describe)),
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Undefined or JsonValueKind.Null => "not supplied",
        _ => value.GetRawText(),
    };

    internal static bool Pins(RecordResponse record, RecordResponse subject) => Property(record.Envelope, "subject") is var pin &&
        Text(pin, "id") == subject.Id && Text(pin, "type") == subject.Type && Text(pin, "hash") == subject.Hash;
    internal static JsonElement Body(RecordResponse record) => Property(record.Envelope, "body");
    internal static JsonElement Property(JsonElement value, string name) => RemoteWorkDetail.Property(value, name);
    internal static string? Text(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    internal static IEnumerable<JsonElement> Rows(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.Array } rows ? rows.EnumerateArray() : [];
}
