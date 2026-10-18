// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ObjectsVerifyCommand : Command
{
    public ObjectsVerifyCommand(CommandContext context)
        : base("verify", "Read every object and check its bytes against its name.")
    {
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, bool json) => context.WithApi(root, api => context.ReportFindings(api.ObjectsVerify(), json));
}
