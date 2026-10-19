// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PoliciesRetireCommand : Command
{
    public PoliciesRetireCommand(CommandContext context)
        : base("retire", "Prepare and validate the exact policy change, then sign as the acting principal.")
    {
        var key = new Argument<string>("key");
        Arguments.Add(key);
        var options = new PolicyWriteOptions(this, true);
        var operations = new PolicyWriteOperations(context);
        SetAction(p => operations.Retire(p.GetValue(context.EstateOption)!, p.GetValue(key)!, p.GetValue(options.Expected)!, p.GetValue(options.By)!, p.GetValue(options.Body)!, p.GetValue(context.JsonOption)));
    }
}
