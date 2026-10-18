// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class CommunicationOperations(CommandContext context)
{
    public int CommunicationShow(string root, CommunicationSelection selection, bool preview, bool json) => context.WithApi(root, api =>
    {
        CommunicationProfile profile = api.Communication(selection.Recipient, selection.Assignment, json ? null : selection.Values("status"));
        context.Output.Write(json ? CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(profile, ProtocolJson.Options)) : CommunicationPresentation.Describe(profile) + (preview ? "\nPreview\n" + CommunicationPresentation.Preview(profile) : string.Empty));
        return profile.Unavailable is null && profile.Conflicts.Count == 0 ? Clean : HasErrors;
    });

    public int CommunicationSet(string root, string by, string scope, CommunicationSelection selection, CommunicationValues changes, bool reset, string? expected, string? authority, bool json, string? basis = null) => context.WithApi(root, api =>
    {
        scope = selection.Assignment is null ? scope : "assignment";
        string? recipient = scope == "estate" ? null : selection.Recipient ?? by;
        CommunicationProfile profile = api.Communication(recipient, selection.Assignment);
        if (profile.Unavailable is not null || profile.Conflicts.Count > 0)
        {
            return context.Fail(profile.Unavailable ?? "Resolve the communication conflict before editing this profile.");
        }

        CommunicationLayer? prior = profile.Revisions.SingleOrDefault(r => r.Current && r.Scope == scope);
        CommunicationValues previous = prior?.Values ?? new();
        static string? Changed(string? supplied, string? existing) => supplied == "inherit" ? null : supplied ?? existing;
        CommunicationValues values = reset ? new() : new(Changed(changes.Progress, previous.Progress), Changed(changes.Status, previous.Status),
            Changed(changes.Decision, previous.Decision), Changed(changes.Result, previous.Result), Changed(changes.Handoff, previous.Handoff));
        string? priorHash = expected == "absent" ? null : expected ?? prior?.Hash;
        return context.ReportOutcome(api.CommunicationSet(new CommunicationSetRequest(by, scope, values, recipient, selection.Assignment, priorHash, authority, basis)), json);
    });

    public int CommunicationDelegate(string root, string by, string grantee, string? assignment, bool revoke, string? expected, string? expires, bool json) => context.WithApi(root, api =>
        context.ReportOutcome(api.CommunicationDelegate(new CommunicationDelegateRequest(by, by, grantee, assignment is null ? "person" : "assignment", assignment, revoke ? "revoked" : "active", expected, expires)), json));
}
