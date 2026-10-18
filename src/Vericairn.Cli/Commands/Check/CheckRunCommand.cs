// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class CheckRunCommand : Command
{
    public CheckRunCommand(CommandContext context)
        : base("run", "Run a command in a repository and record a verification: commit, command, environment, exit code, duration and output as evidence.")
    {
        var operations = new VerificationOperations(context);
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var resourceOption = new Option<string>("--resource") { Description = "Resource record id of the repository checked.", Required = true };
        var inOption = new Option<string>("--in") { Description = "Working tree the command runs in.", DefaultValueFactory = _ => Directory.GetCurrentDirectory() };
        var roleOption = new Option<string>("--role") { Description = "self, independent-agent, operator or automation.", DefaultValueFactory = _ => "self" };
        var summaryOption = new Option<string?>("--summary") { Description = "What the check establishes, in your words; the observed exit and duration are recorded regardless." };
        var timeoutOption = new Option<int>("--timeout") { Description = "Seconds after which the command is stopped and its result recorded as unknown.", DefaultValueFactory = _ => 3600 };
        var commandArgument = new Argument<string[]>("command") { Description = "The command and its arguments; put -- before it.", Arity = ArgumentArity.OneOrMore };
        Options.Add(resourceOption);
        Options.Add(byOption);
        Options.Add(inOption);
        Options.Add(roleOption);
        Options.Add(summaryOption);
        Options.Add(timeoutOption);
        Arguments.Add(commandArgument);
        SetAction(parseResult => operations.CheckRun(
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(resourceOption)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(inOption)!,
            parseResult.GetValue(roleOption)!,
            parseResult.GetValue(summaryOption),
            parseResult.GetValue(timeoutOption),
            parseResult.GetValue(commandArgument)!,
            parseResult.GetValue(context.JsonOption)));
    }
}
