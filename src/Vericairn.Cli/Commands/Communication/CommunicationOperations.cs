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

    /// <summary>From 0.9.4 (GOAL-11): the availability and quiet hours a change names, as the command line gives them.</summary>
    /// <param name="Availability">available, away or inherit; unchanged when null.</param>
    /// <param name="AwayUntil">For away, when the person is back.</param>
    /// <param name="AwayFrom">For away, when the absence begins.</param>
    /// <param name="QuietHours">A quiet-hours JSON file, - for standard input, or inherit; unchanged when null.</param>
    internal sealed record PeopleChanges(string? Availability, string? AwayUntil, string? AwayFrom, string? QuietHours);

    public int CommunicationSet(string root, string by, string scope, CommunicationSelection selection, CommunicationValues changes, bool reset, string? expected, string? authority, bool json, string? basis = null, PeopleChanges? people = null) => context.WithApi(root, api =>
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
            Changed(changes.Decision, previous.Decision), Changed(changes.Result, previous.Result), Changed(changes.Handoff, previous.Handoff))
        {
            // From 0.9.4 (GOAL-11): a change keeps the availability and quiet hours it does not name.
            Availability = previous.Availability,
            QuietHours = previous.QuietHours,
        };
        if (!reset && people is not null)
        {
            if ((people.AwayUntil is not null || people.AwayFrom is not null) && people.Availability != "away")
            {
                return context.Fail("--away-until and --away-from go with --availability away.");
            }

            if (people.Availability is not (null or "inherit" or "available" or "away"))
            {
                return context.Fail("--availability is available, away or inherit.");
            }

            values = people.Availability switch
            {
                null => values,
                "inherit" => values with { Availability = null },
                "available" => values with { Availability = new CommunicationAvailability("available") },
                _ => values with { Availability = new CommunicationAvailability("away", people.AwayUntil, people.AwayFrom) },
            };
            if (people.QuietHours == "inherit")
            {
                values = values with { QuietHours = null };
            }
            else if (people.QuietHours is string path)
            {
                QuietHoursSetting? quiet;
                try
                {
                    using var read = new MemoryStream();
                    using (Stream source = path == "-" ? Console.OpenStandardInput() : File.OpenRead(path))
                    {
                        source.CopyTo(read);
                    }

                    quiet = JsonSerializer.Deserialize<QuietHoursSetting>(read.ToArray(), ProtocolJson.Options);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    return context.Fail("--quiet-hours names a readable JSON file of quiet hours: " + exception.Message);
                }

                if (quiet is null)
                {
                    return context.Fail("--quiet-hours names a JSON object.");
                }

                values = values with { QuietHours = quiet };
            }
        }

        string? priorHash = expected == "absent" ? null : expected ?? prior?.Hash;
        return context.ReportOutcome(api.CommunicationSet(new CommunicationSetRequest(by, scope, values, recipient, selection.Assignment, priorHash, authority, basis)), json);
    });

    public int CommunicationDelegate(string root, string by, string grantee, string? assignment, bool revoke, string? expected, string? expires, bool json) => context.WithApi(root, api =>
        context.ReportOutcome(api.CommunicationDelegate(new CommunicationDelegateRequest(by, by, grantee, assignment is null ? "person" : "assignment", assignment, revoke ? "revoked" : "active", expected, expires)), json));
}
