// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli;

internal static partial class Commands
{
    public static int WorkerConfigure(string? launcher, string? model, string? effort, string[] models, string[] efforts, string? workspaces, bool json)
    {
        bool setting = model is not null || effort is not null || models.Length > 0 || efforts.Length > 0;
        if (workspaces is not null && (launcher is not null || setting)) return Fail("Set --workspaces separately from launcher choices; the workspace root applies to every launcher.");
        if (setting && (launcher is null || model is null || effort is null)) return Fail("Setting worker configuration requires --launcher, --model and --effort; omit choices to show it.");
        try
        {
            var store = new WorkerConfigurationStore();
            if (launcher is null)
            {
                if (workspaces is not null) store.WriteWorkspaceRoot(workspaces);
                string? root = store.ReadWorkspaceRoot();
                if (json) Console.Out.WriteLine(JsonSerializer.Serialize(new { root }, ProtocolJson.Options));
                else Console.Out.WriteLine(root is null
                    ? "Workspace root: default, under VericairnHome/estates/<estate>/workspaces. Set it with vericairn worker configure --workspaces <dir>."
                    : "Workspace root: " + root + " (every launcher; <estate>/<branch> beneath it).");
                return Clean;
            }
            IAgentLauncher? implementation = ProviderRegistry.Current.Launcher(launcher);
            if (implementation is null || launcher is not ("codex" or "claude-code")) return Fail("Worker configuration supports only codex and claude-code.");
            if (setting) store.Write(implementation, new(model!, effort!, models.Length == 0 ? null : models, efforts.Length == 0 ? null : efforts));
            WorkerConfiguration? configuration = store.Read(implementation);
            if (json) Console.Out.WriteLine(JsonSerializer.Serialize(configuration, ProtocolJson.Options));
            else if (configuration is null) Console.Out.WriteLine($"No configuration for {launcher}. Set it with vericairn worker configure --launcher {launcher} --model <model> --effort <effort>.");
            else
            {
                Console.Out.WriteLine($"{launcher}: model {configuration.Model}, effort {configuration.Effort}");
                Console.Out.WriteLine("  allowed models: " + string.Join(", ", configuration.AllowedModels ?? [configuration.Model]));
                Console.Out.WriteLine("  allowed efforts: " + (configuration.AllowedEfforts is null ? "the launcher's supported efforts" : string.Join(", ", configuration.AllowedEfforts)));
            }
            return Clean;
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fail(exception.Message);
        }
    }
}
