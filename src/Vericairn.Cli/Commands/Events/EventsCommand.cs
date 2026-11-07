// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EventsCommand : Command
{
    public EventsCommand(CommandContext context)
        : base("events", "Follow the estate's events: hints that a record was written or a derived state changed, carrying identities and states but no content. An event is never authority; read its subject before acting.")
    {
        Subcommands.Add(new EventsWatchCommand(context));
    }
}
