// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Remote;

/// <summary>One ready operator item and the person's choice for it: sign it, leave it waiting, or, for a request, dismiss it with a reason.</summary>
public sealed class RemoteActChoice(OperatorActItem item) : Observable
{
    // An answer or a request starts chosen to sign; an acceptance, a judgment, starts waiting until the person selects it.
    private string choice = item.IsAcceptance ? "wait" : "sign";
    private string reason = string.Empty;

    /// <summary>The item as the runtime listed it, with what its signature records and its exact subject and basis.</summary>
    public OperatorActItem Item { get; } = item;

    /// <summary>What may be chosen: only a request may be dismissed.</summary>
    public IReadOnlyList<string> Choices { get; } = item.Dismissable ? ["sign", "wait", "dismiss"] : ["sign", "wait"];

    /// <summary>sign, wait or dismiss.</summary>
    public string Choice { get => choice; set { if (Choices.Contains(value)) Set(ref choice, value); } }

    /// <summary>The person's reason for a dismissal.</summary>
    public string Reason { get => reason; set => Set(ref reason, value); }

    /// <summary>The item with what its signature records, its exact subject and basis.</summary>
    public string Label
    {
        get
        {
            var text = new StringBuilder($"[{Item.Kind}] {Item.Title}\nSigns: {Item.Records}\nSubject: {Item.SubjectType} {Item.Subject.Id} at {Item.Subject.Hash}");
            foreach (OperatorClosePin basis in Item.Basis) text.Append("\nBasis: ").Append(basis.Id).Append(" at ").Append(basis.Hash);
            foreach (OperatorActReview review in Item.Reviews ?? [])
                text.Append("\nReview ").Append(review.Id).Append(" at ").Append(review.Hash).Append(" by ").Append(review.By).Append(": ").Append(review.Outcome).Append(", ").Append(review.Independence).Append(", of ").Append(review.SubjectHash);
            if (Item.Recommendation is not null) text.Append("\nRecommendation ").Append(Item.RecommendationHash).Append(": ").Append(Item.Recommendation);
            return text.ToString();
        }
    }
}

/// <summary>
/// From 0.9.4 (OPS-24): the same reviewed list as vericairn operator act --ready. Every operator item whose command is ready for the
/// signed-in operator, each with what its signature records; the person leaves any item waiting or dismisses a request with a reason,
/// reviews the act and confirms it. The act refuses whole, before any signature, if any item changed since it was shown; each record is
/// then the one the runtime's act would sign, prepared by the runtime and signed by this phone's presence key, one presence check each as
/// every phone signature is, and checked against the item as shown before each signature. A change during the act stops it before the
/// next signature; what was written stands, and nothing outside the reviewed list is signed.
/// </summary>
public sealed partial class RemoteViewModel
{
    private IReadOnlyList<RemoteActChoice> actChoices = [];
    private string actMessage = string.Empty;
    private string actStake = "low";
    private bool actReviewed;
    private bool actCurrent;
    private IReadOnlyList<string> actReceipts = [];

    /// <summary>The ready items of the signed-in operator, as last read from the runtime.</summary>
    public IReadOnlyList<RemoteActChoice> ActChoices { get => actChoices; private set { if (Set(ref actChoices, value)) { Raise(nameof(HasActChoices)); NotifyActCommands(); } } }

    /// <summary>Whether any item is ready.</summary>
    public bool HasActChoices => ActChoices.Count > 0;

    /// <summary>What the act observed: a refusal, a stop or its records, never an inferred downstream effect.</summary>
    public string ActMessage { get => actMessage; private set => Set(ref actMessage, value); }

    /// <summary>Each answer's decision stake.</summary>
    public string ActStake { get => actStake; set { if (Set(ref actStake, value)) { ActReviewed = false; NotifyActCommands(); } } }

    /// <summary>A fresh deliberate confirmation of the act as shown, cleared by any change.</summary>
    public bool ActReviewed { get => actReviewed; set { if (Set(ref actReviewed, value && actCurrent && Selected?.Current == true)) NotifyActCommands(); } }

    /// <summary>The records the last act wrote.</summary>
    public IReadOnlyList<string> ActReceipts { get => actReceipts; private set => Set(ref actReceipts, value); }

