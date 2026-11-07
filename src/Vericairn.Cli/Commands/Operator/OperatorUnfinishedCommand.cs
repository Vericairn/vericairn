// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class OperatorUnfinishedCommand : Command
{
    public OperatorUnfinishedCommand(CommandContext context)
        : base("unfinished", "Show this clone's unfinished operator action: its signed record, type and subject, branch, pull request and stage, and what finishing it needs. With --finish, finish an action whose merge is recorded once its target checks pass, from its own records: nothing is signed, branched or committed. Not exposed through MCP.")
    {
        var finish = new Option<bool>("--finish") { Description = "Finish the action from its records; requires --by." };
        var by = new Option<string?>("--by") { Description = "Operator principal acting on their own admitted device; required with --finish." };
        var wait = new Option<int>("--wait-seconds") { Description = "With --finish, the maximum wait for pending target checks, from 0 to 240 seconds; 0 observes once.", DefaultValueFactory = _ => 120 };
        Options.Add(finish);
        Options.Add(by);
        Options.Add(wait);
        SetAction(parse =>
        {
            bool finishing = parse.GetValue(finish);
            if (finishing && string.IsNullOrWhiteSpace(parse.GetValue(by))) return context.Fail("--finish requires --by <operator>.");
            if (!finishing && parse.GetValue(by) is not null) return context.Fail("Use --by with --finish.");
            return context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            {
                bool json = parse.GetValue(context.JsonOption);
                if (!finishing)
                {
                    OperatorUnfinishedResponse unfinished = api.OperatorUnfinished();
                    if (json) context.Output.WriteLine(JsonSerializer.Serialize(unfinished, ProtocolJson.Options));
                    else Show(context.Output, unfinished);
                    if (unfinished.Problem is not null && !json) context.Error.WriteLine(unfinished.Problem.Message);
                    return unfinished.Problem is null ? CommandContext.Clean : CommandContext.HasErrors;
                }
                OperatorRecordResponse response;
                bool transportFailed = false;
                try
                {
                    response = api.OperatorFinish(new(parse.GetValue(by)!, parse.GetValue(wait)));
                }
                // A definite refusal had no effect to wait for; the client boundary reports it.
                catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or HttpRequestException && exception is not ClientRefusedException)
                {
                    transportFailed = true;
                    response = new("operator-record", "stopped", "The client stopped waiting; the runtime may still be settling its current recorded observation.", false,
                        "Wait for that observation to settle, then run vericairn operator unfinished again.");
                }
                if (json) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                else
                {
                    context.Output.WriteLine(response.Message);
                    if (response.Record is RecordWritten record) context.Output.WriteLine($"Record: {record.Id} at {record.Hash}");
                    if (response.Change?.PullRequest is string pull) context.Output.WriteLine("Pull request: " + pull);
                    context.Output.WriteLine(response.NextStep);
                }
                return transportFailed ? CommandContext.Failed : response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
            });
        });
    }

    private static void Show(TextWriter output, OperatorUnfinishedResponse response)
    {
        if (response.Action is not OperatorUnfinishedAction action)
        {
            if (response.Problem is null) output.WriteLine("This clone has no unfinished operator action.");
            return;
        }
        output.WriteLine("Unfinished: " + action.Summary + ".");
        output.WriteLine(action.Record is RecordWritten record ? $"Record: {record.Id} at {record.Hash}" : $"Record: {action.RecordId}, not in this clone");
        output.WriteLine($"Branch: {action.Branch}; head: {action.Head ?? "not committed"}");
        if (action.PullRequest is string pull) output.WriteLine("Pull request: " + pull);
        if (action.Merge is RecordWritten merge)
            output.WriteLine($"Merge: operation {merge.Id}" + (action.MergeCommit is null ? string.Empty : $" at {action.MergeCommit}")
                + $"; target checks: {action.CheckStatus ?? "not yet observed"}" + (action.TargetObservation is RecordWritten target ? $" (observation {target.Id})" : string.Empty));
        output.WriteLine("Stage: " + action.Stage + (action.Finishable ? "; its records can finish it." : "; its records alone cannot finish it."));
        output.WriteLine(action.NextStep);
    }
}
