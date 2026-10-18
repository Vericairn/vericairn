// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class RecordsValidateCommand : Command
{
    public RecordsValidateCommand(CommandContext context)
        : base("validate", "Parse and validate every record and the manifest of an estate.")
    {
        SetAction(result =>
        {
            string root = result.GetValue(context.EstateOption)!;
            bool json = result.GetValue(context.JsonOption);
            return CommandContext.IsEstate(root)
                ? context.WithApi(root, api => context.ReportFindings(api.Validate(), json))
                : context.WithFiles(root, model => context.ReportFindings(CommandContext.ToFindings(model.Findings, $"{model.Records.Count} records"), json));
        });
    }
}
