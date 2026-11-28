// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class WorkerQualifyCommand : Command
{
    public WorkerQualifyCommand(CommandContext context)
        : base("qualify", "Record an authorized human's qualification of one exact installed harness version from local probe digests: version, help-confirmed flags, closed tools, isolated configuration, login boundary and effective MCP. It launches no agent, establishes no live inference or work acceptance, and grants no version range. Admission allows the shipped baseline or a current qualification whose repeated local probes have identical digests. Claude Code setup-token sign-in always requires a format-3 qualification with the operator's account type statement. A superseding record revokes the qualification. The existing inventory policy still applies. Remote cannot qualify a harness: qualification is machine-local. With --check, probe Claude Code's local inventory and report the provisioned sign-in, version qualification and model setting. The check needs no person and writes no record.")
    {
        var operations = new WorkerOperations(context);
        var qualifyLauncher = new Option<string>("--launcher") { Description = "The native launcher: codex or claude-code.", Required = true };
        var qualifyBy = new Option<string?>("--by") { Description = "Required to record a qualification. A human operator or human explicitly granted the qualify scope." };
        var check = new Option<bool>("--check") { Description = "Check the launcher here without recording a qualification or using a person's device." };
        var qualifySettings = new Option<string[]>("--setting") { Description = "Local launcher setting as name=value, including command=<executable or JSON array>; repeatable. Values are never recorded.", AllowMultipleArgumentsPerToken = false };
        Options.Add(qualifyLauncher);
        Options.Add(qualifyBy);
        Options.Add(qualifySettings);
        Options.Add(check);
        Validators.Add(result =>
        {
            bool checking = result.GetValue(check);
            bool hasPerson = result.GetValue(qualifyBy) is not null;
            if (checking && hasPerson) result.AddError("Omit --by when checking a harness.");
            if (checking && result.GetValue(qualifyLauncher) != "claude-code")
                result.AddError("--check supports --launcher claude-code.");
            if (!checking && !hasPerson) result.AddError("--by is required to record a harness qualification. Use --check to probe without recording.");
        });
        SetAction(result =>
        {
            string root = result.GetValue(context.EstateOption)!;
            string launcher = result.GetValue(qualifyLauncher)!;
            string[] settings = result.GetValue(qualifySettings) ?? [];
            bool json = result.GetValue(context.JsonOption);
            return result.GetValue(check) ? operations.WorkerQualifyCheck(root, launcher, settings, json)
                : operations.WorkerQualify(root, launcher, result.GetValue(qualifyBy)!, settings, json);
        });
    }

    internal static string RecordCommand(string launcher) => $"vericairn worker qualify --launcher {launcher} --by <human-principal>";
}
