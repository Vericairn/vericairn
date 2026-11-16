// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Presence sessions at the shared signing boundary (ID-16, design/presence-sessions.md §§ 5, 7, 9 and 10): which signatures one presence
/// check covers. A signature of a person's macos-secure-enclave device with presence required, on the embedded or local-socket profile,
/// in an action of the vocabulary that composes a list, may be signed under the action's session; every other keeps its own check, which
/// from 0.9.4 the helper's sign-record labels with the record's type and short hash.
/// </summary>
public sealed partial class EstateService
{
    private PresenceSessionService? presenceSessionService;

    private PresenceSessionService PresenceSessions => LazyInitializer.EnsureInitialized(ref presenceSessionService, () => new PresenceSessionService(presence) { OutsideChanged = MarkEventsDirty });

    /// <summary>From 0.9.4 (ID-16): the profile of a runtime the command line hosts in its own process, one of the two a presence session opens on.</summary>
    public const string EmbeddedProfile = "embedded";

    /// <summary>From 0.9.4 (ID-16): the profile of a request the daemon receives over its local socket or named pipe, the other of the two.</summary>
    public const string LocalSocketProfile = "local-socket";

    private readonly AsyncLocal<string?> presenceProfile = new();

    /// <summary>
    /// Whether this service is the runtime a command line hosts in its own process: its own callers then act on the embedded profile. The
    /// command line sets it for the embedded runtime it opens; the daemon never does, so a call reaching its service by any other way than
    /// its local socket opens no presence session.
    /// </summary>
    internal bool HostsEmbedded { get; set; }

    /// <summary>
    /// From 0.9.4 (ID-16): marks the operation in flight as a request the daemon received over its local socket or named pipe, where the
    /// person at the machine reads the notices. Disposing it restores the caller's profile. Only the daemon's own listener calls it.
    /// </summary>
    public IDisposable ForLocalSocketRequest()
    {
        string? previous = presenceProfile.Value;
        presenceProfile.Value = LocalSocketProfile;
        return new ProfileScope(this, previous);
    }

    // The profile a presence session may open on, named positively: the embedded runtime's or the local socket's; null for every other.
    private string? PresenceProfile => presenceProfile.Value ?? (HostsEmbedded ? EmbeddedProfile : null);

