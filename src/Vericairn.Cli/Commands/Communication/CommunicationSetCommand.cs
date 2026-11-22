// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class CommunicationSetCommand : Command
{
    public CommunicationSetCommand(CommandContext context)
        : base("set", "Change the named fields, preserving the others; inherit resets a field.")
    {
        var operations = new CommunicationOperations(context);
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
        var availability = new Option<string?>("--availability") { Description = "From 0.9.4, a person's own scope at record format 3: available, away (with --away-until), or inherit." };
        var awayUntil = new Option<string?>("--away-until") { Description = "From 0.9.4: when an away person is back, a UTC time such as 2026-12-02T09:00:00Z." };
        var awayFrom = new Option<string?>("--away-from") { Description = "From 0.9.4: when an absence begins; at once when omitted." };
        var quietHours = new Option<string?>("--quiet-hours") { Description = "From 0.9.4: a JSON file of quiet hours (zone, windows of days, start and end, and optional breakthrough categories), - for standard input, or inherit." };
        Options.Add(by);
        Options.Add(scope);
        Options.Add(expected);
        Options.Add(authority);
        Options.Add(basis);
        Options.Add(progress);
        Options.Add(status);
        Options.Add(decisions);
        Options.Add(results);
        Options.Add(handoffs);
        Options.Add(availability);
        Options.Add(awayUntil);
        Options.Add(awayFrom);
        Options.Add(quietHours);
        SetAction(p => operations.CommunicationSet(p.GetValue(context.EstateOption)!, p.GetValue(by)!, p.GetValue(scope)!, context.Presentation.Read(p),
            new CommunicationValues(p.GetValue(progress), p.GetValue(status), p.GetValue(decisions), p.GetValue(results), p.GetValue(handoffs)),
            false, p.GetValue(expected), p.GetValue(authority), p.GetValue(context.JsonOption), p.GetValue(basis),
            new CommunicationOperations.PeopleChanges(p.GetValue(availability), p.GetValue(awayUntil), p.GetValue(awayFrom), p.GetValue(quietHours))));
    }
}
