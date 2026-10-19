// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesCheckCommand : Command
{
    public PoliciesCheckCommand(CommandContext context)
        : base("check", "Evaluate every policy and print its basis; an explicit time or release target is a preview.")
    {
        var asOf = new Option<string?>("--as-of") { Description = "Canonical UTC preview time." };
        var release = new Option<string?>("--release") { Description = "Release record id selecting its exact product, version and commit." };
        Options.Add(asOf);
        Options.Add(release);
        var operations = new PolicyOperations(context);
        SetAction(p => operations.Check(p.GetValue(context.EstateOption)!, p.GetValue(asOf), p.GetValue(release), p.GetValue(context.JsonOption)));
    }
}
