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
        CommunicationPresentation.Status(status, Profile(api, "status", selection)) + CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, selection?.Recipient));

    public static string Protected(IEstateApi api, string kind, string subject, string original, CommunicationSelection? selection) =>
        CommunicationPresentation.Protected(Profile(api, kind, selection), kind, subject, "Complete source material follows; no recorded content is shortened.", original);
}
