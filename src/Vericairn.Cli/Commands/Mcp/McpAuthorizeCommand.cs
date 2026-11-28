// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class McpAuthorizeCommand : Command
{
    internal McpAuthorizeCommand(CommandContext context)
        : base("authorize", "Issue a one-time approval for an OAuth MCP client from the runtime's machine.")
    {
        var principal = new Option<string?>("--principal") { Description = "The agent or service principal the client will act for." };
        var by = new Option<string>("--by") { Required = true, Description = "The issuing administrator's principal." };
        var scope = new Option<string>("--scope") { Required = true, Description = "The maximum tool scope for this approval, read or contribute." };
        var days = new Option<int>("--days") { DefaultValueFactory = _ => 30, Description = "How many days the authorization lasts, from 1 to 90." };
        var renew = new Option<string?>("--renew") { Description = "Renew an admitted token device instead of creating one." };
        Options.Add(principal);
        Options.Add(by);
        Options.Add(scope);
        Options.Add(days);
        Options.Add(renew);
        SetAction(parse => context.WithDaemon(parse.GetValue(context.EstateOption)!, "MCP approval", client =>
        {
            var request = new McpAuthorizeRequest(parse.GetValue(principal), parse.GetValue(by)!, parse.GetValue(scope)!, parse.GetValue(days), parse.GetValue(renew));
            (McpApprovalIssued? issued, Problem? problem) = client.IssueMcpApprovalAsync(request, context.CancellationToken).GetAwaiter().GetResult();
            if (issued is null) return context.Fail(problem?.Message ?? "No MCP approval code was issued.");

            if (parse.GetValue(context.JsonOption))
            {
                context.Output.WriteLine(JsonSerializer.Serialize(issued, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"approval code  {issued.Code}");
                context.Output.WriteLine($"  endpoint  {issued.Endpoint}");
                context.Output.WriteLine($"  principal  {issued.Principal}, scope {issued.Scope}");
                context.Output.WriteLine($"  valid until {issued.ExpiresAt}, for one client and at most {issued.Attempts} guesses");
            }

            return Clean;
        }));
    }
}
