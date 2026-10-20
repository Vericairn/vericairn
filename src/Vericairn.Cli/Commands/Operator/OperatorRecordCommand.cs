// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class OperatorRecordCommand : Command
{
    public OperatorRecordCommand(CommandContext context)
        : base("record", "Sign your own operator record, commit it with its generated register members on a new estate branch, scan, push, open, wait for declared checks and merge through recorded changes operations. Identical input resumes the original action; failures retain its branch and pull request and report the next command. Requires your admitted device, a clean estate repository and declared checks. Not exposed through MCP.")
    {
        var type = new Argument<string>("type") { Description = "Record type, for example acceptance or decision." };
        var by = new Option<string>("--by") { Description = "Operator principal id acting on their own admitted device.", Required = true };
        var body = new Option<string>("--body") { Description = "JSON body file, or '-' for standard input. Supply the same body to resume.", Required = true };
        var subject = new Option<string?>("--subject") { Description = "Subject as type:id or type:id:hash; acceptances pin the exact hash." };
        var supersedes = new Option<string?>("--supersedes") { Description = "Id of the record this one replaces." };
        var product = new Option<string?>("--product") { Description = "Product declaring changes for this estate repository. Defaults to its unique registered product." };
        var wait = new Option<int>("--wait-seconds") { Description = "Maximum wait for each check phase, from 0 to 240 seconds; 0 observes once.", DefaultValueFactory = _ => 120 };
        var reconciliation = new Option<string?>("--reconciliation") { Description = "How the previous known failure was settled; required before retrying that effect." };
        Arguments.Add(type);
        Options.Add(by);
        Options.Add(body);
        Options.Add(subject);
        Options.Add(supersedes);
        Options.Add(product);
        Options.Add(wait);
        Options.Add(reconciliation);
        SetAction(parse => context.WithBody(parse.GetValue(body)!, input => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
        {
            OperatorRecordResponse response;
            bool transportFailed = false;
            try
            {
                response = api.OperatorRecord(new(parse.GetValue(type)!, parse.GetValue(by)!, input,
                    parse.GetValue(subject), parse.GetValue(supersedes), parse.GetValue(product), parse.GetValue(wait), parse.GetValue(reconciliation)));
            }
            // A definite refusal had no effect to wait for; the client boundary reports it.
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException or HttpRequestException && exception is not ClientRefusedException)
            {
                transportFailed = true;
                response = new("operator-record", "stopped", "The client stopped waiting; the runtime may still be settling its current recorded operation.", false,
                    "Wait for that operation to settle, then rerun this same operator record command with the same JSON body to recover its outcome.");
            }
            if (parse.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            else
            {
                context.Output.WriteLine(response.Message);
                if (response.Record is RecordWritten record) context.Output.WriteLine($"Record: {record.Id} at {record.Hash}");
                if (response.Branch is string branch) context.Output.WriteLine($"Branch: {branch}; head: {response.Head ?? "not committed"}");
                if (response.Change?.PullRequest is string pull) context.Output.WriteLine("Pull request: " + pull);
                context.Output.WriteLine(response.NextStep);
            }
            return transportFailed ? CommandContext.Failed : response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
        })));
    }
}
