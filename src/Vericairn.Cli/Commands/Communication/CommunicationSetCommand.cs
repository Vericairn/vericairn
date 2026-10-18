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
        SetAction(p => operations.CommunicationSet(p.GetValue(context.EstateOption)!, p.GetValue(by)!, p.GetValue(scope)!, context.Presentation.Read(p),
            new CommunicationValues(p.GetValue(progress), p.GetValue(status), p.GetValue(decisions), p.GetValue(results), p.GetValue(handoffs)),
            false, p.GetValue(expected), p.GetValue(authority), p.GetValue(context.JsonOption), p.GetValue(basis)));
    }
}
