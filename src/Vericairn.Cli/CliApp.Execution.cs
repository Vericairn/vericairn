// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal static partial class CliApp
{
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
