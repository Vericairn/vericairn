// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class DecisionsCommand : Command
{
    public DecisionsCommand(CommandContext context)
        : base("decisions", "List the estate's current decisions, or show one cleanly.")
    {
        var operations = new EstateViews(context);
        var decisionIdArgument = new Argument<string?>("id") { Description = "Decision record id (any revision); omit to list.", Arity = ArgumentArity.ZeroOrOne };
        Arguments.Add(decisionIdArgument);
        SetAction(parseResult => operations.Decisions(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(decisionIdArgument), parseResult.GetValue(context.JsonOption), context.Presentation.Read(parseResult)));
    }
}
