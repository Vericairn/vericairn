// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateCheckCommand : Command
{
    public EstateCheckCommand(CommandContext context)
        : base("check", "Validate records, run repository checks and display policy, duty and writing-exception findings with their recorded-time basis. Policy holds are warnings here. Warn when the manifest pins a release below a newer published release known from this estate's release records or the machine-local cache; use vericairn estate upgrade to review and apply an upgrade. This command makes no release-check request, and a newer-release warning never changes its exit code to an error.")
    {
        var operations = new EstateOperations(context);
        SetAction(parseResult => operations.Check(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }
}
