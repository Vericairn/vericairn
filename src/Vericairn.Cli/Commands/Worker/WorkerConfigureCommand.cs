// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerConfigureCommand : Command
{
    public WorkerConfigureCommand(CommandContext context)
        : base("configure", "Show or replace this machine's workspace root, or a native launcher's configuration with --launcher. --workspaces sets an absolute root for every launcher. With no choices, show it; setting requires both model and effort and replaces the allowlists. Stored owner-only under VericairnHome, outside repositories; no secret values, commands or inventory additions. The CLI and daemon read it for each run; this command calls no API and runs no probes.")
    {
        var operations = new WorkerSettings(context);
        var launcher = new Option<string?>("--launcher") { Description = "The native launcher: codex or claude-code; omitted to show or set the workspace root." };
        var workspaces = new Option<string?>("--workspaces") { Description = "Absolute machine-local workspace root for every launcher, outside repositories, estates and ancestors holding harness configuration. New roots are owner-only. Omit --launcher." };
        var model = new Option<string?>("--model") { Description = "Default portable model selector; required with --effort when setting." };
        var effort = new Option<string?>("--effort") { Description = "Default effort in the launcher's native vocabulary; required with --model when setting." };
        var models = new Option<string[]>("--allow-model") { Description = "Permitted model selector; repeatable, including the default. Omitted: only the default model.", AllowMultipleArgumentsPerToken = false };
        var efforts = new Option<string[]>("--allow-effort") { Description = "Permitted native effort; repeatable, including the default. Omitted: the launcher's supported efforts. Never expands its capabilities.", AllowMultipleArgumentsPerToken = false };
        Options.Add(launcher);
        Options.Add(workspaces);
        Options.Add(model);
        Options.Add(effort);
        Options.Add(models);
        Options.Add(efforts);
        SetAction(result => operations.Configure(result.GetValue(launcher), result.GetValue(model), result.GetValue(effort),
            result.GetValue(models) ?? [], result.GetValue(efforts) ?? [], result.GetValue(workspaces), result.GetValue(context.JsonOption)));
    }
}
