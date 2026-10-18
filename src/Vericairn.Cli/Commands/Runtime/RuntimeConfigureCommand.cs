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
        Options.Add(localNetworkOption);
        Options.Add(allowPublicOption);
        SetAction(parseResult => operations.RuntimeConfigure(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(localNetworkOption), parseResult.GetValue(allowPublicOption), parseResult.GetValue(context.JsonOption)));
    }
}
