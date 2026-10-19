// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PolicyWriteOptions
{
    internal Option<string> By { get; } = new("--by") { Description = "Acting principal id.", Required = true };
    internal Option<string> Body { get; } = new("--body") { Description = "Prepared JSON body file, or '-' for standard input.", Required = true };
    internal Option<string> Expected { get; } = new("--expected") { Description = "Expected current target hash.", Required = true };
    internal PolicyWriteOptions(Command command, bool expected)
    {
        command.Options.Add(By);
        command.Options.Add(Body);
        if (expected) command.Options.Add(Expected);
    }
}
