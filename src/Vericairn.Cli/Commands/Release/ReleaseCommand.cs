// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class ReleaseCommand : Command
{
    public ReleaseCommand(CommandContext context)
        : base("release", "Releases: list the records and the providers, submit a release through the provider its product declares or the request names as intent before effect, observe the run, the draft and the publication.")
    {
        Subcommands.Add(new ReleaseListCommand(context));
        Subcommands.Add(new ReleaseProvidersCommand(context));
        Subcommands.Add(new ReleaseSubmitCommand(context));
        Subcommands.Add(new ReleaseBasisCommand(context));
        Subcommands.Add(new ReleaseWithdrawCommand(context));
        Subcommands.Add(new ReleaseObserveCommand(context));
    }
}
