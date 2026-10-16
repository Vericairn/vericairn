// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <summary>Qualifies an installed native harness by an authorized person's local probes, without starting an agent.</summary>
    public WriteOutcome QualifyHarness(HarnessQualifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.Findings.HasErrors) return Refuse("The estate has validation errors; no qualification probes were run.");
            if (session.Manifest.RecordFormat < 2) return Refuse("Harness qualification requires record format 2.");
            if (!HarnessQualificationType.Authorized(model, request.By, DateTimeOffset.UtcNow))
                return Refuse("Only a human operator or a human explicitly granted qualify may qualify a harness; agents and services never may.");
        }
        IAgentLauncher? launcher = request.Launcher is "codex" or "claude-code" ? Providers.Launcher(request.Launcher) : null;
        if (launcher is null) return Refuse("Qualification requires an available codex or claude-code launcher.");
        IReadOnlyDictionary<string, string> settings = request.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (settings.Keys.Any(k => launcher.Descriptor.DeclaredSettings.All(s => s.Name != k))) return Refuse("An unknown launcher setting was supplied.");

        string? scratch = null;
        try
        {
            scratch = Directory.CreateTempSubdirectory("vericairn-qualify-").FullName;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(scratch, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string workspace = Directory.CreateDirectory(Path.Combine(scratch, "workspace")).FullName;
            string mcpFile = Path.Combine(scratch, "mcp.json");
            var environment = WorkerEnvironment.Build(WorkerOptions.Environment ?? WorkerEnvironment.Current(), launcher.EnvironmentNames);
            environment["VERICAIRN_MCP_CONFIG"] = mcpFile;
            var serverEnvironment = WorkerEnvironment.McpServer.Where(environment.ContainsKey).ToDictionary(n => n, n => environment[n], StringComparer.Ordinal);
            IReadOnlyList<string> server = [.. WorkerOptions.VericairnCommand(), "--estate", session.Root, "mcp", "serve", "--as", request.By];
            File.WriteAllText(mcpFile, JsonSerializer.Serialize(new { mcpServers = new { vericairn = new { command = server[0], args = server.Skip(1), env = serverEnvironment } } }));
            var context = new LaunchContext(workspace, "Qualification probes only", "", mcpFile, server, serverEnvironment, settings, environment,
                Execution: JsonSerializer.SerializeToElement(new { requested = new { model = "qualification-probe", effort = "provider-default" } }));
            RunPreflight observed = launcher.Probe(launcher.Prepare(context));
            if (!observed.Complete || !observed.InheritsEnvironment || !HarnessQualificationType.ExactVersion(observed.HarnessVersion)
                || observed.Probes is not { } probes || probes.Digests.Count != HarnessQualificationType.ProbeNames.Length
                || HarnessQualificationType.ProbeNames.Any(n => !probes.Digests.TryGetValue(n, out string? digest) || !ContentHash.IsValid(digest)))
                return Refuse("The launcher did not establish all six qualification probes.");
            var body = new JsonObject
            {
                ["launcher"] = launcher.Launcher, ["version"] = observed.HarnessVersion, ["status"] = "qualified",
                ["probes"] = JsonSerializer.SerializeToNode(probes.Digests),
            };
            lock (gate)
            {
                // Revalidate the person's authority and the whole estate after probing, under the write gate.
                return WriteLocked(new RecordDraft("harness-qualification", request.By, Element(body)), session.Load());
            }
        }
        catch (Exception exception) when (exception is ProviderRefusedException or IOException or UnauthorizedAccessException)
        {
            // A subprocess error may include command arguments, local paths or credentials. Never retain or repeat it.
            return Refuse("The local harness qualification probes failed; no qualification was recorded and no agent was launched.");
        }
        finally
        {
            if (scratch is not null) Directory.Delete(scratch, recursive: true);
        }
    }
}
