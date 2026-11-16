// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal static class ChangesOutput
{
    internal static int Write(CommandContext context, ChangeResponse response, bool json)
    {
        if (json) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        else
        {
            foreach (RecordWritten? record in new[] { response.Operation, response.Observation, response.Ask })
                if (record is not null) context.Output.WriteLine($"{record.Path}  {record.Id}  {record.Hash}");
            // From 0.9.4 (CODE-80) a problem the runtime reports is shown as its own words, marked and escaped.
            if (response.Ok) context.Output.WriteLine(response.Message);
            else CommandContext.WriteRuntime(context.Output, response.Message);
        }
        return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
    }
}
