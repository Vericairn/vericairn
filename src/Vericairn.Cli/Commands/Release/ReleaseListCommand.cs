// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ReleaseListCommand : Command
{
    public ReleaseListCommand(CommandContext context)
        : base("list", "List current release records: version, status, commit, id, signing.")
    {
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<ReleaseRow> rows = api.Releases();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
        }
        else
        {
            foreach (ReleaseRow row in rows)
            {
                context.Output.WriteLine($"{row.Version}  {row.Status}  {row.Commit}  {row.Id}  {row.Signing}");
            }
        }

        return Clean;
    });
}
