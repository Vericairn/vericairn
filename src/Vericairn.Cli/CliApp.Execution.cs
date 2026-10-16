// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal static partial class CliApp
{
    private static Command WorkerConfigure(Option<bool> jsonOption)
    {
        var command = new Command("configure", "Show or replace this machine's worker configuration for a native launcher. With no choices, show it; setting requires both model and effort and replaces the allowlists. Stored owner-only under VericairnHome, outside repositories; no secret values, commands or inventory additions. The CLI and daemon read it for each run; this command calls no API and runs no probes.");
        var launcher = new Option<string>("--launcher") { Description = "The native launcher: codex or claude-code; also the configuration name.", Required = true };
        var model = new Option<string?>("--model") { Description = "Default portable model selector; required with --effort when setting." };
        var effort = new Option<string?>("--effort") { Description = "Default effort in the launcher's native vocabulary; required with --model when setting." };
        var models = new Option<string[]>("--allow-model") { Description = "Permitted model selector; repeatable, including the default. Omitted: only the default model.", AllowMultipleArgumentsPerToken = false };
        var efforts = new Option<string[]>("--allow-effort") { Description = "Permitted native effort; repeatable, including the default. Omitted: the launcher's supported efforts. Never expands its capabilities.", AllowMultipleArgumentsPerToken = false };
        command.Options.Add(launcher);
        command.Options.Add(model);
        command.Options.Add(effort);
        command.Options.Add(models);
        command.Options.Add(efforts);
        command.SetAction(result => Commands.WorkerConfigure(result.GetValue(launcher)!, result.GetValue(model), result.GetValue(effort),
            result.GetValue(models) ?? [], result.GetValue(efforts) ?? [], result.GetValue(jsonOption)));
        return command;
    }

    private sealed class ExecutionOptions
    {
        private readonly Option<string?> configuration = new("--configuration") { Description = "Named local launcher configuration; required choices never fall back to ambient harness settings." };
        private readonly Option<string[]> execution = new("--execution") { Description = "An explicit execution choice as name=value: model, effort, provider, destination, dataBoundary or adapter=disabled; repeatable. Inherited restrictions still apply.", AllowMultipleArgumentsPerToken = false };
        private readonly Option<long?> inputTokens = new("--input-tokens") { Description = "Context input allocation, within the run's token reservation; not measured consumption." };
        private readonly Option<long?> outputTokens = new("--output-tokens") { Description = "Output allocation, within the run's token reservation; not measured consumption." };
        private readonly Option<long?> contextBytes = new("--context-bytes") { Description = "Maximum canonical context payload bytes, separate from token reservations; mandatory originals and accounting must fit." };
        private readonly Option<string?> contextPackage = new("--context-package") { Description = "Optional expected hash of the context package assembled for this run; a mismatch refuses dispatch." };

        public ExecutionOptions(Command command)
        {
            command.Options.Add(configuration);
            command.Options.Add(execution);
            command.Options.Add(inputTokens);
            command.Options.Add(outputTokens);
            command.Options.Add(contextBytes);
            command.Options.Add(contextPackage);
        }

        public Commands.RunChoices Read(ParseResult result) => new(result.GetValue(configuration), result.GetValue(execution) ?? [],
            result.GetValue(inputTokens), result.GetValue(outputTokens), result.GetValue(contextPackage), result.GetValue(contextBytes));
    }
}
