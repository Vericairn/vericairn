// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class EstateFirstAuthorityCommand : Command
{
    public EstateFirstAuthorityCommand(CommandContext context)
        : base("first-authority", "List the self-admissions that could be the estate's first authority, with the evidence for each, or name one of the root operator's own devices in the manifest's firstAuthority (0.9.3). Without --device or --admission it lists and writes nothing. With one, --check shows the change and its plan hash, writing nothing; --expect-plan with that hash and --by the root operator writes it. Refused where witnessed history settles the first authority, where the estate's first authority already works, for a device that is no candidate, for a device-signed request and for anyone but the root operator. Every other self-admission still reads signature.self-admission; removing those records is yours.")
    {
        var operations = new EstateOperations(context);
        var deviceOption = new Option<string?>("--device") { Description = "A device of the root operator's whose self-admission to name: its id, or the id of any revision of its line." };
        var admissionOption = new Option<string?>("--admission") { Description = "The self-admission to name, by id; required when the device admitted itself more than once." };
        var checkOption = new Option<bool>("--check") { Description = "Show the change to the manifest and the plan hash --expect-plan names, writing nothing." };
        var planOption = new Option<string?>("--expect-plan") { Description = "The plan hash --check printed: the change is refused unless the estate still reads as that check read it." };
        var byOption = new Option<string?>("--by") { Description = "The root operator the first charter names; required to write." };
        Options.Add(deviceOption);
        Options.Add(admissionOption);
        Options.Add(checkOption);
        Options.Add(planOption);
        Options.Add(byOption);
        SetAction(parseResult => operations.FirstAuthority(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(deviceOption), parseResult.GetValue(admissionOption),
            parseResult.GetValue(checkOption), parseResult.GetValue(planOption), parseResult.GetValue(byOption), parseResult.GetValue(context.JsonOption)));
    }
}
