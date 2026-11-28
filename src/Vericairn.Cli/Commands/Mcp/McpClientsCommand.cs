// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpClientsCommand : Command
{
    internal McpClientsCommand(CommandContext context)
        : base("clients", "List MCP token devices, their admission standing and local authorizations.")
    {
        SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            DeviceRow[] devices = api.Devices().Where(d => d.Profiles?.Contains("token", StringComparer.Ordinal) == true).ToArray();
            string directory = Path.GetDirectoryName(DaemonHost.EndpointFilePath(parse.GetValue(context.EstateOption)!))!;
            IReadOnlyDictionary<string, McpTokenGrant> grants = devices.Length == 0
                ? new Dictionary<string, McpTokenGrant>() : new McpTokenStore(directory, TimeProvider.System).ClientGrants();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (parse.GetValue(context.JsonOption))
            {
                var rows = new JsonArray();
                foreach (DeviceRow device in devices)
                {
                    JsonObject row = JsonSerializer.SerializeToNode(device, ProtocolJson.Options)!.AsObject();
                    grants.TryGetValue(device.Id, out McpTokenGrant? grant);
                    row["authorization"] = JsonSerializer.SerializeToNode(Authorization(device, grant, now), ProtocolJson.Options);
                    rows.Add(row);
                }
                context.Output.WriteLine(rows.ToJsonString(ProtocolJson.Options));
            }
            else if (devices.Length == 0)
            {
                context.Output.WriteLine("No MCP token devices.");
            }
            else
            {
                foreach (DeviceRow device in devices)
                {
                    grants.TryGetValue(device.Id, out McpTokenGrant? grant);
                    ClientAuthorization authorization = Authorization(device, grant, now);
                    string detail = authorization.ClientId is string id
                        ? $"  {authorization.ClientName ?? id}  {authorization.Scope}  expires {authorization.ExpiresAt}"
                        : string.Empty;
                    context.Output.WriteLine($"{device.Id}  {device.Admission}  {device.PrincipalName ?? device.Principal}  {authorization.State}{detail}");
                }
            }

            return Clean;
        }));
    }

    private static ClientAuthorization Authorization(DeviceRow device, McpTokenGrant? grant, DateTimeOffset now)
    {
        if (grant is null)
            return new("no local token family", null, null, null, null);

        string state = device.Admission != "admitted" ? "inactive" : grant.ExpiresAt <= now ? "expired" : "authorized";
        return new(state, grant.Client, grant.ClientName, grant.Scope, RecordParser.FormatTimestamp(grant.ExpiresAt));
    }

    private sealed record ClientAuthorization(string State, string? ClientId, string? ClientName, string? Scope, string? ExpiresAt);
}
