// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using Vericairn.Protocol;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.4 (OPS-20): the automated observers declared on this machine. The operator declares each with the interval it reports at and
/// the grace its silence is allowed; the observer reports after each run; silence, a failure and not observing are attention.
/// </summary>
internal sealed class ObserversCommand : Command
{
    public ObserversCommand(CommandContext context) : base("observers", "From 0.9.4: the automated observers declared on this machine, such as CI on the main line, scheduled workflows, intake watching and status refresh. Silence past an observer's interval plus its grace, a failed report and a report that it could not observe (an exhausted quota, a disabled schedule) are each attention, never a pass. Nothing is recorded.")
    {
        Subcommands.Add(List(context));
        Subcommands.Add(Declare(context));
        Subcommands.Add(Report(context));
        Subcommands.Add(Withdraw(context));
    }

    private static Command List(CommandContext context)
    {
        var command = new Command("list", "Show each observer declared on this machine: its kind, interval and grace, its latest report and its state now.");
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context, api.Observers(), parse.GetValue(context.JsonOption))));
        return command;
    }

    private static Command Declare(CommandContext context)
    {
        var command = new Command("declare", "Declare an automated observer on this machine, or declare it again, with the interval it reports at and the grace its silence is allowed; the operator's.");
        var name = new Argument<string>("name") { Description = "A lower-case kebab-case name of at most 64 characters, such as ci-main." };
        var kind = new Option<string>("--kind") { Description = "ci, scheduled-workflow, intake, status-refresh or other.", Required = true };
        var interval = new Option<string>("--interval") { Description = "How often it reports, as an ISO 8601 duration from PT1M to P31D, such as PT1H or P1D.", Required = true };
        var grace = new Option<string?>("--grace") { Description = "How long past the interval its silence is allowed, as an ISO 8601 duration up to P7D; a quarter of the interval when omitted." };
        var summary = new Option<string?>("--summary") { Description = "What it observes, in a sentence of at most 300 characters." };
        var by = new Option<string>("--by") { Description = "The operator's principal id.", Required = true };
        command.Arguments.Add(name);
        command.Options.Add(kind); command.Options.Add(interval); command.Options.Add(grace); command.Options.Add(summary); command.Options.Add(by);
        command.SetAction(parse =>
        {
            if (Seconds(parse.GetValue(interval)!) is not int every) return context.Fail("--interval is an ISO 8601 duration such as PT1H or P1D.");
            int? allowed = null;
            if (parse.GetValue(grace) is string text)
            {
                if (Seconds(text) is not int seconds) return context.Fail("--grace is an ISO 8601 duration such as PT15M.");
                allowed = seconds;
            }

            return context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context,
                api.ObserverDeclare(new(parse.GetValue(by)!, parse.GetValue(name)!, parse.GetValue(kind)!, every, allowed, parse.GetValue(summary))), parse.GetValue(context.JsonOption)));
        });
        return command;
    }

    private static Command Report(CommandContext context)
    {
        var command = new Command("report", "Keep one report of a declared observer on this machine: passed, failed, or not-observing with why. Run it at the end of each of the observer's runs.");
        var name = new Argument<string>("name") { Description = "The declared observer's name." };
        var outcome = new Option<string>("--outcome") { Description = "passed (it observed and found nothing wrong), failed (it observed a failure) or not-observing (it could not observe).", Required = true };
        var reason = new Option<string?>("--reason") { Description = "For not-observing, and required there: quota-exhausted, schedule-disabled or unavailable." };
        var at = new Option<string?>("--at") { Description = "When the observation was made, as an RFC 3339 UTC timestamp to the second; now when omitted." };
        var detail = new Option<string?>("--detail") { Description = "What was observed, in at most 300 characters." };
        var by = new Option<string>("--by") { Description = "The principal the reporter acts for.", Required = true };
        command.Arguments.Add(name);
        command.Options.Add(outcome); command.Options.Add(reason); command.Options.Add(at); command.Options.Add(detail); command.Options.Add(by);
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context,
            api.ObserverReport(new(parse.GetValue(by)!, parse.GetValue(name)!, parse.GetValue(outcome)!, parse.GetValue(reason), parse.GetValue(at), parse.GetValue(detail))), parse.GetValue(context.JsonOption))));
        return command;
    }

    private static Command Withdraw(CommandContext context)
    {
        var command = new Command("withdraw", "Withdraw a declared observer on this machine, so that its silence stops being attention; the operator's, with a reason.");
        var name = new Argument<string>("name") { Description = "The declared observer's name." };
        var reason = new Option<string>("--reason") { Description = "Why it no longer reports, in at most 300 characters.", Required = true };
        var by = new Option<string>("--by") { Description = "The operator's principal id.", Required = true };
        command.Arguments.Add(name);
        command.Options.Add(reason); command.Options.Add(by);
        command.SetAction(parse => context.WithApi(parse.GetValue(context.EstateOption)!, api => Write(context,
            api.ObserverWithdraw(new(parse.GetValue(by)!, parse.GetValue(name)!, parse.GetValue(reason)!)), parse.GetValue(context.JsonOption))));
        return command;
    }

    // An ISO 8601 duration in whole seconds, or null when it does not parse or is out of range.
    private static int? Seconds(string text)
    {
        try
        {
            double seconds = XmlConvert.ToTimeSpan(text.Trim()).TotalSeconds;
            return seconds is < 0 or > int.MaxValue || seconds != Math.Floor(seconds) ? null : (int)seconds;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static int Write(CommandContext context, ObserversResponse response, bool json)
    {
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
        }

        foreach (ObserverRow row in response.Observers) context.Output.WriteLine("  " + Line(row));
        context.Output.WriteLine(response.Message);
        return response.Ok ? CommandContext.Clean : CommandContext.HasErrors;
    }

    /// <summary>One observer on one line, as `observers list` and human status show it.</summary>
    internal static string Line(ObserverRow row)
    {
        string cadence = $"every {XmlConvert.ToString(TimeSpan.FromSeconds(row.IntervalSeconds))}, grace {XmlConvert.ToString(TimeSpan.FromSeconds(row.GraceSeconds))}";
        string last = row.LastReport is { } report
            ? $"last report {report.Outcome}{(report.Reason is null ? string.Empty : " (" + report.Reason + ")")} at {report.At} by {report.By}"
            : "no report yet";
        return string.Create(CultureInfo.InvariantCulture, $"{row.Name} ({row.Kind}, {cadence}): {row.State}; {last}; {(row.Attention ? "attention: " : string.Empty)}{row.Why}");
    }
}
