// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkProgressCommand : Command
{
    public WorkProgressCommand(CommandContext context)
        : base("progress", "Submit the contributor's progress claim; only a completed phase writes a record.")
    {
        var operations = new WorkOperations(context);
        var attempt = new Argument<string>("attempt") { Description = "Attempt record id." };
        var by = new Option<string>("--by") { Description = "Contributing principal id.", Required = true };
        var phase = new Option<string>("--phase") { Description = "Stable lower-case phase name.", Required = true };
        var note = new Option<string>("--note") { Description = "The contributor's claim about this phase.", Required = true };
        var state = new Option<string>("--state") { Description = "active or completed.", Required = true };
        state.AcceptOnlyFromAmong("active", "completed");
        var progress = new Option<string?>("--progress") { Description = "Typed value JSON file (accurate, best-effort or not-measurable), or '-' for standard input." };
        var status = new Option<string?>("--status-text") { Description = "Short status sentence; required when the effective progress request says so." };
        Arguments.Add(attempt);
        Options.Add(by);
        Options.Add(phase);
        Options.Add(note);
        Options.Add(state);
        Options.Add(progress);
        Options.Add(status);
        SetAction(result => operations.WorkProgress(result.GetValue(context.EstateOption)!, result.GetValue(attempt)!,
            result.GetValue(by)!, result.GetValue(phase)!, result.GetValue(note)!, result.GetValue(state) == "completed",
            result.GetValue(progress), result.GetValue(status), result.GetValue(context.JsonOption)));
    }
}
