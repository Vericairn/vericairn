// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Workbench;

public sealed partial class ShellViewModel
{
    private CommunicationProfile communication = CommunicationResolution.Resolve(null, null, []);
    private string humanStatus = "Communication settings and current status are unavailable until connected.";
    private string statusViewDepth = "inherit";

    /// <summary>One-view choices for Home status; these are never stored as a person's setting.</summary>
    public IReadOnlyList<string> StatusViewDepths { get; } = ["inherit", "short", "standard", "comprehensive"];

    /// <summary>An ephemeral override for this status view only. Inherit returns to the recorded profile.</summary>
    public string StatusViewDepth
    {
        get => statusViewDepth;
        set { if (Set(ref statusViewDepth, value)) { _ = RefreshCommunicationCommandAsync(); } }
    }

    /// <summary>The human status at the addressed person's effective depth. Canonical API status is kept separate.</summary>
    public string HumanStatus { get => humanStatus; private set => Set(ref humanStatus, value); }

    /// <summary>The settings workspace, with previews, inheritance, sources, conflicts and attributed writes.</summary>
    private async Task<CommunicationDocument> OpenCommunicationCoreAsync(CancellationToken cancellationToken = default)
    {
        var page = new CommunicationDocument(() => connection?.Api, () => ActingPrincipalId, token => RefreshCommunicationAsync(token));
        await page.RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (HomeSelected) { SelectedRegion = Regions.First(r => r.Name == "Work"); }
        Open(page);
        return page;
    }

    private async Task<string?> CommunicationRecipientAsync(CancellationToken cancellationToken = default)
    {
        string? id = ActingPrincipalId;
        if (id is null || connection?.Api is not { } api) { return null; }
        RecordResponse? person = (await TryAsync(async () => (await CurrentPrincipalAsync(api, id, cancellationToken: cancellationToken).ConfigureAwait(true))!).ConfigureAwait(true));
        return HumanRecipient(person);
    }

    private string? HumanRecipient(RecordResponse? person) =>
        person is not null && person.Envelope.TryGetProperty("body", out var body) && Str(body, "kind") == "human" ? ActingPrincipalId : null;

    private async Task RefreshCommunicationAsync(CancellationToken cancellationToken = default) => await RefreshCommunicationAsync(null, (await CommunicationRecipientAsync(cancellationToken: cancellationToken).ConfigureAwait(true)), cancellationToken: cancellationToken).ConfigureAwait(true);

    private async Task RefreshCommunicationAsync(StatusResponse? observedStatus, string? recipient, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is not { } api)
        {
            HumanStatus = "Communication settings unavailable; the retained view is cached.";
            return;
        }

        communication = (await TryAsync(async () => (await api.CommunicationAsync(recipient, cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true)) ?? communication with { Unavailable = "The runtime did not answer; the previous profile is stale." };
        if ((observedStatus ?? (await TryAsync(() => api.StatusAsync(cancellationToken)).ConfigureAwait(true))) is StatusResponse status)
        {
            CommunicationProfile view = StatusViewDepth == "inherit" ? communication :
                (await TryAsync(async () => (await api.CommunicationAsync(recipient, view: new(Status: StatusViewDepth), cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true)) ?? communication with { Unavailable = "The one-view setting could not be read." };
            HumanStatus = CommunicationPresentation.Status(status, view);
            // Periodic attention must use the same renewed status, not the work state retained at the last full refresh.
            estate = Presentation.EstateView.From(status, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private async Task AddCommunicationAttentionAsync(string? recipient, ProgressResponse? progress, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is not { } api) { return; }
        int deliveredAsks = 0;
        foreach (CommunicationAttention item in (await CommunicationPresentation.AttentionAsync(api, recipient, progress, progressObserved: true, cancellationToken: cancellationToken).ConfigureAwait(true)))
        {
            string kind = item.RecordType == "ask" && item.Kind == "required-decision" ? "decision needed" : item.Kind;
            string state = item.RecordType == "ask" ? Presentation.StateLanguage.Label("ask", item.State) : item.State ?? "unknown";
            if (item.Record is null || !Attention.Any(a => a.SubjectId == item.Record && a.Kind == kind))
            {
                var row = new Presentation.AttentionItem(kind, item.Record ?? "Required attention", item.Text, state,
                    item.AsOf ?? "unknown", item.Command ?? "vericairn status", item.Target?.Type ?? item.RecordType, item.Target?.Id ?? item.Record);
                if (item.RecordType is "ask" or "operator-request") { Attention.Insert(deliveredAsks++, row); }
                else { Attention.Add(row); }
            }
        }
    }

    private async Task<CommunicationProfile> AssignmentCommunicationAsync(string assignment, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is not { } api || (await CommunicationRecipientAsync(cancellationToken: cancellationToken).ConfigureAwait(true)) is not string recipient) { return communication; }
        return (await TryAsync(async () => (await api.CommunicationAsync(recipient, assignment, cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true)) ?? communication with { Unavailable = "The assignment's communication profile is unavailable." };
    }

    private async Task<CommunicationProfile> ResultCommunicationAsync(RecordResponse result, CancellationToken cancellationToken = default)
    {
        if (result.Type != "result" || connection?.Api is not { } api) { return communication; }
        string? attemptId = result.Envelope.TryGetProperty("subject", out var subject) ? Str(subject, "id") : null;
        RecordResponse? attempt = attemptId is null ? null : (await TryAsync(async () => (await api.RecordAsync(attemptId, cancellationToken: cancellationToken).ConfigureAwait(true))!).ConfigureAwait(true));
        string? assignmentId = attempt is { Type: "attempt" } && attempt.Envelope.TryGetProperty("subject", out var assigned) && Str(assigned, "type") == "assignment" ? Str(assigned, "id") : null;
        return assignmentId is null ? communication with { Unavailable = "The result's exact assignment could not be read; full presentation is retained." } : (await AssignmentCommunicationAsync(assignmentId, cancellationToken: cancellationToken).ConfigureAwait(true));
    }
}
