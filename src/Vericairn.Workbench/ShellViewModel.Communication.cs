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
        set { if (Set(ref statusViewDepth, value)) { RefreshCommunication(); } }
    }

    /// <summary>The human status at the addressed person's effective depth. Canonical API status is kept separate.</summary>
    public string HumanStatus { get => humanStatus; private set => Set(ref humanStatus, value); }

    /// <summary>The settings workspace, with previews, inheritance, sources, conflicts and attributed writes.</summary>
    public CommunicationDocument OpenCommunication()
    {
        var page = new CommunicationDocument(() => connection?.Api, () => ActingPrincipalId, RefreshCommunication);
        page.Refresh();
        if (HomeSelected) { SelectedRegion = Regions.First(r => r.Name == "Work"); }
        Open(page);
        return page;
    }

    private string? CommunicationRecipient()
    {
        string? id = ActingPrincipalId;
        if (id is null || connection?.Api is not { } api) { return null; }
        RecordResponse? person = Try(() => CurrentPrincipal(api, id)!);
        return person is not null && person.Envelope.TryGetProperty("body", out var body) && Str(body, "kind") == "human" ? id : null;
    }

    private void RefreshCommunication()
    {
        if (connection?.Api is not { } api)
        {
            HumanStatus = "Communication settings unavailable; the retained view is cached.";
            return;
        }

        communication = Try(() => api.Communication(CommunicationRecipient())) ?? communication with { Unavailable = "The runtime did not answer; the previous profile is stale." };
        if (Try(api.Status) is StatusResponse status)
        {
            CommunicationProfile view = StatusViewDepth == "inherit" ? communication :
                Try(() => api.Communication(CommunicationRecipient(), view: new(Status: StatusViewDepth))) ?? communication with { Unavailable = "The one-view setting could not be read." };
            HumanStatus = CommunicationPresentation.Status(status, view);
        }
    }

    private void AddCommunicationAttention()
    {
        if (connection?.Api is not { } api) { return; }
        foreach (CommunicationAttention item in CommunicationPresentation.Attention(api, CommunicationRecipient()))
        {
            if (item.Record is null || !Attention.Any(a => a.SubjectId == item.Record && a.Kind == item.Kind))
            {
                Attention.Add(new Presentation.AttentionItem(item.Kind, item.Record ?? "Required attention", item.Text, "requires attention",
                    communication.ObservedAt ?? "unknown", "Inspect the complete source before proceeding.", item.RecordType, item.Record));
            }
        }
    }
}
