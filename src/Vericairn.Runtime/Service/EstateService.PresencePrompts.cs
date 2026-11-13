// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Presence prompts (FIX-124): every signature this runtime makes with a key that needs its owner's presence passes the shared signing
/// boundary (<see cref="AuthorLocked"/>), which counts it against the operator action that asks for it, names it "Signature k of n"
/// with what it signs, and passes that reason to the key's custody, which shows it in its own prompt. An action states the records it
/// will sign before the first; a single write states its own batch: the record, and the checkpoint or closing it carries.
/// </summary>
public sealed partial class EstateService
{
    private readonly PresenceCounter presence = new();

    /// <summary>
    /// A test's override of which devices need their owner's presence at every signature, by identity; null reads the device's record
    /// and its key's custody, as production does. Tests use file custody and never a real presence prompt.
    /// </summary>
    internal Func<string, bool>? PresenceRequired { get; set; }

    /// <summary>A test's override of the key stores this runtime looks keys up in; null asks every store this machine offers.</summary>
    internal IReadOnlyList<IDeviceKeyStore>? KeyStores { get; set; }

    private IReadOnlyList<IDeviceKeyStore> HeldKeyStores() => KeyStores ?? DeviceKeyStores.All();

    /// <inheritdoc />
    public PresencePromptsResponse PresencePrompts(PresencePromptsRequest request) => presence.Notices.ReadAsync(request, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task<PresencePromptsResponse> PresencePromptsAsync(PresencePromptsRequest request, CancellationToken cancellationToken = default) =>
        presence.Notices.ReadAsync(request, cancellationToken);

    /// <summary>
    /// Whether a signer's signature asks for its owner's presence: a key held in custody that enforces presence, of a device whose record
    /// requires it. The device record being signed states that of the key it introduces: a new device's own key, or a rotation's new key.
    /// </summary>
    private bool NeedsPresence(IRecordSigner signer, RecordDraft draft, EstateModel model)
    {
        if (signer is not StoreSigner held) return false;
        if (PresenceRequired is { } stated) return stated(held.DeviceId);
        if (DeviceKeyStores.PresenceKinds.IndexOf(held.Custody) < 0) return false;
        bool introduced = draft.Type == "device" && (draft.Supersedes is null && draft.Id == held.DeviceId || held.KeyId != held.DeviceId);
        if (introduced) return BodyText(draft.Body, "presence") == "required";
        RecordEnvelope? device = model.Current("device").FirstOrDefault(d => model.Chain(d)[0] == held.DeviceId);
        return device is not null && EstateModel.BodyString(device, "presence") == "required";
    }

    /// <summary>Whether the device this machine signs a principal's records with asks for its owner's presence at each signature.</summary>
    private bool SignsWithPresence(EstateModel model, string by)
    {
        using JsonDocument probe = JsonDocument.Parse("{}");
        var draft = new RecordDraft("operation", by, probe.RootElement);
        return ResolveSigner(model, draft, out IRecordSigner? signer) is null && signer is not null && NeedsPresence(signer, draft, model);
    }

    /// <summary>
    /// The signatures a single write asks presence for, in the order they are made: the record by each of its signers that needs it,
    /// then the closing or the checkpoint it carries; a revision taking a role away in an estate that has adopted signs its checkpoint
    /// first. Empty when nothing it signs needs presence.
    /// </summary>
    private List<PresenceSignature> PresenceOfWrite(RecordDraft draft, EstateModel current, IReadOnlyList<IRecordSigner>? signers)
    {
        List<PresenceSignature> plan = [];
        IReadOnlyList<IRecordSigner> signing = signers ?? (ResolveSigner(current, draft, out IRecordSigner? resolved) is null && resolved is not null ? [resolved] : []);
        foreach (IRecordSigner signer in signing)
        {
            if (NeedsPresence(signer, draft, current)) plan.Add(DescribeRecord(draft, current));
        }

        if (plan.Count == 0) return plan;
        if (ClosingRequired(current, draft.Type, draft.Body, draft.Subject, draft.Supersedes) is not null)
        {
            plan.Add(CheckpointSignature(ClosingTrigger));
        }
        else if (!session.IsReadOnly && TakesRoleAway(draft, current) && SignedHistory.Adopted(current))
        {
            plan.Insert(0, CheckpointSignature("removal"));
        }
        else if (TriggerOf(draft, current, signers) is string trigger && trigger != Interval)
        {
            plan.Add(CheckpointSignature(trigger));
        }

        return plan;
    }

    /// <summary>The plan of an action by a principal, or none when that principal's device here signs without presence.</summary>
    private IEnumerable<PresenceSignature>? PresencePlan(string by, Func<IEnumerable<PresenceSignature>> plan)
    {
        lock (gate) return SignsWithPresence(session.Load(), by) ? plan() : null;
    }

    /// <summary>Adds signatures the open action needs next, when the principal's device here asks for presence (FIX-124).</summary>
    private void ExpectPresence(string by, IReadOnlyList<PresenceSignature> more, string why)
    {
        if (!presence.Counting) return;
        bool needs;
        lock (gate) needs = SignsWithPresence(session.Load(), by);
        if (needs) presence.Expect(more, why);
    }

    // What the recorded changes operations sign, on the path that succeeds (MERGE-01): each effect's intent, its dispatch and its
    // outcome, and each observation's intent, dispatch and outcome. An operation's outcome-dependent records are expected by it.
    private static PresenceSignature[] ObservationPresence(string key, string observation) =>
    [
        OperationSignature("change-observe", "not-started", key, observation),
        OperationSignature("change-observe", "dispatched", key, observation),
        OperationSignature("change-observe", "observed-success", key, observation),
    ];

    private static PresenceSignature[] PushPresence(string key) =>
    [
        OperationSignature("change-push", "not-started", key), OperationSignature("change-push", "dispatched", key),
        .. ObservationPresence(key, "push-head"), OperationSignature("change-push", "observed-success", key),
    ];

    private static PresenceSignature[] OpenPresence(string key) =>
        [OperationSignature("change-open", "not-started", key), OperationSignature("change-open", "dispatched", key), OperationSignature("change-open", "observed-success", key)];

    private static PresenceSignature[] MergePresence(string key) =>
    [
        OperationSignature("change-merge", "not-started", key), .. ObservationPresence(key, "pull-request"),
        OperationSignature("change-merge", "dispatched", key), OperationSignature("change-merge", "observed-success", key),
        .. ObservationPresence(key, "target-run"),
    ];

    /// <summary>What observing a recorded change signs on its base path: one observation of what the operation names.</summary>
    private static PresenceSignature[] ObservePresence(RecordEnvelope operation, string? key = null)
    {
        string action = EstateModel.BodyString(operation, "action") ?? "";
        bool merged = action == "change-merge" && EstateModel.BodyString(operation, "status") == "observed-success"
            && operation.Body.TryGetProperty("target", out JsonElement target) && BodyText(target, "mergeCommit") is not null;
        return ObservationPresence(key ?? OperationKey(operation.Body), action == "change-push" ? "push-head" : merged ? "target-run" : "pull-request");
    }

    /// <summary>A record as a presence prompt names it: its type, the key a person knows it by and what signing it authorizes.</summary>
    internal static PresenceSignature DescribeRecord(RecordDraft draft, EstateModel model)
    {
        JsonElement body = draft.Body;
        string? subject = draft.Subject is { } reference ? SubjectKey(model, reference) : null;
        string revision = draft.Supersedes is null ? "a new" : "a revision of the";
        return draft.Type switch
        {
            "acceptance" => new("acceptance", subject ?? "its subject", draft.Subject is { } accepted
                ? $"accepts {(accepted.Type == "release" ? "the release candidate" : accepted.Type)} {accepted.Id} at its exact hash"
                : "accepts its subject at its exact hash"),
            "operation" => OperationSignature(BodyText(body, "action"), BodyText(body, "status"), OperationKey(body), ObservationOf(body)),
            "release" => new("release", BodyText(body, "version") ?? "?", BodyText(body, "status") switch
            {
                "published" => "records the release's observed publication",
                "withdrawn" => "withdraws the release candidate",
                _ => "records the release candidate for your acceptance",
            }),
            "document-revision" => PinSignature(BodyText(body, "path") ?? "the document"),
            "history-checkpoint" => CheckpointSignature(BodyText(body, "trigger") ?? "manual"),
            "goal-status" => new("goal-status", subject ?? "goal", "sets the goal " + (BodyText(body, "status") ?? "to its next status")),
            "goal" => new("goal", draft.Supersedes ?? "new", draft.Supersedes is null ? "proposes the goal" : "revises the goal"),
            "admission" => new("admission", subject ?? "device", BodyText(body, "status") == "revoked" ? "revokes the device" : "admits the device"),
            "device" => new("device", BodyText(body, "name") ?? draft.Id ?? "new", draft.Supersedes is null ? "records the device and proves its key" : "records the device's revision"),
            "review" => new("review", subject ?? "its subject", "records your review of " + (draft.Subject is { } reviewed ? reviewed.Type + " " + reviewed.Id : "its subject")),
            "decision" => new("decision", BodyText(body, "title") is { Length: > 0 and <= 60 } title ? title : draft.Supersedes ?? "new", "records the decision"),
            "ask" => new("ask", subject ?? "new", "records a question that waits on its principal"),
            _ => new(draft.Type, BodyText(body, "key") ?? subject ?? draft.Supersedes ?? "new", $"records {revision} {draft.Type}"),
        };
    }

    // What a subject is called: a result or work order by its work order's key, a release by its version, anything else by type and id.
    private static string SubjectKey(EstateModel model, RecordReference subject)
    {
        if (model.ById.TryGetValue(subject.Id, out RecordEnvelope? record))
        {
            RecordEnvelope? order = record.Type == "work-order" ? record : record.Type == "result" ? WorkView.OrderOfResult(model, record) : null;
            if (order is not null && EstateModel.BodyString(model.Latest(order), "key") is string key) return key;
            if (record.Type == "release" && EstateModel.BodyString(record, "version") is string version) return "release " + version;
        }

        return subject.Type + " " + subject.Id;
    }

    /// <summary>A document's revision pin as a presence prompt names it, by its path in the repository.</summary>
    private static PresenceSignature PinSignature(string path) => new("document-revision", path, "pins the document's exact revision");

    /// <summary>A history checkpoint as a presence prompt names it, by its trigger.</summary>
    internal static PresenceSignature CheckpointSignature(string trigger) => new("history-checkpoint", "trigger " + trigger, trigger switch
    {
        "release" => "witnesses the estate's history before the release check, as the release gate requires",
        ClosingTrigger => "closes what the act ends, witnessing the estate's history before it",
        "adoption" => "adopts history checkpoints, witnessing the estate's history up to now",
        _ => "witnesses the estate's history up to now",
    });

    /// <summary>An operation record as a presence prompt names it, from its action, status and target.</summary>
    internal static PresenceSignature OperationSignature(string? action, string? status, string key, string? observation = null) =>
        new("operation", key, OperationPurpose(action, status, observation));

    /// <summary>A trial signature, made only to prove the device can still sign, which is never stored.</summary>
    internal static PresenceSignature TrialSignature(string key, string why) => new("operation", key, PresenceCounter.TrialPurpose + why);

    private static string OperationPurpose(string? action, string? status, string? observation) => (action, status) switch
    {
        ("change-push", "not-started") => "records the intent to push the exact head, before its scan",
        ("change-push", "dispatched") => "records that the push is requested, its scan of exactly those commits having passed",
        ("change-push", "observed-success") => "records the push as observed at the remote",
        ("change-open", "not-started") => "records the intent to open the pull request",
        ("change-open", "dispatched") => "records that the opening is requested",
        ("change-open", "observed-success") => "records the opened pull request",
        ("change-merge", "not-started") => "records the intent to merge the pinned head",
        ("change-merge", "dispatched") => "records that the merge is requested, its checks having passed",
        ("change-merge", "observed-success") => "records the merge as confirmed",
        ("change-observe", "not-started") => "records the intent to observe " + Observed(observation),
        ("change-observe", "dispatched") => "records that " + Observed(observation) + " is being observed",
        ("change-observe", _) => "records what was observed of " + Observed(observation),
        (global::Vericairn.Records.Types.OperationType.AgentRun, "not-started") => "records the intent of the agent's run, before it starts",
        (global::Vericairn.Runtime.Release.ReleaseFlow.DeleteDraftAction, "not-started") => "records the intent to delete the withdrawn candidate's draft, which stays your manual step",
        (ReleaseFlowAction, "not-started") => "records the release intent before the build is dispatched",
        (ReleaseFlowAction, "dispatched") => "records the release build's dispatch receipt",
        (ReleaseFlowAction, "observed-success") => "records the release build as observed finished",
        (_, "uncertain") => $"records the {Named(action)} as uncertain, to be observed before any retry",
        (_, "known-failure") => $"records the {Named(action)}'s known failure",
        (_, "canceled") => $"records the {Named(action)} as canceled",
        _ => $"records the {Named(action)} as {status ?? "changed"}",
    };

    private const string ReleaseFlowAction = global::Vericairn.Runtime.Release.ReleaseFlow.DispatchAction;

    private static string Named(string? action) => action switch
    {
        "change-push" => "push",
        "change-open" => "opening",
        "change-merge" => "merge",
        "change-observe" => "observation",
        ReleaseFlowAction => "release build",
        null or "" => "operation",
        _ => action,
    };

    private static string Observed(string? observation) => observation switch
    {
        "push-head" => "the pushed branch at the remote",
        "pull-request" => "the pull request and its checks",
        "target-run" => "the target branch's run at the merge commit",
        _ => "the original effect",
    };

    private static string OperationKey(JsonElement body)
    {
        JsonElement target = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("target", out JsonElement t) ? t : default;
        if (BodyText(target, "version") is string version) return "release " + version;
        if (BodyText(target, "pullRequest") is string pull) return "pull request " + pull;
        if (BodyText(target, "branch") is string branch) return "branch " + branch;
        if (BodyText(target, "operation") is string operation) return "operation " + operation;
        return BodyText(body, "action") ?? "operation";
    }

    private static string? ObservationOf(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("target", out JsonElement target) ? BodyText(target, "observation") : null;

    private static string? BodyText(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// The shared signing boundary's signer (FIX-124): notes whether its signature failed, so the failure is told from the record's own
    /// refusals, and for a key that needs its owner's presence names the prompt first and passes its reason to the key's custody.
    /// </summary>
    private sealed class WatchedSigner(IRecordSigner inner, EstateService owner, RecordDraft draft, EstateModel model) : IRecordSigner
    {
        public bool Failed { get; private set; }

        public string DeviceId => inner.DeviceId;

        public RecordSignature Sign(ReadOnlySpan<byte> digest)
        {
            PresenceTally? own = null;
            bool counted = false;
            try
            {
                if (inner is StoreSigner held && owner.NeedsPresence(held, draft, model))
                {
                    string reason = owner.presence.Prompt(DescribeRecord(draft, model), out own);
                    counted = true;
                    RecordSignature signature = held.Sign(digest, reason);
                    owner.presence.Signed(own);
                    return signature;
                }

                return inner.Sign(digest);
            }
            catch
            {
                Failed = true;
                if (counted) owner.presence.Failed(own);
                throw;
            }
        }
    }
}
