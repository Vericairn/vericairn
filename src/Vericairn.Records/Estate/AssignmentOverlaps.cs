// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Records.Estate;

/// <summary>
/// What a repository's registered clone on this machine shows the overlap check: the tracked paths at its main line, whether a commit is
/// reachable from a branch, whether its filesystem folds case, and a change's changed paths. Every answer is local to this machine and
/// labeled so; status reads none of them, so that it stays identical on every machine for the same records.
/// </summary>
internal interface IRepositoryView
{
    /// <summary>The tracked paths at the head of the clone's main line, and that branch; null when no clone or no answer.</summary>
    (string Branch, IReadOnlyList<string> Paths)? Tracked(string repository);

    /// <summary>Whether a commit is reachable from a branch in the clone; null when the clone, the commit or the branch is missing.</summary>
    bool? Reachable(string repository, string commit, string branch);

    /// <summary>Whether the clone's filesystem folds case, so that two paths differing only in case name one file.</summary>
    bool CaseInsensitive(string repository);

    /// <summary>The paths a change added, modified or removed between two commits, without rename detection; null when a commit is missing.</summary>
    IReadOnlyList<(string Path, string Change)>? Changed(string repository, string from, string to);
}

/// <summary>
/// The overlap check (from 0.9.4, CODE-73; the estate's design/measured-work.md § 5): before two assignments run at once in one
/// repository, their effective permitted paths are compared exactly, every overlap is reported, and an overlap a refuse-level
/// <c>vericairn.estate/concurrent-paths</c> policy holds is resolved by sequence (the later's brief names the earlier in <c>after</c>) or
/// by one owner (every other party excludes the shared paths). An assignment is active while it is current and its work order is neither
/// accepted nor retired, until it is released: its current result is abandoned; completed with a change that is integrated; completed
/// with no path pattern in its brief; or completed before the check's build, labeled. From the build, a completed result with a path
/// pattern and no change keeps its assignment active until acceptance.
/// </summary>
internal static class AssignmentOverlaps
{
    /// <summary>The policy kind whose selection holds an overlap (POLICY-08 evaluates it; the check reads which overlaps it holds).</summary>
    internal const string PolicyKind = "vericairn.estate/concurrent-paths";

    /// <summary>What the estate's own repository leaves out of the comparison: every write there is a new file named by its id or hash.</summary>
    internal static readonly IReadOnlyList<string> EstateLeftOut = ["records/**", "objects/**"];

    /// <summary>The format of the overlap view's JSON.</summary>
    internal const string Format = "vericairn.overlaps/1";

    /// <summary>A change a completed result names, with its integration as the records and this machine show it.</summary>
    /// <param name="Repository">The repository's identity.</param>
    /// <param name="Base">The base commit.</param>
    /// <param name="Head">The head commit.</param>
    /// <param name="Target">The branch it integrates into.</param>
    /// <param name="Integration">merged (a change-merge operation observed at its head), reachable (its head reachable from the target in this machine's clone), not-integrated, or unknown.</param>
    /// <param name="Merge">The change-merge operation that shows it merged.</param>
    /// <param name="Paths">The paths it changed as this machine's clone shows them, by name without rename detection; null when a commit is missing there or no clone was read.</param>
    /// <param name="Read">Whether a clone was read for its paths, so that null paths mean unavailable rather than not asked.</param>
    internal sealed record ChangeView(string Repository, string Base, string Head, string Target, string Integration, RecordEnvelope? Merge,
        IReadOnlyList<(string Path, string Change)>? Paths = null, bool Read = false);

    /// <summary>One assignment as the check reads it.</summary>
    /// <param name="Assignment">Its current revision, or a draft's envelope for a preview.</param>
    /// <param name="Key">Its work order's key.</param>
    /// <param name="Contributor">Its contributor.</param>
    /// <param name="Repository">The repository's identity: the brief's, else its agent runs', else null for unknown.</param>
    /// <param name="RepositoryFrom">brief, run or unknown.</param>
    /// <param name="Patterns">The interpretable patterns of its permitted changes.</param>
    /// <param name="Excluded">The patterns its brief excludes.</param>
    /// <param name="After">The assignments its brief waits for, as named.</param>
    /// <param name="Active">Whether its work may still change its repository.</param>
    /// <param name="State">Why it is active or released, in words.</param>
    /// <param name="Labels">What the reading rests on beyond the records, or what it could not read.</param>
    /// <param name="Result">Its current result, when it has one.</param>
    /// <param name="Change">The change that result names.</param>
    internal sealed record Party(RecordEnvelope Assignment, string Key, string Contributor, string? Repository, string RepositoryFrom, IReadOnlyList<string> Patterns,
        IReadOnlyList<string> Excluded, IReadOnlyList<string> After, bool Active, string State, IReadOnlyList<string> Labels, RecordEnvelope? Result, ChangeView? Change)
    {
        /// <summary>The effective paths' party: its patterns less its exclusions, and in the estate's own repository less records/ and objects/.</summary>
        internal PathIntersection.Party Effective(EstateModel model) =>
            new(Patterns, IsEstate(model, Repository) ? [.. Excluded, .. EstateLeftOut] : Excluded);
    }

