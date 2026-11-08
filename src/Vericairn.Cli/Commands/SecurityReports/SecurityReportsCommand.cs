// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>Security reports read before every release: read them now, and record the operator's triage of one observed revision.</summary>
internal sealed class SecurityReportsCommand : Command
{
    public SecurityReportsCommand(CommandContext context) : base("security-reports", "Read the declared repositories' security advisories, drafts and private reports included, and record the operator's triage; while one is untriaged, every release waits.")
    {
        Subcommands.Add(Poll(context));
        Subcommands.Add(Triage(context));
    }

    private static Command Poll(CommandContext context)
    {
        var command = new Command("poll", "Read every repository a product's security-reports declaration names now, through its reader, and show each advisory's identifier, state, severity and content hash; records and posts nothing.");
        var by = new Option<string>("--by") { Description = "The operator's principal id; anyone else is refused alike.", Required = true };
        command.Options.Add(by);
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api =>
            Write(context, api.SecurityReportsPoll(new(parse.GetValue(by)!)), parse.GetValue(context.JsonOption))));
        return command;
    }

    private static Command Triage(CommandContext context)
    {
        var command = new Command("triage", "Record the operator's triage of one observed advisory revision by identifier, repository and content hash; the advisory's text never enters the record.");
        var advisory = new Argument<string>("advisory") { Description = "The advisory's GHSA identifier, as poll shows it." };
        var repository = new Option<string>("--repository") { Description = "The advisory's repository as owner/name.", Required = true };
        var expected = new Option<string>("--expected") { Description = "The observed revision's content hash, as poll or attention shows it; a changed advisory is refused.", Required = true };
        var disposition = new Option<string>("--disposition") { Description = "security (a vulnerability to fix), duplicate or no-action.", Required = true };
        var reason = new Option<string?>("--reason") { Description = "Why, in your own words and at most 500 characters, never the report's text; required for no-action." };
        var by = new Option<string>("--by") { Description = "The operator's principal id, on an admitted device.", Required = true };
        command.Arguments.Add(advisory);
        command.Options.Add(repository); command.Options.Add(expected); command.Options.Add(disposition); command.Options.Add(reason); command.Options.Add(by);
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context,
            api.SecurityReportsTriage(new(parse.GetValue(by)!, parse.GetValue(advisory)!, parse.GetValue(repository)!, parse.GetValue(expected)!,
                parse.GetValue(disposition)!, parse.GetValue(reason))), parse.GetValue(context.JsonOption))));
        return command;
    }

    private static int Write(CommandContext context, SecurityReportsResponse response, bool json)
    {
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
        }

        if (response.Triage is { } triage) context.Output.WriteLine($"{triage.Path}  {triage.Id}  {triage.Hash}");
        foreach (SecurityReportRead read in response.Reads)
            context.Output.WriteLine($"{read.Repository} through {read.Provider}: " + (read.Complete ? "read completely at " + read.At : "unconfirmed, " + read.Problem + (read.At is null ? "" : " (last attempt " + read.At + ")")));
        foreach (SecurityReportRow report in response.Reports)
            context.Output.WriteLine($"  {report.Advisory} on {report.Repository}: state {report.State}, severity {report.Severity}, revision {report.ContentHash}, "
                + (report.Triage is not null ? "triaged by " + report.Triage : report.Holds ? "untriaged since " + report.ChangedAt + ", holding every release" : "holds nothing and needs no triage"));
        context.Output.WriteLine(response.Message);
        return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
    }
}
