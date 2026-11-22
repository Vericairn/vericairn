// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Workbench;

/// <summary>One ready operator item and the operator's choice for it: sign it, leave it waiting, or, for a request, dismiss it with a reason.</summary>
public sealed class OperatorActRow : Observable
{
    private string choice = "sign";
    private string reason = string.Empty;

    /// <summary>
    /// Creates the row: an answer or a request starts chosen to sign; an acceptance, a judgment, starts waiting until the operator selects it.
    /// </summary>
    public OperatorActRow(OperatorActItem item, Action changed)
    {
        Item = item;
        Changed = changed;
        Choices = item.Dismissable ? ["sign", "wait", "dismiss"] : ["sign", "wait"];
        choice = item.IsAcceptance ? "wait" : "sign";
    }

    /// <summary>The item as the runtime listed it, with its exact subject, basis and what its signature records.</summary>
    public OperatorActItem Item { get; }

    /// <summary>What may be chosen: only a request may be dismissed.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>sign, wait or dismiss.</summary>
    public string Choice { get => choice; set { if (Choices.Contains(value) && Set(ref choice, value)) Changed(); } }

    /// <summary>The operator's reason for a dismissal.</summary>
    public string Reason { get => reason; set { if (Set(ref reason, value)) Changed(); } }

    /// <summary>The item with what its signature records, its exact subject and basis.</summary>
    public string Label => OperatorActDocument.Describe(Item);

    private Action Changed { get; }
}

/// <summary>
/// From 0.9.4 (OPS-24): the same reviewed list as vericairn operator act --ready. Every operator item whose command is ready for the acting
/// principal, each with what its signature records; the operator unselects any item to leave it waiting or dismisses a request with a
/// reason, reviews the act as it will be signed and confirms it; the runtime then signs the rest on the operator's own device under one
/// presence session whose announced list names each record, and signs nothing outside the list. A changed item refuses the whole act.
/// </summary>
public sealed class OperatorActDocument(Func<IEstateApi?> api, Func<string?> author) : Document("Operator act", "operator-act")
{
    private string? by;
    private string stake = "low";
    private bool reviewed;
    private bool current;
    private string note = "Refresh to read the ready items.";

    /// <summary>The ready items and the choices made for them.</summary>
    public ObservableCollection<OperatorActRow> Rows { get; } = [];

    /// <summary>The operator whose items these are.</summary>
    public string? By { get => by; private set => Set(ref by, value); }

    /// <summary>The record format's decision stakes, for each answer's decision.</summary>
    public IReadOnlyList<string> Stakes { get; } = ["low", "medium", "high", "critical"];

    /// <summary>Each answer's decision stake.</summary>
    public string Stake { get => stake; set { if (Set(ref stake, value)) Changed(); } }

    /// <summary>A fresh confirmation of the act as shown; any change clears it.</summary>
    public bool Reviewed { get => reviewed; set { if (Set(ref reviewed, value && current)) Raise(nameof(CanSign)); } }

    /// <summary>Whether the act can be signed now: read current, reviewed, every dismissal with a reason, and something to sign or dismiss.</summary>
    public bool CanSign => CanSubmit && current && Reviewed && Rows.Any(r => r.Choice != "wait") && Rows.All(r => r.Choice != "dismiss" || r.Reason.Trim().Length > 0);

    /// <summary>The complete outcome or refusal of the last action.</summary>
    public string Note { get => note; internal set => Set(ref note, value); }

    /// <summary>The act as it will be signed: what is signed, dismissed and left waiting, each by its exact subject.</summary>
    public string Review
    {
        get
        {
            if (Rows.Count == 0) return "No operator item is ready.";
            var text = new StringBuilder($"One act as {By} signs {Rows.Count(r => r.Choice == "sign")} and dismisses {Rows.Count(r => r.Choice == "dismiss")} of {Rows.Count} ready items; the rest wait, and nothing else is signed. Each answer's decision is of stake {Stake}.");
            foreach (OperatorActRow row in Rows) text.Append('\n').Append(row.Choice).Append(": ").Append(row.Item.Id).Append(" signs ").Append(row.Item.Records);
            return text.ToString();
        }
    }