    /// <summary>A concurrent-paths policy of a repository: the selection's selectors and its level.</summary>
    /// <param name="Record">The policy's current revision.</param>
    /// <param name="Repository">The selection's repository.</param>
    /// <param name="Selectors">The selection's selectors.</param>
    /// <param name="Level">remind, warn or refuse.</param>
    internal sealed record Policy(RecordEnvelope Record, string Repository, IReadOnlyList<string> Selectors, string Level);

    /// <summary>A policy whose selection the overlap meets, with the overlap within it.</summary>
    /// <param name="Policy">The policy.</param>
    /// <param name="Determined">False when the search within its selection reached its bound.</param>
    /// <param name="Witness">The shortest path within the selection, when found.</param>
    internal sealed record Hold(Policy Policy, bool Determined, string? Witness);

    /// <summary>Two active assignments whose effective paths share a valid path, in recorded order.</summary>
    /// <param name="First">The earlier.</param>
    /// <param name="Second">The later.</param>
    /// <param name="Repository">The repository they share; null when neither names one.</param>
    /// <param name="Determined">False when the search reached its bound and no tracked path decided it.</param>
    /// <param name="Witnesses">Up to 20 tracked paths in the intersection, else the shortest valid path either could create.</param>
    /// <param name="Tracked">True when the witnesses are tracked paths at the clone's main line, named by <paramref name="Branch"/>.</param>
    /// <param name="Branch">The branch whose tracked paths were read.</param>
    /// <param name="Sequenced">True when one names the other in after.</param>
    /// <param name="Holds">The concurrent-paths policies whose selection the overlap meets.</param>
    /// <param name="Labels">What the comparison could not see or rests on.</param>
    internal sealed record Overlap(Party First, Party Second, string? Repository, bool Determined, IReadOnlyList<string> Witnesses, bool Tracked, string? Branch,
        bool Sequenced, IReadOnlyList<Hold> Holds, IReadOnlyList<string> Labels)
    {
        /// <summary>Open: neither runs after the other.</summary>
        internal bool Open => !Sequenced;

        /// <summary>Held: open, and a refuse-level policy's selection meets it.</summary>
        internal bool Held => !Sequenced && Holds.Any(h => h.Policy.Level == "refuse");

        /// <summary>The resolution in a word.</summary>
        internal string Resolution => Sequenced ? "sequence" : "none";
    }

    /// <summary>An after assignment that has not been released, which a run or a new attempt waits for.</summary>
    /// <param name="Named">The id the brief names.</param>
    /// <param name="Current">Its current revision.</param>
    /// <param name="Party">How the check reads it.</param>
    internal sealed record Wait(string Named, RecordEnvelope Current, Party Party);

    /// <summary>Whether an id is the identity of the estate or of a repository resource: the first record of its lineage.</summary>
    internal static bool IsRepository(EstateModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Supersedes is null
            && (record.Type == "estate" || record.Type == "resource" && EstateModel.BodyString(model.Latest(record), "kind") == "repository");
    }

    /// <summary>Whether a repository identity is the estate's own.</summary>
    internal static bool IsEstate(EstateModel model, string? repository) =>
        repository is not null && model.ById.TryGetValue(repository, out RecordEnvelope? record) && record.Type == "estate";

    /// <summary>The identity of the estate's own repository: its first charter's id.</summary>
    internal static string? EstateRepository(EstateModel model) => model.FirstEstate?.Id ?? (model.CurrentEstate is { } charter ? model.Chain(charter)[0] : null);

    /// <summary>A repository's identity, from the id of any revision of it; null when the id names no repository.</summary>
    internal static string? IdentityOf(EstateModel model, string? id)
    {
        if (id is null || !model.ById.TryGetValue(id, out RecordEnvelope? record)) return null;
        string identity = model.Chain(model.Latest(record))[0];
        return IsRepository(model, identity) ? identity : null;
    }

    /// <summary>A repository's name in words.</summary>
    internal static string NameOf(EstateModel model, string? repository) => repository is null ? "a repository not named"
        : IsEstate(model, repository) ? "the estate's own repository"
        : model.ById.TryGetValue(repository, out RecordEnvelope? resource) ? $"{EstateModel.BodyString(model.Latest(resource), "name") ?? repository} ({repository})" : repository;

    /// <summary>Whether a name is a plain branch name, as git reads one; refs/heads/ is never required.</summary>
    internal static bool IsBranch(string name) => name.Length is > 0 and <= 200 && !name.StartsWith('-') && !name.StartsWith('/') && !name.EndsWith('/')
        && !name.EndsWith(".lock", StringComparison.Ordinal) && !name.EndsWith('.') && !name.Contains("..", StringComparison.Ordinal) && !name.Contains("//", StringComparison.Ordinal)
        && !name.Contains("@{", StringComparison.Ordinal) && name != "@"
        && !name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\');

