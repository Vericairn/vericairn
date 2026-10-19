// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateWritingExceptionsAddCommand : Command
{
    public EstateWritingExceptionsAddCommand(CommandContext context) : base("add", "Validate and sign one exact writing token entry.")
    {
        var options = new PolicyWriteOptions(this, false);
        var operations = new PolicyWriteOperations(context);
        SetAction(p => operations.WritingAdd(p.GetValue(context.EstateOption)!, p.GetValue(options.By)!, p.GetValue(options.Body)!, p.GetValue(context.JsonOption)));
    }
}
