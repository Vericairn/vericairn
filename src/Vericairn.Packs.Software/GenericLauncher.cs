// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Packs.Software;

/// <summary>
/// The generic launcher (vericairn.launcher.agent/1) for any MCP-capable harness: it starts the command a run names, in the workspace,
/// and tells it where the brief, the MCP configuration and the stop file are. Generic process launch is not complete agent support:
/// it knows nothing of the harness's goal, sign-in, usage or stop, so it is labeled so wherever it is listed or run.
/// </summary>
public sealed class GenericLauncher : IAgentLauncher
{
    /// <summary>The launcher id recorded as the provider of every run it launches.</summary>
    public const string LauncherId = "generic";

    /// <inheritdoc />
    public string Launcher => LauncherId;

    /// <inheritdoc />
    public bool Generic => true;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "generic-process",
        IAgentLauncher.Contract,
        "implemented",
        ["launch"],
        [
            "Generic process launch is not complete agent support: it starts the command a run names in the workspace, and knows nothing of the harness's goal, sign-in, usage or stop.",
            "Tells the process the condition, the brief, the MCP configuration and the stop file in VERICAIRN_CONDITION, VERICAIRN_BRIEF, VERICAIRN_MCP_CONFIG and VERICAIRN_STOP_FILE, and in its arguments where they name {condition}, {brief}, {mcp-config} or {workspace}.",
            "Reads no usage, so every unit the run's cap states other than its wall minutes is charged in full.",
        ],
        [
            new ProviderSetting("command", "The harness's executable, or a JSON array of it and its leading arguments.", true),
            new ProviderSetting("arguments", "A JSON array of the arguments after the command; {condition}, {brief}, {mcp-config} and {workspace} in them are replaced.", false),
        ]);

    /// <inheritdoc />
    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (HarnessProcess.Problem(settings, null) is string problem)
        {
            return problem;
        }

        try
        {
            Arguments(settings);
            return null;
        }
        catch (ProviderRefusedException exception)
        {
            return exception.Message;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Command(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            .. HarnessProcess.Command(context.Settings, null),
            .. Arguments(context.Settings).Select(a => a
                .Replace("{condition}", context.Condition, StringComparison.Ordinal)
                .Replace("{brief}", context.Brief, StringComparison.Ordinal)
                .Replace("{mcp-config}", context.McpConfiguration, StringComparison.Ordinal)
                .Replace("{workspace}", context.Workspace, StringComparison.Ordinal)),
        ];
    }

    private static List<string> Arguments(IReadOnlyDictionary<string, string> settings) =>
        settings.TryGetValue("arguments", out string? arguments) && !string.IsNullOrWhiteSpace(arguments)
            ? HarnessProcess.Strings(arguments.Trim(), "arguments") ?? throw new ProviderRefusedException("The arguments setting is a JSON array of one or more non-empty strings.")
            : [];
}
