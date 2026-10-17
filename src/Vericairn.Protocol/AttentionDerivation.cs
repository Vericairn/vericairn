// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

// One refresh only: current lists and exact historical lookups are memoized, never retained between reads.
internal sealed class AttentionDerivation(
    Func<string, IReadOnlyList<RecordResponse>> readRecords,
    Func<string, RecordResponse?> readRecord,
    Func<ProgressResponse> readProgress,
    Func<string?, AsksResponse> readAsks,
    Func<IReadOnlyList<GoalRow>> readGoals,
    Func<RunsResponse> readRuns)
{
    private readonly Dictionary<string, IReadOnlyList<RecordResponse>> lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecordResponse?> records = new(StringComparer.Ordinal);
    private readonly List<CommunicationAttention> items = [];
    private readonly Dictionary<string, IReadOnlyList<RecordResponse>> histories = new(StringComparer.Ordinal);
    private ProgressResponse? progress;
    private IReadOnlyList<GoalRow>? goals;

    internal IReadOnlyList<CommunicationAttention> Derive(string? recipient)
    {
        Read("asks", () => Asks(recipient));
        foreach (string type in new[] { "verification", "result", "review", "operation" })
        {
            Read(type, () =>
            {
                foreach (RecordResponse record in Current(type))
                {
                    Read(type, () => RecordAttention(record), record);
                }
            });
        }

        Read("progress", Blockers);
        Read("goals", () =>
        {
            foreach (GoalRow goal in Goals().Where(g => g.Status is "blocked" or "budget-limited"))
            {
                Read("goal", () => Add(goal.Status == "blocked" ? "blocker" : "exhausted-budget", Required(goal.Id, "goal", goal.Hash), goal.Status,
                    goal.Status == "blocked" ? "The goal is blocked; inspect its steps." : "The goal's budget is exhausted."), reference: new("goal", goal.Id, goal.Hash));
            }
        });
        Read("runs", Runs);
        return items;
    }

    private IReadOnlyList<RecordResponse> Current(string type)
    {
        if (!lists.TryGetValue(type, out IReadOnlyList<RecordResponse>? found))
        {
            found = readRecords(type).Where(r => r.Current).OrderBy(r => At(r), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            lists.Add(type, found);
            foreach (RecordResponse record in found) { records[record.Id] = record; }
        }

        return found;
    }

    private IReadOnlyList<RecordResponse> History(string type)
    {
        if (!histories.TryGetValue(type, out IReadOnlyList<RecordResponse>? found))
        {
            var chain = new Dictionary<string, RecordResponse>(StringComparer.Ordinal);
            foreach (RecordResponse current in Current(type))
            {
                RecordResponse revision = current;
                while (chain.TryAdd(revision.Id, revision))
                {
                    if (Str(revision.Envelope, "supersedes") is not string prior) { break; }
                    revision = Required(prior);
                    if (revision.Type != type) { throw new InvalidOperationException("A revision chain has inconsistent types."); }
                }
            }
            found = chain.Values.ToList();
            histories.Add(type, found);
        }
        return found;
    }

    private RecordResponse Latest(RecordResponse record)
    {
        IReadOnlyList<RecordResponse> revisions = History(record.Type);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!record.Current)
        {
            if (!seen.Add(record.Id)) { throw new InvalidOperationException("The revision chain is cyclic."); }
            List<RecordResponse> next = revisions.Where(r => Str(r.Envelope, "supersedes") == record.Id).ToList();
            if (next.Count != 1) { throw new InvalidOperationException("The current revision is unavailable or divergent."); }
            record = next[0];
        }
        return record;
    }

    private RecordResponse Required(string id, string? type = null, string? hash = null)
    {
        if (!records.TryGetValue(id, out RecordResponse? record)) { records[id] = record = readRecord(id); }
        if (record is null || (type is not null && record.Type != type) || (hash is not null && record.Hash != hash))
        { throw new InvalidOperationException("A referenced record is unavailable or does not match its pin."); }
        return record;
    }

    private RecordResponse SubjectRecord(RecordResponse record)
    {
        AttentionReference subject = PinnedSubject(record);
        RecordResponse found = Required(subject.Id);
        if (found.Type != subject.Type || (subject.Hash is not null && found.Hash != subject.Hash))
        {
            throw new InvalidOperationException("The subject does not match its exact reference.");
        }

        return found;
    }

    private void RecordAttention(RecordResponse record)
    {
        JsonElement body = Body(record);
        string? outcome = Str(body, "outcome");
        switch (record.Type)
        {
            case "verification" when outcome != "passed":
                // A new commit is a new observation of the same check, not a different check identity.
                string check = Check(record);
                if (AnyResolution(History("verification"), v => Str(Body(v), "outcome") == "passed" && Later(v, record) && Check(v) == check)) { return; }
                Add(outcome == "failed" ? "failed-check" : "unknown-check", record, outcome ?? "unknown",
                    $"Verification {outcome ?? "unknown"}; no later pass of this subject and check.");
                break;
            case "result":
                if (body.GetProperty("checks").ValueKind != JsonValueKind.Array || body.GetProperty("unresolvedEffects").ValueKind != JsonValueKind.Array)
                { throw new InvalidOperationException("The result's checks or effects are unavailable."); }
                bool failed = Array(body, "checks").Any(c => Str(c, "outcome") == "failed");
                bool unknown = Array(body, "checks").Any(c => Str(c, "outcome") is not ("pass" or "passed" or "failed"));
                int effects = Array(body, "unresolvedEffects").Count;
                if (!(failed || unknown || effects > 0 || outcome == "blocked") || ResultResolved(record)) { return; }
                Add(outcome == "blocked" ? "blocker" : effects > 0 ? "uncertain-effect" : failed ? "failed-check" : "unknown-check", record,
                    outcome ?? "unknown", $"Result {outcome ?? "unknown"}; {Array(body, "checks").Count(c => Str(c, "outcome") == "failed")} failed checks, {Array(body, "checks").Count(c => Str(c, "outcome") is not ("pass" or "passed" or "failed"))} unknown checks, {effects} unresolved effects; not accepted or succeeded.");
                break;
            case "review" when outcome is not ("pass" or "passed"):
                if (ReviewResolved(record)) { return; }
                Add(outcome == "fail" ? "failed-check" : "required-decision", record, outcome ?? "unknown",
                    $"Review {outcome ?? "unknown"}; {Array(body, "findings").Count} findings await disposition.", Subject(record));
                break;
            case "operation":
                string? status = Str(body, "status");
                if (status is "observed-success" or "canceled" or "not-started") { return; }
                Add(status == "known-failure" ? "incident" : "uncertain-effect", record, status ?? "unknown",
                    $"Operation {status ?? "unknown"}; reconcile the original effect before retrying.");
                break;
        }
    }

    private bool Accepted(RecordResponse record) => History("acceptance").Any(a => Same(Subject(a), Reference(record)));

    private RecordResponse Assignment(RecordResponse result)
    {
        RecordResponse attempt = SubjectRecord(result);
        RecordResponse assignment = SubjectRecord(attempt);
        if (attempt.Type != "attempt" || assignment.Type != "assignment") { throw new InvalidOperationException("The result's assignment chain is unavailable."); }
        return assignment;
    }

    private bool ResultResolved(RecordResponse result) => ResolvedBy(
        () => Accepted(result),
        () =>
        {
            // Bulk reads avoid a round trip per attempt/assignment on the common current chain.
            Current("attempt");
            Current("assignment");
            RecordResponse assignment = Assignment(result);
            return AnyResolution(Current("result"), r => Later(r, result) && Same(Reference(Assignment(r)), Reference(assignment)));
        },
        () => OrderAccepted(result));

    private bool ReviewResolved(RecordResponse review)
    {
        AttentionReference subject = PinnedSubject(review);
        return ResolvedBy(
            () => History("review").Any(r => Same(Subject(r), subject) && Later(r, review)),
            () => Accepted(SubjectRecord(review)),
            () =>
            {
                RecordResponse record = SubjectRecord(review);
                return !record.Current || Str(Body(record), "status") == "withdrawn" || (record.Type == "ask" && Str(Body(record), "state") == "withdrawn")
                    || (record.Type == "goal" && Goals().Any(g => g.Id == record.Id && g.Status == "abandoned"));
            },
            () => OrderAccepted(SubjectRecord(review), withdrawn: true));
    }

    // Any sufficient resolution wins even if another route is unavailable. Otherwise uncertainty is retained on the item.
    private static bool ResolvedBy(params Func<bool>[] alternatives) => AnyResolution(alternatives, alternative => alternative());

    private static bool AnyResolution<T>(IEnumerable<T> candidates, Func<T, bool> resolves)
    {
        bool unknown = false;
        foreach (T candidate in candidates)
        {
            try { if (resolves(candidate)) { return true; } }
            catch (Exception exception) when (Unavailable(exception)) { unknown = true; }
        }
        if (unknown) { throw new InvalidOperationException("Resolution evidence is unavailable."); }
        return false;
    }

    private bool OrderAccepted(RecordResponse record, bool withdrawn = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (record.Type != "work-order")
        {
            if (!seen.Add(record.Id)) { throw new InvalidOperationException("The subject chain is cyclic."); }
            if (record.Type is not ("result" or "attempt" or "assignment" or "review" or "verification")) { return false; }
            record = SubjectRecord(record);
        }

        // Only acceptance of the current work-order revision closes its work. An earlier pin remains historical.
        record = Latest(record);
        if (Accepted(record)) { return true; }
        if (!withdrawn) { return false; }
        // Work orders are withdrawn using work-status 'retired' in record formats 1 and 2.
        return Orders().Any(o => Str(o, "id") == record.Id && Str(o, "status") == "retired");
    }

    private IReadOnlyList<GoalRow> Goals() => goals ??= readGoals();
    private ProgressResponse Progress() => progress ??= readProgress();
    private List<JsonElement> Orders() => Array(Progress().Json.GetProperty("workOrders"), "states").SelectMany(s => Array(s, "workOrders")).ToList();

    private void Blockers()
    {
        foreach (JsonElement held in RequiredArray(Progress().Json.GetProperty("eligible"), "held"))
        {
            string id = Str(held, "id") ?? throw new InvalidOperationException("A held work order has no identity.");
            Read("held work order", () =>
            {
                var reasons = new List<string>();
                bool unknown = false;
                List<JsonElement> relations = Array(held, "relations");
                if (relations.Count == 0) { unknown = true; }
                foreach (JsonElement relation in relations)
                {
                    string? kind = Str(relation, "relation");
                    string? state = Str(relation, "state");
                    bool known = Str(relation, "type") switch
                    {
                        "work-order" => state is "planned" or "active" or "submitted" or "accepted" or "accepted (earlier revision)" or "paused" or "retired" or "withdrawn" or "failed",
                        "ask" => state is "open" or "notified" or "answered" or "withdrawn",
                        "operation" => state is "not-started" or "dispatched" or "uncertain" or "known-failure" or "observed-success" or "canceled",
                        "decision" => state is "no obligations" or "obligations open" or "obligations discharged",
                        _ => false,
                    };
                    if (!known || kind is not ("after" or "blockedBy") || (kind == "after" && Str(relation, "type") != "work-order")
                        || !relation.TryGetProperty("holds", out JsonElement holds) || holds.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    { unknown = true; continue; }
                    if (!holds.GetBoolean()) { continue; }
                    if (kind == "blockedBy" || state is "paused" or "retired" or "withdrawn" or "failed")
                    {
                        reasons.Add($"{kind} {Str(relation, "type")} {Str(relation, "holder") ?? Str(relation, "id")} ({state})");
                    }
                    else if (state is not ("planned" or "active" or "submitted" or "accepted" or "accepted (earlier revision)")) { unknown = true; }
                }
                if (unknown || reasons.Count > 0)
                {
                    Add(unknown ? "unknown-check" : "blocker", Required(id, "work-order"), unknown ? "unknown" : "blocked",
                        unknown ? "A dependency's current state is unavailable; inspect the work order." : "Held by " + string.Join("; ", reasons) + ".");
                }
            }, reference: new("work-order", id, null));
        }
    }

    internal IReadOnlyList<CommunicationAttention> AskItems(string? recipient)
    {
        Read("asks", () => Asks(recipient));
        return items;
    }

    private void Asks(string? recipient)
    {
        foreach (JsonElement principal in RequiredArray(readAsks(recipient).Json, "principals"))
        {
            int queued = RequiredArray(principal, "queued").Count;
            List<JsonElement> delivered = RequiredArray(principal, "delivered");
            if (principal.TryGetProperty("waiting", out JsonElement waiting) && waiting.GetInt32() > 0 && delivered.Count == 0)
            { throw new InvalidOperationException("The waiting asks are unavailable."); }
            foreach (JsonElement ask in delivered)
            {
                string id = Str(ask, "revision") ?? Str(ask, "id") ?? throw new InvalidOperationException("The ask has no identity.");
                Read("ask", () => Add("required-decision", Required(id, "ask", Str(ask, "hash")), Str(ask, "state") ?? "unknown",
                    "A decision is required; open the ask for its recommendation, alternatives and no-answer consequence." + (queued == 0 ? string.Empty : $" {queued} more asks wait in the queue.")), reference: new("ask", id, Str(ask, "hash")));
            }
        }
    }

    private void Runs()
    {
        RunsResponse runs = readRuns();
        foreach (RunRow run in runs.Runs.Where(r => r.Signals is { Count: > 0 } || r.Scope?.Problem is not null || r.Scope?.OutsideCount > 0))
        {
            Read("run", () => Add("incident", Latest(Required(run.Run, "operation")), run.Status,
                $"Run {run.Status}; {run.Signals?.Count ?? 0} worker signals, {run.Scope?.OutsideCount ?? 0} changes outside scope{(run.Scope?.Problem is null ? string.Empty : "; scope inspection unavailable")}; inspect worker observations.", command: "vericairn worker runs"), reference: new("operation", run.Run, null));
        }
        foreach (LedgerRow ledger in runs.Goals.Where(g => g.Status == "budget-limited" || g.Units.Any(u => u.Remaining <= 0)))
        {
            Read("budget", () => Add("exhausted-budget", Required(ledger.Goal, "goal"), ledger.Status, "The goal's budget is exhausted; inspect its ledger.", command: "vericairn worker runs"), reference: new("goal", ledger.Goal, null));
        }
    }

    private void Add(string kind, RecordResponse record, string state, string why, AttentionReference? target = null, string? command = null)
    {
        AttentionReference source = Reference(record);
        AttentionReference? subject = Subject(record);
        command ??= "vericairn records show " + (target?.Id ?? record.Id);
        string text = $"{record.Type} {record.Id} at {record.Hash}; subject {Describe(subject)}\n  {why} State: {state}; recorded {At(record) ?? "unknown"}.\n  {command}\n";
        var item = new CommunicationAttention(kind, text, record.Id, record.Type)
        {
            Hash = source.Hash, Subject = subject, State = state, AsOf = At(record), Command = command, Target = target ?? source,
        };
        int existing = items.FindIndex(a => a.Kind == kind && a.Record == record.Id);
        if (existing < 0) { items.Add(item); }
        else { items[existing] = item; }
    }

    private void Read(string source, Action action, RecordResponse? record = null, AttentionReference? reference = null)
    {
        try { action(); }
        catch (Exception exception) when (Unavailable(exception))
        {
            // Provider exception messages may contain machine paths or response bodies. The original source remains inspectable.
            if (record is not null) { Add("unknown-check", record, "unknown", $"Cannot determine current {source} attention from the available records."); }
            else
            {
                string command = reference is null ? "vericairn status" : "vericairn records show " + reference.Id;
                items.Add(new("unknown-check", $"Required attention source unavailable: {source}{(reference is null ? string.Empty : "; " + Describe(reference))}; current state unknown.\n  {command}\n", reference?.Id, reference?.Type)
                { Hash = reference?.Hash, State = "unknown", Command = command, Target = reference });
            }
        }
    }

    private static bool Unavailable(Exception exception) => exception is IOException or InvalidOperationException or System.Net.Http.HttpRequestException or JsonException or NotSupportedException or KeyNotFoundException or ArgumentException;

    private static string Check(RecordResponse record)
    {
        JsonElement body = Body(record);
        AttentionReference subject = PinnedSubject(record);
        string method = Str(body, "method") ?? throw new InvalidOperationException("The verification method is unavailable.");
        JsonElement environment = body.GetProperty("environment");
        if (environment.ValueKind != JsonValueKind.Object || environment.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String)) { throw new InvalidOperationException("The verification environment is unavailable."); }
        return JsonSerializer.Serialize(new { subject, method, command = Str(body, "command"), environment = environment.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value.GetString()) });
    }

    private static bool Later(RecordResponse candidate, RecordResponse before)
    {
        string first = At(candidate) ?? throw new InvalidOperationException("The record's ordering timestamp is unavailable.");
        string second = At(before) ?? throw new InvalidOperationException("The record's ordering timestamp is unavailable.");
        int comparison = string.CompareOrdinal(first, second);
        return comparison > 0 || (comparison == 0 && string.CompareOrdinal(candidate.Id, before.Id) > 0);
    }

    private static bool Same(AttentionReference? first, AttentionReference second) => first == second;
    private static AttentionReference Reference(RecordResponse record) => new(record.Type, record.Id, record.Hash);
    private static AttentionReference? Subject(RecordResponse record) => record.Envelope.ValueKind == JsonValueKind.Object && record.Envelope.TryGetProperty("subject", out JsonElement subject) && Str(subject, "type") is string type && Str(subject, "id") is string id ? new(type, id, Str(subject, "hash")) : null;
    private static AttentionReference PinnedSubject(RecordResponse record) => Subject(record) is { Hash: not null } subject
        ? subject : throw new InvalidOperationException("The exact subject pin is unavailable.");
    private static string Describe(AttentionReference? reference) => reference is null ? "none" : $"{reference.Type} {reference.Id}" + (reference.Hash is null ? string.Empty : " at " + reference.Hash);
    private static string? At(RecordResponse record) => Str(record.Envelope, "at");
    private static JsonElement Body(RecordResponse record) => record.Envelope.GetProperty("body");
    private static string? Str(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found) && found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    private static List<JsonElement> RequiredArray(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Array
        ? Array(value, name) : throw new InvalidOperationException("A required list is unavailable.");
    private static List<JsonElement> Array(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found) && found.ValueKind == JsonValueKind.Array ? found.EnumerateArray().ToList() : [];
}
