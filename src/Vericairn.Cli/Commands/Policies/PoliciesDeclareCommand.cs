// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesDeclareCommand : Command
{
    public PoliciesDeclareCommand(CommandContext context)
        : base("declare", "Prepare and validate the exact policy change, then sign as the acting principal.")
    {
        var options = new PolicyWriteOptions(this, false);
        var operations = new PolicyWriteOperations(context);
        SetAction(p => operations.Declare(p.GetValue(context.EstateOption)!, p.GetValue(options.By)!, p.GetValue(options.Body)!, p.GetValue(context.JsonOption)));
    }
}
