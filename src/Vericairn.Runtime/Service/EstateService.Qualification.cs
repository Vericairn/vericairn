// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using static Vericairn.Runtime.Service.EstateServiceValues;

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
            (LaunchContext prepared, RunPreflight observed) = ProbeHarness(launcher, settings, scratch, request.By);
            if (observed.Probes is not { } probes || !HarnessAdmission.CompleteProbes(observed, probes))
                return Refuse("The launcher did not establish all six qualification probes.");
            var body = new JsonObject
            {
                ["launcher"] = launcher.Launcher, ["version"] = observed.HarnessVersion, ["status"] = "qualified",
                ["probes"] = JsonSerializer.SerializeToNode(probes.Digests),
            };
            if (session.Manifest.RecordFormat >= 3)
            {
                if (observed.SignIn is { Form: "setup-token" } login)
                {
                    body["login"] = new JsonObject
                    {
                        ["form"] = login.Form, ["accountType"] = login.AccountType, ["accountTypeSource"] = login.AccountTypeSource,
                    };
                }
                // From 0.9.4 (CODE-82): a marker outside the workspace and a test host, read by a probe in the harness's place, with the
                // harness's environment and working directory; the scratch root holds the workspace and the temporary directory, so the
                // marker planted there is outside both.
                ReachMeasurement reach = HarnessReach.Measure(scratch, prepared.Workspace, prepared.Environment, WorkerOptions.VericairnCommand());
                body["reach"] = new JsonObject { ["file"] = reach.OutsideFile, ["host"] = reach.TestHost };
            }
            lock (gate)
            {
                // Revalidate the person's authority and the whole estate after probing, under the write gate.
                return WriteLocked(new RecordDraft("harness-qualification", request.By, Element(body)), session.Load());
            }
        }
        catch (Exception exception) when (ProbeFailure(exception))
        {
            // A subprocess error may include command arguments, local paths or credentials. Never retain or repeat it.
            return Refuse("The local harness qualification probes failed; no qualification was recorded and no agent was launched.");
        }
        finally
        {
            if (scratch is not null) WorkspaceCleanup.Delete(scratch);
        }
    }

    internal HarnessQualificationCheck CheckHarness(string launcherName, IReadOnlyDictionary<string, string> settings)
    {
        EstateModel model = session.Load();
        var check = new HarnessQualificationCheck(launcherName);
        if (model.Findings.HasErrors) return check with { Problem = "The estate has validation errors; no qualification probes were run." };
        if (session.Manifest.RecordFormat < 2) return check with { Problem = "Harness qualification requires record format 2." };
        IAgentLauncher? launcher = launcherName == "claude-code" ? Providers.Launcher(launcherName) : null;
        if (launcher is null) return check with { Problem = "The check requires an available Claude Code launcher." };
        if (settings.Keys.Any(k => launcher.Descriptor.DeclaredSettings.All(s => s.Name != k)))
            return check with { Problem = "An unknown launcher setting was supplied." };

        string? scratch = null;
        try
        {
            WorkerConfiguration? configured = new WorkerConfigurationStore().Read(launcher);
            if (configured is not null)
            {
                // Apply the same Unicode and whole-term matcher used for authored record text to the configured model selector.
                bool blocked = BlockedTerms.Introduced(BlockedTerms.In(model, DateTimeOffset.UtcNow), "", configured.Model) is not null;
                check = check with { Model = configured.Model, ModelStatus = blocked ? "blocked" : "clear" };
            }
            scratch = Directory.CreateTempSubdirectory("vericairn-qualify-").FullName;
            (_, RunPreflight observed) = ProbeHarness(launcher, settings, scratch, by: null, configured);
            if (observed.Probes is not { } probes || !HarnessAdmission.CompleteProbes(observed, probes))
                return check with { Problem = "The launcher did not establish all six qualification probes." };
            check = check with
            {
                InventoryEstablished = true, SignIn = observed.SignIn,
                Version = HarnessAdmission.CompareVersions(model, launcherName, observed.HarnessVersion, probes.BaselineVersion),
            };
            // Reuse dispatch's full exact-version and digest comparison. Version equality alone only tells what doctor can infer.
            try
            {
                _ = HarnessAdmission.Admit(model, launcherName, observed);
                return check with { Qualified = true };
            }
            catch (ProviderRefusedException exception)
            {
                // Admission messages concern digests and versions only; they never use subprocess output.
                return check with { Problem = exception.Message };
            }
        }
        catch (Exception exception) when (ProbeFailure(exception))
        {
            return check with
            {
                Version = ReadHarnessVersion(launcher, settings, model),
                Problem = "The local harness probes failed. Check the executable and the provisioned sign-in.",
            };
        }
        finally
        {
            if (scratch is not null) WorkspaceCleanup.Delete(scratch);
        }
    }

    private HarnessVersionComparison? ReadHarnessVersion(IAgentLauncher launcher, IReadOnlyDictionary<string, string> settings, EstateModel model)
    {
        try
        {
            var environment = WorkerEnvironment.Build(WorkerOptions.Environment ?? WorkerEnvironment.Current(), launcher.EnvironmentNames);
            return launcher.ProbeVersion(settings, environment) is { } version
                ? HarnessAdmission.CompareVersions(model, launcher.Launcher, version.InstalledVersion, version.ShippedVersion)
                : null;
        }
        catch (Exception exception) when (ProbeFailure(exception))
        {
            return null;
        }
    }

    internal HarnessVersionComparison CompareHarnessVersion(string launcher, HarnessVersionObservation observation)
    {
        EstateModel model = session.Load();
        return HarnessAdmission.CompareVersions(model.Findings.HasErrors ? null : model, launcher, observation.InstalledVersion, observation.ShippedVersion);
    }

    private (LaunchContext Prepared, RunPreflight Observed) ProbeHarness(IAgentLauncher launcher,
        IReadOnlyDictionary<string, string> settings, string scratch, string? by, WorkerConfiguration? configured = null)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(scratch, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string workspace = Directory.CreateDirectory(Path.Combine(scratch, "workspace")).FullName;
        string mcpFile = Path.Combine(scratch, "mcp.json");
        var environment = WorkerEnvironment.Build(WorkerOptions.Environment ?? WorkerEnvironment.Current(), launcher.EnvironmentNames);
        environment["VERICAIRN_MCP_CONFIG"] = mcpFile;
        var serverEnvironment = WorkerEnvironment.McpServer.Where(environment.ContainsKey).ToDictionary(n => n, n => environment[n], StringComparer.Ordinal);
        // As at dispatch, the harness is probed with a temporary directory of its own and the MCP server keeps the user's.
        string temporary = Directory.CreateDirectory(Path.Combine(scratch, "tmp")).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        RunDirectory.UseTemporary(environment, serverEnvironment, temporary);
        IReadOnlyList<string> server = [.. WorkerOptions.VericairnCommand(), "--estate", session.Root, "mcp", "serve"];
        if (by is not null) server = [.. server, "--as", by];
        File.WriteAllText(mcpFile, JsonSerializer.Serialize(new { mcpServers = new { vericairn = new { command = server[0], args = server.Skip(1), env = serverEnvironment } } }));
        var context = new LaunchContext(workspace, "Qualification probes only", "", mcpFile, server, serverEnvironment, settings, environment,
            Execution: JsonSerializer.SerializeToElement(new { requested = new { model = configured?.Model ?? "qualification-probe", effort = configured?.Effort ?? "provider-default" } }));
        LaunchContext prepared = launcher.Prepare(context);
        return (prepared, launcher.Probe(prepared));
    }

    private static bool ProbeFailure(Exception exception) =>
        exception is ProviderRefusedException or IOException or UnauthorizedAccessException;
}

/// <summary>What the local, non-recording qualification check established. No raw output or credential values.</summary>
internal sealed record HarnessQualificationCheck(string Launcher, bool InventoryEstablished = false,
    HarnessSignIn? SignIn = null, HarnessVersionComparison? Version = null, string? Model = null, string ModelStatus = "unconfigured",
    bool Qualified = false, string? Problem = null);
