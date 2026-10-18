// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Sdk;

namespace Vericairn.Cli;

internal sealed class DoctorCommand : Command
{
    public DoctorCommand(CommandContext context, ReleaseUpdates updates)
        : base("doctor", "Check this machine's prerequisites. " + UpdateNotices.Help)
    {
        var operations = new MachineDiagnostics(context);
        SetAction(parseResult => UpdateNotices.Run(context, updates, () => operations.Doctor(parseResult.GetValue(context.EstateOption)!)));
    }
}
