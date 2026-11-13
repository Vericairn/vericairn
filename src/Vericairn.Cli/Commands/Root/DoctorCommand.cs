// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Sdk;

namespace Vericairn.Cli;

internal sealed class DoctorCommand : Command
{
    public DoctorCommand(CommandContext context, ReleaseUpdates updates)
        : base("doctor", "Check this machine's prerequisites. With --json, writes one JSON object with the report's facts under stable names: version, runtime, git, dotnet, estate, custody, enclave, records, search and daemon, each an object of state (present, missing, unavailable or not-applicable), value and detail, and stateWarnings, the per-user state whose permissions cannot be restricted. " + UpdateNotices.Help)
    {
        var operations = new MachineDiagnostics(context);
        SetAction(parseResult => UpdateNotices.Run(context, updates, () => operations.Doctor(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)), parseResult.GetValue(context.EstateOption)!));
    }
}
