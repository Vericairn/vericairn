// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Sdk;

namespace Vericairn.Cli;

internal sealed class DoctorCommand : Command
{
    public DoctorCommand(CommandContext context, ReleaseUpdates updates)
        : base("doctor", "Check this machine's prerequisites. With --json, writes one JSON object with the report's facts under stable names: version, runtime, git, dotnet, estate, custody, enclave, helper, presence, records, search, daemon and claudeCode, each an object of state (present, missing, unavailable or not-applicable), value and detail, and stateWarnings, the per-user state whose permissions cannot be restricted. For a configured Claude Code worker, reports installed versus qualified version. " + UpdateNotices.Help)
    {
        var operations = new MachineDiagnostics(context);
        var settings = new Option<string[]>("--setting") { Description = "Claude Code launcher setting command=<executable or JSON array> for its local version probe.", AllowMultipleArgumentsPerToken = false };
        Options.Add(settings);
        SetAction(result =>
        {
            string root = result.GetValue(context.EstateOption)!;
            Dictionary<string, string>? overrides = CommandSettings.Parse(result.GetValue(settings) ?? [], out string? problem);
            if (overrides is null) return context.Fail(problem!);
            if (overrides.Any(kv => kv.Key != "command" || string.IsNullOrWhiteSpace(kv.Value)))
                return context.Fail("Doctor accepts only the Claude Code launcher setting command=<executable or JSON array>.");
            return UpdateNotices.Run(context, updates, () => operations.Doctor(root, result.GetValue(context.JsonOption), overrides), root);
        });
    }
}
