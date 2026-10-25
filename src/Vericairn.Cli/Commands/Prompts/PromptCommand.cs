// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class PromptCommand : Command
{
    internal PromptCommand(CommandContext context) : base("prompt", "Find and use categorized shared prompts; every use is recorded and grants no authority.")
    {
        Subcommands.Add(List(context));
        Subcommands.Add(Show(context));
        Subcommands.Add(Save(context, "add"));
        Subcommands.Add(Save(context, "revise"));
        Subcommands.Add(Retire(context));
        foreach (string choice in new[] { "favor", "hide", "disable" }) Subcommands.Add(Preference(context, choice));
        Subcommands.Add(Copy(context));
        Subcommands.Add(Use(context));
    }

    private static Option<string> Author() => new("--by") { Required = true, Description = "The actual authoring principal." };
    private static Argument<string> Identity() => new("prompt") { Description = "Prompt source:key, as listed by prompt list." };
    private static Option<string?> Expected() => new("--expected") { Description = "Expected definition hash; defaults to the current offer read by this command." };
    private static string Root(ParseResult p, CommandContext context) => p.GetValue(context.EstateOption)!;

    private static Command List(CommandContext context)
    {
        var command = new Command("list", "List by category and source, with personal favorites first within each category.");
        var person = new Option<string?>("--person") { Description = "Whose favorites, hidden choices and suited roles to apply." };
        var category = new Option<string?>("--category") { Description = "Include this category and its descendants." };
        var role = new Option<string?>("--role") { Description = "Narrow the list to prompts suited to this role." };
        var favorites = new Option<bool>("--favorites") { Description = "Only this person's favorites." };
        var all = new Option<bool>("--all") { Description = "Also show hidden, disabled and retired entries." };
        command.Options.Add(person); command.Options.Add(category); command.Options.Add(role); command.Options.Add(favorites); command.Options.Add(all);
        command.SetAction(p => context.WithApi(Root(p, context), api =>
        {
            PromptsResponse response = api.Prompts(new(p.GetValue(person), p.GetValue(category), p.GetValue(role), p.GetValue(favorites), p.GetValue(all)));
            if (response.Problem is { } problem) return context.Fail(problem.Message);
            if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            else foreach (IGrouping<string, PromptRow> group in response.Prompts.GroupBy(r => r.Definition.GetProperty("category").GetString()!))
            {
                context.Output.WriteLine(group.Key);
                foreach (PromptRow row in group) context.Output.WriteLine($"  {(row.Favorite ? "* " : "")}{row.Identity}: {row.Definition.GetProperty("title").GetString()} [{row.Source} @ {row.Version}]{(row.Hidden ? " hidden" : "")}{(row.Disabled ? " disabled" : "")}{(row.Retired ? " retired" : "")}{(row.Draft ? " draft awaiting operator review" : "")}");
            }
            return Clean;
        }));
        return command;
    }

    private static Command Show(CommandContext context)
    {
        var command = new Command("show", "Inspect a prompt's exact source, revision and described placeholders without using it.");
        var identity = Identity(); command.Arguments.Add(identity);
        command.SetAction(p => context.WithApi(Root(p, context), api =>
        {
            PromptRow row = Find(api, p.GetValue(identity)!);
            if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(row, ProtocolJson.Options));
            else
            {
                context.Output.WriteLine($"{row.Definition.GetProperty("category").GetString()} / {row.Definition.GetProperty("title").GetString()}\n{row.Identity} @ {row.Version}\n{row.Hash}");
                context.Output.WriteLine(row.Definition.GetProperty("description").GetString());
                context.Output.WriteLine(row.Definition.GetProperty("text").GetString());
                foreach (JsonElement argument in row.Definition.GetProperty("placeholders").EnumerateArray())
                    context.Output.WriteLine($"  {argument.GetProperty("name").GetString()}: {argument.GetProperty("description").GetString()}");
                if (row.Draft) context.Output.WriteLine("Draft awaiting operator review; unavailable for use or copying.");
                if (row.Disabled || row.Retired) context.Output.WriteLine("Unavailable for new uses.");
            }
            return Clean;
        }));
        return command;
    }

    private static Command Save(CommandContext context, string action)
    {
        var command = new Command(action, action == "add" ? "Add a signed estate prompt from a complete definition." : "Supersede a shared prompt, preserving every earlier revision.");
        var author = Author(); var expected = Expected();
        var body = new Option<string>("--body") { Required = true, Description = "Definition JSON file, or '-' for standard input." };
        command.Options.Add(author); command.Options.Add(expected); command.Options.Add(body);
        command.SetAction(p => context.WithBody(p.GetValue(body)!, definition => context.WithApi(Root(p, context), api =>
        {
            string? prior = action == "add" ? null : p.GetValue(expected) ?? Find(api, "estate:" + definition.GetProperty("key").GetString()).Hash;
            return context.ReportOutcome(api.PromptSave(new(p.GetValue(author)!, definition, prior)), p.GetValue(context.JsonOption));
        })));
        return command;
    }

    private static Command Retire(CommandContext context)
    {
        var command = new Command("retire", "Retire an estate prompt without deleting it or changing past uses.");
        var identity = Identity(); var author = Author(); var expected = Expected();
        command.Arguments.Add(identity); command.Options.Add(author); command.Options.Add(expected);
        command.SetAction(p => context.WithApi(Root(p, context), api =>
        {
            PromptRow row = Find(api, p.GetValue(identity)!);
            if (row.Source != "estate") return context.Fail("Built-in prompts are read-only; use disable or hide.");
            JsonObject body = JsonNode.Parse(row.Definition.GetRawText())!.AsObject(); body["state"] = "retired";
            return context.ReportOutcome(api.PromptSave(new(p.GetValue(author)!, CommandJson.Element(body), p.GetValue(expected) ?? row.Hash)), p.GetValue(context.JsonOption));
        }));
        return command;
    }

    private static Command Preference(CommandContext context, string choice)
    {
        var command = new Command(choice, choice == "disable" ? "Disable a prompt estate-wide as the operator; --undo enables it again." : "Change your personal choice; --undo reverses it.");
        var identity = Identity(); var author = Author(); var undo = new Option<bool>("--undo");
        command.Arguments.Add(identity); command.Options.Add(author); command.Options.Add(undo);
        command.SetAction(p => context.WithApi(Root(p, context), api => context.ReportOutcome(api.PromptPreference(new(p.GetValue(author)!, p.GetValue(identity)!,
            Favorite: choice == "favor" ? !p.GetValue(undo) : null, Hidden: choice == "hide" ? !p.GetValue(undo) : null, Disabled: choice == "disable" ? !p.GetValue(undo) : null)), p.GetValue(context.JsonOption))));
        return command;
    }

    private static Command Copy(CommandContext context)
    {
        var command = new Command("copy", "Copy a reviewed built-in to a new estate key, recording its exact origin.");
        var identity = Identity(); var author = Author(); var expected = Expected(); var key = new Option<string>("--key") { Required = true };
        command.Arguments.Add(identity); command.Options.Add(author); command.Options.Add(expected); command.Options.Add(key);
        command.SetAction(p => context.WithApi(Root(p, context), api => context.ReportOutcome(api.PromptCopy(new(p.GetValue(author)!, p.GetValue(identity)!,
            p.GetValue(expected) ?? Find(api, p.GetValue(identity)!).Hash, p.GetValue(key)!)), p.GetValue(context.JsonOption))));
        return command;
    }

    private static Command Use(CommandContext context)
    {
        var command = new Command("use", "Use a prompt with one new assignment, new run or ask answer; the target records its exact source and values.");
        var identity = Identity(); var author = Author(); var expected = Expected();
        var assignment = new Option<string?>("--assignment") { Description = "JSON file containing an AssignRequest." };
        var run = new Option<string?>("--run") { Description = "JSON file containing a RunStartRequest." };
        var ask = new Option<string?>("--ask") { Description = "The current ask addressed to the acting principal." };
        var favor = new Option<bool>("--favor") { Description = "Also save this prompt as a personal favorite after successful use." };
        var values = new Option<string[]>("--value") { Description = "Literal name=value placeholder values; repeat for each name.", AllowMultipleArgumentsPerToken = true };
        command.Arguments.Add(identity); command.Options.Add(author); command.Options.Add(expected); command.Options.Add(assignment); command.Options.Add(run); command.Options.Add(ask); command.Options.Add(values); command.Options.Add(favor);
        command.SetAction(p => context.WithApi(Root(p, context), api =>
        {
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string argument in p.GetValue(values) ?? [])
            {
                string[] pair = argument.Split('=', 2);
                if (pair.Length != 2 || !arguments.TryAdd(pair[0], pair[1])) return context.Fail("Use each placeholder once, as name=value.");
            }
            PromptRow row = Find(api, p.GetValue(identity)!);
            var selection = new PromptSelection(row.Identity, p.GetValue(expected) ?? row.Hash, arguments);
            PromptUseResponse result = api.PromptUse(new(p.GetValue(author)!, selection,
                p.GetValue(assignment) is { } assignmentFile ? Read<AssignRequest>(assignmentFile) : null,
                p.GetValue(run) is { } runFile ? Read<RunStartRequest>(runFile) : null, p.GetValue(ask)) { Favor = p.GetValue(favor) });
            if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(result, ProtocolJson.Options));
            else if (result.Written is { } written) context.Output.WriteLine($"{written.Path}  {written.Id}  {written.Hash}\n{result.Text}");
            return result.Ok ? Clean : context.Fail(result.Problem?.Message ?? result.Run?.Message ?? "The prompt use was refused.");
        }));
        return command;
    }

    private static T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(file), ProtocolJson.Options) ?? throw new ArgumentException("The target JSON is empty.", nameof(file));
    private static PromptRow Find(IEstateApi api, string identity)
    {
        PromptsResponse response = api.Prompts(new(IncludeUnavailable: true));
        if (response.Problem is { } problem) throw new InvalidOperationException(problem.Message);
        return response.Prompts.SingleOrDefault(p => p.Identity == identity) ?? throw new ArgumentException("No prompt has that source:key identity.", nameof(identity));
    }
}
