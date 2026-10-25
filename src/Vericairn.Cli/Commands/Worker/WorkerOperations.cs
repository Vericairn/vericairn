// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class WorkerOperations(CommandContext context)
{
    private static readonly string[] ExecutionCapabilityLists = ["scopes", "efforts", "providers", "destinations", "dataBoundaries"];

    /// <summary>Lists the coding-agent launchers, labeling a generic one as not complete agent support.</summary>
    public int WorkerLaunchers(string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<ProviderRow> rows = api.Launchers();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(rows, ProtocolJson.Options));
            return Clean;
        }

        if (rows.Count == 0)
        {
            context.Output.WriteLine("No coding-agent launcher is available to the runtime here.");
            return Clean;
        }

        foreach (ProviderRow row in rows)
        {
            context.Output.WriteLine($"{row.Provider}  {row.Implementation}  {row.Contract}  {row.Status}{(row.Provider == "generic" ? "  (" + Vericairn.Runtime.Workers.IAgentLauncher.GenericLabel + ")" : string.Empty)}");
            if (row.Execution is JsonElement execution)
            {
                foreach (string field in ExecutionCapabilityLists)
                {
                    context.Output.WriteLine($"  {field}: {string.Join(", ", execution.GetProperty(field).EnumerateArray().Select(v => v.GetString()))}");
                }
                context.Output.WriteLine($"  complete environment inventory: {execution.GetProperty("closedEnvironment").GetBoolean()}");
            }
            foreach (ProviderSettingRow setting in row.Settings)
            {
                context.Output.WriteLine($"  {setting.Name,-10} {(setting.Required ? "required" : setting.Default is null ? "optional" : "default " + setting.Default)}: {setting.Summary}");
            }

            foreach (string limitation in row.Limitations)
            {
                context.Output.WriteLine($"  limitation: {limitation}");
            }
        }

        return Clean;
    });

    /// <summary>Records a human's exact-version native harness qualification from local probes, without launching an agent.</summary>
    public int WorkerQualify(string root, string launcher, string by, string[] options, bool json)
    {
        Dictionary<string, string>? settings = CommandSettings.Parse(options, out string? problem);
        return settings is null ? context.Fail(problem!) : context.WithApi(root, api => context.ReportOutcome(api.QualifyHarness(new(launcher, by, settings)), json));
    }

    /// <summary>
    /// Runs a coding agent on an assignment: starts the run, then waits in the foreground until it settles and reports its usage, a
    /// Ctrl+C asking it to stop; with --detach, returns once the daemon serving the estate has launched it.
    /// </summary>
    public int WorkerRun(string root, WorkerRunOptions options, bool json)
    {
        ArgumentNullException.ThrowIfNull(options);
        Dictionary<string, string>? settings = CommandSettings.Parse(options.Settings, out string? problem);
        if (settings is null || !RunChoices.TryParse(options.Choices, out JsonElement? execution, out JsonElement? allocation, out problem))
        {
            return context.Fail(problem!);
        }

        return context.WithApi(root, api =>
        {
            if (options.Detach && !context.UsingDaemon)
            {
                return context.Fail("--detach leaves the run to the daemon that serves the estate, and no daemon serves it here: start one with vericairn runtime start, or run in the foreground.");
            }

            RunResponse started = api.RunStart(new RunStartRequest(options.Assignment, options.Launcher, options.Agent, options.By, options.Repository, options.Goal, options.Branch, options.Base, options.WallMinutes, settings,
                options.Choices?.Configuration, execution, allocation, options.Choices?.ContextPackage));
            if (!started.Ok)
            {
                return started.Problem is { Findings.Count: > 0 } rejected ? context.ReportOutcome(WriteOutcome.Fail(rejected), json) : context.Fail(started.Message);
            }

            if (!json)
            {
                context.Output.WriteLine($"run {started.Run}  {started.Status}  {started.Workspace}");
                context.Output.WriteLine($"  cap {RunConsole.Units(started.Cap)}");
                context.Output.WriteLine("  " + started.Message);
            }

            if (options.Detach)
            {
                if (json)
                {
                    context.Output.WriteLine(JsonSerializer.Serialize(started, ProtocolJson.Options));
                }

                return Clean;
            }

            RunRow? row = RunConsole.Follow(context, api, started.Run, options.By);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(row, ProtocolJson.Options));
            }
            else
            {
                RunConsole.PrintSettled(context, row);
            }

            return row?.Status == "observed-success" ? Clean : HasErrors;
        });
    }

    /// <summary>Asks a run to stop and prints the facts observed; a stop that was not observed is printed as requested.</summary>
    public int WorkerStop(string root, string run, string by, string? reason, int? grace, bool json) => context.WithApi(root, api =>
    {
        RunStopResponse response = api.RunStop(run, new RunStopRequest(by, reason, grace));
        if (response.Problem is not null && response.Stop is null)
        {
            return context.Fail(response.Message);
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else
        {
            context.Output.WriteLine($"run {response.Run}  stop {response.State}");
            if (response.Stop is not null)
            {
                RunConsole.PrintStop(context, response.Stop);
            }

            context.Output.WriteLine("  " + response.Message);
        }

        return response.Ok ? Clean : HasErrors;
    });

    /// <summary>Retries an exited run's settlement from retained facts and reports any unresolved reason.</summary>
    public int WorkerSettle(string root, string run, string by, bool json) => context.WithApi(root, api =>
    {
        RunSettleResponse response = api.RunSettle(run, new RunSettleRequest(by));
        if (json) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        else
        {
            context.Output.WriteLine($"run {response.Run}  {response.Status}");
            context.Output.WriteLine("  " + response.Message);
        }
        return response.Ok ? Clean : HasErrors;
    });

    /// <summary>Lists the agent runs with their caps, usage, charges and stops, and the ledgers of their goals.</summary>
    public int WorkerRuns(string root, bool json) => context.WithApi(root, api =>
    {
        RunsResponse response = api.Runs();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            return Clean;
        }

        if (response.Runs.Count == 0)
        {
            context.Output.WriteLine("No agent runs.");
        }

        foreach (RunRow row in response.Runs)
        {
            RunConsole.PrintRun(context, row);
        }

        foreach (LedgerRow ledger in response.Goals)
        {
            context.Output.WriteLine($"goal {ledger.Goal}  {ledger.Status}  {ledger.Title}");
            context.Output.WriteLine($"  budget {string.Join(", ", ledger.Units.Select(u => $"{u.Unit} {u.Spent} of {u.Budget}{(u.Currency is null ? string.Empty : " " + u.Currency)} charged, {u.Remaining} remain"))}; {ledger.InFlight} of {ledger.Concurrency} runs in flight; {ledger.AttemptsPerStep} attempts per step");
            foreach (string fence in ledger.Fenced)
            {
                context.Output.WriteLine($"  fenced: {fence}");
            }
        }

        return Clean;
    });
}
