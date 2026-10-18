// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal sealed record WritingLocation(string Kind, string Identity, string Member);
internal sealed record WritingOccurrence(WritingLocation Location, SpacingOccurrence Occurrence, string Basis, ImmutableArray<RecordReference> Entries);
internal sealed record WritingEntry(RecordReference Entry, string Token, string Author, string Reason, JsonElement? Scope, int? Matches);
internal sealed record WritingEvaluation(string Basis, string Scanner, bool Complete, ImmutableArray<WritingOccurrence> Warnings,
    ImmutableArray<WritingOccurrence> ExceptedOccurrences, ImmutableArray<WritingEntry> Entries, ImmutableArray<Finding> Diagnostics)
{
    public int WarningCount => Warnings.Count(w => w.Occurrence.Diagnostic.Severity == Severity.Warning) + Diagnostics.Count(d => d.Severity == Severity.Warning);
    public int ErrorCount => Warnings.Count(w => w.Occurrence.Diagnostic.Severity == Severity.Error) + Diagnostics.Count(d => d.Severity == Severity.Error);
    public int ExceptedCount => ExceptedOccurrences.Length;

    internal static WritingEvaluation Evaluate(PolicyBasis basis)
    {
        List<(WritingLocation Location, SpacingOccurrence Occurrence)> candidates = [];
        List<Finding> diagnostics = [.. basis.Findings];
        bool complete = basis.Complete && !basis.Findings.Any(f => f.Severity == Severity.Error);
        foreach (PolicyRepository inventory in basis.Repositories)
        {
            try
            {
                PolicyRepository repository = Repository(basis, inventory.Identity);
                foreach (PolicyFile file in repository.Files.Where(f => (f.Path.StartsWith("design/", StringComparison.Ordinal) || f.Path.StartsWith("docs/", StringComparison.Ordinal)) && f.Path.EndsWith(".md", StringComparison.Ordinal)))
                {
                    try { candidates.AddRange(WritingScanner.Scan(Read(file), file.Path).Select(o => (new WritingLocation("file", repository.Identity, file.Path), o))); }
                    catch (PolicyInputException error) { Missing(file.Path, error.Message); }
                }
            }
            catch (PolicyInputException error) { Missing(inventory.Identity, error.Message); }
        }
        // A complete empty inventory is different from failing to supply the estate's scan inventory.
        if (basis.Model.CurrentEstate is { } estate && !basis.Repositories.Any(r => r.Identity == PolicyWork.Identity(basis.Model, estate))) Missing("writing", "The estate Markdown inventory is unavailable.");
        foreach (RecordEnvelope record in basis.Records)
        foreach (string field in PolicyRecords.ProseFields)
            if (EstateModel.BodyString(record, field) is { } text)
                candidates.AddRange(WritingScanner.Scan(text, RecordPath(record) + "#body." + field).Select(o => (new WritingLocation("record", record.Id, field), o)));
        return Match(basis, candidates, diagnostics, complete);

        void Missing(string location, string message)
        {
            complete = false;
            diagnostics.Add(new(Severity.Warning, "writing.source-unavailable", location, message));
        }
    }

    // Also used by conformance tests: filtering is structurally restricted to warning occurrences of this
    // scanner code. Diagnostics are never searched or rewritten to discover tokens.
    internal static WritingEvaluation Match(PolicyBasis basis, IEnumerable<(WritingLocation Location, SpacingOccurrence Occurrence)> candidates,
        IEnumerable<Finding> diagnostics, bool complete)
    {
        RecordEnvelope[] entries = basis.Model.Current("writing-exception").Where(e => EstateModel.BodyString(e, "state") == "active" && EstateModel.BodyString(e, "code") == "writing.spacing" &&
            !basis.Findings.Any(f => f.Severity == Severity.Error) && SignatureChecks.IsAuthentic(basis.Model, e))
            .OrderBy(e => EstateModel.BodyString(e, "token"), StringComparer.Ordinal).ThenBy(e => PolicyWork.Identity(basis.Model, e), StringComparer.Ordinal).ToArray();
        var counts = entries.ToDictionary(e => e.Id, _ => 0, StringComparer.Ordinal);
        List<WritingOccurrence> warnings = [], excepted = [];
        List<Finding> findings = [.. diagnostics];
        foreach (var candidate in candidates.OrderBy(c => c.Location.Kind, StringComparer.Ordinal).ThenBy(c => c.Location.Identity, StringComparer.Ordinal)
            .ThenBy(c => c.Location.Member, StringComparer.Ordinal).ThenBy(c => c.Occurrence.ByteOffset))
        {
            RecordEnvelope[] matches = candidate.Occurrence.Diagnostic is { Code: "writing.spacing", Severity: Severity.Warning }
                ? entries.Where(e => EstateModel.BodyString(e, "token") == candidate.Occurrence.Token && Covered(Get(e.Body, "scope"), candidate.Location)).ToArray() : [];
            foreach (RecordEnvelope entry in matches) counts[entry.Id]++;
            var occurrence = new WritingOccurrence(candidate.Location, candidate.Occurrence, basis.Digest, matches.Select(e => e.Reference).OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Hash, StringComparer.Ordinal).ToImmutableArray());
            if (matches.Length > 0) excepted.Add(occurrence); else warnings.Add(occurrence);
        }
        if (complete)
            foreach (RecordEnvelope entry in entries.Where(e => counts[e.Id] == 0)) findings.Add(new(Severity.Warning, "writing.exception-unused", RecordPath(entry), "writing exception unused: `" + EstateModel.BodyString(entry, "token") + "`"));
        return new(basis.Digest, WritingScanner.Contract, complete, [.. warnings], [.. excepted], entries.Select(e => new WritingEntry(e.Reference, EstateModel.BodyString(e, "token")!,
            basis.Model.ById[PolicyWork.Identity(basis.Model, e)].By, EstateModel.BodyString(e, "reason")!, Get(e.Body, "scope").ValueKind == JsonValueKind.Undefined ? null : Get(e.Body, "scope"), complete ? counts[e.Id] : null)).ToImmutableArray(),
            findings.OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal).ThenBy(f => f.Severity).ToImmutableArray());
    }

    private static bool Covered(JsonElement scope, WritingLocation location) => scope.ValueKind == JsonValueKind.Undefined ||
        location.Kind == "record" && Items(Get(scope, "records")).Any(s => Text(s, "record") == location.Identity && (Text(s, "field") is null || Text(s, "field") == location.Member)) ||
        location.Kind == "file" && Items(Get(scope, "paths")).Any(s => Text(s, "repository") == location.Identity && Items(Get(s, "selectors")).Any(p => p.ValueKind == JsonValueKind.String && Matches(p.GetString()!, location.Member)));
}
