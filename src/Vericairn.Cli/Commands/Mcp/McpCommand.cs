// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class McpCommand : Command
{
    public McpCommand(CommandContext context)
        : base("mcp", "The Model Context Protocol server: serve the estate to a coding agent over standard input and output, or generate the agent orientation files.")
    {
        Subcommands.Add(new McpServeCommand(context));
        Subcommands.Add(new McpOrientationCommand(context));
    }
}
