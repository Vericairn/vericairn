// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkPlaceCommand : Command
{
    public WorkPlaceCommand(CommandContext context) : base("place", "Prepare and sign a work revision in one current release plan or named backlog.")
    {
        var key = new Argument<string>("key");
        var by = new Option<string>("--by") { Required = true, Description = "Acting principal id." };
        var expected = new Option<string>("--expected") { Required = true, Description = "Expected current work-order hash." };
        var selection = new Option<string>("--expected-selection") { Required = true, Description = "Expected current carrier or placement-policy hash." };
        var release = new Option<string?>("--release-plan") { Description = "Carrier work-order key or id." };
        var backlog = new Option<string?>("--backlog") { Description = "Named backlog in the placement registry." };
        Arguments.Add(key);
        Options.Add(by);
        Options.Add(expected);
        Options.Add(selection);
        Options.Add(release);
        Options.Add(backlog);
        var operations = new PolicyWriteOperations(context);
        SetAction(p => operations.Place(p.GetValue(context.EstateOption)!, p.GetValue(key)!, p.GetValue(by)!, p.GetValue(expected)!, p.GetValue(selection)!, p.GetValue(release), p.GetValue(backlog), p.GetValue(context.JsonOption)));
    }
}