    /// <summary>The act as it would be signed: what is signed, dismissed and left waiting, and how the phone signs.</summary>
    public string ActReview
    {
        get
        {
            if (ActChoices.Count == 0) return "No operator item is ready.";
            var text = new StringBuilder($"One act signs {ActChoices.Count(c => c.Choice == "sign")} and dismisses {ActChoices.Count(c => c.Choice == "dismiss")} of {ActChoices.Count} ready items; the rest wait, and nothing else is signed. ");
            text.Append("Each record is signed by this phone's presence key, which asks for your presence at every signature.");
            foreach (RemoteActChoice choice in ActChoices) text.Append('\n').Append(choice.Choice).Append(": ").Append(choice.Item.Id).Append(" at ").Append(choice.Item.Subject.Hash);
            return text.ToString();
        }
    }

    /// <summary>Reads the ready items of the signed-in operator. Reading signs nothing.</summary>
    public AsyncCommand ReadActCommand { get; private set; } = null!;

    /// <summary>Signs the reviewed act with this phone's presence key.</summary>
    public AsyncCommand SignActCommand { get; private set; } = null!;

    private void InitializeActCommands()
    {
        ReadActCommand = new(ReadActAsync, () => !Busy && Selected?.Current == true);
        SignActCommand = new(SignActAsync, CanSignAct);
    }

    private bool CanSignAct() => !Busy && Selected?.Current == true && actCurrent && ActReviewed && HasPresenceKey && ActChoices.Any(c => c.Choice != "wait") &&
        ActChoices.All(c => c.Choice != "dismiss" || c.Reason.Trim().Length > 0) && Records.Types.DecisionType.Stakes.Contains(ActStake, StringComparer.Ordinal);

    private void NotifyActCommands()
    {
        ReadActCommand?.NotifyCanExecuteChanged();
        SignActCommand?.NotifyCanExecuteChanged();
        Raise(nameof(ActReview));
    }

