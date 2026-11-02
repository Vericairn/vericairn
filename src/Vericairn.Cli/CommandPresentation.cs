// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed record CommunicationSelection(string? Recipient = null, string? Assignment = null, string? Depth = null)
{
    public CommunicationValues Values(string kind) => new(Status: kind == "status" ? Depth : null, Decision: kind == "decision" ? Depth : null,
        Result: kind == "result" ? Depth : null, Handoff: kind == "handoff" ? Depth : null);
}

internal static class CommandPresentation
{

    private static CommunicationProfile Profile(IEstateApi api, string kind, CommunicationSelection? selection) =>
        api.Communication(selection?.Recipient, selection?.Assignment, selection?.Values(kind));

    public static string Status(IEstateApi api, StatusResponse status, CommunicationSelection? selection) =>
        CommunicationPresentation.Status(status, Profile(api, "status", selection)) + Attention(api, selection?.Recipient);

    public static string Attention(IEstateApi api, string? recipient)
    {
        try
        {
            AttentionResponse attention = api.AttentionGet(recipient);
            return attention.Items.Count == 0 ? string.Empty : "\nRequired attention (every frequency):\n" +
                string.Join('\n', attention.Items.Select(item => item.Kind + ": " + item.Why + "\n  " + item.AttentionState +
                    (item.AcknowledgedAt is null ? "" : " at " + item.AcknowledgedAt) + "; subject state: " + item.State + "; as of " + item.AsOf + "; " + item.NextAction));
        }
        catch (NotSupportedException) { return CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, recipient)); }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        { return CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, recipient)); }
    }

    public static string Protected(IEstateApi api, string kind, string subject, string original, CommunicationSelection? selection) =>
        CommunicationPresentation.Protected(Profile(api, kind, selection), kind, subject, "Complete source material follows; no recorded content is shortened.", original);
}
