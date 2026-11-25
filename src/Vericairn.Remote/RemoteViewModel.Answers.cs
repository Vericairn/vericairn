// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Remote;

/// <summary>An open ask the act may answer with its recommendation, and whether the person selected it.</summary>
public sealed class RemoteAnswerChoice(OperatorAnswerItem item) : Observable
{
    private bool selected = true;

    /// <summary>The exact ask revision, its recommendation and that recommendation's hash, as the runtime listed them.</summary>
    public OperatorAnswerItem Item { get; } = item;

    /// <summary>Whether the act answers this ask; every ask starts selected and can be left out.</summary>
    public bool Selected { get => selected; set => Set(ref selected, value); }

    /// <summary>The ask and its recommendation by hash first, then the words.</summary>
    public string Label => "Ask " + Item.Ask.Id + " at " + Item.Ask.Hash + "\nRecommendation " + Item.RecommendationHash + ": " + Item.Recommendation +
        "\nQuestion: " + Item.Question + "\nIf unanswered: " + Item.Consequence;
}

/// <summary>
/// From 0.9.4 (OPS-16): the operator answers the asks waiting on them, a selection or all, each with its recommendation, in one act. The
/// act shows each ask and recommendation by hash first and refuses before any signature if any changed since; each ask gets its own
/// decision naming the act, decidedAt the act's time, signed by this phone's presence key, which asks for presence at each signature as
/// every phone signature does, and is checked again before each signature. A change during the act stops it before the next signature;
/// what was written stands, and an ask once answered is never answered again.
/// </summary>
public sealed partial class RemoteViewModel
{
    private IReadOnlyList<RemoteAnswerChoice> answerChoices = [];
    private string answerMessage = string.Empty;
    private string answerStake = "low";
    private bool answersReviewed;
    private bool answersCurrent;
    private IReadOnlyList<string> answerReceipts = [];

    /// <summary>The asks waiting on the signed-in operator, each with its recommendation, as last read from the runtime.</summary>
    public IReadOnlyList<RemoteAnswerChoice> AnswerChoices { get => answerChoices; private set { if (Set(ref answerChoices, value)) { Raise(nameof(HasAnswerChoices)); Raise(nameof(AnswerReview)); NotifyAnswerCommands(); } } }

    /// <summary>Whether any ask waits to be answered with its recommendation.</summary>
    public bool HasAnswerChoices => AnswerChoices.Count > 0;

    /// <summary>What the act observed: a refusal, a stop or its decisions, never an inferred downstream effect.</summary>
    public string AnswerMessage { get => answerMessage; private set => Set(ref answerMessage, value); }

    /// <summary>Each decision's stake; the asks supply none.</summary>
    public string AnswerStake { get => answerStake; set { if (Set(ref answerStake, value)) { AnswersReviewed = false; NotifyAnswerCommands(); } } }

    /// <summary>A fresh deliberate confirmation of the act as shown, cleared by any change.</summary>
    public bool AnswersReviewed { get => answersReviewed; set { if (Set(ref answersReviewed, value && answersCurrent && Selected?.Current == true)) NotifyAnswerCommands(); } }

    /// <summary>The decisions the last act wrote, each by its record id and hash.</summary>
    public IReadOnlyList<string> AnswerReceipts { get => answerReceipts; private set => Set(ref answerReceipts, value); }

    /// <summary>The act as it would be signed: the selected asks and recommendations by hash, the stake and how the phone signs.</summary>
    public string AnswerReview
    {
        get
        {
            RemoteAnswerChoice[] chosen = [.. AnswerChoices.Where(c => c.Selected)];
            if (chosen.Length == 0) return "Select at least one ask to answer with its recommendation.";
            var text = new StringBuilder($"One act answers {chosen.Length} {(chosen.Length == 1 ? "ask" : "asks")} with {(chosen.Length == 1 ? "its recommendation" : "their recommendations")}, each its own decision of stake {AnswerStake}, ");
            text.Append("each signed by this phone's presence key, which asks for your presence at every signature:");
            foreach (RemoteAnswerChoice choice in chosen) text.Append("\n- ask ").Append(choice.Item.Ask.Id).Append(" at ").Append(choice.Item.Ask.Hash).Append(", recommendation ").Append(choice.Item.RecommendationHash);
            return text.ToString();
        }
    }

