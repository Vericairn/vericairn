// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Service;

/// <summary>One step of a change an action takes: its own records and the observations of it (design/presence-sessions.md § 5).</summary>
/// <param name="Action">change-push, change-open or change-merge.</param>
/// <param name="Branch">The branch it fixes, for a push or an opening.</param>
/// <param name="PullRequest">The pull request it fixes, for a merge, when known now.</param>
/// <param name="PullRequestFrom">For a merge whose pull request the session's opening reveals, the index of the opening's step.</param>
/// <param name="Records">How many records of its own it may sign: intent, dispatch and outcome; 0 when none remain.</param>
/// <param name="Observations">Each observation of it and how many records it may sign; "at most" when it may repeat.</param>
internal sealed record PresenceChangeStep(string Action, string? Branch, string? PullRequest, int? PullRequestFrom, int Records, IReadOnlyList<(string Observation, int Count, bool AtMost)> Observations);

/// <summary>
/// The lists each action of the vocabulary composes (design/presence-sessions.md § 5): the changes' push, opening, merge and observations,
/// an operator record new or resumed, the release build and its records, and the goal step, whose composition waits here for its action.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>How long operator record pauses between observations while checks run, which bounds the repetitions its wait allows.</summary>
    private const int ObservationPauseSeconds = 2;

    private static readonly string[] AllStatuses = PresenceSessionProtocol.OperationStatuses;

    /// <summary>
    /// The kind entries and references of a change's steps under its declaration and head: each step's own records by kind, fixed by the
    /// provider, repository, product, product hash, branch or pull request and the head when known (the first change record signed fixes
    /// it otherwise), and each observation of it; a step whose operation is recorded lists that operation as a reference, which its
    /// revisions supersede and its observations name. Caller holds the gate.
    /// </summary>
    private static (List<PresencePlannedKind> Kinds, List<string> References) ChangeKinds(ChangeConfiguration configuration, string? head,
        List<PresenceChangeStep> steps, EstateModel model)
    {
        var kinds = new List<PresencePlannedKind>();
        var references = new List<string>();
        var own = new Dictionary<int, int>();
        for (int i = 0; i < steps.Count; i++)
        {
            PresenceChangeStep step = steps[i];
            string? name = step.Action == "change-merge" ? step.PullRequest : step.Branch;
            RecordEnvelope? current = head is null || name is null ? null : model.Current("operation").FirstOrDefault(o => EstateModel.BodyString(o, "identity")
                == $"{configuration.Provider.Provider}:{configuration.Repository.Id}:{step.Action}:{name}:{head}:{configuration.Product.ComputedHash[7..]}");
            if (current is not null) references.Add(current.Id);
            string key = step.Action == "change-merge" ? (name is null ? "the pull request the opening reveals" : "pull request " + name) : "branch " + name;
            string at = head is null ? "at the head the first change record states" : "at head " + head[..12];
            if (step.Records > 0)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["provider"] = configuration.Provider.Provider, ["repository"] = configuration.Repository.Id, ["product"] = configuration.Product.Id,
                    ["productHash"] = configuration.Product.ComputedHash,
                };
                if (head is not null) values["head"] = head;
                int? from = null;
                if (step.Action == "change-merge")
                {
                    if (step.PullRequest is not null) values["pullRequest"] = step.PullRequest;
                    else if (step.PullRequestFrom is int opening && own.TryGetValue(opening, out int openingEntry)) from = openingEntry;
                    else continue;
                }
                else if (step.Branch is not null) values["branch"] = step.Branch;
                else continue;

                own[i] = kinds.Count;
                kinds.Add(new PresencePlannedKind(new PresenceListEntry
                {
                    Type = "operation", Action = step.Action, Count = step.Records, AtMost = true, Statuses = AllStatuses, Fixed = values, PullRequestFrom = from,
                }, $"records the {OperationName(step.Action)}'s intent, dispatch and outcome", $"adds or revises the {OperationName(step.Action)} of {key} {at}"));
            }

            foreach ((string observation, int count, bool atMost) in step.Observations)
            {
                string what = observation switch
                {
                    "push-head" => "the pushed branch at the remote",
                    "pull-request" => "the pull request and its checks",
                    _ => "the target branch's run at the merge commit",
                };
                PresenceListEntry entry = new()
                {
                    Type = "operation", Action = "change-observe", Count = count, AtMost = atMost, Statuses = AllStatuses,
                    Fixed = new Dictionary<string, string>(StringComparer.Ordinal), Observation = observation,
                };
                if (own.TryGetValue(i, out int observed))
                    kinds.Add(new PresencePlannedKind(entry with { Observes = observed }, "records what is observed of " + what, $"adds observations of the {OperationName(step.Action)} of {key}"));
                if (current is not null)
                    kinds.Add(new PresencePlannedKind(entry with { ObservesReference = current.Id }, "records what is observed of " + what, $"adds observations of recorded operation {current.Id}"));
            }
        }

        return (kinds, references);
    }

    /// <summary>The session of one changes command: its step, under the product's declaration.</summary>
    private PresenceSessionAction ChangeSession(string action, string product, string head, PresenceChangeStep step) => new(_ =>
    {
        lock (gate)
        {
            ChangeConfiguration configuration;
            try
            {
                configuration = ChangeConfigurationFor(product, currentDeclaration: true);
            }
            catch (ProviderRefusedException)
            {
                return null;
            }

            (List<PresencePlannedKind> kinds, List<string> references) = ChangeKinds(configuration, head, [step], session.Load());
            return new PresenceSessionPlan { Action = action, Identity = $"{action}:{product}:{step.Branch ?? step.PullRequest}:{head}", Kinds = kinds, References = references };
        }
    });

    /// <summary>The session of changes observe: the observed operation as a reference, its outcome's revisions and the observation of it.</summary>
    private PresenceSessionAction ObserveSession(string operation) => new(_ =>
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(operation, out RecordEnvelope? found) || !ChangeOperationRulesForRuntime(found)) return null;
            RecordEnvelope named = model.Latest(found);
            JsonElement target = named.Body.GetProperty("target");
            string action = EstateModel.BodyString(named, "action")!;
            ChangeConfiguration configuration;
            try
            {
                configuration = ChangeConfigurationFor(ChangeText(target, "product")!, currentDeclaration: false);
            }
            catch (ProviderRefusedException)
            {
                return null;
            }

            string observation = action == "change-push" ? "push-head" : action == "change-merge" && ChangeText(target, "mergeCommit") is not null ? "target-run" : "pull-request";
            List<(string, int, bool)> observations = [(observation, 3, true)];
            if (action == "change-merge" && observation != "target-run") observations.Add(("target-run", 3, true));
            var step = new PresenceChangeStep(action, ChangeText(target, "branch"), ChangeText(target, "pullRequest"), null, 2, observations);
            (List<PresencePlannedKind> kinds, List<string> references) = ChangeKinds(configuration, ChangeText(target, "head"), [step], model);
            if (!references.Contains(named.Id, StringComparer.Ordinal)) references.Add(named.Id);
            return new PresenceSessionPlan { Action = "changes observe", Identity = "changes observe:" + named.Id, Kinds = kinds, References = references };
        }
    });

    /// <summary>
    /// The session of an operator record (§ 5, J132): the record itself by exact hash while it is not stored, then from where the action
    /// stands its push and the push's observation, its opening, the pull request's observations with the repetitions its wait allows
    /// within the list, its merge, whose pull request the session's opening reveals, and the merge's observations; a resumed action lists
    /// its recorded operations as references. <paramref name="own"/> gives the record's draft once authored; <paramref name="state"/> the
    /// checkpoint as the action holds it now.
    /// </summary>
    private PresenceSessionAction OperatorRecordSession(OperatorRecordRequest request, string identity, Func<RecordDraft?> own, Func<OperatorCheckpoint?> state, string? earlierAction) => new(_ =>
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            ChangeConfiguration configuration;
            try
            {
                configuration = OperatorConfiguration(request.Product);
            }
            catch (ProviderRefusedException)
            {
                return null;
            }

            OperatorCheckpoint? checkpoint = state();
            RecordDraft? draft = own();
            string? id = checkpoint?.Id ?? draft?.Id;
            if (id is null) return null;
            OperatorProgress? progress = checkpoint is null ? null : OperatorProgressLocked(identity, checkpoint);
            string stage = progress?.Stage ?? "prepared";
            int repetitions = Math.Min(request.WaitSeconds / ObservationPauseSeconds, 17);
            List<RecordDraft> exact = draft is not null && (progress?.Record is null) ? [draft] : [];
            var steps = new List<PresenceChangeStep>();
            if (stage is "prepared" or "recorded" or "committed") steps.Add(new("change-push", "operator/" + id, null, null, 3, [("push-head", 3, true)]));
            bool openPending = stage is "prepared" or "recorded" or "committed" or "pushed";
            int opening = steps.Count;
            steps.Add(new("change-open", "operator/" + id, null, null, openPending ? 3 : 0, [("pull-request", 3 * (1 + repetitions), true)]));
            if (stage != "merged")
                steps.Add(new("change-merge", null, progress?.PullRequest, openPending ? opening : null, 3, [("pull-request", 3, true), ("target-run", 3 * (1 + repetitions), true)]));
            else
                steps.Add(new("change-merge", null, progress?.PullRequest, null, 0, [("target-run", 3 * (1 + repetitions), true)]));
            (List<PresencePlannedKind> kinds, List<string> references) = ChangeKinds(configuration, checkpoint?.Head, steps, model);
            // An earlier action finished from its own records first observes its merge's target run.
            if (!string.IsNullOrEmpty(earlierAction) && earlierAction != identity && OperatorCheckpointLocked(earlierAction) is { } earlier
                && OperatorProgressLocked(earlierAction, earlier) is { Finishable: true, Merge: { } merge } before && before.CheckStatus != "passed")
            {
                references.Add(merge.Id);
                kinds.Add(new PresencePlannedKind(new PresenceListEntry
                {
                    Type = "operation", Action = "change-observe", Count = 3 * (1 + repetitions), AtMost = true, Statuses = AllStatuses,
                    Fixed = new Dictionary<string, string>(StringComparer.Ordinal), Observation = "target-run", ObservesReference = merge.Id,
                }, "records what is observed of the earlier action's target run", "adds observations of the earlier action's merge " + merge.Id));
            }

            return new PresenceSessionPlan { Action = "operator record", Identity = identity, Exact = exact, Kinds = kinds, References = references };
        }
    });

    /// <summary>The session of operator unfinished --finish: the observations of the recorded merge's target run.</summary>
    private static PresenceSessionAction FinishSession(string identity, RecordEnvelope merge, int waitSeconds) => new(_ =>
    {
        int repetitions = Math.Min(waitSeconds / ObservationPauseSeconds, 40);
        return new PresenceSessionPlan
        {
            Action = "operator unfinished", Identity = identity + ":finish",
            Kinds =
            [
                new PresencePlannedKind(new PresenceListEntry
                {
                    Type = "operation", Action = "change-observe", Count = 3 * (1 + repetitions), AtMost = true, Statuses = AllStatuses,
                    Fixed = new Dictionary<string, string>(StringComparer.Ordinal), Observation = "target-run", ObservesReference = merge.Id,
                }, "records what is observed of the target branch's run at the merge commit", "adds observations of recorded merge " + merge.Id),
            ],
            References = [merge.Id],
        };
    });

    /// <summary>
    /// The release build's kind entry (§ 5): its provider, its destination (the release's repository), its version, its product and the
    /// commit it builds; null when the submission names no commit, or states another ref or commit in a field that may carry it, so the
    /// build keeps one check per signature. The only place a release build entry is composed: a field the entry should also fix, such as
    /// the workflow, is added here and to the helper's closed set together.
    /// </summary>
    internal static PresencePlannedKind? ReleaseBuildEntry(string provider, string destination, string version, string product, string? commit,
        IReadOnlyDictionary<string, string> settings, int count)
    {
        if (!PresenceText.IsCommit(commit)) return null;
        if (settings.TryGetValue("ref", out string? stated) && stated != commit || settings.TryGetValue("commit", out string? named) && named != commit) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = provider, ["destination"] = destination, ["version"] = version, ["product"] = product, ["commit"] = commit!,
        };
        if (!values.All(v => PresenceSessionProtocol.ValidFixed(v.Key, v.Value))) return null;
        return new PresencePlannedKind(new PresenceListEntry
        {
            Type = "operation", Action = global::Vericairn.Runtime.Release.ReleaseFlow.DispatchAction, Count = count, AtMost = true, Statuses = AllStatuses, Fixed = values,
        }, "records the release build's intent, dispatch and outcome", $"adds or revises the release build of {version} at commit {commit![..12]} to {destination}");
    }

    /// <summary>A release record's kind entry (§ 5): its product, version, destination and commit, with the statuses it may state.</summary>
    internal static PresencePlannedKind? ReleaseRecordEntry(string product, string version, string destination, string? commit, IReadOnlyList<string> statuses, int count)
    {
        if (!PresenceText.IsCommit(commit) || !PresenceText.IsVersion(version) || !PresenceText.IsBranch(destination) || !PresenceText.IsUlid(product)) return null;
        return new PresencePlannedKind(new PresenceListEntry
        {
            Type = "release", Count = count, AtMost = count > 1, Statuses = statuses,
            Fixed = new Dictionary<string, string>(StringComparer.Ordinal) { ["product"] = product, ["version"] = version, ["destination"] = destination, ["commit"] = commit! },
        }, "records the release " + string.Join(" or ", statuses), $"adds the release record of {version} at commit {commit![..12]}");
    }

    /// <summary>The release gate's history checkpoint's kind entry: trigger release, a count of one.</summary>
    internal static PresencePlannedKind ReleaseCheckpointEntry() => new(new PresenceListEntry { Type = "history-checkpoint", Count = 1, Trigger = "release" },
        "witnesses the estate's history before the release check", "adds the release gate's history checkpoint");

    /// <summary>
    /// The goal step's list (§ 5), composed here until its action, behind routing, passes it to its tally: the records the step signs
    /// together once its route is known, the step's assignment and, for a step routed to a person, the ask, or for one routed to an agent,
    /// the intent of the agent's run, each authored completely with its id and time (<see cref="Fixed"/>) before the first signature and
    /// listed by exact hash. <paramref name="drafts"/> is read at the step's first signature, so routing fills it once the route is known.
    /// </summary>
    internal static PresenceSessionAction GoalStepSession(string goal, Func<IReadOnlyList<RecordDraft>> drafts) =>
        new(_ => drafts() is { Count: > 0 } known && known.All(d => d.Id is not null && d.At is not null)
            ? new PresenceSessionPlan { Action = "goal step", Identity = "goal step:" + goal, Exact = known }
            : null);
}
