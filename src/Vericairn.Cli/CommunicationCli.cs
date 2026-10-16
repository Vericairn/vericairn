// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal static partial class CliApp
{
    private sealed class PresentationOptions
    {
        public Option<string?> Recipient { get; } = new("--recipient") { Description = "The person addressed by human presentation (ULID); omitted means estate defaults, never an inferred person.", Recursive = true };
        public Option<string?> Assignment { get; } = new("--for-assignment") { Description = "Exact assignment for the recipient's communication override.", Recursive = true };
        public Option<string?> Depth { get; } = new("--depth") { Description = "One-view depth: short, standard or comprehensive. Canonical --json is independent of depth.", Recursive = true };
        public CommunicationSelection Read(ParseResult result) => new(result.GetValue(Recipient), result.GetValue(Assignment), result.GetValue(Depth));
    }

    private static Command CommunicationFamily(Option<string> estate, Option<bool> json, PresentationOptions presentation)
    {
        var family = new Command("communication", "Inspect or change attributable personal communication settings. Remote is not built.");
        var show = new Command("show", "Show effective values, contributing revisions, conflicts and freshness.");
        var preview = new Option<bool>("--preview") { Description = "Show the shared status, decision, result and handoff examples." };
        show.Options.Add(preview);
        show.SetAction(p => Commands.CommunicationShow(p.GetValue(estate)!, presentation.Read(p), p.GetValue(preview), p.GetValue(json)));
        family.Subcommands.Add(show);

        foreach (string name in new[] { "set", "reset" })
        {
            var command = new Command(name, name == "set" ? "Change the named fields, preserving the others; inherit resets a field." : "Reset this complete scope to inheritance as a new record.");
            var by = new Option<string>("--as") { Description = "Actual author; defaults to their own setting unless --recipient names another person.", Required = true };
            var scope = new Option<string>("--scope") { Description = "person, estate, or assignment (also selected by --for-assignment).", DefaultValueFactory = _ => "person" };
            var expected = new Option<string?>("--expected") { Description = "Expected prior revision hash, or absent to require no existing profile. Without it the command reads the current revision before applying the named fields." };
            var authority = new Option<string?>("--authority") { Description = "Explicit communication-delegation record for a delegated change." };
            var basis = new Option<string?>("--basis") { Description = "Exact decision being explicitly adopted; does not grant delegated authority." };
            var progress = new Option<string?>("--progress") { Description = "none, completion-and-need, milestones, verbose, or inherit." };
            var status = new Option<string?>("--status") { Description = "short, standard, comprehensive, or inherit." };
            var decisions = new Option<string?>("--decisions") { Description = "Decision lead depth, or inherit." };
            var results = new Option<string?>("--results") { Description = "Result explanation depth, or inherit." };
            var handoffs = new Option<string?>("--handoffs") { Description = "Handoff introduction depth, or inherit." };
            foreach (Option option in new Option[] { by, scope, expected, authority, basis, progress, status, decisions, results, handoffs }) { command.Options.Add(option); }
            bool reset = name == "reset";
            command.SetAction(p => Commands.CommunicationSet(p.GetValue(estate)!, p.GetValue(by)!, p.GetValue(scope)!, presentation.Read(p),
                new CommunicationValues(p.GetValue(progress), p.GetValue(status), p.GetValue(decisions), p.GetValue(results), p.GetValue(handoffs)),
                reset, p.GetValue(expected), p.GetValue(authority), p.GetValue(json), p.GetValue(basis)));
            family.Subcommands.Add(command);
        }

        var grant = new Command("delegate", "Explicitly grant or revoke changes to one's own communication setting; grants no other permission.");
        var owner = new Option<string>("--as") { Description = "The recipient granting consent.", Required = true };
        var grantee = new Option<string>("--to") { Description = "The delegated principal.", Required = true };
        var revoke = new Option<bool>("--revoke") { Description = "Revoke the current delegation." };
        var prior = new Option<string?>("--expected") { Description = "Expected prior delegation hash; required when revising a grant." };
        var expires = new Option<string?>("--expires") { Description = "Expiry as a UTC record timestamp." };
        foreach (Option option in new Option[] { owner, grantee, revoke, prior, expires }) { grant.Options.Add(option); }
        grant.SetAction(p => Commands.CommunicationDelegate(p.GetValue(estate)!, p.GetValue(owner)!, p.GetValue(grantee)!, p.GetValue(presentation.Assignment), p.GetValue(revoke), p.GetValue(prior), p.GetValue(expires), p.GetValue(json)));
        family.Subcommands.Add(grant);
        return family;
    }
}
