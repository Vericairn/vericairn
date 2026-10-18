// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class AskCommand : Command
{
    public AskCommand(CommandContext context)
        : base("ask", "Show what waits on a principal, or on every principal: the asks still open or notified, at most three in one delivery with the recommendation first and the consequence of leaving each unanswered, the rest waiting in order. Reading them answers nothing; an ask is answered by a decision of the principal it is put to that names it in its answers.")
    {
        var operations = new EstateViews(context);
        var toOption = new Option<string?>("--to") { Description = "The principal asked (ULID); every principal when omitted." };
        Options.Add(toOption);
        SetAction(parseResult => operations.Ask(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(toOption), parseResult.GetValue(context.JsonOption)));
    }
}