    /// <summary>
    /// The assignment through which waiting for these makes a cycle, following each named assignment to its current revision and on through
    /// its brief's after, or null: a cycle returns to an assignment whose lineage is <paramref name="own"/>.
    /// </summary>
    internal static string? Cycle(EstateModel model, IReadOnlySet<string> own, IReadOnlyList<string> after)
    {
        ArgumentNullException.ThrowIfNull(model);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string start in after)
        {
            var stack = new Stack<string>([start]);
            while (stack.Count > 0)
            {
                string id = stack.Pop();
                if (!model.ById.TryGetValue(id, out RecordEnvelope? named) || named.Type != "assignment") continue;
                RecordEnvelope current = model.Latest(named);
                if (!seen.Add(current.Id)) continue;
                foreach (string next in Brief(current).After)
                {
                    if (model.ById.TryGetValue(next, out RecordEnvelope? waited) && model.Chain(model.Latest(waited)).Any(own.Contains)) return start;
                    stack.Push(next);
                }
            }
        }

        return null;
    }

    /// <summary>What a brief says to the check: its repository, its interpretable patterns, its exclusions and its after, as written.</summary>
    internal static (string? Repository, IReadOnlyList<string> Patterns, IReadOnlyList<string> Excluded, IReadOnlyList<string> After) Brief(RecordEnvelope assignment) =>
        ReadBrief(assignment.Body.TryGetProperty("brief", out JsonElement brief) ? brief : default);

    /// <summary>What a brief's JSON says to the check.</summary>
    internal static (string? Repository, IReadOnlyList<string> Patterns, IReadOnlyList<string> Excluded, IReadOnlyList<string> After) ReadBrief(JsonElement brief) =>
        (TJson.Text(brief, "repository"),
            [.. TJson.Strings(brief, "permittedChanges").Select(PathPatterns.Pattern).OfType<string>().Distinct(StringComparer.Ordinal)],
            [.. TJson.Strings(brief, "excludedChanges").Select(PathPatterns.Pattern).OfType<string>().Distinct(StringComparer.Ordinal)],
            [.. TJson.Strings(brief, "after")]);

    /// <summary>Whether a brief holds an interpretable path pattern.</summary>
    internal static bool HoldsPattern(JsonElement brief) => TJson.Strings(brief, "permittedChanges").Any(e => PathPatterns.Pattern(e) is not null);

    /// <summary>
    /// The first record of the check's build in this estate: the earliest assignment whose brief names a repository, exclusions or after, or
    /// result that names a change, by time and id; null before any. A completed result recorded before it counts as released at completion,
    /// since nothing records its integration. From the build, every new assignment with a path pattern names its repository.
    /// </summary>
    internal static RecordEnvelope? Began(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.OverlapCheckBegan(m => m.OfType("assignment").Where(a => a.Body.TryGetProperty("brief", out JsonElement b) && b.ValueKind == JsonValueKind.Object
                && (b.TryGetProperty("repository", out _) || b.TryGetProperty("excludedChanges", out _) || b.TryGetProperty("after", out _)))
            .Concat(m.OfType("result").Where(r => r.Body.TryGetProperty("change", out _)))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault());
    }

    /// <summary>The active concurrent-paths policies, each on its repository selection, whatever its level.</summary>
    internal static IReadOnlyList<Policy> Policies(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var policies = new List<Policy>();
        foreach (RecordEnvelope policy in model.Current("estate-policy").OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (EstateModel.BodyString(policy, "state") != "active") continue;
            JsonElement selection = TJson.Object(policy.Body, "appliesTo");
            if (TJson.Text(selection, "kind") != "repository" || IdentityOf(model, TJson.Text(selection, "repository")) is not string repository) continue;
            if (!TJson.Array(policy.Body, "rules").Any(r => TJson.Text(r, "kind") == PolicyKind && TJson.Object(r, "version") is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out long n) && n == 1)) continue;
            policies.Add(new Policy(policy, repository, [.. TJson.Strings(selection, "selectors")], EstateModel.BodyString(policy, "level") ?? "remind"));
        }

        return policies;
    }

    /// <summary>Every current assignment as the check reads it, active and released, by work-order key and id.</summary>
    internal static IReadOnlyList<Party> Parties(EstateModel model, IRepositoryView? view = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        Reading reading = new(model, view);
        return [.. model.Current("assignment").Select(reading.Party).OfType<Party>()
            .OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Assignment.Id, StringComparer.Ordinal)];
    }

    /// <summary>How the check reads one assignment, at its current revision.</summary>
    internal static Party? PartyOf(EstateModel model, RecordEnvelope assignment, IRepositoryView? view = null) => new Reading(model, view).Party(model.Latest(assignment));

    /// <summary>A draft brief as the check reads it, for a preview before it is recorded: active, in the repository named.</summary>
    internal static Party Draft(EstateModel model, JsonElement brief, string? repository, string key = "draft")
    {
        ArgumentNullException.ThrowIfNull(model);
        (string? named, IReadOnlyList<string> patterns, IReadOnlyList<string> excluded, IReadOnlyList<string> after) = ReadBrief(brief);
        string? identity = IdentityOf(model, repository ?? named);
        var body = new JsonObject { ["brief"] = JsonNode.Parse(brief.ValueKind == JsonValueKind.Undefined ? "{}" : brief.GetRawText()) };
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        var envelope = new RecordEnvelope("assignment", "draft", "9999-12-31T23:59:59Z", string.Empty, null, null, document.RootElement.Clone(), string.Empty, string.Empty, []);
        return new Party(envelope, key, string.Empty, identity, identity is null ? "unknown" : "brief", patterns, excluded, after, true, "a draft brief, not recorded", [], null, null);
    }

    /// <summary>Every overlap among the active assignments, sequenced ones included, by repository and then recorded order.</summary>
    internal static IReadOnlyList<Overlap> Overlaps(EstateModel model, IRepositoryView? view = null) =>
        view is null ? model.RecordOverlaps(m => Overlaps(m, Parties(m), null)) : Overlaps(model, Parties(model, view), view);

    /// <summary>Every overlap among the active parties given.</summary>
    internal static IReadOnlyList<Overlap> Overlaps(EstateModel model, IReadOnlyList<Party> parties, IRepositoryView? view)
    {
        List<Party> active = [.. parties.Where(p => p.Active && p.Patterns.Count > 0).OrderBy(p => p.Assignment.At, StringComparer.Ordinal).ThenBy(p => p.Assignment.Id, StringComparer.Ordinal)];
        IReadOnlyList<Policy> policies = Policies(model);
        var tracked = new Dictionary<string, (string Branch, IReadOnlyList<string> Paths)?>(StringComparer.Ordinal);
        var overlaps = new List<Overlap>();
        for (int i = 0; i < active.Count; i++)
        {
            for (int j = i + 1; j < active.Count; j++)
            {
                if (Compare(model, active[i], active[j], policies, view, tracked) is Overlap overlap) overlaps.Add(overlap);
            }
        }

        return [.. overlaps.OrderBy(o => o.Repository ?? "~", StringComparer.Ordinal).ThenBy(o => o.First.Assignment.At, StringComparer.Ordinal)
            .ThenBy(o => o.First.Assignment.Id, StringComparer.Ordinal).ThenBy(o => o.Second.Assignment.Id, StringComparer.Ordinal)];
    }

    /// <summary>The overlaps of one party, recorded or a draft, with every other active assignment; the party counts as the later.</summary>
    internal static IReadOnlyList<Overlap> With(EstateModel model, Party party, IRepositoryView? view = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(party);
        IReadOnlyList<Policy> policies = Policies(model);
        var tracked = new Dictionary<string, (string Branch, IReadOnlyList<string> Paths)?>(StringComparer.Ordinal);
        HashSet<string> own = party.Assignment.Id == "draft" ? [] : [.. model.Chain(model.Latest(party.Assignment))];
        return [.. Parties(model, view).Where(p => p.Active && p.Patterns.Count > 0 && !own.Contains(p.Assignment.Id))
            .OrderBy(p => p.Assignment.At, StringComparer.Ordinal).ThenBy(p => p.Assignment.Id, StringComparer.Ordinal)
            .Select(other => Compare(model, other, party, policies, view, tracked)).OfType<Overlap>()];
    }

    /// <summary>The assignments a brief's after names that are not released yet, each followed to its current revision.</summary>
    internal static IReadOnlyList<Wait> Waits(EstateModel model, RecordEnvelope assignment, IRepositoryView? view = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        var reading = new Reading(model, view);
        var waits = new List<Wait>();
        foreach (string named in Brief(assignment).After)
        {
            if (!model.ById.TryGetValue(named, out RecordEnvelope? record) || record.Type != "assignment") continue;
            RecordEnvelope current = model.Latest(record);
            if (reading.Party(current) is { Active: true } party) waits.Add(new Wait(named, current, party));
        }

        return waits;
    }

    /// <summary>Why a run or a new attempt waits, in one sentence, or null when nothing it waits for is unreleased.</summary>
    internal static string? WaitRefusal(EstateModel model, RecordEnvelope assignment, string what, IRepositoryView? view = null)
    {
        IReadOnlyList<Wait> waits = Waits(model, assignment, view);
        if (waits.Count == 0) return null;
        return $"Assignment {assignment.Id} runs after {string.Join(", ", waits.Select(w => $"{w.Party.Key}'s assignment {w.Current.Id}"))}, and {(waits.Count == 1 ? "it is" : "they are")} not released yet ({string.Join("; ", waits.Select(w => w.Party.State))}): {what} waits until {(waits.Count == 1 ? "it is" : "each is")} released, when {(waits.Count == 1 ? "its" : "each one's")} current result is abandoned, or completed with a change that is integrated, or {(waits.Count == 1 ? "its" : "each")} work order is accepted or retired.";
    }

    /// <summary>One overlap in a sentence, as a write or a run reports it.</summary>
    internal static string Sentence(EstateModel model, Overlap overlap)
    {
        var text = new StringBuilder();
        text.Append("Overlap: ").Append(Describe(overlap.First)).Append(" and ").Append(Describe(overlap.Second)).Append(" in ").Append(NameOf(model, overlap.Repository))
            .Append(" share ").Append(Witnesses(overlap)).Append('.');
        if (overlap.Sequenced) text.Append(" Sequenced: one runs after the other.");
        foreach (Hold hold in overlap.Holds)
        {
            text.Append(' ').Append(HoldText(overlap, hold));
        }

        foreach (string label in overlap.Labels) text.Append(' ').Append(Capital(label)).Append('.');
        return text.ToString();
    }

    /// <summary>The open overlaps as status shows them under work, from the records alone; empty when none is open.</summary>
    internal static string StatusText(EstateModel model, IReadOnlyList<Overlap> overlaps)
    {
        List<Overlap> open = [.. overlaps.Where(o => o.Open)];
        if (open.Count == 0) return string.Empty;
        var text = new StringBuilder("Overlapping assignments: ").Append(open.Count.ToString(CultureInfo.InvariantCulture)).Append(" open")
            .Append(open.Count(o => o.Held) is int held and > 0 ? $", {held.ToString(CultureInfo.InvariantCulture)} held at refuse" : string.Empty)
            .Append(" (vericairn work overlap)\n");
        foreach (Overlap overlap in open)
        {
            text.Append("  ").Append(overlap.First.Key).Append(' ').Append(overlap.First.Assignment.Id).Append(" and ").Append(overlap.Second.Key).Append(' ').Append(overlap.Second.Assignment.Id)
                .Append(" in ").Append(NameOf(model, overlap.Repository)).Append(": ").Append(Witnesses(overlap))
                .Append(overlap.Held ? "; held at refuse by " + string.Join(", ", overlap.Holds.Where(h => h.Policy.Level == "refuse").Select(h => PolicyName(h.Policy))) : string.Empty)
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The open overlaps as status's canonical JSON carries them.</summary>
    internal static JsonArray StatusJson(EstateModel model, IReadOnlyList<Overlap> overlaps) => [.. overlaps.Where(o => o.Open).Select(o => (JsonNode)OverlapJson(model, o))];

    /// <summary>The overlap view: the active assignments by repository and every overlap, with patterns, witnesses and resolution.</summary>
    internal static string RenderText(EstateModel model, IReadOnlyList<Party> parties, IReadOnlyList<Overlap> overlaps, string? repository, IRepositoryView? view)
    {
        var text = new StringBuilder();
        List<Party> active = [.. parties.Where(p => p.Active && p.Patterns.Count > 0 && Shown(p, repository))];
        text.Append("Active assignments with permitted paths: ").Append(active.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (IGrouping<string?, Party> group in active.GroupBy(p => p.Repository).OrderBy(g => g.Key ?? "~", StringComparer.Ordinal))
        {
            text.Append("  ").Append(group.Key is null ? "No repository named (compared with every repository)" : Capital(NameOf(model, group.Key))).Append('\n');
            foreach (Party party in group)
            {
                text.Append("    ").Append(party.Key).Append(' ').Append(party.Assignment.Id).Append("  ").Append(Paths(party)).Append("  ").Append(party.State);
                if (party.After.Count > 0) text.Append("; after ").Append(string.Join(", ", party.After));
                text.Append('\n');
                if (party.Change is { Read: true } change)
                {
                    text.Append("      Change ").Append(change.Base[..12]).Append("..").Append(change.Head[..12]).Append(" into ").Append(change.Target).Append(": ")
                        .Append(change.Paths is null ? "its paths are unavailable here, since a commit is missing from this machine's clone" : $"{change.Paths.Count.ToString(CultureInfo.InvariantCulture)} {(change.Paths.Count == 1 ? "path" : "paths")} changed, by name without rename detection")
                        .Append('\n');
                }

                foreach (string label in party.Labels) text.Append("      ").Append(Capital(label)).Append('\n');
            }
        }

        List<Overlap> shown = [.. overlaps.Where(o => repository is null || o.Repository == repository || o.Repository is null)];
        text.Append("Overlaps: ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
        if (shown.Count > 0)
        {
            text.Append(" (").Append(shown.Count(o => o.Open).ToString(CultureInfo.InvariantCulture)).Append(" open, ")
                .Append(shown.Count(o => o.Held).ToString(CultureInfo.InvariantCulture)).Append(" held at refuse, ")
                .Append(shown.Count(o => o.Sequenced).ToString(CultureInfo.InvariantCulture)).Append(" sequenced)");
        }

        text.Append('\n');
        foreach (Overlap overlap in shown)
        {
            text.Append("  ").Append(Describe(overlap.First)).Append(" and ").Append(Describe(overlap.Second)).Append(" in ").Append(NameOf(model, overlap.Repository))
                .Append(": ").Append(overlap.Sequenced ? "sequenced" : overlap.Held ? "open, held" : "open").Append('\n');
            text.Append("    patterns: ").Append(Paths(overlap.First)).Append(" | ").Append(Paths(overlap.Second)).Append('\n');
            text.Append("    ").Append(overlap.Tracked ? $"tracked at {overlap.Branch}: " : overlap.Witnesses.Count > 0 ? "a path either could create: " : string.Empty)
                .Append(overlap.Witnesses.Count > 0 ? string.Join(", ", overlap.Witnesses) : "no witness found within the search's bound").Append('\n');
            foreach (Hold hold in overlap.Holds) text.Append("    ").Append(HoldText(overlap, hold)).Append('\n');
            foreach (string label in overlap.Labels) text.Append("    ").Append(Capital(label)).Append(".\n");
        }

        if (shown.Any(o => o.Held))
        {
            text.Append("A held overlap is resolved before both run: record the later with after naming the earlier, or exclude the shared paths from every brief but the owner's (excludedChanges).\n");
        }

        return text.ToString();
    }

    /// <summary>The overlap view's canonical JSON.</summary>
    internal static string RenderJson(EstateModel model, IReadOnlyList<Party> parties, IReadOnlyList<Overlap> overlaps, string? repository, Party? draft = null)
    {
        var json = new JsonObject
        {
            ["format"] = Format,
            ["repository"] = repository,
            ["assignments"] = new JsonArray([.. parties.Where(p => p.Active && p.Patterns.Count > 0 && Shown(p, repository)).Select(p => (JsonNode)PartyJson(model, p))]),
            ["overlaps"] = new JsonArray([.. overlaps.Where(o => repository is null || o.Repository == repository || o.Repository is null).Select(o => (JsonNode)OverlapJson(model, o))]),
        };
        if (draft is not null)
        {
            json["draft"] = PartyJson(model, draft);
        }

        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    /// <summary>One assignment in the view's JSON.</summary>
    internal static JsonObject PartyJson(EstateModel model, Party party)
    {
        var json = new JsonObject
        {
            ["assignment"] = party.Assignment.Id == "draft" ? null : party.Assignment.Id,
            ["hash"] = party.Assignment.Id == "draft" ? null : party.Assignment.ComputedHash,
            ["key"] = party.Key,
            ["contributor"] = party.Contributor.Length == 0 ? null : party.Contributor,
            ["repository"] = party.Repository,
            ["repositoryFrom"] = party.RepositoryFrom,
            ["patterns"] = new JsonArray([.. party.Patterns.Select(p => (JsonNode)p)]),
            ["excluded"] = new JsonArray([.. party.Excluded.Select(p => (JsonNode)p)]),
            ["leftOut"] = new JsonArray([.. (IsEstate(model, party.Repository) ? EstateLeftOut : []).Select(p => (JsonNode)p)]),
            ["after"] = new JsonArray([.. party.After.Select(p => (JsonNode)p)]),
            ["active"] = party.Active,
            ["state"] = party.State,
            ["labels"] = new JsonArray([.. party.Labels.Select(l => (JsonNode)l)]),
            ["result"] = party.Result?.Id,
        };
        if (party.Change is ChangeView change)
        {
            json["change"] = new JsonObject
            {
                ["repository"] = change.Repository, ["base"] = change.Base, ["head"] = change.Head, ["target"] = change.Target,
                ["integration"] = change.Integration, ["merge"] = change.Merge?.Id,
                ["paths"] = change.Paths is null ? null : new JsonArray([.. change.Paths.Select(p => (JsonNode)new JsonObject { ["path"] = p.Path, ["change"] = p.Change })]),
                ["pathsUnavailable"] = change.Read && change.Paths is null ? "a commit of the change is missing from this machine's registered clone, or no clone of its repository is registered here" : null,
            };
        }

        return json;
    }

    /// <summary>One overlap in JSON.</summary>
    internal static JsonObject OverlapJson(EstateModel model, Overlap overlap) => new()
    {
        ["repository"] = overlap.Repository,
        ["first"] = Side(overlap.First),
        ["second"] = Side(overlap.Second),
        ["determined"] = overlap.Determined,
        ["witnesses"] = new JsonArray([.. overlap.Witnesses.Select(w => (JsonNode)w)]),
        ["witnessKind"] = overlap.Witnesses.Count == 0 ? null : overlap.Tracked ? "tracked" : "possible",
        ["branch"] = overlap.Branch,
        ["resolution"] = overlap.Resolution,
        ["held"] = overlap.Held,
        ["policies"] = new JsonArray([.. overlap.Holds.Select(h => (JsonNode)new JsonObject
        {
            ["policy"] = h.Policy.Record.Id, ["key"] = EstateModel.BodyString(h.Policy.Record, "key"), ["level"] = h.Policy.Level, ["determined"] = h.Determined, ["witness"] = h.Witness,
        })]),
        ["labels"] = new JsonArray([.. overlap.Labels.Select(l => (JsonNode)l)]),
    };

    private static JsonObject Side(Party party) => new()
    {
        ["assignment"] = party.Assignment.Id == "draft" ? null : party.Assignment.Id,
        ["key"] = party.Key,
        ["patterns"] = new JsonArray([.. party.Patterns.Select(p => (JsonNode)p)]),
        ["excluded"] = new JsonArray([.. party.Excluded.Select(p => (JsonNode)p)]),
    };

    private static bool Shown(Party party, string? repository) => repository is null || party.Repository == repository || party.Repository is null;

    private static string Describe(Party party) => party.Assignment.Id != "draft" ? $"{party.Key}'s assignment {party.Assignment.Id}"
        : party.Key == "draft" ? "the draft brief" : $"{party.Key}'s new assignment";

    private static string Paths(Party party) => string.Join(", ", party.Patterns) + (party.Excluded.Count > 0 ? " less " + string.Join(", ", party.Excluded) : string.Empty);

    private static string Witnesses(Overlap overlap) => overlap.Witnesses.Count == 0
        ? "paths the search could not rule out within its bound"
        : (overlap.Tracked ? $"{overlap.Witnesses.Count.ToString(CultureInfo.InvariantCulture)} tracked {(overlap.Witnesses.Count == 1 ? "path" : "paths")} at {overlap.Branch}, such as " : "paths either could create, such as ")
            + overlap.Witnesses[0];

    private static string PolicyName(Policy policy) => $"policy {EstateModel.BodyString(policy.Record, "key") ?? policy.Record.Id} ({policy.Record.Id})";

    private static string HoldText(Overlap overlap, Hold hold) =>
        $"Within the selection of {PolicyName(hold.Policy)} at {hold.Policy.Level}{(hold.Witness is null ? string.Empty : $", such as {hold.Witness}")}" +
        (overlap.Sequenced ? ", resolved by sequence." : hold.Policy.Level == "refuse"
            ? ": held. Record the later with after naming the earlier, or exclude the shared paths from every brief but the owner's."
            : ": reported.");

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static Overlap? Compare(EstateModel model, Party first, Party second, IReadOnlyList<Policy> policies, IRepositoryView? view,
        Dictionary<string, (string Branch, IReadOnlyList<string> Paths)?> tracked)
    {
        if (first.Repository is not null && second.Repository is not null && first.Repository != second.Repository) return null;
        string? repository = first.Repository ?? second.Repository;
        PathIntersection.Party a = first.Effective(model), b = second.Effective(model);
        PathIntersection.Outcome outcome = PathIntersection.Search(a, b);
        if (!outcome.Overlap) return null;
        var labels = new List<string>();
        (string Branch, IReadOnlyList<string> Paths)? inventory = null;
        if (repository is not null && view is not null)
        {
            if (!tracked.TryGetValue(repository, out inventory)) tracked[repository] = inventory = view.Tracked(repository);
        }

        List<string> shared = inventory is { } listed ? [.. listed.Paths.Where(p => a.Contains(p) && b.Contains(p)).Order(StringComparer.Ordinal).Take(20)] : [];
        bool determined = outcome.Determined || shared.Count > 0;
        if (!determined)
        {
            labels.Add($"undetermined: the search reached its bound of {PathIntersection.StateBound.ToString("N0", CultureInfo.InvariantCulture)} states, so the pair counts as overlapping");
        }

        foreach (Party party in new[] { first, second }.Where(p => p.Repository is null))
        {
            labels.Add($"{Describe(party)} names no repository and no run of it named one, so it is compared as possibly in any repository");
        }

        if (repository is not null && view is not null && view.CaseInsensitive(repository))
        {
            labels.Add("this machine's registered clone is on a case-insensitive filesystem: two paths that differ only in case name one file there, which ordinal matching does not see");
        }

        bool sequenced = Names(model, first, second) || Names(model, second, first);
        var holds = new List<Hold>();
        foreach (Policy policy in policies.Where(p => repository is null || p.Repository == repository))
        {
            PathIntersection.Outcome within = PathIntersection.Search(a, b, policy.Selectors);
            List<string> sharedWithin = [.. shared.Where(p => PathIntersection.Selected(policy.Selectors, p))];
            if (within.Overlap || sharedWithin.Count > 0)
            {
                holds.Add(new Hold(policy, within.Determined || sharedWithin.Count > 0, sharedWithin.FirstOrDefault() ?? within.Witness));
            }
        }

        IReadOnlyList<string> witnesses = shared.Count > 0 ? shared : outcome.Witness is string w ? [w] : [];
        return new Overlap(first, second, repository, determined, witnesses, shared.Count > 0, shared.Count > 0 ? inventory!.Value.Branch : null, sequenced, holds, labels);
    }

    // Whether a party's after names the other, followed to its current revision.
    private static bool Names(EstateModel model, Party waiting, Party waited) =>
        waited.Assignment.Id != "draft" && waiting.After.Any(id => model.ById.TryGetValue(id, out RecordEnvelope? named) && named.Type == "assignment" && model.Latest(named).Id == model.Latest(waited.Assignment).Id);

    /// <summary>One read of the estate's assignments: work-order states, agent runs and the build's beginning computed once.</summary>
    private sealed class Reading
    {
        private readonly EstateModel model;
        private readonly IRepositoryView? view;
        private readonly Lazy<Dictionary<string, string>> orders;
        private readonly Lazy<Dictionary<string, string>> runs;
        private readonly Lazy<RecordEnvelope?> began;

        public Reading(EstateModel model, IRepositoryView? view)
        {
            this.model = model;
            this.view = view;
            orders = new(() => new Dictionary<string, string>(StringComparer.Ordinal));
            runs = new(() =>
            {
                var repositories = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (AgentRuns.Run run in AgentRuns.All(model))
                {
                    if (run.Assignment is string assignment && IdentityOf(model, run.Target("repository")) is string repository) repositories[assignment] = repository;
                }

                return repositories;
            });
            began = new(() => Began(model));
        }

        public Party? Party(RecordEnvelope assignment)
        {
            if (WorkView.OrderOfAssignment(model, assignment) is not RecordEnvelope pinned) return null;
            RecordEnvelope order = model.Latest(pinned);
            string key = EstateModel.BodyString(order, "key") ?? order.Id;
            (string? named, IReadOnlyList<string> patterns, IReadOnlyList<string> excluded, IReadOnlyList<string> after) = Brief(assignment);
            var labels = new List<string>();
            string? repository = IdentityOf(model, named);
            string from = repository is null ? "unknown" : "brief";
            if (repository is null)
            {
                string? ran = model.Chain(assignment).Select(id => runs.Value.GetValueOrDefault(id)).LastOrDefault(r => r is not null);
                if (ran is not null)
                {
                    repository = ran;
                    from = "run";
                    labels.Add($"its brief names no repository; compared under the one its agent runs named, {NameOf(model, ran)}");
                }
            }

            string contributor = EstateModel.BodyString(assignment, "contributor") ?? string.Empty;
            if (!orders.Value.TryGetValue(order.Id, out string? status))
            {
                orders.Value[order.Id] = status = EstateStatus.StatusOf(model, order);
            }

            IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
            RecordEnvelope? result = attempts.Count == 0 ? null : WorkView.ResultOf(model, attempts[^1]);
            ChangeView? change = result is null ? null : Change(result);
            (bool active, string state) = State(status, attempts.Count, result, change, patterns.Count > 0, labels);
            return new Party(assignment, key, contributor, repository, from, patterns, excluded, after, active, state, labels, result, change);
        }

        private (bool Active, string State) State(string status, int attempts, RecordEnvelope? result, ChangeView? change, bool pattern, List<string> labels)
        {
            if (status == "accepted") return (false, "released: its work order is accepted");
            if (status == "retired") return (false, "released: its work order is retired");
            if (result is null) return (true, attempts == 0 ? "active: no attempt yet" : "active: an attempt in progress");
            string outcome = EstateModel.BodyString(result, "outcome") ?? "?";
            if (outcome == "abandoned") return (false, $"released: its current result {result.Id} is abandoned");
            if (outcome != "completed") return (true, $"active: its current result {result.Id} is {outcome}");
            if (change is not null)
            {
                switch (change.Integration)
                {
                    case "merged":
                        return (false, $"released: its change's head {change.Head} merged into {change.Target} (operation {change.Merge!.Id})");
                    case "reachable":
                        labels.Add($"observed here: its change's head is reachable from {change.Target} in this machine's registered clone");
                        return (false, $"released: its change's head {change.Head} is reachable from {change.Target}");
                    case "not-integrated":
                        return (true, $"active: its change's head {change.Head} is not yet integrated into {change.Target}");
                    default:
                        return (true, $"active: its change's head {change.Head} is not observed integrated into {change.Target}");
                }
            }

            if (!pattern) return (false, $"released: its current result {result.Id} is completed and its brief holds no path pattern");
            if (began.Value is not RecordEnvelope start || GoalRules.Before(result, start))
            {
                labels.Add("its result was recorded before the overlap check, which records no integration, so it counts as released at completion");
                return (false, $"released: its current result {result.Id} is completed");
            }

            return (true, $"active until acceptance: its current result {result.Id} is completed and names no change");
        }

        private ChangeView? Change(RecordEnvelope result)
        {
            if (!result.Body.TryGetProperty("change", out JsonElement change) || change.ValueKind != JsonValueKind.Object) return null;
            string? repository = IdentityOf(model, TJson.Text(change, "repository"));
            string? head = TJson.Text(change, "head"), target = TJson.Text(change, "target"), from = TJson.Text(change, "base");
            if (repository is null || head is null || target is null || from is null) return null;
            RecordEnvelope? merge = model.Current("operation").Where(o => EstateModel.BodyString(o, "action") == "change-merge" && EstateModel.BodyString(o, "status") == "observed-success")
                .FirstOrDefault(o => o.Body.TryGetProperty("target", out JsonElement t) && TJson.Text(t, "head") == head && IdentityOf(model, TJson.Text(t, "repository")) == repository
                    && (TJson.Text(t, "target") is not string merged || merged == target));
            IReadOnlyList<(string Path, string Change)>? paths = view?.Changed(repository, from, head);
            if (merge is not null) return new ChangeView(repository, from, head, target, "merged", merge, paths, view is not null);
            bool? reachable = view?.Reachable(repository, head, target);
            return new ChangeView(repository, from, head, target, reachable switch { true => "reachable", false => "not-integrated", null => "unknown" }, null, paths, view is not null);
        }
    }
}
