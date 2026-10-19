// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ReleaseBasisCommand : Command
{
    public ReleaseBasisCommand(CommandContext context) : base("basis", "Read a retained release basis by its evidence hash; grants no new submission authority.")
    {
        var hash = new Argument<string>("hash") { Description = "Retained release-basis object hash from operation evidence or a release response." };
        Arguments.Add(hash);
        SetAction(parse => Execute(context, parse.GetValue(context.EstateOption)!, parse.GetValue(hash)!, parse.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string hash, bool json) => context.WithApi(root, api =>
    {
        ReleaseBasisResponse? retained = api.ReleaseBasis(hash);
        if (retained is null) return context.Fail("No supported retained release basis has this hash.");
        if (json) context.Output.WriteLine(JsonSerializer.Serialize(retained, ProtocolJson.Options));
        else
        {
            context.Output.WriteLine("Retained release evidence " + retained.Hash);
            context.Output.Write(PolicyPresentation.Render(retained.Evidence.Policies));
            foreach (string comparison in retained.Evidence.Documents) context.Output.WriteLine("  " + comparison);
        }
        return CommandContext.Clean;
    });
}