    private void SetActChoices(IReadOnlyList<OperatorActItem> items, bool current)
    {
        foreach (RemoteActChoice old in ActChoices) old.PropertyChanged -= ChoiceChanged;
        RemoteActChoice[] choices = [.. items.Select(i => new RemoteActChoice(i))];
        foreach (RemoteActChoice choice in choices) choice.PropertyChanged += ChoiceChanged;
        actCurrent = current;
        actReviewed = false;
        Raise(nameof(ActReviewed));
        ActChoices = choices;
        NotifyActCommands();

        void ChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            actReviewed = false;
            Raise(nameof(ActReviewed));
            NotifyActCommands();
        }
    }

    private async Task ReadActAsync()
    {
        if (Selected is not { Current: true, Client: { } api } estate) return;
        Busy = true;
        try
        {
            OperatorActListResponse listed = await api.OperatorActItemsAsync(estate.Connection.Principal, CancellationToken.None).ConfigureAwait(true);
            if (Selected != estate) return;
            SetActChoices(listed.Ok ? listed.Items : [], listed.Ok);
            ActMessage = !listed.Ok ? "The runtime cannot list the ready items: " + listed.Problem!.Message
                : listed.Items.Count == 0 ? "No operator item is ready." : "Leave any item waiting, or dismiss a request with your reason; review the act and confirm it before signing.";
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            if (Selected == estate) { SetActChoices([], false); ActMessage = "The ready items could not be read. Refresh and read them again; nothing was signed or sent."; }
        }
        finally { Busy = false; }
    }

    private async Task SignActAsync()
    {
        if (!CanSignAct() || Selected is not { Client: { } api } estate) return;
        RemoteActChoice[] acting = [.. ActChoices.Where(c => c.Choice != "wait")];
        string principal = estate.Connection.Principal;
        Busy = true;
        var receipts = new List<string>();
        try
        {
            // The act refuses whole, before any signature, when an item changed since it was shown or another became ready.
            OperatorActListResponse fresh = await api.OperatorActItemsAsync(principal, CancellationToken.None).ConfigureAwait(true);
            if (Selected != estate) return;
            if (!fresh.Ok || fresh.Items.Count != ActChoices.Count || ActChoices.Any(c => !Unchanged(fresh, c.Item)))
            {
                SetActChoices(fresh.Ok ? fresh.Items : [], fresh.Ok);
                ActMessage = "A ready item changed since the act showed it, or the list changed. Nothing was signed or sent; review the current list.";
                return;
            }

            string act = Records.Identity.Ulid.NewUlid(clock.GetUtcNow()).Value;
            string decidedAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()));
            int answers = acting.Count(c => c.Item.Kind == "answer");
            for (int i = 0; i < acting.Length; i++)
            {
                RemoteActChoice choice = acting[i];
                OperatorActItem item = choice.Item;
                NewRecordRequest request = item.Kind switch
                {
                    "answer" => new("decision", principal, JsonSerializer.SerializeToElement(OperatorAnswers.DecisionBody(item.Subject.Id, item.Subject.Hash, item.Question ?? item.Title,
                        item.Recommendation ?? string.Empty, principal, act, decidedAt, answers, ActStake))),
                    "request" => new("operator-request-close", principal, JsonSerializer.SerializeToElement(OperatorActs.RequestClose(choice.Choice == "dismiss" ? choice.Reason.Trim() : null)),
                        Subject: item.SubjectType + ":" + item.Subject.Id + ":" + item.Subject.Hash, Expected: item.Subject.Hash),
                    _ => new("acceptance", principal, JsonSerializer.SerializeToElement(OperatorActs.Acceptance(item.Kind, item.Basis.Select(b => b.Id), item.Self)),
                        Subject: item.SubjectType + ":" + item.Subject.Id + ":" + item.Subject.Hash, Expected: item.Subject.Hash),
                };
                // Checked again before the presence key asks and once more before its signature leaves: the exact prepared record and the item
                // as shown. The runtime checks the record against the estate again when it writes.
                bool StillCurrent(RecordPrepared prepared) => PreparedMatches(prepared, request) &&
                    Unchanged(api.OperatorActItemsAsync(principal, CancellationToken.None).GetAwaiter().GetResult(), item);
                WriteOutcome outcome = await Task.Run(() => connections.WriteSignedAsync(estate.Connection, request, StillCurrent, StillCurrent)).ConfigureAwait(true);
                if (outcome.Written is { } written && outcome.Problem is null)
                {
                    receipts.Add((choice.Choice == "dismiss" ? "Dismissed " : "Signed ") + item.Id + " as " + written.Id + " at " + written.Hash);
                    continue;
                }

                string reason = outcome.Written is { } stored ? $"record {stored.Id} was stored with a recovery problem: {outcome.Problem!.Message}" : outcome.Problem?.Message ?? "no receipt and no refusal was returned";
                ActMessage = (receipts.Count == 0 ? "The act signed nothing" : $"The act stopped after {receipts.Count} of {acting.Length} records") +
                    $": {reason} Read the ready items again before acting on the rest; an item already signed is no longer ready, so nothing is signed twice.";
                if (outcome.Written is { } kept) receipts.Add("Signed " + item.Id + " as " + kept.Id + " at " + kept.Hash + " (reconcile on the desktop)");
                return;
            }

            ActMessage = $"Act {act}: {receipts.Count} {(receipts.Count == 1 ? "record" : "records")} written. This confirms the recorded acts only; no publication or other downstream effect is implied.";
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            ActMessage = (receipts.Count == 0 ? "The act has no confirmed outcome" : $"The act stopped after {receipts.Count} of {acting.Length} records") +
                ": the runtime could not be reached or answered unreadably. Read the ready items again; an item already signed is no longer ready, so nothing is signed twice.";
        }
        finally
        {
            ActReceipts = receipts;
            Busy = false;
            if (Selected == estate && estate.Client is not null)
            {
                try
                {
                    OperatorActListResponse after = await api.OperatorActItemsAsync(principal, CancellationToken.None).ConfigureAwait(true);
                    SetActChoices(after.Ok ? after.Items : [], after.Ok);
                }
                catch (Exception exception) when (DecisionFailure(exception)) { SetActChoices([], false); }
            }
        }
    }

    private static bool Unchanged(OperatorActListResponse current, OperatorActItem shown) =>
        current.Ok && current.Items.SingleOrDefault(i => i.Id == shown.Id) is { } now
        && CanonicalJson.Serialize(JsonSerializer.SerializeToElement(now, ProtocolJson.Options)) == CanonicalJson.Serialize(JsonSerializer.SerializeToElement(shown, ProtocolJson.Options));
}
