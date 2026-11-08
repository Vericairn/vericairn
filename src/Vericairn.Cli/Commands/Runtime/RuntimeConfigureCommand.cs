// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RuntimeConfigureCommand : Command
{
    public RuntimeConfigureCommand(CommandContext context)
        : base("configure", "Configure the runtime of this estate on this machine; without options, show the configuration. Nothing listens beyond the local socket and loopback unless an address is set.")
    {
        var operations = new RuntimeNetworkConfiguration(context);
        var localNetworkOption = new Option<string?>("--local-network") { Description = "The address and port the local-network profile listens on from the runtime's next start, an IP address of this machine such as 192.168.1.20:7443; 'off' turns it off. Off unless set." };
        var allowPublicOption = new Option<bool>("--allow-public") { Description = "With --local-network, allow a wildcard address (every interface) or one other networks can reach; such an address is refused without it." };
        var mcpOption = new Option<string?>("--mcp") { Description = "From 0.9.2: 'on' serves network MCP at /mcp on the loopback and local-network listeners from the runtime's next start, to agents' and services' devices whose every request is signed; 'off' stops it. Off unless set." };
        var originOption = new Option<string[]>("--mcp-origin") { Description = "From 0.9.2: an origin (scheme://host[:port]) a request to /mcp may name, repeated for more; replaces the list, and 'none' empties it. A request naming any other origin is refused before authentication; none is allowed unless set.", AllowMultipleArgumentsPerToken = false };
        Options.Add(localNetworkOption);
        Options.Add(allowPublicOption);
        Options.Add(mcpOption);
        Options.Add(originOption);
        SetAction(parseResult => operations.RuntimeConfigure(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(localNetworkOption), parseResult.GetValue(allowPublicOption), parseResult.GetValue(context.JsonOption),
            parseResult.GetValue(mcpOption), parseResult.GetValue(originOption)));
    }
}
