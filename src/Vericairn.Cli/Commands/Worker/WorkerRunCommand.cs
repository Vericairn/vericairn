// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerRunCommand : Command
{
    public WorkerRunCommand(CommandContext context)
        : base("run", "Run a coding agent on an assignment: refused while the goal is not active or its budget is spent, while a device of an operator or the goal's sponsor signs here without presence and the sponsor recorded no custody exception in force for the goal, or when the launcher cannot run; otherwise the workspace is prepared, the intent recorded with the cap, the harness launched with the brief and the MCP server, observed, and its usage recorded once it exits.")
    {
        var operations = new WorkerOperations(context);
        var assignmentArgument = new Argument<string>("assignment") { Description = "The assignment the agent carries, whose contributor it is." };
        var launcherOption = new Option<string>("--launcher") { Description = "The launcher: claude-code, codex or generic (generic process launch is not complete agent support).", Required = true };
        var agentOption = new Option<string>("--agent") { Description = "The agent principal the harness acts for: the assignment's contributor, with an admitted device whose key this machine holds.", Required = true };
        var byOption = new Option<string>("--by") { Description = "The principal recording the run: the operator, the goal's sponsor when the sponsor is a person, or a principal with the local-engineering scope.", Required = true };
        var repositoryOption = new Option<string>("--repository") { Description = "The repository resource whose clone, registered on this machine (vericairn sources clone), the workspace is made from.", Required = true };
        var goalOption = new Option<string?>("--goal") { Description = "The goal the run is charged to, when the work belongs to more than one active goal." };
        var branchOption = new Option<string?>("--branch") { Description = "The work branch; work/ and the work order's key when omitted. A later run of the branch continues in its workspace." };
        var baseOption = new Option<string?>("--base") { Description = "What a new work branch starts from; the clone's HEAD when omitted." };
        var wallMinutesOption = new Option<long?>("--wall-minutes") { Description = "The run's cap in wall minutes; what remains of the goal's budget when omitted, and never more." };
        var settingOption = new Option<string[]>("--setting") { Description = "A launcher setting as name=value, for example command=<executable or JSON array>; repeatable.", AllowMultipleArgumentsPerToken = false };
        var detachOption = new Option<bool>("--detach") { Description = "Return once the harness is launched, leaving the run to the daemon that serves the estate; without it the command waits until the run settles, and Ctrl+C asks it to stop." };
        Arguments.Add(assignmentArgument);
        Options.Add(launcherOption);
        Options.Add(agentOption);
        Options.Add(byOption);
        Options.Add(repositoryOption);
        Options.Add(goalOption);
        Options.Add(branchOption);
        Options.Add(baseOption);
        Options.Add(wallMinutesOption);
        Options.Add(settingOption);
        Options.Add(detachOption);
        var executionOptions = new ExecutionOptions(this);
        SetAction(parseResult => operations.WorkerRun(
            parseResult.GetValue(context.EstateOption)!,
            new WorkerRunOptions(
                parseResult.GetValue(assignmentArgument)!,
                parseResult.GetValue(launcherOption)!,
                parseResult.GetValue(agentOption)!,
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(repositoryOption)!,
                parseResult.GetValue(goalOption),
                parseResult.GetValue(branchOption),
                parseResult.GetValue(baseOption),
                parseResult.GetValue(wallMinutesOption),
                parseResult.GetValue(settingOption) ?? [],
                parseResult.GetValue(detachOption), executionOptions.Read(parseResult)),
            parseResult.GetValue(context.JsonOption)));
    }
}
