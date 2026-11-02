// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    private RemoteDecisionDraft? decision;
    private RemoteDecisionDraft? selectedDraft;
    private IReadOnlyList<RemoteDecisionDraft> decisionDrafts = [];
    private string decisionMessage = string.Empty;
    private string decisionStorageProblem = string.Empty;
    private bool decisionCurrent;
    private bool materialReviewed;
    private int decisionEpoch;
    private string presenceCode = string.Empty;
    private string presenceMessage = string.Empty;

    /// <summary>The open review and its retained original draft or submission.</summary>
    public RemoteDecisionDraft? Decision => decision;
    /// <summary>Whether the decision review replaces the home areas.</summary>
    public bool HasDecisionReview => ShowHome && decision is not null;
    /// <summary>The five home areas remain available behind the review.</summary>
    public bool ShowAreas => ShowHome && !HasDecisionReview;
    /// <summary>Drafts and original submissions retained for this exact connection.</summary>
    public IReadOnlyList<RemoteDecisionDraft> DecisionDrafts { get => decisionDrafts; private set => Set(ref decisionDrafts, value); }
    /// <summary>The stored draft to reopen. Selecting never submits it.</summary>
    public RemoteDecisionDraft? SelectedDraft { get => selectedDraft; set { if (Set(ref selectedDraft, value)) NotifyDecisionCommands(); } }
    /// <summary>Observation, refusal or uncertainty, never inferred downstream success.</summary>
    public string DecisionMessage { get => decisionMessage; private set => Set(ref decisionMessage, value); }
    /// <summary>A storage problem blocks signing because an original submission could otherwise be lost.</summary>
    public string DecisionStorageProblem { get => decisionStorageProblem; private set { if (Set(ref decisionStorageProblem, value)) NotifyDecisionCommands(); } }
    /// <summary>The selected estate, runtime and person stay explicit throughout review, with the presence key that alone signs here.</summary>
    public string DecisionTarget => Selected is null ? "" : Selected.Name + "\nRuntime " + RuntimeIdentityScheme.Fingerprint(Selected.Connection.Identity) +
        "\nActing as " + Selected.Connection.Principal + "\nPhone device " + Selected.Connection.Device +
        (HasPresenceKey ? "\nPresence key " + Selected.Connection.PresenceDevice + ", which asks for your presence when it signs" : "\nNo presence key on this phone for this estate");
    /// <summary>Whether this phone holds the selected connection's presence key, the only key that signs a decision here.</summary>
    public bool HasPresenceKey => Selected is { } estate && connections.HasPresenceKey(estate.Connection);
    /// <summary>A phone without a presence key is told how to add one beside the review.</summary>
    public bool NeedsPresenceKey => decision is { Attempted: false } && Selected is not null && !HasPresenceKey;
    /// <summary>A new pairing code for the same principal, which adds this phone's presence key to the connection.</summary>
    public string PresenceCode { get => presenceCode; set { if (Set(ref presenceCode, value)) AddPresenceKeyCommand?.NotifyCanExecuteChanged(); } }
    /// <summary>What adding a presence key observed.</summary>
    public string PresenceMessage { get => presenceMessage; private set => Set(ref presenceMessage, value); }
    /// <summary>How to add a presence key: the administrator issues a code for the same person, as for pairing.</summary>
    public static string PresenceHelp => "Decisions are signed only by this phone's presence key, which asks for Face ID, Touch ID, a fingerprint or the device passcode at every signature. " +
        "To add one, an administrator runs vericairn devices pair for you on the runtime's machine; enter that code here. The runtime records and admits the presence key as another device of yours.";
    /// <summary>The exact target and observation time.</summary>
    public string DecisionIdentity => decision is null ? "" : decision.Review.Subject.Type + " " + decision.Review.Subject.Id + "\n" + decision.Review.Subject.Hash +
        "\n" + (decisionCurrent && Selected?.Current == true ? "Observed " : "Cached ") + decision.Review.AsOf;
    /// <summary>The ask's recommendation precedes its question, alternatives and no-answer consequence.</summary>
    public string DecisionQuestion => decision is null ? "" : IsAsk ? "Recommendation: " + Field("recommendation") + "\nQuestion: " + Field("question") +
        "\nIf unanswered: " + Field("consequence") : "Accept this exact " + decision.Review.Subject.Type + " from the evidence below. Acceptance records a judgment; it dispatches no publication or other external action.";
    /// <summary>Every offered option, with the recommendation first when it is itself an option. None is selected automatically.</summary>
    public IReadOnlyList<string> DecisionOptions => !IsAsk ? [] : RemoteDecisionReader.Rows(RemoteDecisionReader.Body(decision!.Review.Subject), "options")
        .Select(o => o.GetString()!).OrderBy(o => o == Field("recommendation") ? 0 : 1).ToArray();
    /// <summary>Choosing an option copies its exact words into the draft; it does not send them.</summary>
    public string? SelectedDecisionOption { get => DecisionOptions.FirstOrDefault(o => o == DecisionStatement); set { if (value is not null) DecisionStatement = value; } }
    /// <summary>All fetched records and readable evidence, without shortening their fields.</summary>
    public string DecisionMaterial => decision is null ? "" : RemoteDecisionReader.Describe(decision.Review);
    /// <summary>The original target and input remain visible after a revision changes.</summary>
    public string OriginalDecisionMaterial => decision is null ? "" : "Original target " + decision.Original.Subject.Id + "\n" + decision.Original.Subject.Hash + "\n" + RemoteDecisionReader.Describe(decision.Original);
    /// <summary>Whether this is an ask answer; acceptances have different required input.</summary>
    public bool IsAsk => decision?.Review.Subject.Type == "ask";
    /// <summary>An attempted original is immutable; only its outcome is reconciled.</summary>
    public bool CanEditDecision => decision is { Attempted: false } && !Busy;
    /// <summary>The operator's exact words, kept verbatim.</summary>
    public string DecisionStatement { get => decision?.Statement ?? ""; set { if (decision is { Attempted: false } draft && value != draft.Statement) EditDecision(draft with { Statement = value }); } }
    /// <summary>The person chooses the stake; the ask does not supply one.</summary>
    public string? DecisionStake { get => decision?.Stake; set { if (decision is { Attempted: false } draft && value != draft.Stake) EditDecision(draft with { Stake = value }); } }
    /// <summary>The record format's decision stakes.</summary>
    public static IReadOnlyList<string> DecisionStakes => DecisionType.Stakes;
    /// <summary>The estate basis the runtime supplied for an answer: its head commit, with no design document changed since. Never typed or inferred here.</summary>
    public string DecisionBasis => decision is null || !IsAsk ? "" : decision.Review.Head is string head
        ? "Estate head " + head + "\nNo design document changed since it, as the runtime observed at " + decision.Review.AsOf + ". The answer's change set uses this commit as its base and head and pins no documents; the runtime checks that basis again before it writes."
        : "The runtime supplied no basis for an answer without document changes. The reasons above say why; answer on the desktop.";
    /// <summary>A fresh deliberate confirmation after every change of material or input. Never restored from storage.</summary>
    public bool MaterialReviewed
    {
        get => materialReviewed;
        set { if (Set(ref materialReviewed, value && decisionCurrent && Selected?.Current == true && decision?.Review.Eligible == true)) NotifyDecisionCommands(); }
    }
    /// <summary>Why signing is unavailable, including a precise desktop route.</summary>
    public string DecisionEligibility => decision is null ? "" : !decision.Review.Eligible ? string.Join("\n", decision.Review.Reasons) + "\nDecide on the desktop in Workbench; your draft stays here." :
        decision.Attempted ? "This original has already been attempted. Read its records to reconcile; it will not be sent again." :
        !decisionCurrent || Selected?.Current != true ? "Offline or cached: draft only. Reconnect, refresh this review and review the current material before signing." :
        !HasPresenceKey ? "This phone holds no presence key for this estate, so it cannot sign a decision: its request key never does. Add the presence key below with a new pairing code, or decide on the desktop. Your draft stays here." :
        IsAsk && !Patterns.Commit.IsMatch(decision.Review.Head ?? "") ? "The answer needs the estate basis the runtime supplies. Refresh the review, or decide on the desktop." :
        IsAsk && !DecisionStakes.Contains(DecisionStake, StringComparer.Ordinal) ? "Choose the decision's stake." :
        string.IsNullOrWhiteSpace(DecisionStatement) ? "Enter your own answer or acceptance statement; choosing an option copies its exact words." :
        DecisionStatement.Length > (IsAsk ? 2000 : 8000) ? "The complete statement exceeds this phone flow's record fields. Preserve it as a draft and decide on the desktop." :
        !MaterialReviewed ? "Review the complete material and proposed record below, then confirm it before signing." : "Eligible for phone review. Signing asks for your presence; the runtime rechecks the record, its basis and the presence key's authority before writing.";
    /// <summary>Exact proposed fields, before the runtime assigns a record id and authoring time.</summary>
    public string DecisionPreview => decision is null ? "" : CanonicalJson.SerializePretty(CreateDecisionRequest(decision, "<answer time at submission>").Body);
    /// <summary>The prepared identity and any observed receipt, retained even if a later read contradicts it.</summary>
    public string DecisionReceipt => decision?.Prepared is not { } prepared ? "No signed submission attempted." :
        "Original record " + prepared.Id + "\n" + prepared.Hash + "\n" +
        (decision.Receipt is { } receipt ? "Runtime receipt: " + receipt.Id + " at " + receipt.Hash : "No write receipt observed.");
    /// <summary>Refreshes material only, never sending a draft.</summary>
    public AsyncCommand RefreshDecisionCommand { get; private set; } = null!;
    /// <summary>Signs the reviewed record with this phone's presence key, once, after the owner confirms their presence.</summary>
    public AsyncCommand SubmitDecisionCommand { get; private set; } = null!;
    /// <summary>Adds this phone's presence key to the selected connection with a new pairing code; signs nothing else.</summary>
    public AsyncCommand AddPresenceKeyCommand { get; private set; } = null!;
    /// <summary>Reads the original prepared record to reconcile its outcome; never resends.</summary>
    public AsyncCommand ReconcileDecisionCommand { get; private set; } = null!;
    /// <summary>Restores an existing local draft or pending submission.</summary>
    public AsyncCommand OpenDraftCommand { get; private set; } = null!;
    /// <summary>Leaves a review without losing its local input or pending identity.</summary>
    public AsyncCommand CloseDecisionCommand { get; private set; } = null!;
    /// <summary>Defers by changing only the original notice's attention state.</summary>
    public AsyncCommand DeferDecisionCommand { get; private set; } = null!;
    /// <summary>Explains the desktop route for a change request; no unsuitable record is invented.</summary>
    public AsyncCommand RequestDecisionChangesCommand { get; private set; } = null!;

    private void InitializeDecisionCommands()
    {
        RefreshDecisionCommand = new(() => OpenDecisionAsync(decision!.Review.Subject.Id), () => !Busy && Selected?.Current == true && decision is not null);
        SubmitDecisionCommand = new(SubmitDecisionAsync, CanSubmitDecision);
        ReconcileDecisionCommand = new(ReconcileDecisionAsync, () => !Busy && Selected?.Current == true && decision?.Attempted == true);
        OpenDraftCommand = new(() =>
        {
            SetDecision(SelectedDraft); decisionCurrent = false; NotifyDecision();
            DecisionMessage = decision?.Message ?? "";
            return Task.CompletedTask;
        }, () => !Busy && SelectedDraft is not null && DecisionStorageProblem.Length == 0);
        CloseDecisionCommand = new(() => { SetDecision(null); return Task.CompletedTask; }, () => !Busy && decision is not null);
        DeferDecisionCommand = new(AcknowledgeAsync, () => !Busy && Selected?.Current == true && SelectedAttention?.Notice is { AcknowledgedAt: null } notice &&
            decision is not null && notice.SubjectId == decision.Review.Subject.Id);
        RequestDecisionChangesCommand = new(() =>
        {
            DecisionMessage = "Request changes on the desktop in Workbench. This phone flow has no suitable change-request record; your words remain a draft and nothing was sent.";
            return Task.CompletedTask;
        }, () => !Busy && decision is { Attempted: false });
        AddPresenceKeyCommand = new(AddPresenceKeyAsync, () => !Busy && Selected?.Current == true && !HasPresenceKey && PresenceCode.Trim().Length > 0);
    }

    private bool CanSubmitDecision() => !Busy && Selected?.Current == true && decisionCurrent && MaterialReviewed && DecisionStorageProblem.Length == 0 && HasPresenceKey &&
        decision is { Attempted: false, Review.Eligible: true } && !string.IsNullOrWhiteSpace(DecisionStatement) && DecisionStatement.Length <= (IsAsk ? 2000 : 8000) &&
        (!IsAsk || DecisionStakes.Contains(DecisionStake, StringComparer.Ordinal) && Patterns.Commit.IsMatch(decision.Review.Head ?? ""));

    private void NotifyDecisionCommands()
    {
        RefreshDecisionCommand?.NotifyCanExecuteChanged(); SubmitDecisionCommand?.NotifyCanExecuteChanged(); ReconcileDecisionCommand?.NotifyCanExecuteChanged();
        OpenDraftCommand?.NotifyCanExecuteChanged(); CloseDecisionCommand?.NotifyCanExecuteChanged(); DeferDecisionCommand?.NotifyCanExecuteChanged();
        RequestDecisionChangesCommand?.NotifyCanExecuteChanged(); AddPresenceKeyCommand?.NotifyCanExecuteChanged();
        Raise(nameof(DecisionEligibility)); Raise(nameof(CanEditDecision)); Raise(nameof(HasPresenceKey)); Raise(nameof(NeedsPresenceKey));
    }

    private void NotifyDecision()
    {
        foreach (string property in new[] { nameof(Decision), nameof(HasDecisionReview), nameof(ShowAreas), nameof(DecisionTarget), nameof(DecisionIdentity),
            nameof(DecisionQuestion), nameof(DecisionOptions), nameof(SelectedDecisionOption), nameof(DecisionMaterial), nameof(OriginalDecisionMaterial), nameof(IsAsk),
            nameof(DecisionStatement), nameof(DecisionStake), nameof(DecisionBasis), nameof(MaterialReviewed), nameof(DecisionPreview), nameof(DecisionReceipt) }) Raise(property);
        NotifyDecisionCommands();
    }

    private string Field(string name) => decision is null ? "" : RemoteDecisionReader.Text(RemoteDecisionReader.Body(decision.Review.Subject), name) ?? "not supplied";
    private void SetDecision(RemoteDecisionDraft? value)
    {
        Interlocked.Increment(ref decisionEpoch);
        decision = value;
        materialReviewed = false;
        decisionCurrent = false;
        NotifyDecision();
    }

    private void RestoreDecisions()
    {
        SelectedDraft = null;
        DecisionDrafts = [];
        DecisionStorageProblem = "";
        if (Selected is not { } estate) return;
        try { DecisionDrafts = connections.LoadDecisions(estate.Connection); }
        catch (Exception exception) when (StorageFailure(exception))
        { DecisionStorageProblem = "Decision storage could not be read. Recover the retained drafts and original submissions before signing; do not clear them to retry."; }
    }

    private bool KeepDecision(RemoteEstate estate, RemoteDecisionDraft draft)
    {
        try
        {
            connections.SaveDecision(estate.Connection, draft);
            if (Selected == estate)
            {
                DecisionDrafts = connections.LoadDecisions(estate.Connection);
                DecisionStorageProblem = "";
            }
            return true;
        }
        catch (Exception exception) when (StorageFailure(exception))
        {
            if (Selected == estate) DecisionStorageProblem = "The draft or receipt could not be saved. Keep this view open and recover app storage before signing. Any attempted original remains blocked from resubmission.";
            return false;
        }
    }

    private void EditDecision(RemoteDecisionDraft draft)
    {
        draft = draft with { State = "draft", Message = "Draft; nothing signed or sent.", Request = null, Problem = null };
        decision = draft; materialReviewed = false; Interlocked.Increment(ref decisionEpoch);
        if (Selected is { } estate) KeepDecision(estate, draft);
        NotifyDecision();
    }

    private async Task OpenDecisionAsync(string id)
    {
        if (Selected is not { } estate) return;
        RemoteDecisionDraft? cached = DecisionDrafts.FirstOrDefault(d => d.Key == id || d.Review.Subject.Id == id || d.Original.Subject.Id == id);
        if (!estate.Current || estate.Client is not { } api)
        {
            if (cached is not null) SetDecision(cached);
            DecisionMessage = cached is null ? "This decision was not cached. Reconnect to inspect it; nothing was sent." : "Cached review. Your answer is a draft; nothing is queued.";
            return;
        }
        Busy = true;
        int epoch = Interlocked.Increment(ref decisionEpoch);
        materialReviewed = false;
        try
        {
            RemoteDecisionReview fresh = await new RemoteDecisionReader(api).ReadAsync(id, estate.Connection.Principal, Now).ConfigureAwait(true);
            if (Selected != estate || epoch != decisionEpoch) return;
            RemoteDecisionDraft[] related = DecisionDrafts.Where(d => d.Review.Root == fresh.Root).ToArray();
            // An unresolved original holds the entire subject history. A later revision gets a separate draft only after
            // the earlier submission has an observed outcome; its retained identity and receipt are never replaced.
            cached = related.FirstOrDefault(d => d.Attempted && d.State is not ("completed" or "rejected"))
                ?? related.FirstOrDefault(d => d.Review.Subject.Id == fresh.Subject.Id)
                ?? related.FirstOrDefault(d => !d.Attempted) ?? cached ?? related.LastOrDefault();
            bool newRevision = cached is { Attempted: true, State: "completed" or "rejected" } && cached.Review.Subject.Id != fresh.Subject.Id;
            RemoteDecisionDraft draft = newRevision ? new(fresh.Subject.Id, fresh, fresh, "") :
                cached is null ? new(fresh.Root, fresh, fresh, "") : cached.Attempted ? cached : cached with { Review = fresh };
            SetDecision(draft);
            decisionCurrent = !draft.Attempted;
            KeepDecision(estate, draft);
            DecisionMessage = draft.Attempted ? draft.Message + (draft.Review.Subject.Id == fresh.Subject.Id ? "" :
                    " A newer revision " + fresh.Subject.Id + " at " + fresh.Subject.Hash + " was observed. Reconcile this original before starting another decision.") :
                newRevision ? "The earlier submission's outcome is retained separately. This is a new draft for the current revision; review it and enter your words before signing." :
                cached is not null && !RemoteDecisionReader.Same(cached.Review, fresh)
                ? "The subject or its review material changed. Your original and words remain a draft. Review the current revision before deciding."
                : "Current material observed. Review your draft and the proposed record; nothing has been signed or sent.";
            NotifyDecision();
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            if (Selected == estate)
            {
                if (cached is not null) SetDecision(cached);
                DecisionMessage = "The complete current material is unavailable. Keep this as a draft and inspect it on the desktop; nothing was signed or sent.";
                decisionCurrent = false;
                NotifyDecision();
            }
        }
        finally { Busy = false; }
    }

    private async Task AddPresenceKeyAsync()
    {
        if (Selected is not { Current: true } estate || HasPresenceKey) return;
        Busy = true;
        PresenceMessage = "Adding the presence key. The phone asks for your presence once, to sign its own device record.";
        try
        {
            RuntimeConnection updated = await connections.AddPresenceKeyAsync(estate.Connection, PresenceCode.Trim(), DeviceName.Trim().Length > 0 ? DeviceName.Trim() : "Remote", CancellationToken.None).ConfigureAwait(true);
            estate.Connection = updated;
            PresenceCode = string.Empty;
            PresenceMessage = connections.HasPresenceKey(updated)
                ? "Presence key " + updated.PresenceDevice + " added and admitted. Decisions from this phone are signed only with it, after you confirm your presence. Review the material again before signing."
                : "The runtime recorded no presence key this phone holds. Nothing will be signed; decide on the desktop.";
        }
        catch (PairingException exception) { PresenceMessage = $"No presence key added ({exception.Code}): {exception.Message}"; }
        catch (Exception exception) when (ClientFailure.FromException(exception) is ClientFailure failure) { PresenceMessage = $"No presence key added ({failure.Code}): {failure.Message}"; }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidOperationException) { PresenceMessage = "No presence key added: " + exception.Message; }
        finally
        {
            Busy = false;
            materialReviewed = false;
            NotifyDecision();
        }
    }

    private NewRecordRequest CreateDecisionRequest(RemoteDecisionDraft draft, string at)
    {
        string principal = Selected!.Connection.Principal;
        RecordResponse subject = draft.Review.Subject;
        if (subject.Type != "ask")
            return new("acceptance", principal, JsonSerializer.SerializeToElement(new
            {
                statement = draft.Statement, basis = draft.Review.Basis, self = RemoteDecisionReader.Text(subject.Envelope, "by") == principal,
            }), Subject: subject.Type + ":" + subject.Id + ":" + subject.Hash, Expected: subject.Hash);
        JsonElement named = RemoteDecisionReader.Property(subject.Envelope, "subject");
        string[] affects = RemoteDecisionReader.Text(named, "type") == "work-order" && RemoteDecisionReader.Text(named, "id") is string order ? [order] : [];
        return new("decision", principal, JsonSerializer.SerializeToElement(new
        {
            title = "Answer to " + subject.Id, statement = draft.Statement, summary = draft.Statement, synthesis = draft.Statement,
            effects = new[] { draft.Statement }, affects, decidedBy = principal, decidedAt = at, stake = draft.Stake,
            notAuthorized = Array.Empty<string>(), answers = new[] { subject.Id },
            answer = new { ask = subject.Id, hash = subject.Hash, at },
            changeSet = new { baseCommit = draft.Review.Head ?? "", head = draft.Review.Head ?? "", pins = Array.Empty<object>() },
        }));
    }

    private async Task SubmitDecisionAsync()
    {
        if (!CanSubmitDecision() || Selected is not { Client: { } api } estate || decision is not { } original) return;
        Busy = true;
        int epoch = decisionEpoch;
        RemoteDecisionDraft retained = original;
        RemoteDecisionReview? changed = null;
        try
        {
            RemoteDecisionReview fresh = await new RemoteDecisionReader(api).ReadAsync(original.Review.Subject.Id, estate.Connection.Principal, Now).ConfigureAwait(true);
            if (Selected != estate || epoch != decisionEpoch) return;
            if (!RemoteDecisionReader.Same(original.Review, fresh) || !fresh.Eligible) { changed = fresh; return; }
            NewRecordRequest request = CreateDecisionRequest(original, RecordParser.FormatTimestamp(clock.GetUtcNow()));
            retained = original with { Request = request };
            if (!KeepDecision(estate, retained)) return;

            // The SDK's callbacks are synchronous. Run them away from the UI: the material and basis are read again immediately before
            // the presence key asks for the person's presence, and again once it has signed, since the prompt can take a while. The
            // exact original id is durably reserved after the signature exists and before it can leave, so a declined or failed
            // presence prompt leaves only a draft, never an original to reconcile.
            bool StillCurrent(RecordPrepared prepared)
            {
                if (epoch != Volatile.Read(ref decisionEpoch)) return false;
                if (!PreparedMatches(prepared, request)) throw new InvalidDataException("The prepared record differs from the reviewed request.");
                RemoteDecisionReview checkedAgain = new RemoteDecisionReader(api).ReadAsync(original.Review.Subject.Id, estate.Connection.Principal, Now).GetAwaiter().GetResult();
                if (!RemoteDecisionReader.Same(original.Review, checkedAgain) || !checkedAgain.Eligible)
                { changed = checkedAgain; return false; }
                return epoch == Volatile.Read(ref decisionEpoch);
            }
            WriteOutcome outcome = await Task.Run(async () => await connections.WriteSignedAsync(estate.Connection, request, StillCurrent, prepared =>
            {
                if (!StillCurrent(prepared)) return false;
                RemoteDecisionDraft pending = retained with { Prepared = prepared, State = "uncertain", Message = "Submission may be in flight. Reconcile the original record; never send it again." };
                connections.SaveDecision(estate.Connection, pending);
                retained = pending;
                return true;
            }).ConfigureAwait(false)).ConfigureAwait(true);
            if (!retained.Attempted && (epoch != decisionEpoch || Selected != estate)) return;

            // Retain a receipt before any fallible persistence or correlation read. A display failure cannot erase a write.
            if (outcome.Written is { } receipt)
                retained = retained with { Receipt = receipt, Problem = outcome.Problem, State = outcome.Problem is null ? "accepted for processing" : "uncertain",
                    Message = outcome.Problem is null ? "The runtime returned a write receipt. Read the exact record to observe completion; no downstream effect is implied."
                        : "The runtime returned a write receipt with a recovery problem: " + outcome.Problem.Message + " Reconcile the original; never resubmit." };
            else if (changed is null && !retained.Attempted && outcome.Problem is { Code: "not-signed" or "presence-required" } unsigned)
                retained = retained with { State = "draft", Message = "Nothing was signed or sent: " + unsigned.Message + " Your words remain a draft; review them again before signing." };
            else if (changed is null && outcome.Problem is { } refusal)
                retained = retained with { State = "rejected", Problem = refusal, Message = "Rejected: " + refusal.Message +
                    string.Concat(refusal.Findings.Select(f => "\n" + f.Code + ": " + f.Message)) };
            else if (changed is null)
                retained = retained with { State = retained.Attempted ? "uncertain" : "draft", Message = retained.Attempted
                    ? "Uncertain: the runtime returned neither a receipt nor a refusal. Reconcile the retained original; never resubmit."
                    : "No prepared record or outcome was observed. Your words remain a draft." };
            KeepDecision(estate, retained);
            if (retained.Receipt is not null) retained = await ObserveDecisionAsync(api, estate, retained).ConfigureAwait(true);
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            if (!retained.Attempted && (epoch != decisionEpoch || Selected != estate)) return;
            retained = retained with { State = retained.Attempted ? "uncertain" : "draft", Message = retained.Attempted
                ? "Uncertain: the original submission has no confirmed outcome. Read its records to reconcile; it will never be sent again."
                : "The current record could not be prepared and checked. Your answer remains a draft; nothing was signed or sent." };
            KeepDecision(estate, retained);
        }
        finally
        {
            if (changed is not null && !retained.Attempted && epoch == decisionEpoch && Selected == estate)
            {
                retained = retained with { Review = changed, State = "draft", Message = "The subject or evidence changed before signing. Nothing was signed or sent. Your words remain a draft beside the new revision; review again." };
                KeepDecision(estate, retained);
            }
            if (Selected == estate && epoch == decisionEpoch)
            {
                decision = retained; materialReviewed = false; decisionCurrent = changed is not null;
                DecisionMessage = retained.Message; NotifyDecision();
            }
            Busy = false;
        }
    }

    private async Task ReconcileDecisionAsync()
    {
        if (Selected is not { Current: true, Client: { } api } estate || decision is not { Attempted: true } draft) return;
        Busy = true;
        try
        {
            RemoteDecisionDraft observed = await ObserveDecisionAsync(api, estate, draft).ConfigureAwait(true);
            if (Selected == estate && decision?.Key == draft.Key) { decision = observed; DecisionMessage = observed.Message; NotifyDecision(); }
        }
        finally { Busy = false; }
    }

    private async Task<RemoteDecisionDraft> ObserveDecisionAsync(IEstateApi api, RemoteEstate estate, RemoteDecisionDraft draft)
    {
        RemoteDecisionDraft observed = draft;
        try
        {
            RecordResponse? record = await api.RecordAsync(draft.Prepared!.Id).ConfigureAwait(true);
            if (record is not null) RemoteDecisionReader.Validate(record);
            // Completion is the exact record carrying this phone's presence key's signature; the request key never signs a decision.
            bool verified = record is not null && record.Id == draft.Prepared.Id && record.Hash == draft.Prepared.Hash &&
                RemoteDecisionReader.Text(record.Envelope, "by") == estate.Connection.Principal &&
                estate.Connection is { PresenceDevice: string presenceDevice, PresenceDevicePublicKey: string presenceKey } &&
                RemoteDecisionReader.Rows(record.Envelope, "signatures").Any(s => RemoteDecisionReader.Text(s, "device") == presenceDevice &&
                    RemoteDecisionReader.Text(s, "algorithm") == RecordSigning.Algorithm &&
                    RecordSigning.Verify(presenceKey, RecordSigning.Digest(record.Hash), RemoteDecisionReader.Text(s, "signature") ?? ""));
            bool receiptMatches = draft.Receipt is null || draft.Receipt.Id == draft.Prepared.Id && draft.Receipt.Hash == draft.Prepared.Hash;
            observed = verified && receiptMatches ? draft with { State = draft.Problem is null ? "completed" : "uncertain", Message = draft.Problem is null
                ? "Completed: the exact record and this phone's presence-key signature were observed. This confirms the recorded answer or acceptance only; no publication or other downstream effect is established."
                : "The exact phone-signed record was observed, but the runtime's recovery problem remains: " + draft.Problem.Message + " Reconcile on the desktop; never resubmit." }
                : draft with { State = "uncertain", Message = draft.Receipt is null ? "Uncertain: the exact record signed by this phone's presence key was not observed. Keep the original identity and reconcile on the desktop; do not resend."
                    : "Uncertain: the record read contradicts the retained receipt. Keep both observations and reconcile on the desktop; do not resend." };
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            if (draft.State != "completed") observed = draft with { State = draft.Receipt is null || draft.Problem is not null ? "uncertain" : "accepted for processing",
                Message = draft.Receipt is null ? "Uncertain: the original record could not be read; nothing was resubmitted." :
                    "The receipt is retained, but the exact record could not be read. Completion and downstream effects remain unconfirmed." + (draft.Problem is null ? "" : " Recovery problem: " + draft.Problem.Message) };
        }
        KeepDecision(estate, observed);
        return observed;
    }

    private static bool PreparedMatches(RecordPrepared prepared, NewRecordRequest request)
    {
        RemoteDecisionReader.Validate(new("", prepared.Id, request.Type, prepared.Hash, true, prepared.Record));
        JsonElement record = prepared.Record;
        string? subject = request.Subject;
        JsonElement pin = RemoteDecisionReader.Property(record, "subject");
        return RemoteDecisionReader.Text(record, "by") == request.By && RemoteDecisionReader.Text(record, "type") == request.Type &&
            RemoteDecisionReader.Text(record, "format") == RecordEnvelope.Format3 &&
            RemoteDecisionReader.Text(record, "at") == prepared.At && RemoteDecisionReader.Text(record, "supersedes") == request.Supersedes &&
            CanonicalJson.Serialize(RemoteDecisionReader.Property(record, "body")) == CanonicalJson.Serialize(request.Body) &&
            (subject is null ? pin.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null :
                subject == RemoteDecisionReader.Text(pin, "type") + ":" + RemoteDecisionReader.Text(pin, "id") + ":" + RemoteDecisionReader.Text(pin, "hash"));
    }

    private static bool StorageFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException;
    private static bool DecisionFailure(Exception exception) => StorageFailure(exception) || ClientFailure.FromException(exception) is not null || exception is System.Security.Cryptography.CryptographicException or ArgumentException;
}
