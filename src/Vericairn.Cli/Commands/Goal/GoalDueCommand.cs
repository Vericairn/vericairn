// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalDueCommand : Command
{
    public GoalDueCommand(CommandContext context)
        : base("due", "From 0.9.4: show a standing agent's occurrences as of an explicit UTC time: its state, each trigger and when it is next due, its period's caps and spend, its concurrency and failure pause, and each occurrence with its key and state (due, deferred with its reason, running, assigned, settled, disposed or held). The same on every machine for the same records and time; it records and starts nothing, and vericairn goal step carries one occurrence per call.")
    {
        var operations = new GoalOperations(context);
        var shownArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omit for the current goal.", Arity = ArgumentArity.ZeroOrOne };
        var asOfOption = new Option<string?>("--as-of") { Description = "The UTC time its triggers are evaluated at, for example 2026-12-01T00:00:00Z; this runtime's clock when omitted." };
        Arguments.Add(shownArgument);
        Options.Add(asOfOption);
        SetAction(parseResult => operations.GoalDue(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(shownArgument), parseResult.GetValue(asOfOption), parseResult.GetValue(context.JsonOption)));
    }
}
