// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpClientsCommand : Command
{
    internal McpClientsCommand(CommandContext context)
        : base("clients", "List this estate's recorded MCP token devices and their admission standing.")
    {
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            DeviceRow[] devices = api.Devices().Where(d => d.Profiles?.Contains("token", StringComparer.Ordinal) == true).ToArray();
            if (parse.GetValue(context.JsonOption))
            {
                context.Output.WriteLine(JsonSerializer.Serialize(devices, ProtocolJson.Options));
            }
            else if (devices.Length == 0)
            {
                context.Output.WriteLine("No MCP token devices.");
            }
            else
            {
                foreach (DeviceRow device in devices)
                    context.Output.WriteLine($"{device.Id}  {device.Admission}  {device.PrincipalName ?? device.Principal}");
            }

            return Clean;
        }));
    }
}