    /// <summary>Reads the asks waiting on the signed-in operator from the runtime. Reading answers nothing.</summary>
    public AsyncCommand ReadAnswersCommand { get; private set; } = null!;

    /// <summary>Answers the selected asks with their recommendations in one act signed by this phone's presence key.</summary>
    public AsyncCommand SignAnswersCommand { get; private set; } = null!;

    private void InitializeAnswerCommands()
    {
        ReadAnswersCommand = Command(ReadAnswersAsync, () => !Busy && Selected?.Current == true);
        SignAnswersCommand = Command(SignAnswersAsync, CanSignAnswers);
    }

    private bool CanSignAnswers() => !Busy && Selected?.Current == true && answersCurrent && AnswersReviewed && HasPresenceKey &&
        AnswerChoices.Any(c => c.Selected) && DecisionType.Stakes.Contains(AnswerStake, StringComparer.Ordinal);

    private void NotifyAnswerCommands()
    {
        ReadAnswersCommand?.NotifyCanExecuteChanged();
        SignAnswersCommand?.NotifyCanExecuteChanged();
        Raise(nameof(AnswerReview));
    }

    private void SetAnswerChoices(IReadOnlyList<OperatorAnswerItem> items, bool current)
    {
        foreach (RemoteAnswerChoice old in AnswerChoices) old.PropertyChanged -= ChoiceChanged;
        RemoteAnswerChoice[] choices = [.. items.Select(i => new RemoteAnswerChoice(i))];
        foreach (RemoteAnswerChoice choice in choices) choice.PropertyChanged += ChoiceChanged;
        answersCurrent = current;
        answersReviewed = false;
        Raise(nameof(AnswersReviewed));
        AnswerChoices = choices;
        NotifyAnswerCommands();

        void ChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            answersReviewed = false;
            Raise(nameof(AnswersReviewed));
            NotifyAnswerCommands();
        }
    }

    private async Task ReadAnswersAsync()
    {
        if (Selected is not { Current: true, Client: { } api } estate) return;
        Busy = true;
        try
        {
            OperatorAnswerCandidatesResponse listed = await api.OperatorAnswerCandidatesAsync(estate.Connection.Principal, CancellationToken.None).ConfigureAwait(true);
            if (Selected != estate) return;
            SetAnswerChoices(listed.Ok ? listed.Items : [], listed.Ok);
            AnswerMessage = !listed.Ok ? "The runtime cannot list the asks: " + listed.Problem!.Message
                : listed.Items.Count == 0 ? "No open ask waits on you." : "Review each ask and its recommendation, leave out any you do not answer this way, then confirm the act.";
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            if (Selected == estate) { SetAnswerChoices([], false); AnswerMessage = "The asks could not be read. Refresh and read them again; nothing was signed or sent."; }
        }
        finally { Busy = false; }
    }

    private async Task SignAnswersAsync()
    {
        if (!CanSignAnswers() || Selected is not { Client: { } api } estate) return;
        OperatorAnswerItem[] chosen = [.. AnswerChoices.Where(c => c.Selected).Select(c => c.Item)];
        string principal = estate.Connection.Principal;
        Busy = true;
        var receipts = new List<string>();
        try
        {
            // The act refuses whole, before any signature, when an ask or its recommendation changed since it was shown.
            OperatorAnswerCandidatesResponse fresh = await api.OperatorAnswerCandidatesAsync(principal, CancellationToken.None).ConfigureAwait(true);
            if (Selected != estate) return;
            if (!fresh.Ok || chosen.Any(c => !Unchanged(fresh, c)))
            {
                SetAnswerChoices(fresh.Ok ? fresh.Items : [], fresh.Ok);
                AnswerMessage = "An ask or its recommendation changed since the act showed it, or no longer waits on you. Nothing was signed or sent; review the current asks.";
                return;
            }

            string act = Records.Identity.Ulid.NewUlid(clock.GetUtcNow()).Value;
            string decidedAt = RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()));
            for (int i = 0; i < chosen.Length; i++)
            {
                OperatorAnswerItem item = chosen[i];
                JsonElement body = JsonSerializer.SerializeToElement(OperatorAnswers.DecisionBody(item.Ask.Id, item.Ask.Hash, item.Question, item.Recommendation,
                    principal, act, decidedAt, chosen.Length, AnswerStake));
                var request = new NewRecordRequest("decision", principal, body);
                // Checked again before the presence key asks and once more before its signature leaves: the exact prepared record, and the ask
                // and recommendation as shown. The runtime checks both again when it writes.
                bool StillCurrent(RecordPrepared prepared) => PreparedMatches(prepared, request) &&
                    Unchanged(api.OperatorAnswerCandidatesAsync(principal, CancellationToken.None).GetAwaiter().GetResult(), item);
                WriteOutcome outcome = await Task.Run(() => connections.WriteSignedAsync(estate.Connection, request, StillCurrent, StillCurrent)).ConfigureAwait(true);
                if (outcome.Written is { } written && outcome.Problem is null)
                {
                    receipts.Add("Decision " + written.Id + " at " + written.Hash + " answers ask " + item.Ask.Id);
                    continue;
                }

                string reason = outcome.Written is { } stored ? $"decision {stored.Id} was stored with a recovery problem: {outcome.Problem!.Message}" : outcome.Problem?.Message ?? "no receipt and no refusal was returned";
                AnswerMessage = (receipts.Count == 0 ? "The act signed nothing" : $"The act stopped after {receipts.Count} of {chosen.Length} decisions") +
                    $": {reason} Read the asks again before answering the rest; an ask already answered is never answered again.";
                if (outcome.Written is { } kept) receipts.Add("Decision " + kept.Id + " at " + kept.Hash + " answers ask " + item.Ask.Id + " (reconcile on the desktop)");
                return;
            }

            AnswerMessage = $"Act {act}: {receipts.Count} {(receipts.Count == 1 ? "decision" : "decisions")} written, each answering its ask with its recommendation. This confirms the recorded answers only.";
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            AnswerMessage = (receipts.Count == 0 ? "The act has no confirmed outcome" : $"The act stopped after {receipts.Count} of {chosen.Length} decisions") +
                ": the runtime could not be reached or answered unreadably. Read the asks again; an ask already answered is never answered again, so nothing is answered twice.";
        }
        finally
        {
            AnswerReceipts = receipts;
            Busy = false;
            if (Selected == estate && estate.Client is not null)
            {
                try
                {
                    OperatorAnswerCandidatesResponse after = await api.OperatorAnswerCandidatesAsync(principal, CancellationToken.None).ConfigureAwait(true);
                    SetAnswerChoices(after.Ok ? after.Items : [], after.Ok);
                }
                catch (Exception exception) when (DecisionFailure(exception)) { SetAnswerChoices([], false); }
            }
        }
    }

    private static bool Unchanged(OperatorAnswerCandidatesResponse current, OperatorAnswerItem shown) =>
        current.Ok && current.Items.SingleOrDefault(i => i.Ask.Id == shown.Ask.Id) is { } now && now.Ask == shown.Ask && now.RecommendationHash == shown.RecommendationHash
        && CanonicalJson.Serialize(JsonSerializer.SerializeToElement(now, ProtocolJson.Options)) == CanonicalJson.Serialize(JsonSerializer.SerializeToElement(shown, ProtocolJson.Options));
}
