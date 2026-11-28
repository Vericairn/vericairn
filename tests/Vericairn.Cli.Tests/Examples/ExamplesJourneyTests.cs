// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ExamplesJourneyTests
{
    private static string TestRoot()
    {
        for (DirectoryInfo? parent = new(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "Vericairn.slnx"))) return Path.Combine(parent.FullName, "tests", "examples");
        throw new DirectoryNotFoundException("The tests need the repository's examples.");
    }

    private static ExampleJourney Sample() => ExampleJourney.Read(Path.Combine(TestRoot(), "sample.json"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothDriversRunTheSameJourneyFromFreshState(bool process)
    {
        string expectedDriver = process ? "process" : "in-process";
        ExampleJourneyRun run = ExampleJourneyRunner.Run(Sample(), "people", process ? CliHarness.BuiltExecutable : null);
        Assert.Equal("passed", run.Result);
        ExampleJourneyAttempt attempt = Assert.Single(run.Attempts);
        Assert.All(attempt.Steps, step => Assert.Equal(expectedDriver, step.Driver));
        Assert.Equal(process ? "instant" : "injected clock", attempt.Steps[^1].Timing);
    }

    [Theory]
    [InlineData("steps", "[]")]
    [InlineData("format", "\"unknown\"")]
    [InlineData("extra", "true")]
    public void SchemaRejectsInvalidJourneyData(string name, string value)
    {
        string fixture = CliHarness.TempDirectory("journey-schema");
        try
        {
            string path = Path.Combine(fixture, "journey.json");
            JsonObject journey = JsonNode.Parse(File.ReadAllText(Path.Combine(TestRoot(), "sample.json")))!.AsObject();
            journey[name] = JsonNode.Parse(value);
            File.WriteAllText(path, journey.ToJsonString());
            Assert.True(Record.Exception(() => ExampleJourney.Read(path)) is JsonException or InvalidDataException);
        }
        finally { CliHarness.Delete(fixture); }
    }

    [Fact]
    public void WholeJourneyRetryStartsWithFreshStateAndClock()
    {
        ExampleJourney journey = Sample();
        var roots = new List<string>();
        var times = new List<DateTimeOffset>();
        ExampleJourneyRun run = ExampleJourneyRunner.Run(journey, "people", run: invocation =>
        {
            times.Add(invocation.Clock.GetUtcNow());
            string marker = Path.Combine(invocation.Scratch, "marker");
            if (invocation.Arguments is ["estate", "init", ..])
            {
                Assert.False(File.Exists(marker));
                roots.Add(invocation.Scratch);
                File.WriteAllText(marker, "setup finished");
                return new(0, """{"manifest":"estate/manifest.json","operator":"fixture-operator"}""", "");
            }
            Assert.True(File.Exists(marker));
            if (times.Count == 3) return new(1, "{}", "planted first-attempt failure");
            return new(0, """{"estate":{"name":"Smoke sample"}}""", "");
        });

        Assert.Equal("intermittent", run.Result);
        Assert.Equal(2, roots.Distinct().Count());
        Assert.All(roots, root => Assert.False(Directory.Exists(root)));
        DateTimeOffset start = journey.StartsAt;
        Assert.Equal([start, start, start.AddMinutes(1), start, start, start.AddMinutes(1)], times);
        Assert.Contains("planted first-attempt failure", run.Attempts[0].Failure, StringComparison.Ordinal);
        Assert.Null(run.Attempts[1].Failure);
    }

    [Fact]
    public void ASecondFailureFailsTheJourney()
    {
        ExampleJourneyRun run = ExampleJourneyRunner.Run(Sample(), "people", run: _ => new(1, "{}", "planted refusal"));
        Assert.Equal("failed", run.Result);
        Assert.Equal(2, run.Attempts.Count);
        Assert.All(run.Attempts, attempt => Assert.Contains("planted refusal", attempt.Failure, StringComparison.Ordinal));
    }

    [Fact]
    public void TheProcessDriverPreviewsAgentStepsUsingTheChecksOwnVerdict()
    {
        JourneyStep agent = new("run", "builder", ["worker", "run"], new(0),
            ProcessArguments: ["goal", "step", "--check"], ProcessExpect: new(1, Refusal: "operation.exposure", Verdict: "refused"),
            Variant: "agents", StandIn: "native-a");
        ExampleJourney journey = Sample() with { Steps = [agent] };
        ExampleJourneyRun run = ExampleJourneyRunner.Run(journey, "agents", CliHarness.BuiltExecutable, invocation =>
        {
            Assert.Equal(["goal", "step", "--check"], invocation.Arguments);
            return new(1, """{"step":{"problem":{"code":"operation.exposure"},"exposure":{"verdict":"refused"}}}""", "");
        });
        Assert.Equal("passed", run.Result);
        ExampleJourneyStep step = Assert.Single(Assert.Single(run.Attempts).Steps);
        Assert.Equal("process", step.Driver);
        Assert.Empty(step.StandIns);
    }

    [Theory]
    [InlineData("agent", 0, "failed")]
    [InlineData("person", 1, "passed")]
    public void TheShippedDriverOnlyActsOnAConfirmedPeopleRoute(string route, int expectedActions, string verdict)
    {
        JourneyStep person = new("step", "operator", ["goal", "step"], new(0), Variant: "people");
        ExampleJourney journey = Sample() with { Steps = [person] };
        int action = 0;
        ExampleJourneyRun run = ExampleJourneyRunner.Run(journey, "people", CliHarness.BuiltExecutable, invocation =>
        {
            if (invocation.Arguments.Contains("--check"))
                return new(0, JsonSerializer.Serialize(new { step = new { ok = true, route, basis = new { roster = "fixture" } } }, JsonSerializerOptions.Web), "");

            action++;
            Assert.Equal("--basis", invocation.Arguments[^2]);
            Assert.Equal("""{"roster":"fixture"}""", File.ReadAllText(invocation.Arguments[^1]));
            return new(0, "{}", "");
        });
        Assert.Equal(expectedActions, action);
        Assert.Equal(verdict, run.Result);
    }

    [Theory]
    [InlineData("agents", "agent", 0, "failed")]
    [InlineData("agents", "person", 1, "passed")]
    [InlineData("people", "person", 1, "passed")]
    public void TheTypedPathPreviewCanConfirmAPeopleRouteBeforeBasisSupport(string variant, string route, int actions, string result)
    {
        ExampleJourney journey = Sample() with { Steps = [new("take-step", "operator", ["goal", "step"], new(0))] };
        int dispatched = 0;
        ExampleJourneyRun run = ExampleJourneyRunner.Run(journey, variant, CliHarness.BuiltExecutable, invocation =>
        {
            if (invocation.Arguments.Contains("--check"))
                return new(0, JsonSerializer.Serialize(new { step = new { ok = true, route } }, JsonSerializerOptions.Web), "");
            dispatched++;
            Assert.Equal(["goal", "step"], invocation.Arguments);
            return new(0, "{}", "");
        });
        Assert.Equal(result, run.Result);
        Assert.Equal(actions, dispatched);
    }

    [Theory]
    [InlineData("test-local-step", "passed", 1)]
    [InlineData("test-restore", "failed", 0)]
    public void ATestSurfaceCanTakeALocalStepButCannotDispatchThroughTheShippedBinary(string surface, string result, int actions)
    {
        ExampleJourney journey = Sample() with { Steps = [new("take-step", "operator", ["goal", "step"], new(0), Surface: surface)] };
        int dispatched = 0;
        ExampleJourneyRun run = ExampleJourneyRunner.Run(journey, "agents", CliHarness.BuiltExecutable, invocation =>
        {
            if (invocation.Arguments.Contains("--check"))
                return new(0, """{"step":{"ok":true,"route":"agent"}}""", "");
            dispatched++;
            Assert.Null(invocation.Executable);
            return new(0, "{}", "");
        });
        Assert.Equal(result, run.Result);
        Assert.Equal(actions, dispatched);
    }

    [Fact]
    public void ASecondContextSharesCapturesAndFixturesWithItsOwnHome()
    {
        string directory = CliHarness.TempDirectory("journey-context-test");
        try
        {
            string path = Path.Combine(directory, "journey.json");
            File.WriteAllText(path, """
                {
                  "format": "vericairn.example-journey/1", "startsAt": "2026-11-01T00:00:00Z",
                  "steps": [
                    { "id": "initial", "role": "operator", "arguments": ["status"], "expect": { "exitCode": 0 }, "capture": { "value": "/record" } },
                    { "id": "archive-only", "role": "operator", "driver": "process", "arguments": ["status"], "expect": { "exitCode": 42 } },
                    { "id": "restored", "role": "operator", "arguments": ["status", "${home}"], "context": "restored", "surface": "test-restore", "text": true,
                      "files": { "payload/record.json": { "records": ["${value}", "${estate}"] } },
                      "expect": { "exitCode": 0, "fields": { "/records": ["${value}", "${estate}"] }, "outputContains": "${value}" } }
                  ]
                }
                """);
            string? original = null;
            ExampleJourneyRun run = ExampleJourneyRunner.Run(ExampleJourney.Read(path), "agents", run: invocation =>
            {
                if (invocation.Arguments is ["status"])
                {
                    original = invocation.Home;
                    return new(0, """{"record":"fixture-record"}""", "");
                }
                Assert.NotEqual(original, invocation.Home);
                Assert.Equal(Path.Combine(invocation.Scratch, "restored", "home"), invocation.Home);
                Assert.Equal(invocation.Home, invocation.Arguments[1]);
                Assert.True(invocation.Text);
                return new(0, File.ReadAllText(Path.Combine(invocation.Scratch, "payload", "record.json")), "");
            });
            ExampleJourneyAttempt attempt = Assert.Single(run.Attempts);
            Assert.Null(attempt.Failure);
            Assert.Equal(["initial", "restored"], attempt.Steps.Select(step => step.Step).ToArray());
            Assert.Equal("test surface", attempt.Steps[1].Driver);
            Assert.Equal(["test-restore"], attempt.Steps[1].StandIns);
        }
        finally { CliHarness.Delete(directory); }
    }

    private sealed class TestRestoreSurface : IExampleJourneySurface
    {
        public string Name => "test-restore";

        public CliHarness.Outcome Run(ExampleJourneyRunner.ExampleCommand command, Func<ExampleJourneyRunner.ExampleCommand, CliHarness.Outcome> invoke)
            => invoke(command);
    }

    private sealed class TestLocalStepSurface : IExampleJourneySurface
    {
        public string Name => "test-local-step";

        public CliHarness.Outcome Run(ExampleJourneyRunner.ExampleCommand command, Func<ExampleJourneyRunner.ExampleCommand, CliHarness.Outcome> invoke)
            => invoke(command with { Executable = null });
    }

    [Theory]
    [InlineData("native-codex")]
    [InlineData("native-claude")]
    public void StandInsRunAsExecutablesOnThisPlatform(string scenario)
    {
        string scratch = CliHarness.TempDirectory("example-stand-in");
        try
        {
            string receipt = Path.Combine(scratch, "receipt");
            List<string> command = FakeHarness.Command(scenario, receipt);
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = command[0];
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.WorkingDirectory = scratch;
            foreach (string argument in command.Skip(1).Append("--version")) process.StartInfo.ArgumentList.Add(argument);
            Assert.True(process.Start());
            bool exited = process.WaitForExit(10_000);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            Assert.True(exited, "The stand-in did not answer its version probe.");
            Assert.Equal(0, process.ExitCode);
            Assert.True(File.Exists(receipt + ".probed"));
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void TierOneRunsEveryRegisteredJourney()
    {
        if (Environment.GetEnvironmentVariable("VERICAIRN_EXAMPLES_BINARY") is not null) return;
        RunRegistered("tier1", null);
    }

    [Fact]
    public void TierTwoRunsEveryRegisteredJourney()
    {
        string? binary = Environment.GetEnvironmentVariable("VERICAIRN_EXAMPLES_BINARY");
        if (binary is not null) RunRegistered("tier2", binary);
    }

    private static void RunRegistered(string leg, string? binary)
    {
        string root = TestRoot();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "examples.json")));
        var reports = new List<object>();
        bool failed = false;
        foreach (JsonElement entry in manifest.RootElement.GetProperty("examples").EnumerateArray())
        {
            if (!entry.GetProperty("legs").EnumerateArray().Any(item => item.GetString() == leg)) continue;
            string platform = OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "windows";
            if (!entry.GetProperty("platforms").EnumerateArray().Any(item => item.GetString() == platform)) continue;
            string name = entry.GetProperty("key").GetString()!;
            string relative = entry.GetProperty("journey").GetString() ?? throw new InvalidDataException($"Example {name} has no journey.");
            ExampleJourney journey = ExampleJourney.Read(Path.Combine(root, relative));
            string[] variants = entry.TryGetProperty("variants", out JsonElement declared)
                ? [.. declared.EnumerateArray().Select(item => item.GetString()!)]
                : binary is null ? ["agents"] : ["agents", "people"];
            if (variants.Length == 0 || variants.Distinct(StringComparer.Ordinal).Count() != variants.Length
                || variants.Any(variant => variant is not ("agents" or "people")))
                throw new InvalidDataException($"Example {name} needs distinct agents or people variants.");
            foreach (string variant in variants)
            {
                ExampleJourneyRun report = ExampleJourneyRunner.Run(journey, variant, binary);
                reports.Add(new { example = name, variant, report });
                failed |= report.Result == "failed";
            }
        }
        string reportJson = JsonSerializer.Serialize(reports, JsonSerializerOptions.Web);
        Console.WriteLine(reportJson);
        if (reports.Count > 0 && Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { } summary && binary is null)
            File.AppendAllText(summary, "\n### Examples smoke tier 1\n\n    " + reportJson + "\n");
        if (Environment.GetEnvironmentVariable("VERICAIRN_EXAMPLES_JOURNEY_REPORT") is { } path)
            File.WriteAllText(path, reportJson + "\n");
        Assert.False(failed, reportJson);
    }
}
