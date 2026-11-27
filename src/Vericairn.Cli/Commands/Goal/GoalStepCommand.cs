// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class GoalStepCommand : Command
{
    public GoalStepCommand(CommandContext context)
        : base("step", "Take a goal's next step, one at a time: its first eligible step whose route acts, routed from this machine by the goal's roster, for an operator, the goal's sponsor when a person, or a principal holding local-engineering. An agent's step goes on with the assignment that holds it, or is assigned to the agent with the run's intent, after the worker's whole preflight, and its run starts through the worker; every refusal before the intent records nothing, the assignment included, as while a key of an operator or of the sponsor signs here without presence and no custody exception is in force. A person's step is assigned to the person and asked of them, the two recorded together; a step no principal is available for is asked of the sponsor, never given to another contributor. A run moves one step of its flow per command: the first work or review step, in the flow's order, whose route acts, its assignment naming the step and its brief pinning the step's inputs by hash; a reserved step and the other kinds of step are never dispatched. From 0.9.4 a standing agent carries one occurrence per command, evaluated from the records at --now: the occurrence named, a request made now, or the oldest due one, as one assignment naming its key, within its period caps, its concurrency and its failure pause; a key holds one assignment, whoever records it. It activates, funds, accepts and answers nothing.")
    {
        var operations = new GoalOperations(context);
        var shownArgument = new Argument<string?>("goal") { Description = "Goal record id (any revision); omit for the current goal.", Arity = ArgumentArity.ZeroOrOne };
        var byOption = new Option<string>("--by") { Description = "Acting principal id (ULID).", Required = true };
        var repositoryOption = new Option<string?>("--repository") { Description = "For an agent's step, the repository resource whose clone, registered on this machine (vericairn sources clone), its workspace is made from." };
        var settingOption = new Option<string[]>("--setting") { Description = "For an agent's step, a setting of the launcher routing chose, as name=value, for example command=<executable or JSON array>; repeatable.", AllowMultipleArgumentsPerToken = false };
        var wallMinutesOption = new Option<long?>("--wall-minutes") { Description = "For an agent's step, the run's cap in wall minutes; what remains of the goal's budget when omitted, and never more." };
        var branchOption = new Option<string?>("--branch") { Description = "For an agent's step, the work branch; work/ and the work order's key when omitted." };
        var baseOption = new Option<string?>("--base") { Description = "For an agent's step, what a new work branch starts from; the clone's HEAD when omitted." };
        var occurrenceOption = new Option<string?>("--occurrence") { Description = "From 0.9.4, for a standing agent: the occurrence to carry, by its key as vericairn goal due lists it, or request for an occurrence of its request trigger made now; the oldest due occurrence when omitted." };
        var nowOption = new Option<string?>("--now") { Description = "From 0.9.4, for a standing agent: the UTC time its triggers are evaluated at, never later than the runtime's clock; the runtime's clock when omitted." };
        var detachOption = new Option<bool>("--detach") { Description = "Return once an agent's run is launched, leaving it to the daemon that serves the estate; without it the command waits until the run settles, and Ctrl+C asks it to stop." };
        var checkOption = new Option<bool>("--check") { Description = "Preview the next step and its permitted change patterns. Signs, records and dispatches nothing." };
        Arguments.Add(shownArgument);
        Options.Add(byOption);
        Options.Add(repositoryOption);
        Options.Add(settingOption);
        Options.Add(wallMinutesOption);
        Options.Add(branchOption);
        Options.Add(baseOption);
        Options.Add(detachOption);
        Options.Add(checkOption);
        Options.Add(occurrenceOption);
        Options.Add(nowOption);
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
                parseResult.GetValue(detachOption), executionOptions.Read(parseResult), parseResult.GetValue(occurrenceOption), parseResult.GetValue(nowOption), parseResult.GetValue(checkOption)),
            parseResult.GetValue(context.JsonOption)));
    }
}
