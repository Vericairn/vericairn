// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerQualifyCommand : Command
{
    public WorkerQualifyCommand(CommandContext context)
        : base("qualify", "Record an authorized human's qualification of one exact installed harness version from local probe digests: version, help-confirmed flags, closed tools, isolated configuration, login boundary and effective MCP. It launches no agent, establishes no live inference or work acceptance, and grants no version range. Admission allows the shipped baseline or a current qualification whose repeated local probes have identical digests; a superseding record revokes the qualification. The existing inventory policy still applies. Remote is not built.")
    {
        var operations = new WorkerOperations(context);
        var qualifyLauncher = new Option<string>("--launcher") { Description = "The native launcher: codex or claude-code.", Required = true };
        var qualifyBy = new Option<string>("--by") { Description = "A human operator or human explicitly granted the qualify scope; never an agent.", Required = true };
        var qualifySettings = new Option<string[]>("--setting") { Description = "Local launcher setting as name=value, including command=<executable or JSON array>; repeatable. Values are never recorded.", AllowMultipleArgumentsPerToken = false };
        Options.Add(qualifyLauncher);
        Options.Add(qualifyBy);
        Options.Add(qualifySettings);
        SetAction(parseResult => operations.WorkerQualify(parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(qualifyLauncher)!, parseResult.GetValue(qualifyBy)!, parseResult.GetValue(qualifySettings) ?? [], parseResult.GetValue(context.JsonOption)));
    }
}
