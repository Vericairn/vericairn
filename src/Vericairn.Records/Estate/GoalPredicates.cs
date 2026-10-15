// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// The record-predicate language of a goal's completion conditions and its evaluator (the estate's design/goals.md § 5): a predicate
/// counts the current records of one type that match it and holds when the count is within its bounds. A record matches when it is
/// about the predicate's subject (any revision of it), belongs to its product (living documents), has every body field it names
/// equal to the string it gives, and is in the state it names, the state status derives. Without bounds a predicate asks that at
/// least one record match. Every verdict is met, not-met or unknown, the last when the records cannot decide it here, and names the
/// records it read at their exact hashes.
/// </summary>
public static class GoalPredicates
{
    /// <summary>A predicate's verdicts.</summary>
    public static readonly string[] Verdicts = ["met", "not-met", "unknown"];

    /// <summary>The states a predicate may name, by record type: the states status derives for records of that type.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> States { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["ask"] = ["open", "notified", "withdrawn", "answered"],
        ["living-document"] = ["current", "due"],
        ["work-order"] = ["planned", "active", "paused", "submitted", "retired", "accepted", "accepted (earlier revision)"],
    };

    private enum Match
    {
        No,
        Yes,
        Undecided,
    }

    /// <summary>A condition's verdict: met, not-met or unknown; why, in words; and the records it read, each at its exact hash.</summary>
    /// <param name="Value">met, not-met or unknown.</param>
    /// <param name="Reason">Why, in words.</param>
    /// <param name="Read">The records the verdict read, oldest first.</param>
    public sealed record Verdict(string Value, string Reason, IReadOnlyList<RecordReference> Read);

    /// <summary>
    /// Evaluates one completion condition from the current records: a record predicate gets its verdict here; an evidence statement is
    /// unknown here, since an evaluator who did none of the work judges it.
    /// </summary>
    public static Verdict Evaluate(EstateModel model, JsonElement condition)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (condition.ValueKind == JsonValueKind.Object && condition.TryGetProperty("record", out JsonElement predicate))
        {
            return EvaluatePredicate(model, predicate);
        }

        string check = Text(condition, "check") ?? "how it is shown is not stated";
        return new Verdict("unknown", $"An evidence statement, judged by an evaluator who did none of the work: {check}", []);
    }

    /// <summary>Evaluates a record predicate from the current records.</summary>
    public static Verdict EvaluatePredicate(EstateModel model, JsonElement predicate)
    {
        ArgumentNullException.ThrowIfNull(model);
        string? type = Text(predicate, "type");
        if (type is null || RecordTypeRegistry.Find(type) is null)
        {
            return new Verdict("unknown", $"The predicate names no record type this build reads ({type ?? "none"}).", []);
        }

        string? subject = Text(predicate, "subject");
        string? product = Text(predicate, "product");
        string? state = Text(predicate, "state");
        List<(string Name, string Value)> fields = Fields(predicate);
        (long min, long? max) = Bounds(predicate);
        // A subject is followed across its revisions, as a product is: by the first record of its chain.
        string? subjectIdentity = subject is null ? null : LivingDocuments.ProductIdentity(model, subject);
        List<RecordEnvelope> candidates = model.Current(type)
            .Where(r => subjectIdentity is null || (r.Subject is not null && LivingDocuments.ProductIdentity(model, r.Subject.Id) == subjectIdentity))
            .Where(r => product is null || (type == "living-document" && LivingDocuments.NamesProduct(model, r, product)))
            .Where(r => fields.All(f => EstateModel.BodyString(r, f.Name) == f.Value))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var read = new List<RecordEnvelope>(candidates);
        var matches = new List<RecordEnvelope>();
        var undecided = new List<(RecordEnvelope Record, string Why)>();
        var details = new List<string>();
        StateReader? states = state is null ? null : new(model, type);
        foreach (RecordEnvelope candidate in candidates)
        {
            if (states is null || state is null)
            {
                matches.Add(candidate);
                continue;
            }

            (Match match, IReadOnlyList<RecordEnvelope> basis, string? detail) = states.Matches(candidate, state);
            read.AddRange(basis);
            if (match == Match.Yes)
            {
                matches.Add(candidate);
                if (detail is not null)
                {
                    details.Add(detail);
                }
            }
            else if (match == Match.Undecided)
            {
                undecided.Add((candidate, detail ?? candidate.Id));
            }
        }

        long known = matches.Count;
        long possible = known + undecided.Count;
        string value = known >= min && (max is null || possible <= max) ? "met" : possible < min || (max is not null && known > max) ? "not-met" : "unknown";
        var reason = new StringBuilder();
        reason.Append(DescribePredicate(predicate)).Append(": ").Append(Count(matches, undecided.Count > 0));
        if (undecided.Count > 0)
        {
            reason.Append(", and ").Append(undecided.Count.ToString(CultureInfo.InvariantCulture)).Append(" more cannot be decided here (").Append(string.Join("; ", undecided.Select(u => u.Why))).Append(')');
        }

        reason.Append("; ").Append(Bound(min, max)).Append('.');
        foreach (string detail in details)
        {
            reason.Append(' ').Append(detail).Append('.');
        }

        IReadOnlyList<RecordReference> references = read
            .GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => r.Reference).ToList();
        return new Verdict(value, reason.ToString(), references);
    }

    /// <summary>A completion condition in words: what a record predicate counts and its bounds, or an evidence statement and its check.</summary>
    public static string Describe(JsonElement condition)
    {
        if (condition.ValueKind == JsonValueKind.Object && condition.TryGetProperty("record", out JsonElement predicate))
        {
            (long min, long? max) = Bounds(predicate);
            return $"{DescribePredicate(predicate)}: {Bound(min, max)}";
        }

        return Text(condition, "evidence") is string evidence ? $"{evidence} (shown by: {Text(condition, "check") ?? "not stated"})" : "an unreadable condition";
    }

    /// <summary>What a record predicate counts, in words: its type, subject, product, fields and state.</summary>
    public static string DescribePredicate(JsonElement predicate)
    {
        var text = new StringBuilder().Append(Text(predicate, "type") ?? "?").Append(" records");
        if (Text(predicate, "subject") is string subject)
        {
            text.Append(" about ").Append(subject);
        }

        if (Text(predicate, "product") is string product)
        {
            text.Append(" of product ").Append(product);
        }

        List<(string Name, string Value)> fields = Fields(predicate);
        if (fields.Count > 0)
        {
            text.Append(" with ").Append(string.Join(" and ", fields.Select(f => $"{f.Name} {f.Value}")));
        }

        if (Text(predicate, "state") is string state)
        {
            text.Append(" that are ").Append(state);
        }

        return text.ToString();
    }

    /// <summary>The bounds a predicate states: without either, at least one; a maximum alone leaves the minimum at zero.</summary>
    private static (long Min, long? Max) Bounds(JsonElement predicate)
    {
        long? min = Number(predicate, "min");
        long? max = Number(predicate, "max");
        return (min ?? (max is null ? 1 : 0), max);
    }

    private static string Bound(long min, long? max) => (min, max) switch
    {
        (0, null) => "any number is allowed",
        (1, null) => "at least one is required",
        (_, null) => $"at least {min} are required",
        (0, 0) => "none is allowed",
        (0, _) => $"at most {max} {(max == 1 ? "is" : "are")} allowed",
        (_, _) when min == max => $"exactly {min} {(min == 1 ? "is" : "are")} required",
        _ => $"{min} to {max} are required",
    };

    private static string Count(List<RecordEnvelope> matches, bool forCertain)
    {
        string certainly = forCertain ? " for certain" : string.Empty;
        return matches.Count switch
        {
            0 => $"no current record matches{certainly}",
            1 => $"1 current record matches{certainly} ({matches[0].Id})",
            _ => $"{matches.Count.ToString(CultureInfo.InvariantCulture)} current records match{certainly} ({string.Join(", ", matches.Select(m => m.Id))})",
        };
    }

    private static List<(string Name, string Value)> Fields(JsonElement predicate) =>
        predicate.ValueKind == JsonValueKind.Object && predicate.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object
            ? fields.EnumerateObject().Select(f => (f.Name, f.Value.ValueKind == JsonValueKind.String ? f.Value.GetString() ?? string.Empty : f.Value.GetRawText())).ToList()
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;

    private static RecordEnvelope? Newest(IEnumerable<RecordEnvelope> records) =>
        records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();

    /// <summary>
    /// The derived state of the records of one type, as status shows it, with the records each state rests on: a work order's latest
    /// status and acceptance; an ask's answering decisions; a living document's clearance and the records that made it due.
    /// </summary>
    private sealed class StateReader
    {
        private readonly EstateModel model;
        private readonly Dictionary<string, string> workOrders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LivingDocuments.State> documents = new(StringComparer.Ordinal);

        public StateReader(EstateModel model, string type)
        {
            this.model = model;
            if (type == "work-order")
            {
                foreach (EstateStatus.WorkItem item in EstateStatus.WorkItems(model))
                {
                    workOrders[item.Id] = item.Status;
                }
            }
            else if (type == "living-document")
            {
                foreach (LivingDocuments.State document in LivingDocuments.Derive(model))
                {
                    documents[document.Declaration.Id] = document;
                }
            }
        }

        public (Match Match, IReadOnlyList<RecordEnvelope> Basis, string? Detail) Matches(RecordEnvelope record, string state)
        {
            switch (record.Type)
            {
                case "work-order":
                {
                    IReadOnlyList<string> chain = model.Chain(record);
                    var basis = new List<RecordEnvelope>();
                    if (Newest(chain.SelectMany(id => model.About(id, "work-status"))) is RecordEnvelope status)
                    {
                        basis.Add(status);
                    }

                    if (Newest(chain.SelectMany(id => model.About(id, "acceptance"))) is RecordEnvelope acceptance)
                    {
                        basis.Add(acceptance);
                    }

                    string derived = workOrders.TryGetValue(record.Id, out string? found) ? found : "planned";
                    return (derived == state ? Match.Yes : Match.No, basis, null);
                }

                case "ask":
                {
                    IReadOnlyList<string> chain = model.Chain(record);
                    List<RecordEnvelope> answers = EstateModel.BodyString(record, "state") == "withdrawn" ? [] : model.Current("decision")
                        .Where(d => d.Body.TryGetProperty("answers", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array && ids.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.String && chain.Contains(i.GetString()!)))
                        .ToList();
                    string derived = answers.Count > 0 ? "answered" : EstateModel.BodyString(record, "state") ?? "open";
                    return (derived == state ? Match.Yes : Match.No, answers, null);
                }

                case "living-document" when documents.TryGetValue(record.Id, out LivingDocuments.State? document):
                {
                    var basis = new List<RecordEnvelope>();
                    if (document.Clearance is not null)
                    {
                        basis.Add(document.Clearance);
                    }

                    basis.AddRange(document.Reasons.Where(r => r.Record is not null && model.ById.ContainsKey(r.Record)).Select(r => model.ById[r.Record!]));

                    // Due is certain once a trigger fired; current only when every trigger could be evaluated here.
                    bool decided = document.Due || (document.Unavailable.Count == 0 && document.Notes.Count == 0);
                    string? why = document.Unavailable.Count > 0
                        ? $"{document.Path} is unavailable, since it names {string.Join(", ", document.Unavailable)}, which no installed pack provides"
                        : document.Notes.Count > 0 ? $"{document.Path}: {string.Join("; ", document.Notes)}" : null;
                    if (!decided)
                    {
                        return (Match.Undecided, basis, why);
                    }

                    string derived = document.Due ? "due" : "current";
                    return (derived == state ? Match.Yes : Match.No, basis, derived == "due" && state == "due" ? document.Describe() : null);
                }

                default:
                    return (Match.No, [], null);
            }
        }
    }
}
