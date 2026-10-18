// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ObjectsListCommand : Command
{
    public ObjectsListCommand(CommandContext context)
        : base("list", "List held objects.")
    {
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<ObjectRow> rows = api.Objects();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows.Select(r => r.Hash)));
        }
        else
        {
            foreach (ObjectRow row in rows)
            {
                context.Output.WriteLine(row.Hash);
            }
        }

        return Clean;
    });
}
