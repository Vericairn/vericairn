// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Portability;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class RecoverCompareCommand : Command
{
    public RecoverCompareCommand(CommandContext context)
        : base("compare", "Records only in this copy, only in the other, or (never expected) differing in content. With --json, one object: thisEstate, otherEstate, sameEstate, and onlyHere, onlyThere and differing, each a list of type and id.")
    {
        var withOption = new Option<string>("--with") { Description = "Root of the other copy.", Required = true };
        Options.Add(withOption);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(withOption)!, parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string other, bool json)
    {
        try
        {
            EstateModel here = CommandContext.LoadCopy(root), there = CommandContext.LoadCopy(other);
            if (json)
            {
                (string? hereEstate, string? thereEstate, List<string> onlyHere, List<string> onlyThere, List<string> differing) = Recovery.Differences(here, there);
                context.Output.WriteLine(JsonSerializer.Serialize(new
                {
                    thisEstate = hereEstate,
                    otherEstate = thereEstate,
                    sameEstate = hereEstate == thereEstate,
                    onlyHere = onlyHere.Select(id => new { type = here.ById[id].Type, id }),
                    onlyThere = onlyThere.Select(id => new { type = there.ById[id].Type, id }),
                    differing = differing.Select(id => new { type = here.ById[id].Type, id }),
                }, ProtocolJson.Options));
                return Clean;
            }

            context.Output.Write(Recovery.Compare(here, "this copy", there, "the other copy"));
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }
}
