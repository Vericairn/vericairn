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
    /// <summary>The tracked paths at the head of the clone's main line, in ordinal order, and that branch; null when no clone or no answer.</summary>
    (string Branch, IReadOnlyList<string> Paths)? Tracked(string repository);

    /// <summary>The branch of the clone's main line, its integration line (origin's HEAD, else main, else master, as a local branch); null when none.</summary>
    string? MainLine(string repository);

    /// <summary>Whether a commit is reachable from a branch in the clone; null when the clone, the commit or the branch is missing.</summary>
    bool? Reachable(string repository, string commit, string branch);

    /// <summary>Whether one commit is a proper ancestor of another in the clone; null when the clone or either commit is missing.</summary>
    bool? Precedes(string repository, string ancestor, string descendant);

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
/// pattern and no change keeps its assignment active until acceptance. A change releases only as this assignment's integrated work: it
/// is in the assignment's repository, its base is a proper ancestor of its head, its head was merged by a change-merge operation or is
/// reachable from the repository's main line in this machine's registered clone, never from the branch the result names, and where the
/// clone shows the paths it touched, one of them is in the assignment's effective paths.
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
    /// <param name="Target">The branch the result names as the one it integrates into; it shows nothing of the integration.</param>
    /// <param name="Integration">merged (a change-merge operation observed at its head), reachable (its head reachable from the main line of this machine's clone), not-integrated, mismatched (it cannot be this assignment's integrated work, as <paramref name="Problem"/> says), or unknown.</param>
    /// <param name="Merge">The change-merge operation that shows it merged.</param>
    /// <param name="Paths">The paths it changed as this machine's clone shows them, by name without rename detection; null when a commit is missing there or no clone was read.</param>
    /// <param name="Read">Whether a clone was read for its paths, so that null paths mean unavailable rather than not asked.</param>
    /// <param name="Problem">Why the change cannot release its assignment: another repository, a base that is its head or no ancestor of it, or fields the estate cannot read.</param>
    /// <param name="Branch">The main line of this machine's clone that its head was compared with, when one was read.</param>
    internal sealed record ChangeView(string Repository, string Base, string Head, string Target, string Integration, RecordEnvelope? Merge,
        IReadOnlyList<(string Path, string Change)>? Paths = null, bool Read = false, string? Problem = null, string? Branch = null);

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

    /// <summary>
    /// Why a draft brief would be refused for the fields the check reads, were it recorded, as the write boundary validates a brief (from
    /// 0.9.4): its permitted changes and exclusions each a string of 1 to 2,000 characters, each exclusion an interpretable pattern, its
    /// after at most 20 distinct assignment ids, and its repository an id. Empty when the preview may compare it.
    /// </summary>
    internal static IReadOnlyList<string> DraftProblems(JsonElement brief)
    {
        if (brief.ValueKind != JsonValueKind.Object) return ["A draft brief is a JSON object, as work assign takes it."];
        var findings = new FindingList();
        var reader = new Types.BodyReader(brief, "brief", findings, 3);
        reader.StringArray("permittedChanges", false, allowEmpty: false, maxItemLength: PathIntersection.LengthLimit);
        IReadOnlyList<string>? excluded = reader.StringArray("excludedChanges", false, allowEmpty: false, maxItemLength: PathIntersection.LengthLimit);
        var problems = new List<string>(findings.Select(f => $"{f.Location}: {f.Message}"));
        problems.AddRange((excluded ?? []).Where(e => PathPatterns.Pattern(e) is null)
            .Select(e => $"brief.excludedChanges: '{(e.Length > 80 ? e[..80] + "..." : e)}' is not a repository-relative pattern; an exclusion is a pattern, since prose excludes nothing."));
        findings = new FindingList();
        reader = new Types.BodyReader(brief, "brief", findings, 3);
        IReadOnlyList<string>? after = reader.StringArray("after", false, allowEmpty: false, maxItemLength: Records.Identity.Ulid.Length);
        if (reader.Has("repository")) reader.Text("repository", true, Records.Identity.Ulid.Length);
        problems.AddRange(findings.Select(f => $"{f.Location}: {f.Message}"));
        if (after is not null)
        {
            if (after.Count > Types.AssignmentType.MaxAfter) problems.Add($"brief.after: a brief waits for at most {Types.AssignmentType.MaxAfter} assignments; it names {after.Count}.");
            if (after.Distinct(StringComparer.Ordinal).Count() != after.Count) problems.Add("brief.after: each assignment is named once.");
            problems.AddRange(after.Where(id => !Records.Identity.Ulid.IsValid(id)).Take(3).Select(id => $"brief.after: '{id}' is not an assignment id."));
        }

        return problems;
    }

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
                ["integration"] = change.Integration, ["merge"] = change.Merge?.Id, ["mainLine"] = change.Branch, ["problem"] = change.Problem,
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

        List<string> shared = inventory is { } listed ? Shared(listed.Paths, a, b) : [];
        bool determined = outcome.Determined || shared.Count > 0;
        if (!determined)
        {
            labels.Add($"undetermined: the search reached its bound of {PathIntersection.StateBound.ToString("N0", CultureInfo.InvariantCulture)} states or {PathIntersection.WorkBound.ToString("N0", CultureInfo.InvariantCulture)} steps of work, so the pair counts as overlapping");
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

    /// <summary>The bound on the work of finding tracked witnesses for one pair: characters compared, a wildcard match costing its pattern's length times the path's.</summary>
    internal const long ScanBound = 50_000_000;

    /// <summary>
    /// Up to 20 tracked paths, in their ordinal order, in both parties' effective paths, within <see cref="ScanBound"/>: a pattern's
    /// literal prefix is compared first, a wildcard pattern whose prefix a path begins with costs its length times the path's, and a
    /// scan that spends the bound keeps what it found.
    /// </summary>
    internal static List<string> Shared(IReadOnlyList<string> paths, PathIntersection.Party a, PathIntersection.Party b)
    {
        Compiled first = new(a), second = new(b);
        long work = 0;
        var shared = new List<string>();
        foreach (string path in paths)
        {
            work += path.Length + 1;
            if (work > ScanBound) break;
            bool both = PathPatterns.Valid(path) && first.Contains(path, ref work) && second.Contains(path, ref work);
            if (work > ScanBound) break;
            if (!both) continue;
            shared.Add(path);
            if (shared.Count == 20) break;
        }

        return shared;
    }

    /// <summary>
    /// Whether one of these paths is in a party's effective paths, within <see cref="ScanBound"/>: null when the bound is spent first, so
    /// that a scan cut short shows nothing either way.
    /// </summary>
    internal static bool? Touches(IEnumerable<string> paths, PathIntersection.Party party)
    {
        Compiled compiled = new(party);
        long work = 0;
        foreach (string path in paths)
        {
            work += path.Length + 1;
            if (work > ScanBound) return null;
            bool inside = PathPatterns.Valid(path) && compiled.Contains(path, ref work);
            if (work > ScanBound) return null;
            if (inside) return true;
        }

        return false;
    }

    // A party's patterns and exclusions, each with its literal prefix and whether it holds a wildcard, matched as PathPatterns matches them.
    private sealed class Compiled(PathIntersection.Party party)
    {
        private readonly (string Pattern, string Prefix, bool Wild)[] patterns = [.. party.Patterns.Select(Prepare)];
        private readonly (string Pattern, string Prefix, bool Wild)[] exclusions = [.. party.Exclusions.Select(Prepare)];

        public bool Contains(string path, ref long work) => Any(patterns, path, ref work) && !Any(exclusions, path, ref work);

        private static (string, string, bool) Prepare(string pattern)
        {
            int wild = pattern.IndexOfAny(['*', '?']);
            return (pattern, wild < 0 ? pattern.TrimEnd('/') : pattern[..wild], wild >= 0);
        }

        private static bool Any((string Pattern, string Prefix, bool Wild)[] list, string path, ref long work)
        {
            foreach ((string pattern, string prefix, bool wild) in list)
            {
                work += prefix.Length + 1;
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (wild) work += (long)pattern.Length * (path.Length + 1);
                // A spent bound answers no: the scan stops before this path counts.
                if (work > ScanBound) return false;
                if (PathPatterns.Matches(path, pattern)) return true;
            }

            return false;
        }
    }

    // Whether a party's after names the other, followed to its current revision.
    private static bool Names(EstateModel model, Party waiting, Party waited) =>
        waited.Assignment.Id != "draft" && waiting.After.Any(id => model.ById.TryGetValue(id, out RecordEnvelope? named) && named.Type == "assignment" && model.Latest(named).Id == model.Latest(waited.Assignment).Id);

    /// <summary>
    /// One read of the estate's assignments: work-order states, agent runs, change-merge operations and the build's beginning computed
    /// once, and each answer of this machine's clone kept for the reading, so that a change named twice reads the clone once.
    /// </summary>
    private sealed class Reading
    {
        private readonly EstateModel model;
        private readonly IRepositoryView? view;
        private readonly Dictionary<string, string> orders = new(StringComparer.Ordinal);
        private readonly Lazy<Dictionary<string, string>> runs;
        private readonly Lazy<Dictionary<string, HashSet<string>>> branches;
        private readonly Lazy<Dictionary<(string Repository, string Head), RecordEnvelope>> merges;
        private readonly Lazy<RecordEnvelope?> began;
        private readonly Dictionary<string, string?> lines = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Repository, string Base, string Head), bool?> precedes = [];
        private readonly Dictionary<(string Repository, string Head, string Branch), bool?> reachable = [];
        private readonly Dictionary<(string Repository, string Base, string Head), IReadOnlyList<(string Path, string Change)>?> changed = [];

        public Reading(EstateModel model, IRepositoryView? view)
        {
            this.model = model;
            this.view = view;
            runs = new(() =>
            {
                var repositories = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (AgentRuns.Run run in AgentRuns.All(model))
                {
                    if (run.Assignment is string assignment && IdentityOf(model, run.Target("repository")) is string repository) repositories[assignment] = repository;
                }

                return repositories;
            });
            // The branches agent runs worked on, by repository: a run's branch is never the integration line, whatever the clone names.
            branches = new(() =>
            {
                var named = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (AgentRuns.Run run in AgentRuns.All(model))
                {
                    if (run.Target("branch") is string branch && IdentityOf(model, run.Target("repository")) is string repository)
                    {
                        (named.TryGetValue(repository, out HashSet<string>? set) ? set : named[repository] = new HashSet<string>(StringComparer.Ordinal)).Add(branch);
                    }
                }

                return named;
            });
            merges = new(() =>
            {
                var observed = new Dictionary<(string, string), RecordEnvelope>();
                foreach (RecordEnvelope operation in model.Current("operation"))
                {
                    if (EstateModel.BodyString(operation, "action") != "change-merge" || EstateModel.BodyString(operation, "status") != "observed-success"
                        || !operation.Body.TryGetProperty("target", out JsonElement target) || TJson.Text(target, "head") is not string head
                        || IdentityOf(model, TJson.Text(target, "repository")) is not string repository) continue;
                    observed.TryAdd((repository, head), operation);
                }

                return observed;
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
            if (!orders.TryGetValue(order.Id, out string? status))
            {
                orders[order.Id] = status = EstateStatus.StatusOf(model, order);
            }

            IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
            RecordEnvelope? result = attempts.Count == 0 ? null : WorkView.ResultOf(model, attempts[^1]);
            // The paths a change must touch to be this assignment's work: its effective paths, or none to ask of a brief with no path pattern.
            PathIntersection.Party? scope = patterns.Count == 0 ? null : new(patterns, IsEstate(model, repository) ? [.. excluded, .. EstateLeftOut] : excluded);
            (bool active, string state, ChangeView? change) = State(status, attempts.Count, result, repository, scope, labels);
            return new Party(assignment, key, contributor, repository, from, patterns, excluded, after, active, state, labels, result, change);
        }

        // The records decide first: an accepted or retired work order and an abandoned or unfinished result read no clone, and only a
        // completed result of open work whose change no change-merge operation shows merged asks this machine's clone.
        private (bool Active, string State, ChangeView? Change) State(string status, int attempts, RecordEnvelope? result, string? repository, PathIntersection.Party? scope, List<string> labels)
        {
            ChangeView? named = result is null ? null : Named(result, repository);
            if (status == "accepted") return (false, "released: its work order is accepted", named);
            if (status == "retired") return (false, "released: its work order is retired", named);
            if (result is null) return (true, attempts == 0 ? "active: no attempt yet" : "active: an attempt in progress", null);
            string outcome = EstateModel.BodyString(result, "outcome") ?? "?";
            if (outcome == "abandoned") return (false, $"released: its current result {result.Id} is abandoned", named);
            if (outcome != "completed") return (true, $"active: its current result {result.Id} is {outcome}", named);
            if (named is not null)
            {
                ChangeView change = Integrate(named, scope, labels);
                switch (change.Integration)
                {
                    case "merged":
                        string? into = change.Merge!.Body.TryGetProperty("target", out JsonElement merged) ? TJson.Text(merged, "target") : null;
                        return (false, $"released: its change's head {change.Head} merged{(into is null ? string.Empty : " into " + into)} (operation {change.Merge.Id})", change);
                    case "reachable":
                        labels.Add($"observed here: its change's head is reachable from {change.Branch}, the main line of this machine's registered clone");
                        return (false, $"released: its change's head {change.Head} is reachable from {change.Branch}", change);
                    case "not-integrated":
                        return (true, $"active: its change's head {change.Head} is not yet integrated into {change.Branch}", change);
                    case "mismatched":
                        return (true, $"active: its change cannot release it, since {change.Problem}", change);
                    default:
                        return (true, $"active: its change's head {change.Head} is not observed integrated into its repository's main line", change);
                }
            }

            if (scope is null) return (false, $"released: its current result {result.Id} is completed and its brief holds no path pattern", null);
            if (began.Value is not RecordEnvelope start || GoalRules.Before(result, start))
            {
                labels.Add("its result was recorded before the overlap check, which records no integration, so it counts as released at completion");
                return (false, $"released: its current result {result.Id} is completed", null);
            }

            return (true, $"active until acceptance: its current result {result.Id} is completed and names no change", null);
        }

        // The change a result names as the records alone read it, with what keeps it from being this assignment's work: another
        // repository than the assignment's, the same commit as base and head, or fields the estate cannot read. Reads no clone.
        private ChangeView? Named(RecordEnvelope result, string? assignmentRepository)
        {
            if (!result.Body.TryGetProperty("change", out JsonElement change) || change.ValueKind != JsonValueKind.Object) return null;
            string? repository = IdentityOf(model, TJson.Text(change, "repository"));
            string? head = TJson.Text(change, "head"), target = TJson.Text(change, "target"), from = TJson.Text(change, "base");
            string? problem = repository is null || head is null || target is null || from is null
                    || !Types.Patterns.Commit.IsMatch(head) || !Types.Patterns.Commit.IsMatch(from) ? "it does not name a repository the estate knows, a base and a head commit and a target"
                : assignmentRepository is null ? "its assignment names no repository and no run of it named one, so no change can be shown to be its work"
                : repository != assignmentRepository ? $"it is in {NameOf(model, repository)}, not in {NameOf(model, assignmentRepository)}, the repository its assignment changes"
                : head == from ? $"its base and head are the same commit, {head}, so it changes nothing"
                : null;
            return new ChangeView(repository ?? string.Empty, from ?? string.Empty, head ?? string.Empty, target ?? string.Empty, problem is null ? "unknown" : "mismatched", null, Problem: problem);
        }

        // A named change's integration: a change-merge operation observed at its head, from the records; else, in this machine's clone, a
        // base that is a proper ancestor of its head and that head reachable from the repository's main line, never from the branch the
        // result names as its target and never from a branch an agent run worked on. Where the clone shows the paths the change touched,
        // one of them is in the assignment's effective paths, so that an unrelated range on the main line is not its work. A change that
        // stays active has its paths read.
        private ChangeView Integrate(ChangeView named, PathIntersection.Party? scope, List<string> labels)
        {
            if (named.Problem is not null) return named with { Paths = null, Read = false };
            string repository = named.Repository, from = named.Base, head = named.Head;
            if (merges.Value.TryGetValue((repository, head), out RecordEnvelope? merge))
            {
                // A merge the records show stands unless the clone shows the change touched none of the assignment's paths.
                IReadOnlyList<(string Path, string Change)>? merged = view is null || scope is null ? null : Cached(changed, (repository, from, head), () => view.Changed(repository, from, head));
                return merged is not null && Touches(merged.Select(p => p.Path), scope!) == false
                    ? named with { Integration = "mismatched", Problem = Untouched(merged.Count), Paths = merged, Read = true }
                    : named with { Integration = "merged", Merge = merge };
            }

            if (view is null) return named;
            string? line = Cached(lines, repository, () => view.MainLine(repository));
            bool? ancestor = Cached(precedes, (repository, from, head), () => view.Precedes(repository, from, head));
            ChangeView read = named with { Branch = line };
            if (ancestor == false)
            {
                read = read with { Integration = "mismatched", Problem = $"its base {from} is not an ancestor of its head {head} in this machine's registered clone" };
            }
            else if (line is null)
            {
                labels.Add("this machine has no registered clone of its repository with a main line (origin's HEAD, main or master), so no integration is observed here");
            }
            else if (branches.Value.TryGetValue(repository, out HashSet<string>? worked) && worked.Contains(line))
            {
                labels.Add($"the main line of this machine's registered clone, {line}, is a branch an agent run worked on, so reaching it shows no integration");
            }
            else if (ancestor is null)
            {
                labels.Add("a commit of its change is missing from this machine's registered clone, so no integration is observed here");
            }
            else
            {
                bool? reached = Cached(reachable, (repository, head, line), () => view.Reachable(repository, head, line));
                if (reached == true)
                {
                    if (scope is null) return read with { Integration = "reachable" };
                    IReadOnlyList<(string Path, string Change)>? touched = Cached(changed, (repository, from, head), () => view.Changed(repository, from, head));
                    bool? inside = touched is null ? null : Touches(touched.Select(p => p.Path), scope);
                    if (inside == true) return read with { Integration = "reachable" };
                    if (inside == false) return read with { Integration = "mismatched", Problem = Untouched(touched!.Count), Paths = touched, Read = true };
                    labels.Add(touched is null
                        ? "the paths its change touched are unavailable in this machine's registered clone, so it is not shown to be its assignment's work"
                        : "the paths its change touched could not be compared with its assignment's within the bound, so it is not shown to be its assignment's work");
                    return read with { Paths = touched, Read = true };
                }

                if (reached == false) read = read with { Integration = "not-integrated" };
            }

            if (line is not null && named.Target != line)
            {
                labels.Add($"its change names {named.Target} as its target; only the repository's main line, {line}, shows integration here");
            }

            return read with { Paths = Cached(changed, (repository, from, head), () => view.Changed(repository, from, head)), Read = true };
        }

        private static string Untouched(int count) =>
            $"none of the {count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? "path" : "paths")} it changed is in its assignment's effective permitted paths, so it is not that assignment's work";

        private static TValue Cached<TKey, TValue>(Dictionary<TKey, TValue> cache, TKey key, Func<TValue> read) where TKey : notnull
        {
            if (!cache.TryGetValue(key, out TValue? value)) cache[key] = value = read();
            return value;
        }
    }
}
