// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkTakeoverCommand : Command
{
    public WorkTakeoverCommand(CommandContext context)
        : base("takeover", "Take over a step an agent holds: the hand-over is a new assignment of the step to a person, carrying the agent's brief and naming the agent's assignment, which holds the step from then on; every run of the agent's assignment still in flight is asked to stop, and its later attempts, progress claims and results are refused. From 0.9.4 a person may hand it to any principal the roster names for its role, citing the lapse ask (--basis). A person records it, the one taking the step over, the sponsor of its goal or an operator, never an agent.")
    {
        var operations = new WorkOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var assignmentArgument = new Argument<string>("assignment") { Description = "Assignment record id." };
        var takenOption = new Option<string?>("--contributor") { Description = "Who takes the step over: a person, or from 0.9.4 any principal the roster names for its role with --basis; the recording principal when omitted." };
        var takeoverNoteOption = new Option<string?>("--note") { Description = "Why; a sentence naming the hand-over when omitted." };
        var basisOption = new Option<string?>("--basis") { Description = "From 0.9.4: the lapse ask about the agent's assignment that the hand-over answers, recorded as its basis; required to hand the step to anyone but a person." };
        Arguments.Add(assignmentArgument);
        Options.Add(byOption);
        Options.Add(takenOption);
        Options.Add(takeoverNoteOption);
        Options.Add(basisOption);
        SetAction(parseResult => operations.WorkTakeover(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(assignmentArgument)!, parseResult.GetValue(byOption)!, parseResult.GetValue(takenOption), parseResult.GetValue(takeoverNoteOption), parseResult.GetValue(context.JsonOption), parseResult.GetValue(basisOption)));
    }
}
