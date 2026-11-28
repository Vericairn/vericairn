// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ExampleWorkbenchCheckpointTests
{
    [Fact]
    public void SaveJourneyCheckpointForWorkbench()
    {
        if (Environment.GetEnvironmentVariable("VERICAIRN_EXAMPLE_WORKBENCH_SCRATCH") is not { } scratch) return;
        using JsonDocument request = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch, "request.json")));
        string journeyFile = request.RootElement.GetProperty("journey").GetString()!;
        string variant = request.RootElement.GetProperty("variant").GetString()!;
        string checkpoint = request.RootElement.GetProperty("checkpoint").GetString()!;
        Assert.Equal(Path.GetFileName(journeyFile), journeyFile);
        Assert.True(variant is "agents" or "people", $"Unknown journey variant '{variant}'.");

        ExampleJourney journey = ExampleJourney.Read(Path.Combine(ExamplesJourneyTests.TestRoot(), journeyFile));
        int index = Array.FindIndex(journey.Steps, step => step.Id == checkpoint
            && (step.Variant is null || step.Variant == variant) && step.Driver is not "process");
        Assert.True(index >= 0, $"No in-process {variant} step '{checkpoint}' in {journeyFile}.");
        ExampleJourneyRun run = ExampleJourneyRunner.Run(journey with { Steps = journey.Steps[..(index + 1)] }, variant,
            retainedRoot: scratch, onCompleted: captures =>
                File.WriteAllText(Path.Combine(scratch, "captures.json"), JsonSerializer.Serialize(captures)));
        Assert.True(run.Result is "passed" or "intermittent", JsonSerializer.Serialize(run.Attempts));
    }
}
