// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerConfigureCommand : Command
{
    public WorkerConfigureCommand(CommandContext context)
        : base("configure", "Show or replace this machine's workspace root, a native launcher's configuration with --launcher, or a workspace setup command for a launcher or a repository. --workspaces sets an absolute root for every launcher. With no choices, show what is configured; setting a launcher requires both model and effort and replaces the allowlists. Stored owner-only under VericairnHome, outside repositories and never in a record; launcher configuration holds no secret values, commands or inventory additions. The CLI and daemon read it for each run; this command calls no API and runs no probes.")
    {
        var operations = new WorkerSettings(context);
        var launcher = new Option<string?>("--launcher") { Description = "The native launcher: codex or claude-code; omitted to show or set the workspace root." };
        var workspaces = new Option<string?>("--workspaces") { Description = "Absolute machine-local workspace root for every launcher, outside repositories, estates and ancestors holding harness configuration. New roots are owner-only. Omit --launcher." };
        var model = new Option<string?>("--model") { Description = "Default portable model selector; required with --effort when setting." };
        var effort = new Option<string?>("--effort") { Description = "Default effort in the launcher's native vocabulary; required with --model when setting." };
        var models = new Option<string[]>("--allow-model") { Description = "Permitted model selector; repeatable, including the default. Omitted: only the default model.", AllowMultipleArgumentsPerToken = false };
        var efforts = new Option<string[]>("--allow-effort") { Description = "Permitted native effort; repeatable, including the default. Omitted: the launcher's supported efforts. Never expands its capabilities.", AllowMultipleArgumentsPerToken = false };
        var adapter = new Option<string?>("--adapter") { Description = "Operator-only efficiency-adapter admission: local-lossless or disabled. Separate from launcher defaults. Do not enable before its security review." };
        var adapterLaunchers = new Option<string[]>("--allow-launcher") { Description = "Launcher admitted for --adapter local-lossless: codex or claude-code; repeatable.", AllowMultipleArgumentsPerToken = false };
        var repository = new Option<string?>("--repository") { Description = "The repository resource a setup command belongs to; alone, show its setup command." };
        var setupCommand = new Option<string?>("--setup-command") { Description = "An executable, or a JSON array of it and its arguments, that the worker runs in a run's workspace before the agent starts, as you and outside the agent's sandbox, for --launcher or --repository; a repository's comes before a launcher's. Only its status, exit code and duration are recorded." };
        var setupTimeout = new Option<int?>("--setup-timeout") { Description = $"The setup command's deadline in seconds, 1 through {Vericairn.Runtime.Workers.WorkspaceSetupConfiguration.MaximumTimeoutSeconds}; {Vericairn.Runtime.Workers.WorkspaceSetupConfiguration.DefaultTimeoutSeconds} when omitted." };
        var clearSetup = new Option<bool>("--clear-setup") { Description = "Remove the setup command of --launcher or --repository." };
        Options.Add(launcher);
        Options.Add(workspaces);
        Options.Add(model);
        Options.Add(effort);
        Options.Add(models);
        Options.Add(efforts);
        Options.Add(adapter);
        Options.Add(adapterLaunchers);
        Options.Add(repository);
        Options.Add(setupCommand);
        Options.Add(setupTimeout);
        Options.Add(clearSetup);
        SetAction(result =>
        {
            bool launcherChoices = result.GetValue(workspaces) is not null || result.GetValue(model) is not null || result.GetValue(effort) is not null
                || result.GetValue(models) is { Length: > 0 } || result.GetValue(efforts) is { Length: > 0 };
            bool setup = result.GetValue(repository) is not null || result.GetValue(setupCommand) is not null || result.GetValue(setupTimeout) is not null || result.GetValue(clearSetup);
            if (result.GetValue(adapter) is not null || result.GetValue(adapterLaunchers) is { Length: > 0 })
                return operations.Adapter(result.GetValue(adapter), result.GetValue(adapterLaunchers) ?? [], result.GetValue(launcher) is not null || launcherChoices || setup, result.GetValue(context.JsonOption));
            return setup
                ? operations.Setup(result.GetValue(launcher), result.GetValue(repository), result.GetValue(setupCommand), result.GetValue(setupTimeout), result.GetValue(clearSetup),
                    launcherChoices, result.GetValue(context.JsonOption))
                : operations.Configure(result.GetValue(launcher), result.GetValue(model), result.GetValue(effort),
                    result.GetValue(models) ?? [], result.GetValue(efforts) ?? [], result.GetValue(workspaces), result.GetValue(context.JsonOption));
        });
    }
}
