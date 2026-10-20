// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class VericairnCommand : RootCommand
{
    public VericairnCommand(CommandContext context)
        : base("Vericairn: records, estates and the tools that keep them honest.")
    {
        Options.Add(context.EstateOption);
        Options.Add(context.JsonOption);
        Options.Add(context.McpPrincipalOption);
        Options.Add(context.Presentation.Recipient);
        Options.Add(context.Presentation.Assignment);
        Options.Add(context.Presentation.Depth);
    }
}
