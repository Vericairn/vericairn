// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.CommandLine.Invocation;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>The command tree of <c>vericairn</c>; built once per invocation so tests can drive it in-process.</summary>
internal static class CliApp
{
    /// <summary>Builds the root command with every family: records, estate, work, goal, worker, devices, connections, release, sources, check, runtime, recover, objects, docs, mcp, status, progress, ask, decisions, doctor, version, updates.</summary>
    public static RootCommand Build(ReleaseUpdates? updates = null, CancellationToken cancellationToken = default)
    {
        updates ??= new ReleaseUpdates(Vericairn.Providers.GitHub.Releases.GitHubReleaseSource.Default);
        Composition.Register(Vericairn.Runtime.Providers.ProviderRegistry.Current);
        var context = new CommandContext(cancellationToken: cancellationToken);
        var root = new VericairnCommand(context);
        root.Subcommands.Add(new CommunicationCommand(context));
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
        root.Subcommands.Add(new SourcesCommand(context));
        root.Subcommands.Add(new CheckCommand(context));
        root.Subcommands.Add(new RuntimeCommand(context));
        root.Subcommands.Add(new RecoverCommand(context));
        root.Subcommands.Add(new ObjectsCommand(context));
        root.Subcommands.Add(new DocsCommand(context));
        root.Subcommands.Add(new McpCommand(context));
        root.Subcommands.Add(new ContextCommand(context));
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
            if (command.Action is SynchronousCommandLineAction action)
            {
                command.SetAction(parseResult => context.Invoke(() => action.Invoke(parseResult)));
            }
            foreach (Command child in command.Subcommands) Bind(child);
        }
    }
}
