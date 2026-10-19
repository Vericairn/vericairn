// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkResultCommand : Command
{
    public WorkResultCommand(CommandContext context)
        : base("result", "Submit the result of an attempt: outcome, summary, outputs by hash, evidence, checks, limitations, unresolved effects, next step.")
    {
        var operations = new WorkOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var attemptArgument = new Argument<string>("attempt") { Description = "Attempt record id." };
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the body, or '-' for standard input.", Required = true };
        var expectedOption = new Option<string?>("--expected") { Description = "Expected current attempt hash; required when the result carries duties." };
        Options.Add(expectedOption);
        Arguments.Add(attemptArgument);
        Options.Add(byOption);
        Options.Add(bodyOption);
        SetAction(parseResult => operations.WorkResult(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(attemptArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(expectedOption),
            parseResult.GetValue(context.JsonOption)));
    }
}
