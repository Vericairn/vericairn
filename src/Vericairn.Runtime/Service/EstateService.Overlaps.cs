// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The overlap check in the runtime (from 0.9.4, CODE-73; the estate's design/measured-work.md § 5): the view and its preview, the
/// repository a new assignment's brief names, the write boundary's refusals of a new assignment with a path pattern and no repository, of
/// narrowing an assignment while its run is in flight and of a new attempt while an assignment it runs after is unreleased, and the
/// overlaps a recorded assignment, a run and a new attempt report.
/// </summary>
public sealed partial class EstateService
{
    /// <inheritdoc />
    public OverlapResponse WorkOverlap(string? repository = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            string? identity = null;
            if (repository is not null && (identity = RepositoryIdentity(model, repository)) is null)
            {
                return OverlapProblem($"No repository has id {repository}: name a repository resource or estate.");
            }

            RepositoryView view = View(model);
            IReadOnlyList<AssignmentOverlaps.Party> parties = AssignmentOverlaps.Parties(model, view);
            IReadOnlyList<AssignmentOverlaps.Overlap> overlaps = AssignmentOverlaps.Overlaps(model, parties, view);
            using JsonDocument json = JsonDocument.Parse(AssignmentOverlaps.RenderJson(model, parties, overlaps, identity));
            return new OverlapResponse(AssignmentOverlaps.RenderText(model, parties, overlaps, identity, view), json.RootElement.Clone());
        }
    }

    /// <inheritdoc />
    public OverlapResponse WorkOverlapPreview(OverlapPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Brief.ValueKind != JsonValueKind.Object)
        {
            return OverlapProblem("A draft brief is a JSON object, as work assign takes it.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            string? named = request.Repository ?? TJson.Text(request.Brief, "repository");
            string? identity = named is null ? null : RepositoryIdentity(model, named);
            if (named is not null && identity is null)
            {
                return OverlapProblem($"No repository has id {named}: name a repository resource or estate.");
            }

            RepositoryView view = View(model);
            AssignmentOverlaps.Party draft = AssignmentOverlaps.Draft(model, request.Brief, identity);
            IReadOnlyList<AssignmentOverlaps.Overlap> overlaps = AssignmentOverlaps.With(model, draft, view);
            IReadOnlyList<AssignmentOverlaps.Party> parties = AssignmentOverlaps.Parties(model, view);
            var text = new StringBuilder("Draft brief in ").Append(AssignmentOverlaps.NameOf(model, identity)).Append(": ")
                .Append(draft.Patterns.Count == 0 ? "no interpretable path pattern, so it changes no repository and overlaps nothing" : string.Join(", ", draft.Patterns))
                .Append(draft.Excluded.Count > 0 ? " less " + string.Join(", ", draft.Excluded) : string.Empty).Append('\n');
            if (draft.Patterns.Count > 0 && identity is null)
            {
                text.Append("It names no repository, so it is compared with every repository; a recorded brief with a path pattern names its repository.\n");
            }

            text.Append(overlaps.Count == 0 ? "It overlaps no active assignment.\n" : string.Concat(overlaps.Select(o => AssignmentOverlaps.Sentence(model, o) + "\n")));
            using JsonDocument json = JsonDocument.Parse(AssignmentOverlaps.RenderJson(model, parties, overlaps, identity, draft));
            return new OverlapResponse(text.ToString(), json.RootElement.Clone());
        }
    }

    /// <summary>What this machine's registered clones show the overlap check for a model.</summary>
    private RepositoryView View(EstateModel model) => RepositoryView.For(model, Providers, session.Manifest.Estate);

    private static OverlapResponse OverlapProblem(string message)
    {
        using JsonDocument json = JsonDocument.Parse(new JsonObject { ["format"] = AssignmentOverlaps.Format, ["problem"] = message }.ToJsonString());
        return new OverlapResponse(message + "\n", json.RootElement.Clone());
    }

    /// <summary>The repository identity a request names: estate for the estate's own, else the id of any revision of a repository resource or of the estate.</summary>
    internal static string? RepositoryIdentity(EstateModel model, string named) =>
        named == "estate" ? AssignmentOverlaps.EstateRepository(model) : AssignmentOverlaps.IdentityOf(model, named);

    /// <summary>
    /// The brief a new assignment is recorded with (from 0.9.4, CODE-73): in record format 3, a brief that holds a path pattern and names no
    /// repository takes the one named (a repository resource or estate); else the one the work order's other assignments, or their runs,
    /// name, when they name exactly one, as the steps of a run share theirs; else the estate's own repository when the estate names no
    /// repository resource; otherwise it stays without one and the write boundary refuses it, naming the candidates. Null with a refusal
    /// when the repository named is none.
    /// </summary>
    internal JsonElement? FillRepository(EstateModel model, JsonElement brief, string? named, out string? refusal, RecordEnvelope? order = null)
    {
        refusal = null;
        if (brief.ValueKind != JsonValueKind.Object || session.Manifest.RecordFormat < 3 || brief.TryGetProperty("repository", out _)) return brief;
        string? identity;
        if (named is not null)
        {
            identity = RepositoryIdentity(model, named);
            if (identity is null)
            {
                refusal = $"No repository has id {named}: name a repository resource or estate. Nothing was written.";
                return null;
            }
        }
        else if (!AssignmentOverlaps.HoldsPattern(brief))
        {
            return brief;
        }
        else if (order is not null && OrderRepository(model, order) is string shared)
        {
            identity = shared;
        }
        else if (model.Current("resource").Any(r => EstateModel.BodyString(r, "kind") == "repository"))
        {
            return brief;
        }
        else
        {
            identity = AssignmentOverlaps.EstateRepository(model);
            if (identity is null) return brief;
        }

        JsonObject filled = JsonNode.Parse(brief.GetRawText())!.AsObject();
        filled["repository"] = identity;
        return Element(filled);
    }

    /// <summary>The one repository a work order's current assignments name, or else their agent runs named; null for none or several.</summary>
    private static string? OrderRepository(EstateModel model, RecordEnvelope order)
    {
        HashSet<string> chain = [.. model.Chain(model.Latest(order))];
        List<RecordEnvelope> assignments = [.. model.Current("assignment").Where(a => a.Subject is { } subject && chain.Contains(subject.Id))];
        List<string> named = [.. assignments.Select(a => AssignmentOverlaps.Brief(a).Repository).Select(r => AssignmentOverlaps.IdentityOf(model, r)).OfType<string>().Distinct(StringComparer.Ordinal)];
        if (named.Count > 0) return named.Count == 1 ? named[0] : null;
        HashSet<string> carried = [.. assignments.SelectMany(a => model.Chain(a))];
        List<string> ran = [.. AgentRuns.All(model).Where(r => r.Assignment is string a && carried.Contains(a)).Select(r => AssignmentOverlaps.IdentityOf(model, r.Target("repository"))).OfType<string>().Distinct(StringComparer.Ordinal)];
        return ran.Count == 1 ? ran[0] : null;
    }

    /// <summary>The overlaps a recorded assignment has with the active assignments, each a sentence; null when none.</summary>
    private List<string>? AssignmentNotices(EstateModel model, string assignment)
    {
        if (!model.ById.TryGetValue(assignment, out RecordEnvelope? record)) return null;
        RepositoryView view = View(model);
        if (AssignmentOverlaps.PartyOf(model, record, view) is not { Active: true, Patterns.Count: > 0 } party) return null;
        List<string> notices = [.. AssignmentOverlaps.With(model, party, view).Select(o => AssignmentOverlaps.Sentence(model, o))];
        return notices.Count == 0 ? null : notices;
    }

    /// <summary>A write's outcome with the overlaps of the assignment it recorded, when it recorded one.</summary>
    private WriteOutcome WithAssignmentNotices(WriteOutcome outcome)
    {
        if (outcome.Written is not RecordWritten written) return outcome;
        try
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(written.Id, out RecordEnvelope? record) || record.Type != "assignment") return outcome;
            return AssignmentNotices(model, written.Id) is { } notices ? outcome with { Notices = [.. outcome.Notices ?? [], .. notices] } : outcome;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return outcome;
        }
    }

    /// <summary>
    /// The write boundary's warning for a new completed result (from 0.9.4, CODE-73): its assignment holds a path pattern and the result
    /// names no change, so the assignment stays active until acceptance rather than until its change is integrated.
    /// </summary>
    private WriteOutcome WithResultNotices(WriteOutcome outcome)
    {
        if (outcome.Written is not RecordWritten written || session.Manifest.RecordFormat < 3) return outcome;
        try
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(written.Id, out RecordEnvelope? result) || result.Type != "result" || EstateModel.BodyString(result, "outcome") != "completed"
                || result.Body.TryGetProperty("change", out _) || WorkView.AttemptOfResult(model, result) is not { } attempt || WorkView.AssignmentOfAttempt(model, attempt) is not { } assignment
                || !assignment.Body.TryGetProperty("brief", out JsonElement brief) || !AssignmentOverlaps.HoldsPattern(brief)) return outcome;
            string notice = $"Warning: result {result.Id} is completed and names no change, so assignment {assignment.Id}, whose brief holds a path pattern, stays active until its work order is accepted. Name the change (repository, base, head and target) in a corrected result, and the assignment is released once that change is integrated.";
            return outcome with { Notices = [.. outcome.Notices ?? [], notice] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return outcome;
        }
    }

    /// <summary>
    /// The write boundary's overlap rules (from 0.9.4, CODE-73) for proposed records: a new assignment in record format 3 whose brief holds a
    /// path pattern names its repository, and an assignment is narrowed (exclusions or after changed) only once no run of it is in flight.
    /// Records already in the estate keep their reading.
    /// </summary>
    private static void OverlapBoundary(EstateModel model, IReadOnlyList<AuthoredRecord> records, FindingList relevant)
    {
        foreach (AuthoredRecord record in records.Where(r => r.Envelope.Type == "assignment"))
        {
            RecordEnvelope envelope = record.Envelope;
            if (!envelope.Body.TryGetProperty("brief", out JsonElement brief) || brief.ValueKind != JsonValueKind.Object) continue;
            if (envelope.Format >= 3 && !brief.TryGetProperty("repository", out _) && AssignmentOverlaps.HoldsPattern(brief))
            {
                IEnumerable<string> resources = model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository")
                    .Select(r => $"{EstateModel.BodyString(r, "name") ?? r.Id} ({model.Chain(r)[0]})").Order(StringComparer.Ordinal);
                relevant.Add(new Finding(Severity.Error, "assignment.repository-required", record.RelativePath + "#body.brief.repository",
                    $"From 0.9.4 a new assignment whose permitted changes hold a path pattern names the repository it changes, so that the overlap check compares it with the work in that repository: set brief.repository to the estate's identity ({AssignmentOverlaps.EstateRepository(model) ?? "none"}) or a repository resource's ({string.Join(", ", resources.DefaultIfEmpty("none"))}); work assign and goal step take --repository."));
            }

            if (envelope.Supersedes is string previous && model.ById.TryGetValue(previous, out RecordEnvelope? earlier) && earlier.Type == "assignment"
                && (Field(brief, "excludedChanges") != Field(earlier, "excludedChanges") || Field(brief, "after") != Field(earlier, "after")))
            {
                HashSet<string> chain = [.. model.Chain(envelope)];
                if (AgentRuns.All(model).FirstOrDefault(r => r.InFlight && r.Assignment is string carried && chain.Contains(carried)) is { } run)
                {
                    relevant.Add(new Finding(Severity.Error, "assignment.narrowing-in-flight", record.RelativePath + "#body.brief",
                        $"Run {run.Id} of this assignment is in flight: an assignment's exclusions and after change only once its run settles or is stopped, so that the work in flight keeps the paths it was granted."));
                }
            }
        }
    }

    private static string Field(JsonElement brief, string name) => brief.ValueKind == JsonValueKind.Object && brief.TryGetProperty(name, out JsonElement value) ? CanonicalJson.Serialize(value) : string.Empty;

    private static string Field(RecordEnvelope assignment, string name) => assignment.Body.TryGetProperty("brief", out JsonElement brief) ? Field(brief, name) : string.Empty;

    /// <summary>
    /// The write boundary's wait (from 0.9.4, CODE-73): a new attempt on an assignment whose brief names after is refused while an
    /// assignment it names, followed to its current revision, is not released.
    /// </summary>
    private void AttemptWaits(EstateModel model, IReadOnlyList<AuthoredRecord> records, FindingList relevant)
    {
        RepositoryView? view = null;
        foreach (AuthoredRecord record in records.Where(r => r.Envelope.Type == "attempt" && r.Envelope.Subject is not null))
        {
            if (!model.ById.TryGetValue(record.Envelope.Subject!.Id, out RecordEnvelope? assignment) || assignment.Type != "assignment" || AssignmentOverlaps.Brief(assignment).After.Count == 0) continue;
            if (AssignmentOverlaps.WaitRefusal(model, assignment, "a new attempt", view ??= View(model)) is string refusal)
            {
                relevant.Add(new Finding(Severity.Error, "attempt.after", record.RelativePath + "#subject", refusal));
            }
        }
    }

    /// <summary>
    /// Routing's assignment brief with its repository (the run's, else filled as work assign fills it) and, for each overlap with an active
    /// assignment that a refuse-level concurrent-paths policy holds, the earlier assignment in after (from 0.9.4, CODE-73; the estate's
    /// design/measured-work.md § 5, Routing); with every overlap reported, and the assignments it now runs after.
    /// </summary>
    private (JsonObject Body, IReadOnlyList<string> Notices, IReadOnlyList<string> Held) Sequence(EstateModel model, JsonObject body, string? repository, string key, RecordEnvelope order)
    {
        if (body["brief"] is not JsonObject brief) return (body, [], []);
        if (session.Manifest.RecordFormat >= 3 && !brief.ContainsKey("repository") && FillRepository(model, Element(brief), repository, out _, order) is JsonElement filled)
        {
            body["brief"] = brief = JsonNode.Parse(filled.GetRawText())!.AsObject();
        }

        JsonElement written = Element(brief);
        string? identity = TJson.Text(written, "repository") is string named ? AssignmentOverlaps.IdentityOf(model, named) : repository is null ? null : RepositoryIdentity(model, repository);
        AssignmentOverlaps.Party draft = AssignmentOverlaps.Draft(model, written, identity, key);
        if (draft.Patterns.Count == 0) return (body, [], []);
        IReadOnlyList<AssignmentOverlaps.Overlap> overlaps = AssignmentOverlaps.With(model, draft, View(model));
        List<string> held = session.Manifest.RecordFormat < 3 ? [] : [.. overlaps.Where(o => o.Held).Select(o => model.Latest(o.First.Assignment).Id).Distinct(StringComparer.Ordinal)];
        List<string> after = [.. TJson.Strings(written, "after"), .. held];
        after = [.. after.Distinct(StringComparer.Ordinal).Take(global::Vericairn.Records.Types.AssignmentType.MaxAfter)];
        held = [.. held.Where(after.Contains)];
        if (held.Count > 0)
        {
            brief["after"] = new JsonArray([.. after.Select(id => (JsonNode)id)]);
        }

        List<string> notices = [.. overlaps.Select(o => held.Contains(model.Latest(o.First.Assignment).Id)
            ? $"{key} is recorded to run after {o.First.Key}'s assignment {model.Latest(o.First.Assignment).Id}, since a refuse-level concurrent-paths policy holds their overlap. {AssignmentOverlaps.Sentence(model, o)}"
            : AssignmentOverlaps.Sentence(model, o))];
        return (body, notices, held);
    }

    /// <summary>
    /// An agent's step whose overlap a refuse-level policy holds (from 0.9.4, CODE-73): routing records its assignment alone, naming in after
    /// the assignments it waits for, so that it holds the step; nothing is dispatched, and its run starts from goal step once each is released.
    /// </summary>
    private GoalStepResponse SequencedStep(EstateModel model, string goalId, Taken taken, NextEligible.Route route, GoalStepRequest request, JsonObject body,
        IReadOnlyList<string> held, IReadOnlyList<string> notices, string agentName, List<RecordDraft> stepDrafts)
    {
        body.Remove("routedWith");
        RecordDraft assigning = Fixed(new RecordDraft("assignment", request.By, Element(body), taken.Order.Reference), SessionNow());
        ExpectPresence(request.By, [DescribeRecord(assigning, model)], "the step is routed to an agent and recorded to run after the assignments it overlaps");
        stepDrafts.Add(assigning);
        WriteOutcome written = WriteLocked(SessionDraft(assigning), model);
        if (!written.Ok)
        {
            return new GoalStepResponse(goalId, "none", "Not recorded: " + written.Problem!.Message, false, taken.Key, route.State, route.Principal, Problem: written.Problem) { Notices = notices };
        }

        return new GoalStepResponse(goalId, "assigned", $"{taken.Key} is routed to the agent {agentName} [principal {route.Principal}] through {route.Launcher}, and its assignment {written.Written!.Id} is recorded to run after {string.Join(", ", held)}, whose overlap with it a refuse-level concurrent-paths policy holds: its run starts from goal step once each is released. Nothing was dispatched.",
            true, taken.Key, route.State, route.Principal, written.Written) { Notices = notices };
    }

    /// <summary>
    /// What goal next shows of the overlap check (from 0.9.4, CODE-73): the overlaps among active assignments that involve the goal's work,
    /// and those a new assignment of its next step would have, from the work order's scope as routing writes it, with whether goal step
    /// would record it to run after the earlier one.
    /// </summary>
    private (string Text, JsonArray Json) GoalOverlaps(EstateModel model, IReadOnlyCollection<RecordEnvelope> work, (RecordEnvelope Order, string Key)? next)
    {
        HashSet<string> orders = [.. work.Select(o => model.Chain(model.Latest(o))[0])];
        RepositoryView view = View(model);
        IReadOnlyList<AssignmentOverlaps.Party> parties = AssignmentOverlaps.Parties(model, view);
        List<AssignmentOverlaps.Overlap> overlaps = [.. AssignmentOverlaps.Overlaps(model, parties, view).Where(o => Involves(o.First) || Involves(o.Second))];
        var json = new JsonArray([.. overlaps.Select(o => (JsonNode)AssignmentOverlaps.OverlapJson(model, o))]);
        var text = new StringBuilder();
        if (overlaps.Count > 0)
        {
            text.Append("Overlapping assignments of this goal's work: ").Append(overlaps.Count).Append('\n');
            foreach (AssignmentOverlaps.Overlap overlap in overlaps) text.Append("  ").Append(AssignmentOverlaps.Sentence(model, overlap)).Append('\n');
        }

        if (next is { } step)
        {
            var brief = new JsonObject { ["permittedChanges"] = new JsonArray([.. Strings(step.Order.Body, "scope").Select(s => (JsonNode)s)]) };
            JsonElement filled = FillRepository(model, Element(brief), null, out _, step.Order) ?? Element(brief);
            string? identity = TJson.Text(filled, "repository") is string named ? AssignmentOverlaps.IdentityOf(model, named) : null;
            AssignmentOverlaps.Party draft = AssignmentOverlaps.Draft(model, filled, identity, step.Key);
            List<AssignmentOverlaps.Overlap> ahead = draft.Patterns.Count == 0 ? [] : [.. AssignmentOverlaps.With(model, draft, view)];
            if (ahead.Count > 0)
            {
                text.Append("A new assignment of ").Append(step.Key).Append(" would overlap: ").Append(ahead.Count).Append('\n');
                foreach (AssignmentOverlaps.Overlap overlap in ahead)
                {
                    text.Append("  ").Append(AssignmentOverlaps.Sentence(model, overlap))
                        .Append(overlap.Held ? $" Goal step records it to run after {overlap.First.Key}'s assignment {overlap.First.Assignment.Id}." : string.Empty).Append('\n');
                    JsonObject entry = AssignmentOverlaps.OverlapJson(model, overlap);
                    entry["next"] = step.Key;
                    json.Add(entry);
                }
            }
        }

        return (text.ToString(), json);

        bool Involves(AssignmentOverlaps.Party party) => party.Assignment.Subject is { } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? order) && orders.Contains(model.Chain(model.Latest(order))[0]);
    }

    /// <summary>The overlaps an attempt's assignment has with the active assignments, reported when the attempt is recorded.</summary>
    private WriteOutcome WithAttemptNotices(WriteOutcome outcome)
    {
        if (outcome.Written is not RecordWritten written) return outcome;
        try
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(written.Id, out RecordEnvelope? attempt) || WorkView.AssignmentOfAttempt(model, attempt) is not { } assignment) return outcome;
            return AssignmentNotices(model, model.Latest(assignment).Id) is { } notices ? outcome with { Notices = [.. outcome.Notices ?? [], .. notices] } : outcome;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return outcome;
        }
    }
}
