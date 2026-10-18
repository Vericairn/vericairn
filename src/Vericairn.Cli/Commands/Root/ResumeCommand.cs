// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ResumeCommand : Command
{
    public ResumeCommand(CommandContext context)
        : base("resume", "Show every record, pin and observed-state change since a retained hand-over. The world may have moved; this restarts nothing.")
    {
        var operations = new HandoffFiles(context);
        var resumePackage = new Argument<string>("package") { Description = "The retained hand-over JSON file, or - for standard input." };
        Arguments.Add(resumePackage);
        SetAction(parseResult => operations.Resume(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(resumePackage)!, parseResult.GetValue(context.JsonOption)));
    }
}