    /// <summary>One item's description, as the command line shows it.</summary>
    public static string Describe(OperatorActItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var text = new StringBuilder($"[{item.Kind}] {item.Title}\nSigns: {item.Records}\nSubject: {item.SubjectType} {item.Subject.Id} at {item.Subject.Hash}");
        foreach (OperatorClosePin basis in item.Basis) text.Append("\nBasis: ").Append(basis.Id).Append(" at ").Append(basis.Hash);
        foreach (OperatorActReview review in item.Reviews ?? [])
            text.Append("\nReview ").Append(review.Id).Append(" at ").Append(review.Hash).Append(" by ").Append(review.By).Append(": ").Append(review.Outcome).Append(", ").Append(review.Independence).Append(", of ").Append(review.SubjectHash);
        if (item.Recommendation is not null) text.Append("\nRecommendation ").Append(item.RecommendationHash).Append(": ").Append(item.Recommendation);
        return text.ToString();
    }

    /// <summary>Reads the ready items for the acting principal. Reading signs nothing.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await ReadAsync(async estate =>
        {
            OperatorActListResponse listing = await estate.OperatorActItemsAsync(author(), cancellationToken).ConfigureAwait(true);
            Show(listing);
            Note = !listing.Ok ? listing.Problem!.Code + ": " + listing.Problem.Message
                : Rows.Count == 0 ? "No operator item is ready." : "Unselect any item to leave it waiting, or dismiss a request with your reason; review the act, confirm it, then sign.";
        }).ConfigureAwait(true);
    }

    /// <summary>Signs the act as reviewed: every listed item chosen to sign, wait or dismiss, the whole list pinned.</summary>
    public async Task SignAsync(CancellationToken cancellationToken = default)
    {
        if (author() is not string actor || actor != By)
        {
            Note = "Select the operator whose items these are, and refresh, before signing.";
            return;
        }

        if (!CanSign)
        {
            Note = "Review the act and confirm it; give each dismissal your reason.";
            return;
        }

        OperatorActChoice[] choices = [.. Rows.Select(r => r.Item.Choose(r.Choice, r.Choice == "dismiss" ? r.Reason.Trim() : null))];
        await ReadAsync(async estate =>
        {
            OperatorActResponse response = await estate.OperatorActAsync(new(actor, choices, true, Stake), cancellationToken).ConfigureAwait(true);
            var outcome = new StringBuilder(response.Act is null ? string.Empty : $"Act {response.Act}: {response.Written.Count} records");
            foreach (OperatorActReceipt receipt in response.Written)
                outcome.Append(outcome.Length == 0 ? string.Empty : "\n").Append(receipt.Choice == "dismiss" ? "Dismissed " : "Signed ").Append(receipt.Item).Append(" as ").Append(receipt.Record.Id).Append(" at ").Append(receipt.Record.Hash);
            if (response.Problem is { } problem) outcome.Append(outcome.Length == 0 ? string.Empty : "\n").Append(problem.Code).Append(": ").Append(problem.Message);
            OperatorActListResponse after = await estate.OperatorActItemsAsync(actor, cancellationToken).ConfigureAwait(true);
            Show(after);
            Note = outcome.ToString();
        }).ConfigureAwait(true);
    }

    private void Show(OperatorActListResponse listing)
    {
        Rows.Clear();
        By = listing.By;
        current = listing.Ok;
        foreach (OperatorActItem item in listing.Ok ? listing.Items : []) Rows.Add(new OperatorActRow(item, Changed));
        Changed();
    }

    private void Changed()
    {
        reviewed = false;
        Raise(nameof(Reviewed));
        Raise(nameof(Review));
        Raise(nameof(CanSign));
    }

    private async Task ReadAsync(Func<IEstateApi, Task> action)
    {
        if (api() is not { } estate)
        {
            Note = "Operator act unavailable: the runtime did not answer. Nothing was signed.";
            return;
        }

        try { await action(estate).ConfigureAwait(true); }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null)
        {
            Note = ClientFailure.FromException(exception)!.Message;
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException or ArgumentException)
        {
            current = false;
            Note = "Operator act unavailable: " + exception.Message;
            Changed();
        }
    }
}
