// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class SupportCommand : Command
{
    public SupportCommand(CommandContext context)
        : base("support", "Gather what support needs, redacted and shown in full before you share it; sharing is always your own act.")
    {
        Subcommands.Add(new SupportBundleCommand(context));
    }
}
