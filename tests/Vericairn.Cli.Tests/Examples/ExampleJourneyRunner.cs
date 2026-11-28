// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

internal sealed record ExampleJourneyRun(string Result, IReadOnlyList<ExampleJourneyAttempt> Attempts);
internal sealed record ExampleJourneyAttempt(string Variant, IReadOnlyList<ExampleJourneyStep> Steps, string? Failure);
internal sealed record ExampleJourneyStep(string Step, string Role, string Driver, string Timing, IReadOnlyList<string> StandIns, int? ExitCode = null);

// A product-owned test adapter can seed fixture state or drive a surface such as MCP. The public example repositories supply no code.
internal interface IExampleJourneySurface
{
    string Name { get; }
    CliHarness.Outcome Run(ExampleJourneyRunner.ExampleCommand command, Func<ExampleJourneyRunner.ExampleCommand, CliHarness.Outcome> invoke);
}

internal static class ExampleJourneyRunner
{
    private static readonly Lazy<Dictionary<string, IExampleJourneySurface>> Surfaces = new(() => typeof(ExampleJourneyRunner).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(IExampleJourneySurface).IsAssignableFrom(type))
        .Select(type => (IExampleJourneySurface)Activator.CreateInstance(type, nonPublic: true)!)
        .ToDictionary(surface => surface.Name, StringComparer.Ordinal));

    internal static ExampleJourneyRun Run(ExampleJourney journey, string variant, string? executable = null,
        Func<ExampleCommand, CliHarness.Outcome>? run = null, string? retainedRoot = null,
        Action<IReadOnlyDictionary<string, string>>? onCompleted = null)
    {
        Func<ExampleCommand, CliHarness.Outcome> invoke = run ?? Invoke;
        var attempts = new List<ExampleJourneyAttempt>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string scratch = retainedRoot is null ? CliHarness.TempDirectory("example-journey")
                : Path.Combine(retainedRoot, attempt == 0 ? "journey" : "retry");
            if (retainedRoot is not null && Path.Exists(scratch))
                throw new IOException("The retained journey needs a fresh attempt directory.");
            bool retain = false;
            string? original = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
            var steps = new List<ExampleJourneyStep>();
            try
            {
                var clock = new ClientClock(journey.StartsAt);
                var registry = new ProviderRegistry();
                Composition.Register(registry);
                registry.Register(new FakeReleaseProvider { Now = clock.GetUtcNow });
                var captures = new Dictionary<string, string>(StringComparer.Ordinal) { ["scratch"] = scratch };
                var homes = new HashSet<string>(StringComparer.Ordinal);
                string driver = executable is null ? "in-process" : "process";
                foreach (JourneyStep step in journey.Steps.Where(step => (step.Variant is null || step.Variant == variant)
                    && (step.Driver is null || step.Driver == driver)))
                {
                    string context = step.Context is null ? scratch : Path.Combine(scratch, step.Context);
                    string estate = Path.Combine(context, "estate");
                    string home = Path.Combine(context, "home");
                    Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
                    if (homes.Add(home))
                    {
                        Directory.CreateDirectory(estate);
                        Directory.CreateDirectory(home);
                        new ReleaseUpdateStore().Configure(false);
                    }
                    captures["estate"] = estate;
                    captures["home"] = home;
                    if (step.Files is not null) WriteFixtures(scratch, step.Files, captures);
                    bool instant = executable is not null && step.Clock is not null;
                    if (!instant && step.Clock is int elapsed) clock.Elapse(TimeSpan.FromSeconds(elapsed));
                    string[] command = (instant ? step.InstantArguments! : step.Arguments).Select(arg => ExampleJourney.Expand(arg, captures)).ToArray();
                    string? binary = step.Provider ? null : executable;
                    JourneyExpectation expected = step.Expect;
                    if (binary is not null && command is ["worker", "run", ..])
                    {
                        string[] check = step.ProcessArguments ?? throw new InvalidDataException("The shipped driver only previews an agent step.");
                        command = check.Select(arg => ExampleJourney.Expand(arg, captures)).ToArray();
                        expected = step.ProcessExpect!;
                    }

                    var standIns = new List<string>(3);
                    if (step.Provider)
                    {
                        if (!ExampleJourney.IsProvider(command)) throw new InvalidDataException("The substituted command is outside the fake release provider.");
                        standIns.Add("release provider");
                    }
                    if (step.StandIn is not null && !ExampleJourney.AgentCheck(command))
                    {
                        // The same managed executable handles the launcher's probes and stream on all three platforms.
                        string scenario = step.StandIn == "native-a" ? "native-codex" : "native-claude";
                        string report = Path.Combine(scratch, step.Id + "-launch");
                        command = [.. command, "--setting", "command=" + FakeHarness.Setting(scenario, report)];
                        standIns.Add(step.StandIn);
                    }
                    if (step.Surface is not null) standIns.Add(step.Surface);
                    string stepDriver = step.Surface is not null ? "test surface" : binary is null ? "in-process" : "process";
                    var reportStep = new ExampleJourneyStep(step.Id, step.Role, stepDriver,
                        instant ? "instant" : step.Clock is null ? "ordinary" : "injected clock", standIns);
                    steps.Add(reportStep);
                    var invocation = new ExampleCommand(command, estate, scratch, home, clock, registry, binary, captures, variant, step.Text);
                    CliHarness.Outcome InvokeChecked(ExampleCommand next)
                    {
                        if (next.Executable is not null)
                        {
                            if (next.Arguments is ["worker", "run", ..]) throw new InvalidDataException("The shipped driver only previews an agent step.");
                            if (next.Arguments is ["goal", "step", ..] && !ExampleJourney.AgentCheck(next.Arguments))
                                next = CheckPeopleRoute(invoke, next, step.Id);
                        }
                        return invoke(next);
                    }
                    CliHarness.Outcome outcome = step.Surface is null ? InvokeChecked(invocation)
                        : Surfaces.Value.TryGetValue(step.Surface, out IExampleJourneySurface? surface) ? surface.Run(invocation, InvokeChecked)
                        : throw new InvalidDataException($"Unknown journey test surface '{step.Surface}'.");
                    steps[^1] = reportStep with { ExitCode = outcome.Code };
                    expected.Assert(outcome, captures);
                    if (step.Capture is not null)
                    {
                        JsonNode? output = JsonNode.Parse(outcome.Out);
                        foreach ((string key, string value) in step.Capture)
                        {
                            JsonNode? captured = ExampleJourney.At(output, value);
                            captures.Add(key, captured is JsonValue scalar && scalar.TryGetValue(out string? text) ? text
                                : captured?.ToJsonString() ?? throw new InvalidDataException($"Capture '{key}' is null."));
                        }
                    }
                }
                onCompleted?.Invoke(captures);
                retain = retainedRoot is not null;
                attempts.Add(new(variant, steps, null));
                return new(attempts.Count == 1 ? "passed" : "intermittent", attempts);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException or JsonException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                attempts.Add(new(variant, steps, failure.Message));
            }
            finally
            {
                Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, original);
                if (!retain) CliHarness.Delete(scratch);
            }
        }
        return new("failed", attempts);
    }

    private static void WriteFixtures(string scratch, Dictionary<string, JsonNode?> files, IReadOnlyDictionary<string, string> captures)
    {
        foreach ((string relative, JsonNode? template) in files)
        {
            if (!ExampleJourney.ValidFixturePath(relative)) throw new InvalidDataException("A fixture path must stay inside scratch.");
            string path = Path.Combine(scratch, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonNode? expanded = ExampleJourney.ExpandJson(template, captures);
            File.WriteAllText(path, expanded is JsonValue scalar && scalar.TryGetValue(out string? text) ? text : expanded?.ToJsonString() ?? "null");
        }
    }

    private static ExampleCommand CheckPeopleRoute(Func<ExampleCommand, CliHarness.Outcome> invoke, ExampleCommand command, string id)
    {
        CliHarness.Outcome preview = invoke(command with { Arguments = [.. command.Arguments, "--check"] });
        if (preview.Code != 0 || JsonNode.Parse(preview.Out) is not JsonObject response || response["step"] is not JsonObject step
            || step["ok"] is not JsonValue ok || !ok.TryGetValue(out bool mayStep) || !mayStep
            || step["route"] is not JsonValue route || !route.TryGetValue(out string? name)
            || name is not ("person" or "reserved" or "rule" or "request" or "attention"))
            throw new InvalidDataException("The shipped driver requires a checked non-agent route before taking a step.");
        // The typed-path preview shipped before the CLI could return or consume a basis.
        if (!step.TryGetPropertyValue("basis", out JsonNode? value)) return command;
        if (value is not JsonObject basis) throw new InvalidDataException("The shipped driver received an invalid check basis.");
        string path = Path.Combine(command.Scratch, id + "-basis.json");
        File.WriteAllText(path, basis.ToJsonString());
        return command with { Arguments = [.. command.Arguments, "--basis", path] };
    }

    private static CliHarness.Outcome Invoke(ExampleCommand command)
    {
        string[] args = command.Text ? ["--estate", command.Estate, .. command.Arguments] : ["--estate", command.Estate, "--json", .. command.Arguments];
        if (command.Executable is null)
        {
            if (!File.Exists(Path.Combine(command.Estate, "estate", "manifest.json"))) return CliHarness.Run(args);
            using EstateService api = EstateService.Open(command.Estate);
            api.FlowClock = command.Clock;
            api.Providers = command.Providers;
            return CliHarness.RunWithApi(api, null, args);
        }

        var start = new ProcessStartInfo(command.Executable)
        {
            WorkingDirectory = command.Estate,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        string[] permitted = ["PATH", "SYSTEMROOT", "WINDIR", "COMSPEC", "PATHEXT", "TMP", "TEMP", "TMPDIR",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64"];
        StringComparer names = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (string name in start.Environment.Keys.ToArray())
            if (!permitted.Contains(name, names)) start.Environment.Remove(name);
        start.Environment["HOME"] = command.Home;
        start.Environment["USERPROFILE"] = command.Home;
        start.Environment[VericairnHome.EnvironmentVariable] = command.Home;
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        using Process process = Process.Start(start) ?? throw new IOException("The candidate command line did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("The candidate command line exceeded the step timeout.");
        }
        return new(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    internal sealed record ExampleCommand(string[] Arguments, string Estate, string Scratch, string Home, ClientClock Clock,
        ProviderRegistry Providers, string? Executable, IReadOnlyDictionary<string, string> Captures, string Variant, bool Text);
}
