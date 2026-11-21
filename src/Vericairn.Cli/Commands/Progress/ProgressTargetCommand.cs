// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.4 (WATCH-06): the estate's progress kept at a target, such as a pinned GitHub discussion, through the adapter its
/// configuration chooses: set, list and remove a target on this machine, refresh it now, and follow the event journal to keep it equal.
/// </summary>
internal sealed class ProgressTargetCommand : Command
{
    public ProgressTargetCommand(CommandContext context)
        : base("target", "From 0.9.4: keep the estate's progress at a target, such as a pinned GitHub discussion, through the adapter its configuration on this machine chooses. Vericairn composes every text and checks it against the disclosure rules; the adapter only places it.")
    {
        var operations = new ProgressTargetOperations(context);
        Subcommands.Add(Set(context, operations));
        Subcommands.Add(List(context, operations));
        Subcommands.Add(Remove(context, operations));
        Subcommands.Add(Refresh(context, operations));
        Subcommands.Add(Follow(context, operations));
    }

    private static Command Set(CommandContext context, ProgressTargetOperations operations)
    {
        var command = new Command("set", "Configure a progress target on this machine, or replace its configuration: the adapter that places it, the adapter's settings, its form, its title and the events that refresh it and add a comment. Sends nothing.");
        var name = new Argument<string>("name") { Description = "The target's name, lower-case kebab-case." };
        var adapter = new Option<string>("--adapter") { Description = "The adapter that places the target, by id, such as github-discussions; no other is ever selected.", Required = true };
        var setting = new Option<string[]>("--setting") { Description = "One of the adapter's settings as name=value, such as repository=owner/name or category=Announcements; repeatable.", AllowMultipleArgumentsPerToken = false };
        var form = new Option<string>("--form") { Description = "private (the report's sections as the records give them) or public (nothing a disclosure rule holds). A destination readable beyond its own members always takes the public form.", Required = true };
        form.AcceptOnlyFromAmong("public", "private");
        var title = new Option<string?>("--title") { Description = "The place's title. \"Progress of\" and the estate's name when omitted." };
        var refresh = new Option<string[]>("--refresh") { Description = "An event that refreshes the target: a named trigger (release-state, work-order-merged, result-or-acceptance, step-waiting-on-person, step-waiting-on-operator) or an event kind with filters, such as \"record type=decision\"; repeatable. The defaults when omitted; none for no event.", AllowMultipleArgumentsPerToken = false };
        var comment = new Option<string[]>("--comment") { Description = "An event that adds a comment, which notifies the target's readers, written as for --refresh; repeatable. The defaults when omitted (a step waiting on the operator and a release's state change); none for no comment.", AllowMultipleArgumentsPerToken = false };
        command.Arguments.Add(name);
        foreach (Option option in new Option[] { adapter, setting, form, title, refresh, comment }) command.Options.Add(option);
        command.SetAction(p => operations.Set(p.GetValue(context.EstateOption)!, p.GetValue(name)!, p.GetValue(adapter)!, p.GetValue(setting) ?? [], p.GetValue(form)!,
            p.GetValue(title), p.GetValue(refresh), p.GetValue(comment), p.GetValue(context.JsonOption)));
        return command;
    }

    private static Command List(CommandContext context, ProgressTargetOperations operations)
    {
        var command = new Command("list", "List the progress targets configured on this machine, each with its adapter, settings, form, triggers and the place it keeps.");
        command.SetAction(p => operations.List(p.GetValue(context.EstateOption)!, p.GetValue(context.JsonOption)));
        return command;
    }

    private static Command Remove(CommandContext context, ProgressTargetOperations operations)
    {
        var command = new Command("remove", "Remove a progress target's configuration and what this machine kept of it. The place it kept, such as a discussion, stays where it is.");
        var name = new Argument<string>("name") { Description = "The target's name." };
        command.Arguments.Add(name);
        command.SetAction(p => operations.Remove(p.GetValue(context.EstateOption)!, p.GetValue(name)!, p.GetValue(context.JsonOption)));
        return command;
    }

    private static Command Refresh(CommandContext context, ProgressTargetOperations operations)
    {
        var command = new Command("refresh", "Bring a progress target's text equal to the estate's progress now: locate its place, compose the text in the form its destination allows, and create the place once or replace its text when it differs. Names every record the text carries.");
        var name = new Argument<string>("name") { Description = "The target's name." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Locate the place and compose the text, show it, and send nothing." };
        command.Arguments.Add(name);
        command.Options.Add(dryRun);
        command.SetAction(p => operations.Refresh(p.GetValue(context.EstateOption)!, p.GetValue(name)!, p.GetValue(dryRun), p.GetValue(context.JsonOption)));
        return command;
    }

    private static Command Follow(CommandContext context, ProgressTargetOperations operations)
    {
        var command = new Command("follow", "Keep a progress target equal to the estate's progress: follow the event journal of the daemon serving this estate, refresh the target once the stream is open and again on each event its refresh triggers name, and add a comment on each event its comment triggers name, until interrupted. Resumes after the cursor it last reached on this machine.");
        var name = new Argument<string>("name") { Description = "The target's name." };
        var count = new Option<int?>("--count") { Description = "Stop after this many outcomes, each refresh (an unchanged one included) and each comment counting one. Follows until interrupted when omitted." };
        command.Arguments.Add(name);
        command.Options.Add(count);
        command.SetAction(p => operations.Follow(p.GetValue(context.EstateOption)!, p.GetValue(name)!, p.GetValue(count), p.GetValue(context.JsonOption)));
        return command;
    }
}
