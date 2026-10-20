// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesHashCommand : Command
{
    public PoliciesHashCommand(CommandContext context)
        : base("hash", "Print a prepared policy or duty body's definition hash; reads only the body and writes nothing.")
    {
        var body = new Option<string>("--body") { Description = "Prepared policy or duty body JSON file, or '-' for standard input.", Required = true };
        Options.Add(body);
        var operations = new PolicyOperations(context);
        SetAction(p => operations.Hash(p.GetValue(body)!, p.GetValue(context.JsonOption)));
    }
}
