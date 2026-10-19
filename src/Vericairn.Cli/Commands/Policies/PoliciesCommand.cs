// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesCommand : Command
{
    public PoliciesCommand(CommandContext context)
        : base("policies", "Inspect and manage estate policy declarations and their evidence.")
    {
        Subcommands.Add(new PoliciesDeclareCommand(context));
        Subcommands.Add(new PoliciesReviseCommand(context));
        Subcommands.Add(new PoliciesRetireCommand(context));
        Subcommands.Add(new PoliciesExceptCommand(context));
        Subcommands.Add(new PoliciesRevokeExceptionCommand(context));
        Subcommands.Add(new PoliciesListCommand(context));
        Subcommands.Add(new PoliciesShowCommand(context));
        Subcommands.Add(new PoliciesKindsCommand(context));
        Subcommands.Add(new PoliciesCheckCommand(context));
    }
}
