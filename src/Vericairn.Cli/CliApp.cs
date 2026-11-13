// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.CommandLine.Invocation;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>The command tree of <c>vericairn</c>; built once per invocation so tests can drive it in-process.</summary>
internal static class CliApp
{
    /// <summary>Builds the root command with every family: records, estate, work, goal, worker, devices, connections, release, sources, check, runtime, recover, objects, docs, mcp, events, status, progress, ask, decisions, doctor, version, updates.</summary>
    public static RootCommand Build(ReleaseUpdates? updates = null, IEstateApi? borrowedApi = null, CancellationToken cancellationToken = default)
    {
        updates ??= new ReleaseUpdates(Vericairn.Providers.GitHub.Releases.GitHubReleaseSource.Default);
        // Per-user state on a filesystem that cannot restrict its permissions is used as it is and said once per process, on standard
        // error, which a detached daemon writes to its log; never in a command's output.
        Vericairn.Records.Signing.OwnerOnlyState.Report = path => Console.Error.WriteLine("warning: " + Vericairn.Records.Signing.OwnerOnlyState.Warning(path));
        if (borrowedApi is null) Composition.Register(Vericairn.Runtime.Providers.ProviderRegistry.Current);
        var context = new CommandContext(cancellationToken: cancellationToken) { BorrowedApi = borrowedApi };
        var root = new VericairnCommand(context);
        root.Subcommands.Add(new CommunicationCommand(context));
        root.Subcommands.Add(new PromptCommand(context));
        root.Subcommands.Add(new RecordsCommand(context));
        root.Subcommands.Add(new EstateCommand(context));
        root.Subcommands.Add(new PoliciesCommand(context));
        root.Subcommands.Add(new WorkCommand(context));
        root.Subcommands.Add(new OperatorCommand(context));
        root.Subcommands.Add(new GoalCommand(context));
        root.Subcommands.Add(new WorkerCommand(context));
        root.Subcommands.Add(new DevicesCommand(context));
        root.Subcommands.Add(new ConnectionsCommand(context));
        root.Subcommands.Add(new ReleaseCommand(context));
        root.Subcommands.Add(new ChangesCommand(context));
        root.Subcommands.Add(new PackagesCommand(context));
        root.Subcommands.Add(new SecurityReportsCommand(context));
        root.Subcommands.Add(new SourcesCommand(context));
        root.Subcommands.Add(new CheckCommand(context));
        root.Subcommands.Add(new RuntimeCommand(context));
        root.Subcommands.Add(new RecoverCommand(context));
        root.Subcommands.Add(new SupportCommand(context));
        root.Subcommands.Add(new ObjectsCommand(context));
        root.Subcommands.Add(new DocsCommand(context));
        root.Subcommands.Add(new GuidesCommand(context));
        root.Subcommands.Add(new TemplatesCommand(context));
        root.Subcommands.Add(new McpCommand(context));
        root.Subcommands.Add(new ContextCommand(context));
        root.Subcommands.Add(new EventsCommand(context));
        root.Subcommands.Add(new SearchCommand(context));
        root.Subcommands.Add(new UpdatesCommand(context));

        root.Subcommands.Add(new StatusCommand(context, updates));
        root.Subcommands.Add(new ProgressCommand(context));
        root.Subcommands.Add(new ResumeCommand(context));
        root.Subcommands.Add(new HandoffCommand(context));
        root.Subcommands.Add(new AskCommand(context));
        root.Subcommands.Add(new DecisionsCommand(context));
        root.Subcommands.Add(new DoctorCommand(context, updates));
        root.Subcommands.Add(new VersionCommand(context, updates));
        Bind(root);
        return root;

        void Bind(Command command)
        {
            // System.CommandLine validates the selected command, not its ancestors. Check the
            // root MCP input there so another command can never silently ignore that identity.
            if (command is not McpServeCommand)
            {
                command.Validators.Add(result =>
                {
                    if (result.GetResult(context.McpPrincipalOption) is not null)
                        result.AddError("The root --as option applies only to mcp serve. Use other commands' own identity options.");
                });
            }
            // Every command writes JSON with the global --json, except the few whose standard output cannot be one JSON result:
            // they refuse the option before they run, so none accepts it and writes text.
            if (command is ITextOutputCommand text)
            {
                command.Validators.Add(result =>
                {
                    if (result.GetValue(context.JsonOption))
                        result.AddError($"--json does not apply to vericairn {Path(command)}: {text.WhyNotJson}");
                });
            }
            if (command.Action is SynchronousCommandLineAction action)
            {
                command.SetAction(parseResult => context.Invoke(() => action.Invoke(parseResult)));
            }
            foreach (Command child in command.Subcommands) Bind(child);
        }
    }

    /// <summary>The command's words after vericairn.</summary>
    internal static string Path(Command command)
    {
        var words = new List<string>();
        for (Command? current = command; current is not null and not RootCommand; current = current.Parents.OfType<Command>().FirstOrDefault())
            words.Insert(0, current.Name);
        return string.Join(' ', words);
    }
}

/// <summary>A command whose standard output cannot be one JSON result, so it refuses the global --json before it runs.</summary>
internal interface ITextOutputCommand
{
    /// <summary>Why, and where the same facts are available as JSON when they are; a whole sentence.</summary>
    string WhyNotJson { get; }
}
