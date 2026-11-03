// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Encodings.Web;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class WorkerSettings(CommandContext context)
{
    // A command is shown as it would be typed back, without escaping quotes or plus signs.
    private static readonly JsonSerializerOptions Shown = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public int Adapter(string? adapter, string[] launchers, bool otherChoices, bool json)
    {
        if (Environment.GetEnvironmentVariable("VERICAIRN_RUN") is not null)
            return context.Fail("Only the operator outside a worker run may set efficiency-adapter admission.");
        if (adapter is null || otherChoices) return context.Fail("Set --adapter and --allow-launcher separately from launcher defaults and workspace placement.");
        try
        {
            var permission = new EfficiencyAdapterConfiguration { Adapter = adapter, Launchers = launchers };
            new WorkerConfigurationStore().WriteAdapter(permission);
            if (json) context.Output.WriteLine(JsonSerializer.Serialize(permission, ProtocolJson.Options));
            else context.Output.WriteLine("efficiency-adapter: " + adapter + "; allowed launchers: " + string.Join(", ", launchers)
                + ". Runs remain disabled unless explicitly selected. Do not enable before its security review.");
            return Clean;
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException or ArgumentException)
        { return context.Fail(exception.Message); }
    }

    public int Configure(string? launcher, string? model, string? effort, string[] models, string[] efforts, string? workspaces, bool json)
    {
        bool setting = model is not null || effort is not null || models.Length > 0 || efforts.Length > 0;
        if (workspaces is not null && (launcher is not null || setting)) return context.Fail("Set --workspaces separately from launcher choices; the workspace root applies to every launcher.");
        if (setting && (launcher is null || model is null || effort is null)) return context.Fail("Setting worker configuration requires --launcher, --model and --effort; omit choices to show it.");
        try
        {
            var store = new WorkerConfigurationStore();
            if (launcher is null)
            {
                if (workspaces is not null) store.WriteWorkspaceRoot(workspaces);
                string? root = store.ReadWorkspaceRoot();
                IReadOnlyList<WorkspaceSetupEntry> setup = store.ListSetup();
                if (json) context.Output.WriteLine(JsonSerializer.Serialize(new { root, setup = setup.Select(Row) }, ProtocolJson.Options));
                else
                {
                    context.Output.WriteLine(root is null
                        ? "Workspace root: default, under VericairnHome/estates/<estate>/workspaces. Set it with vericairn worker configure --workspaces <dir>."
                        : "Workspace root: " + root + " (every launcher; <estate>/<branch> beneath it).");
                    if (setup.Count == 0) context.Output.WriteLine("Workspace setup: none. Set one with vericairn worker configure --repository <resource> --setup-command <executable or JSON array>.");
                    foreach (WorkspaceSetupEntry entry in setup) context.Output.WriteLine(Describe(entry));
                }

                return Clean;
            }
            IAgentLauncher? implementation = ProviderRegistry.Current.Launcher(launcher);
            if (implementation is null || launcher is not ("codex" or "claude-code")) return context.Fail("Worker configuration supports only codex and claude-code.");
            if (setting) store.Write(implementation, new(model!, effort!, models.Length == 0 ? null : models, efforts.Length == 0 ? null : efforts));
            WorkerConfiguration? configuration = store.Read(implementation);
            if (json) context.Output.WriteLine(JsonSerializer.Serialize(configuration, ProtocolJson.Options));
            else if (configuration is null) context.Output.WriteLine($"No configuration for {launcher}. Set it with vericairn worker configure --launcher {launcher} --model <model> --effort <effort>.");
            else
            {
                context.Output.WriteLine($"{launcher}: model {configuration.Model}, effort {configuration.Effort}");
                context.Output.WriteLine("  allowed models: " + string.Join(", ", configuration.AllowedModels ?? [configuration.Model]));
                context.Output.WriteLine("  allowed efforts: " + (configuration.AllowedEfforts is null ? "the launcher's supported efforts" : string.Join(", ", configuration.AllowedEfforts)));
            }
            return Clean;
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    /// <summary>Sets, clears or shows the workspace setup command of one launcher or one repository resource on this machine.</summary>
    public int Setup(string? launcher, string? repository, string? command, int? timeout, bool clear, bool otherChoices, bool json)
    {
        if (Environment.GetEnvironmentVariable("VERICAIRN_RUN") is not null)
            return context.Fail("A workspace setup command runs outside the agent's sandbox, so only a person outside a worker run may configure one.");
        if (otherChoices) return context.Fail("Set a workspace setup command separately from launcher defaults and workspace placement.");
        if ((launcher is null) == (repository is null)) return context.Fail("A workspace setup command belongs to exactly one --launcher or --repository.");
        if (clear && (command is not null || timeout is not null)) return context.Fail("--clear-setup removes the setup command; give it without --setup-command or --setup-timeout.");
        if (timeout is not null && command is null) return context.Fail("--setup-timeout is the deadline of the --setup-command it is given with.");
        if (launcher is not null && ProviderRegistry.Current.Launcher(launcher) is null)
            return context.Fail($"No launcher '{launcher}' is available here; available: {string.Join(", ", ProviderRegistry.Current.Launchers.Select(l => l.Launcher))}.");
        try
        {
            var store = new WorkerConfigurationStore();
            bool removed = clear && store.ClearSetup(launcher, repository);
            if (command is not null)
            {
                store.WriteSetup(launcher, repository, new WorkspaceSetupConfiguration
                {
                    Command = WorkerConfigurationStore.SetupCommand(command),
                    TimeoutSeconds = timeout ?? WorkspaceSetupConfiguration.DefaultTimeoutSeconds,
                });
            }

            WorkspaceSetupEntry entry = store.ListSetup().FirstOrDefault(e => e.Launcher == launcher && e.Repository == repository) ?? new(launcher, repository, null);
            if (json) context.Output.WriteLine(JsonSerializer.Serialize(Row(entry), ProtocolJson.Options));
            else
            {
                if (clear) context.Output.WriteLine(removed ? "Removed the workspace setup command." : "There was no workspace setup command to remove.");
                context.Output.WriteLine(Describe(entry));
            }

            return Clean;
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    private static object Row(WorkspaceSetupEntry entry) => new
    {
        launcher = entry.Launcher,
        repository = entry.Repository,
        command = entry.Setup?.Command,
        timeoutSeconds = entry.Setup?.TimeoutSeconds,
        problem = entry.Problem,
    };

    private static string Describe(WorkspaceSetupEntry entry)
    {
        string owner = entry.Launcher is not null ? "launcher " + entry.Launcher : "repository " + entry.Repository;
        return entry.Setup is { } setup ? $"Workspace setup for {owner}: {JsonSerializer.Serialize(setup.Command, Shown)}, deadline {setup.TimeoutSeconds} seconds."
            : entry.Problem is not null ? $"Workspace setup for {owner}: {entry.Problem}"
            : $"Workspace setup for {owner}: none.";
    }
}
