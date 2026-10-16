// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;

namespace Vericairn.Cli;

internal sealed record CommunicationSelection(string? Recipient = null, string? Assignment = null, string? Depth = null)
{
    public CommunicationValues Values(string kind) => new(Status: kind == "status" ? Depth : null, Decision: kind == "decision" ? Depth : null,
        Result: kind == "result" ? Depth : null, Handoff: kind == "handoff" ? Depth : null);
}

internal static partial class Commands
{
    public static int RecordShow(string root, string id) => WithApi(root, api =>
    {
        RecordResponse? record = api.Record(id);
        if (record is null) { return Fail("No record has id " + id + "."); }
        Console.Out.Write(CanonicalJson.SerializePretty(record.Envelope));
        return Clean;
    });

    public static int CommunicationShow(string root, CommunicationSelection selection, bool preview, bool json) => WithApi(root, api =>
    {
        CommunicationProfile profile = api.Communication(selection.Recipient, selection.Assignment, selection.Values("status"));
        Console.Out.Write(json ? CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(profile, ProtocolJson.Options)) : CommunicationPresentation.Describe(profile) + (preview ? "\nPreview\n" + CommunicationPresentation.Preview(profile) : string.Empty));
        return profile.Unavailable is null && profile.Conflicts.Count == 0 ? Clean : HasErrors;
    });

    public static int CommunicationSet(string root, string by, string scope, CommunicationSelection selection, CommunicationValues changes, bool reset, string? expected, string? authority, bool json, string? basis = null) => WithApi(root, api =>
    {
        scope = selection.Assignment is null ? scope : "assignment";
        string? recipient = scope == "estate" ? null : selection.Recipient ?? by;
        CommunicationProfile profile = api.Communication(recipient, selection.Assignment);
        if (profile.Unavailable is not null || profile.Conflicts.Count > 0)
        {
            return Fail(profile.Unavailable ?? "Resolve the communication conflict before editing this profile.");
        }

        CommunicationLayer? prior = profile.Revisions.SingleOrDefault(r => r.Current && r.Scope == scope);
        CommunicationValues previous = prior?.Values ?? new();
        static string? Changed(string? supplied, string? existing) => supplied == "inherit" ? null : supplied ?? existing;
        CommunicationValues values = reset ? new() : new(Changed(changes.Progress, previous.Progress), Changed(changes.Status, previous.Status),
            Changed(changes.Decision, previous.Decision), Changed(changes.Result, previous.Result), Changed(changes.Handoff, previous.Handoff));
        string? priorHash = expected == "absent" ? null : expected ?? prior?.Hash;
        return ReportOutcome(api.CommunicationSet(new CommunicationSetRequest(by, scope, values, recipient, selection.Assignment, priorHash, authority, basis)), json);
    });

    public static int CommunicationDelegate(string root, string by, string grantee, string? assignment, bool revoke, string? expected, string? expires, bool json) => WithApi(root, api =>
        ReportOutcome(api.CommunicationDelegate(new CommunicationDelegateRequest(by, by, grantee, assignment is null ? "person" : "assignment", assignment, revoke ? "revoked" : "active", expected, expires)), json));

    private static CommunicationProfile Profile(IEstateApi api, string kind, CommunicationSelection? selection) =>
        api.Communication(selection?.Recipient, selection?.Assignment, selection?.Values(kind));

    private static string StatusPresentation(IEstateApi api, StatusResponse status, CommunicationSelection? selection) =>
        CommunicationPresentation.Status(status, Profile(api, "status", selection)) + CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, selection?.Recipient));

    private static string ProtectedPresentation(IEstateApi api, string kind, string subject, string original, CommunicationSelection? selection) =>
        CommunicationPresentation.Protected(Profile(api, kind, selection), kind, subject, "Complete source material follows; no recorded content is shortened.", original);
}
