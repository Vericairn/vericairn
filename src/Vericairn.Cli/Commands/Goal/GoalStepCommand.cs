// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalStepCommand : Command
{
    public GoalStepCommand(CommandContext context)
        : base("step", "Take a goal's next step, one at a time: its first eligible step whose route acts, routed from this machine by the goal's roster, for an operator, the goal's sponsor when a person, or a principal holding local-engineering. An agent's step goes on with the assignment that holds it, or is assigned to the agent with the run's intent, after the worker's whole preflight, and its run starts through the worker; every refusal before the intent records nothing, the assignment included, as while a key of an operator or of the sponsor signs here without presence and no custody exception is in force. A person's step is assigned to the person and asked of them, the two recorded together; a step no principal is available for is asked of the sponsor, never given to another contributor. It activates, funds, accepts and answers nothing.")
    {
        var operations = new GoalOperations(context);
        var shownArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omit for the current goal.", Arity = ArgumentArity.ZeroOrOne };
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var repositoryOption = new Option<string?>("--repository") { Description = "For an agent's step, the repository resource whose clone, registered on this machine (vericairn sources clone), its workspace is made from." };
        var settingOption = new Option<string[]>("--setting") { Description = "For an agent's step, a setting of the launcher routing chose, as name=value, for example command=<executable or JSON array>; repeatable.", AllowMultipleArgumentsPerToken = false };
        var wallMinutesOption = new Option<long?>("--wall-minutes") { Description = "For an agent's step, the run's cap in wall minutes; what remains of the goal's budget when omitted, and never more." };
        var branchOption = new Option<string?>("--branch") { Description = "For an agent's step, the work branch; work/ and the work order's key when omitted." };
        var baseOption = new Option<string?>("--base") { Description = "For an agent's step, what a new work branch starts from; the clone's HEAD when omitted." };
        var detachOption = new Option<bool>("--detach") { Description = "Return once an agent's run is launched, leaving it to the daemon that serves the estate; without it the command waits until the run settles, and Ctrl+C asks it to stop." };
        Arguments.Add(shownArgument);
        Options.Add(byOption);
        Options.Add(repositoryOption);
        Options.Add(settingOption);
        Options.Add(wallMinutesOption);
        Options.Add(branchOption);
        Options.Add(baseOption);
        Options.Add(detachOption);
        var executionOptions = new ExecutionOptions(this);
        SetAction(parseResult => operations.GoalStep(
            parseResult.GetValue(context.EstateOption)!,
            new GoalStepOptions(
                parseResult.GetValue(shownArgument),
                parseResult.GetValue(byOption)!,
                parseResult.GetValue(repositoryOption),
                parseResult.GetValue(settingOption) ?? [],
                parseResult.GetValue(wallMinutesOption),
                parseResult.GetValue(branchOption),
                parseResult.GetValue(baseOption),
                parseResult.GetValue(detachOption), executionOptions.Read(parseResult)),
            parseResult.GetValue(context.JsonOption)));
    }
}
