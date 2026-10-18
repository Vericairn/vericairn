// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ContextCatalogueCommand : Command
{
    public ContextCatalogueCommand(CommandContext context)
        : base("catalogue", "List the current document revision catalogue without selecting or reading its bodies.")
    {
        SetAction(result => context.WithApi(result.GetValue(context.EstateOption)!, api =>
        {
            var rows = api.Records("document-revision").Select(r => new { r.Id, r.Hash, Path = r.Envelope.GetProperty("body").GetProperty("path").GetString() }).ToList();
            if (result.GetValue(context.JsonOption))
            {
                context.Output.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            }
            else
            {
                foreach (var row in rows)
                {
                    context.Output.WriteLine($"{row.Path}  revision {row.Id}  {row.Hash}");
                }
            }

            return CommandContext.Clean;
        }));
    }
}