    private sealed class ProfileScope(EstateService service, string? previous) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) service.presenceProfile.Value = previous;
        }
    }

    /// <summary>A test's replacement for what presence sessions depend on outside the runtime: the build, the platform, the helper and the clock.</summary>
    internal PresenceSessionHost PresenceHost
    {
        get => PresenceSessions.Host;
        set => PresenceSessions.Host = value;
    }

    /// <summary>The refusals that ended presence sessions here (attention presence.outside-list), newest last.</summary>
    internal IReadOnlyList<PresenceOutsideList> PresenceOutside => PresenceSessions.Outside;

    /// <summary>
    /// Why presence sessions are off for this machine and build, or null when its release helper may open them: doctor says it. The
    /// estate's charter and each device owner's setting may still ask one check per signature.
    /// </summary>
    internal static string? PresenceSessionsOff(PresenceSessionHost? host = null)
    {
        host ??= new PresenceSessionHost();
        return host.MachineOff(SecureEnclaveCustody.HelperPath(host.Build));
    }

    /// <summary>The presence the estate's charter asks of every person's device: per-signature, per-action, or null when it states none.</summary>
    internal string? EstatePresencePolicy()
    {
        lock (gate) return EstateType.PresencePolicy(session.Read());
    }

    /// <summary>Opens an action's tally with what composes its presence session's list (§ 5).</summary>
    private IDisposable SessionAction(string action, IEnumerable<PresenceSignature>? planned, PresenceSessionAction? session) => presence.Action(action, planned, session);

    /// <summary>
    /// An exact action's session state: its drafts, each authored completely with its id and time before the announcement, signed in order
    /// through <see cref="SessionDraft"/>; <paramref name="redatable"/> when no draft depends on another's hash, so a later list may author
    /// those not yet signed again with a new time.
    /// </summary>
    private static PresenceSessionAction ExactSession(string action, string identity, IReadOnlyList<RecordDraft> drafts, bool redatable)
    {
        PresenceSessionAction? state = null;
        state = new PresenceSessionAction(_ => new PresenceSessionPlan { Action = action, Identity = identity, Exact = [.. drafts.Select(d => state!.Current(d))] }, redatable);
        state.ExactIds.AddRange(drafts.Select(d => d.Id!));
        return state;
    }

    /// <summary>The draft an exact action signs now (<see cref="PresenceSessionService.Draft"/>).</summary>
    private RecordDraft SessionDraft(RecordDraft draft) => PresenceSessions.Draft(draft);

    /// <summary>Marks the open action's next signature as the record that dispatches an effect with the runtime's two-minute bound (§ 9).</summary>
    private void PresenceEffectAhead() => PresenceSessions.EffectAhead(PresenceSessionService.EffectDeadline);

    /// <summary>The present time on the presence sessions' clock, which exact drafts are dated by.</summary>
    private DateTimeOffset SessionNow() => PresenceSessions.Host.Clock.GetUtcNow();

    /// <summary>A draft with its time and id fixed now, to the whole second, so its bytes and hash are known before the announcement.</summary>
    private static RecordDraft Fixed(RecordDraft draft, DateTimeOffset at)
    {
        DateTimeOffset second = DateTimeOffset.FromUnixTimeSeconds((draft.At ?? at).ToUnixTimeSeconds());
        return draft with { At = second, Id = draft.Id ?? global::Vericairn.Records.Identity.Ulid.NewUlid(second).Value };
    }

    /// <summary>
    /// Signs one record under the open action's presence session, or null when it keeps its own check (§§ 7 and 10): only on the embedded
    /// and local-socket profiles, which the host names (<see cref="HostsEmbedded"/>, <see cref="ForLocalSocketRequest"/>), only for a
    /// person's device whose key is in the Secure Enclave with presence required, by its own key.
    /// </summary>
    private RecordSignature? SessionSignature(StoreSigner held, RecordDraft draft, EstateModel model, byte[] content)
    {
        if (presence.Current?.Session is null) return null;
        // Only the embedded and local-socket profiles, named positively, and never a request a paired device signed.
        if (PresenceProfile is not (EmbeddedProfile or LocalSocketProfile) || requestDevice.Value is { Closed: false }) return null;
        if (held.KeyId != held.DeviceId) return null;
        RecordEnvelope? device = model.Current("device").FirstOrDefault(d => model.Chain(d)[0] == held.DeviceId);
        if (device is null || EstateModel.BodyString(device, "principal") is not string principal || principal != draft.By || EstateModel.BodyString(device, "publicKey") is not string publicKey)
            return null;
        PresenceSessionHost host = PresenceSessions.Host;
        PresenceSetting setting = host.Setting(held.DeviceId);
        int limit = Math.Clamp(Math.Min(setting.LimitSeconds ?? PresenceSessionProtocol.MaxLimitSeconds, PresenceSessionProtocol.MaxLimitSeconds),
            PresenceSessionProtocol.MinLimitSeconds, PresenceSessionProtocol.MaxLimitSeconds);
        var signing = new PresenceSessionSigning
        {
            Signer = held, Device = held.DeviceId, Principal = principal, PublicKey = publicKey, Content = content, Describe = DescribeRecord(draft, model), Limit = limit,
            WhyOff = () => SessionOff(held, model, principal, setting),
            ComposeList = (action, agentRun) => ComposeSessionList(action, agentRun, draft, content, model, principal, held.DeviceId, publicKey),
        };
        return PresenceSessions.Sign(signing);
    }

    // Why no session applies to a signer's signatures: § 10's custody, person and settings, then § 11's machine and build.
    private string? SessionOff(StoreSigner held, EstateModel model, string principal, PresenceSetting setting)
    {
        if (held.Custody != SecureEnclaveCustody.Kind) return $"{held.Custody} custody keeps one check per signature";
        if (model.CurrentPrincipal(principal) is not { } person || EstateModel.BodyString(person, "kind") != "human") return "only a person's device opens a presence session";
        if (CharterPresenceOff(model) is string charter) return charter;
        if (setting.Problem is not null) return setting.Problem;
        if (setting.PerSignature) return "the device's owner asks one presence check per signature on this machine";
        PresenceSessionHost host = PresenceSessions.Host;
        return host.MachineOff(host.HelperOf(held));
    }

    /// <summary>
    /// Why the estate's charter keeps one check per signature, or null when it allows presence sessions: it asks per-signature, or it
    /// states a value that is neither per-signature nor per-action, which fails closed whether or not the charter validates.
    /// </summary>
    internal static string? CharterPresenceOff(EstateModel model) => EstateType.PresencePolicy(model) switch
    {
        null or "per-action" => null,
        "per-signature" => "the estate's charter asks one presence check per signature",
        _ => "the estate's charter states a presence policy that is neither per-signature nor per-action, so every signature asks its own check",
    };

    /// <summary>
    /// Composes the open action's list for the record about to be signed: its exact entries not yet signed, from the first, at most 16, each
    /// authored completely and dated inside the helper's window, none while an agent run this machine's worker started is unsettled under
    /// this user; its kind entries; its references, with the operations and releases its earlier sessions signed; and, when the record is
    /// not covered, an entry of its own where § 10 admits one. Null when no list covers the record. Caller holds the gate.
    /// </summary>
    private PresenceSessionList? ComposeSessionList(PresenceSessionAction action, bool agentRun, RecordDraft signing, byte[] content, EstateModel model,
        string principal, string device, string publicKey)
    {
        PresenceSessionPlan? plan = action.Compose(new PresenceComposeContext(signing, action.Signed));
        if (plan is null || !PresenceSessionProtocol.Actions.Contains(plan.Action)) return null;
        long now = PresenceSessions.Host.Clock.GetUtcNow().ToUnixTimeSeconds();
        var entries = new List<PresenceListEntry>();
        var lines = new List<(PresenceSignature, string, bool)>();
        var exactIds = new List<string>();
        if (!agentRun)
        {
            foreach (RecordDraft planned in plan.Exact)
            {
                RecordDraft draft = action.Current(planned);
                if (exactIds.Count == PresenceSessionProtocol.MaxExact) break;
                if (draft.Id is not string id || draft.At is not DateTimeOffset at || action.Signed.Contains(id) || draft.By != principal) continue;
                if (!PresenceSessionProtocol.ExactAllowed(draft.Type, BodyText(draft.Body, "action"), BodyText(draft.Body, "status"))) continue;
                // Inside the helper's window (120 seconds before its opening to 60 after), with room for the announcement.
                if (at.ToUnixTimeSeconds() < now - 100 || at.ToUnixTimeSeconds() > now + 50) continue;
                (AuthoredRecord? unsigned, _) = AuthorUnsignedLocked(draft, model);
                if (unsigned is null) continue;
                entries.Add(new PresenceListEntry
                {
                    Exact = true, Type = draft.Type, Action = draft.Type == "operation" ? BodyText(draft.Body, "action") : null,
                    Hash = unsigned.Envelope.ComputedHash, Bytes = RecordAuthor.ContentBytes(unsigned.Bytes),
                });
                lines.Add((DescribeRecord(draft, model), ChangeLine(draft, model), SameHarnessReview(draft, model, Providers.Launchers)));
                exactIds.Add(id);
            }
        }

        int offset = entries.Count;
        foreach (PresencePlannedKind kind in plan.Kinds)
        {
            entries.Add(kind.Entry with
            {
                Observes = kind.Entry.Observes is int observed ? observed + offset : null,
                PullRequestFrom = kind.Entry.PullRequestFrom is int from ? from + offset : null,
            });
            lines.Add((new PresenceSignature(kind.Entry.Type, KindKey(kind.Entry), kind.Summary), kind.Changes, false));
        }

        var references = new Dictionary<string, PresenceListReference>(StringComparer.Ordinal);
        foreach (string id in plan.References.Concat(action.Signed))
        {
            if (ReferenceOf(model, id) is { } reference) references[id] = reference;
        }

        var list = new PresenceSessionList
        {
            Action = plan.Action, ActionIdentity = ContentHash.Sha256(Encoding.UTF8.GetBytes(plan.Identity)), Entries = entries,
            References = [.. references.Values], Lines = lines, ExactIds = exactIds,
        };
        list = Bounded(list);
        if (list.Entries.Count > 0 && Covers(list, content, principal, device, publicKey, now)) return list;
        entries = [.. list.Entries];
        lines = [.. list.Lines];

        // The record is not covered: an entry of its own, where § 10 admits it, and the records it supersedes or observes as references.
        PresenceRecord record;
        try
        {
            record = PresenceRecord.Read(content, "record.form");
        }
        catch (PresenceSessionRefusal)
        {
            return null;
        }

        var actionRecords = new HashSet<string>(references.Keys.Concat(action.Signed), StringComparer.Ordinal);
        if (OwnEntry(record, signing, model, agentRun || exactIds.Count == PresenceSessionProtocol.MaxExact, now, entries, actionRecords) is not { } own) return null;
        foreach (string id in own.References)
        {
            if (!references.ContainsKey(id) && ReferenceOf(model, id) is { } reference) references[id] = reference;
        }

        entries.Add(own.Entry);
        lines.Add((DescribeRecord(signing, model), own.Changes, false));
        if (own.Entry.Exact) exactIds.Add(record.Id);
        list = new PresenceSessionList
        {
            Action = plan.Action, ActionIdentity = list.ActionIdentity, Entries = entries, References = [.. references.Values], Lines = lines, ExactIds = exactIds,
        };
        list = Bounded(list);
        return Covers(list, content, principal, device, publicKey, now) ? list : null;
    }

    // Whether the runtime's own checks, the helper's, admit the list and cover the record at an opening now.
    private static bool Covers(PresenceSessionList list, byte[] content, string principal, string device, string publicKey, long now)
    {
        try
        {
            byte[] terms = PresenceTermsWriter.Write(list.Action, list.ActionIdentity, principal, device, publicKey, global::Vericairn.Records.Identity.Ulid.NewUlid().Value,
                PresenceSessionProtocol.MaxLimitSeconds, list.Entries, list.References);
            PresenceSessionRules rules = PresenceSessionRules.Open(terms, PresenceTermsWriter.Bytes(list.Entries, list.References), now);
            rules.Match(content, now, 0, out _);
            return true;
        }
        catch (PresenceSessionRefusal)
        {
            return false;
        }
    }

    // A list over 128 signatures gives up the repetitions of its "at most" entries first, then its last kind entries; a later one renews.
    private static PresenceSessionList Bounded(PresenceSessionList list)
    {
        if (list.Total <= PresenceSessionProtocol.MaxSignatures) return list;
        List<PresenceListEntry> entries = [.. list.Entries];
        List<(PresenceSignature, string, bool)> lines = [.. list.Lines];
        while (entries.Sum(e => e.Signatures) > PresenceSessionProtocol.MaxSignatures)
        {
            int index = entries.FindLastIndex(e => !e.Exact && e.AtMost && e.Count > 3);
            if (index >= 0)
            {
                entries[index] = entries[index] with { Count = Math.Max(3, entries[index].Count - 3) };
                continue;
            }

            // Only the last entry goes, and only when no other names it, so every index the others hold stays theirs.
            int last = entries.Count - 1;
            if (last < 0 || entries[last].Exact || entries.Any(o => o.Observes == last || o.PullRequestFrom == last)) break;
            entries.RemoveAt(last);
            lines.RemoveAt(last);
        }

        return new PresenceSessionList { Action = list.Action, ActionIdentity = list.ActionIdentity, Entries = entries, References = list.References, Lines = lines, ExactIds = list.ExactIds };
    }

    // A record as a reference of a list: an operation or a release this estate holds, by its id, type, hash and canonical bytes.
    private PresenceListReference? ReferenceOf(EstateModel model, string id)
    {
        if (!model.ById.TryGetValue(id, out RecordEnvelope? record) || record.Type is not ("operation" or "release")) return null;
        byte[]? bytes = session.LoadedFile(record.ExpectedPath)?.Bytes.ToArray();
        return bytes is null ? null : new PresenceListReference(record.Id, record.Type, record.ComputedHash, RecordAuthor.ContentBytes(bytes));
    }

    /// <summary>
    /// An entry of a record's own, for a record the composed list does not cover, held to the action (§ 5): an exact entry where § 10's
    /// allow-list admits the record, its time is inside the helper's window and the list may hold one more; otherwise a kind entry only
    /// where it re-issues one the action planned: the same kind with every value that entry fixes (provider, repository, product, product
    /// hash, branch or pull request, and the head the action's changes state; or the release build's provider, destination, version,
    /// product, commit and workflow, through <see cref="ReleaseBuildEntry"/>), or an observation of one of the action's own operations, of
    /// an observation the action planned. Its count is what remains of that record's step, the record included. A record of another branch,
    /// head, pull request, destination, version or workflow gets none and keeps its own check.
    /// </summary>
    internal static (PresenceListEntry Entry, string Changes, IReadOnlyList<string> References)? OwnEntry(PresenceRecord record, RecordDraft draft, EstateModel model,
        bool noExact, long now, List<PresenceListEntry> planned, HashSet<string> actionRecords)
    {
        string? action = record.Field("action"), status = record.Field("status");
        bool kind = record.Type == "history-checkpoint" && record.Field("trigger") == "release"
            || record.Type == "release" && status is "candidate" or "published"
            || record.Type == "operation" && action is not null && PresenceSessionProtocol.KindActions.Contains(action);
        if (!kind)
        {
            if (noExact || !PresenceSessionProtocol.ExactAllowed(record.Type, action, status) || record.At < now - 100 || record.At > now + 50) return null;
            return (new PresenceListEntry { Exact = true, Type = record.Type, Action = record.Type == "operation" ? action : null, Hash = record.Hash, Bytes = record.Bytes },
                ChangeLine(draft, model), []);
        }

        List<string> references = record.Supersedes is string previous && actionRecords.Contains(previous) ? [previous] : [];
        if (record.Supersedes is not null && references.Count == 0) return null;
        int remaining = Remaining(record, model);
        switch (record.Type)
        {
            case "history-checkpoint":
                return planned.Any(p => p.Type == "history-checkpoint") ? (ReleaseCheckpointEntry().Entry, "adds the release gate's history checkpoint", references) : null;
            case "release":
                PresenceListEntry? release = planned.FirstOrDefault(p => p.Type == "release" && p.Statuses.Contains(status!, StringComparer.Ordinal)
                    && p.Fixed.GetValueOrDefault("product") == record.Subject?.Id && p.Fixed.GetValueOrDefault("version") == record.Field("version")
                    && p.Fixed.GetValueOrDefault("destination") == record.Field("repository") && p.Fixed.GetValueOrDefault("commit") == record.Field("commit"));
                return release is not null && ReleaseRecordEntry(release.Fixed["product"], release.Fixed["version"], release.Fixed["destination"], release.Fixed["commit"], [status!], 1) is { } releaseEntry
                    ? (releaseEntry.Entry, releaseEntry.Changes, references) : null;
        }

        if (action == "change-observe")
        {
            // An observation of one of the action's own operations, of an observation the action planned.
            if (record.Target("operation") is not string observed || !actionRecords.Contains(observed)
                || !planned.Any(p => p.Action == "change-observe" && p.Observation == record.Target("observation"))) return null;
            references.Add(observed);
            return (new PresenceListEntry
            {
                Type = "operation", Action = action, Count = remaining, Statuses = PresenceSessionProtocol.OperationStatuses,
                Fixed = new Dictionary<string, string>(StringComparer.Ordinal), Observation = record.Target("observation"), ObservesReference = observed,
            }, $"adds what remains of an observation of recorded operation {observed}", references);
        }

        if (action == "dispatch-release-workflow")
        {
            // The release build the action planned, its count what remains of this step: composed by ReleaseBuildEntry alone.
            PresenceListEntry? build = planned.FirstOrDefault(p => p.Action == action);
            if (build is null) return null;
            // The workflow is the planned entry's, and the record must name it: a record naming another workflow, or none, gets none.
            if (build.Fixed.GetValueOrDefault("workflow") is not string workflow || record.Target("workflow") != workflow) return null;
            var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["workflow"] = workflow };
            if (record.Target("ref") is string stated) settings["ref"] = stated;
            if (record.Target("commit") is string named) settings["commit"] = named;
            return ReleaseBuildEntry(build.Fixed["provider"], build.Fixed["destination"], build.Fixed["version"], build.Fixed["product"], build.Fixed.GetValueOrDefault("commit"), settings, remaining) is { } rebuilt
                ? (rebuilt.Entry with { AtMost = false }, rebuilt.Changes, references) : null;
        }

        // A push, an opening or a merge: a planned entry of the same action whose every fixed value the record states, at the head the
        // action's own changes state; a merge's pull request only one the action's opening recorded.
        string? head = record.Target("head");
        string? actionHead = actionRecords.Select(id => model.ById.GetValueOrDefault(id)).OfType<RecordEnvelope>()
            .Where(r => r.Type == "operation" && EstateModel.BodyString(r, "action") is string a && PresenceSessionProtocol.ChangeActions.Contains(a))
            .Select(r => r.Body.TryGetProperty("target", out JsonElement t) ? ChangeText(t, "head") : null).FirstOrDefault(h => h is not null);
        if (head is null || actionHead is not null && head != actionHead) return null;
        string? Value(string name) => name == "provider" ? record.Field("provider") : record.Target(name);
        PresenceListEntry? change = planned.FirstOrDefault(p => p.Action == action && p.Fixed.All(f => Value(f.Key) == f.Value));
        if (change is null) return null;
        var values = new Dictionary<string, string>(change.Fixed, StringComparer.Ordinal) { ["head"] = head };
        if (action == "change-merge" && !values.ContainsKey("pullRequest"))
        {
            string? pull = record.Target("pullRequest");
            bool opened = pull is not null && actionRecords.Select(id => model.ById.GetValueOrDefault(id)).OfType<RecordEnvelope>().Any(r => EstateModel.BodyString(r, "action") == "change-open"
                && r.Body.TryGetProperty("target", out JsonElement t) && ChangeText(t, "pullRequest") == pull);
            if (!opened) return null;
            values["pullRequest"] = pull!;
        }

        return (new PresenceListEntry { Type = "operation", Action = action, Count = remaining, Statuses = PresenceSessionProtocol.OperationStatuses, Fixed = values },
            $"adds what remains of the {OperationName(action)} the action planned, at head {head[..Math.Min(12, head.Length)]}", references);
    }

    // What remains of a record's step, the record included: an intent, its dispatch and its outcome (3); a retry, uncertain over a known
    // failure or a cancellation, likewise; a dispatch, or an uncertainty a reconciliation settles, and its outcome (2); an outcome alone (1).
    private static int Remaining(PresenceRecord record, EstateModel model) => record.Field("status") switch
    {
        "not-started" => 3,
        "uncertain" when record.Supersedes is string previous && model.ById.TryGetValue(previous, out RecordEnvelope? before)
            && EstateModel.BodyString(before, "status") is "known-failure" or "canceled" => 3,
        "dispatched" or "uncertain" => 2,
        _ => 1,
    };

    private static string KindKey(PresenceListEntry entry)
    {
        IReadOnlyDictionary<string, string> f = entry.Fixed;
        if (f.TryGetValue("branch", out string? branch)) return "branch " + branch;
        if (f.TryGetValue("pullRequest", out string? pull)) return "pull request " + pull;
        if (f.TryGetValue("version", out string? version)) return "release " + version;
        if (entry.ObservesReference is string observed) return "operation " + observed;
        return entry.Type == "history-checkpoint" ? "trigger release" : entry.Action ?? entry.Type;
    }

    private static string OperationName(string? action) => action switch
    {
        "change-push" => "push",
        "change-open" => "opening",
        "change-merge" => "merge",
        "change-observe" => "observation",
        "dispatch-release-workflow" => "release build",
        _ => action ?? "operation",
    };

    /// <summary>
    /// One line on what a signed record changes (decision 01M7DHA3SE98Z2PX0CBT3S1T05): for a revision, the body members it changes from
    /// the record it supersedes; for a new record, what it adds and what it is about.
    /// </summary>
    internal static string ChangeLine(RecordDraft draft, EstateModel model)
    {
        string about = draft.Subject is { } subject ? " about " + SubjectKey(model, subject) : string.Empty;
        if (draft.Supersedes is not string previous || !model.ById.TryGetValue(previous, out RecordEnvelope? before))
            return draft.Type switch
            {
                "acceptance" => "adds an acceptance" + about + (draft.Body.ValueKind == JsonValueKind.Object && draft.Body.TryGetProperty("basis", out JsonElement basis) && basis.ValueKind == JsonValueKind.Array
                    ? $" on the basis of {basis.GetArrayLength()} {(basis.GetArrayLength() == 1 ? "review" : "reviews")}" : string.Empty),
                _ => $"adds a new {draft.Type}{about}",
            };
        var changed = new List<string>();
        if (draft.Body.ValueKind == JsonValueKind.Object)
        {
            var names = draft.Body.EnumerateObject().Select(p => p.Name).Union(before.Body.EnumerateObject().Select(p => p.Name), StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (string name in names)
            {
                bool now = draft.Body.TryGetProperty(name, out JsonElement after), then = before.Body.TryGetProperty(name, out JsonElement prior);
                if (now != then || now && global::Vericairn.Records.Canonical.CanonicalJson.Serialize(after) != global::Vericairn.Records.Canonical.CanonicalJson.Serialize(prior)) changed.Add(name);
            }
        }

        string fields = changed.Count == 0 ? "with no change to its body" : "changing " + string.Join(", ", changed.Take(6)) + (changed.Count > 6 ? $" and {changed.Count - 6} more" : string.Empty);
        return $"revises {draft.Type} {previous}{about}, {fields}";
    }

    /// <summary>
    /// Whether a judgment rests on a review by the same agent harness that authored the reviewed work (decision 01M7DHA3SE98Z2PX0CBT3S1T05):
    /// a review whose author and the reviewed result's author are agents of one product, or a self review; or an acceptance whose basis
    /// holds such a review.
    /// </summary>
    internal static bool SameHarnessReview(RecordDraft draft, EstateModel model, IReadOnlyList<Workers.IAgentLauncher> launchers)
    {
        if (draft.Type == "review" && draft.Subject is { } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? reviewed))
            return BodyText(draft.Body, "independence") == "self" || SameHarness(model, draft.By, reviewed.By, launchers);
        if (draft.Type != "acceptance" || draft.Body.ValueKind != JsonValueKind.Object || !draft.Body.TryGetProperty("basis", out JsonElement basis) || basis.ValueKind != JsonValueKind.Array)
            return false;
        foreach (JsonElement item in basis.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && model.ById.TryGetValue(item.GetString()!, out RecordEnvelope? review) && review.Type == "review"
                && review.Subject is { } result && model.ById.TryGetValue(result.Id, out RecordEnvelope? work)
                && (EstateModel.BodyString(review, "independence") == "self" || SameHarness(model, review.By, work.By, launchers)))
                return true;
        }

        return false;
    }

    private static bool SameHarness(EstateModel model, string reviewer, string author, IReadOnlyList<Workers.IAgentLauncher> launchers)
    {
        string? Harness(string principal) => model.CurrentPrincipal(principal) is { } record && EstateModel.BodyString(record, "kind") == "agent"
            && record.Body.TryGetProperty("agent", out JsonElement agent) && agent.ValueKind == JsonValueKind.Object
            && agent.TryGetProperty("product", out JsonElement product) && product.ValueKind == JsonValueKind.String ? HarnessIdentity(product.GetString(), launchers) : null;
        return Harness(reviewer) is { Length: > 0 } first && Harness(author) is { Length: > 0 } second && first == second;
    }

    /// <summary>
    /// An agent principal's product as the harness it names, the way routing reads it: the id of the launcher whose harness name or id it
    /// matches, ignoring case, so a harness named by either of the two spellings routing accepts reads as one; otherwise the name itself,
    /// lower-cased with its spaces, underscores and dots as hyphens, the form launcher ids take. Null for an empty product.
    /// </summary>
    internal static string? HarnessIdentity(string? product, IReadOnlyList<Workers.IAgentLauncher> launchers)
    {
        ArgumentNullException.ThrowIfNull(launchers);
        string? named = product?.Trim();
        if (string.IsNullOrEmpty(named)) return null;
        if (launchers.FirstOrDefault(l => l.Harness is not null && (string.Equals(l.Harness, named, StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Launcher, named, StringComparison.OrdinalIgnoreCase))) is { } launcher) return launcher.Launcher.ToLowerInvariant();
        var identity = new StringBuilder(named.Length);
        foreach (char c in named.ToLowerInvariant())
        {
            char next = c is ' ' or '_' or '.' or '\t' ? '-' : c;
            if (next == '-' && (identity.Length == 0 || identity[^1] == '-')) continue;
            identity.Append(next);
        }

        return identity.ToString().TrimEnd('-');
    }

    /// <summary>Machine-local attention: each refusal that ended a presence session here (presence.outside-list), for the principal it signed for.</summary>
    private IEnumerable<(string Kind, string Subject, string Why, string State, string Next, string Type, string Id, string? Hash, string? At, AttentionReference? Target, string? Basis)>
        PresenceAttention(string principal)
    {
        foreach (PresenceOutsideList refusal in PresenceSessions.Outside.Where(r => r.Principal == principal))
        {
            yield return ("incident", $"{refusal.Type} {refusal.Key}",
                $"During {refusal.Action}, the release helper refused to sign {refusal.Type} {refusal.Key} at {refusal.Hash} under the presence session ({refusal.Code}), "
                + "which the runtime's own checks of the announced list admitted: a defect or an attack. Nothing was signed, the session ended and the action stopped.",
                "refused", "Inspect the action's records before running it again; each later signature asks a new presence check.",
                "presence.outside-list", refusal.Hash, refusal.Hash, RecordParser.FormatTimestamp(refusal.At), null, refusal.Code);
        }
    }
}
