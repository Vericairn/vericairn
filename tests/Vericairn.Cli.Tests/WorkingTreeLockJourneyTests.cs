// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;

namespace Vericairn.Cli.Tests;

/// <summary>Two embedded command-line runtimes with their own Vericairn homes write one working tree one at a time (FIX-99, issue #312).</summary>
[Collection("cli")]
public sealed class WorkingTreeLockJourneyTests
{
    public WorkingTreeLockJourneyTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public async Task TwoProcessesWithTheirOwnHomesWriteOneIdentityOnceAndDifferentIdentitiesBoth()
    {
        using var estate = new TestEstate(3);
        string scratch = CliHarness.TempDirectory("tree-lock");
        try
        {
            string[] homes = [Path.Combine(scratch, "home-a"), Path.Combine(scratch, "home-b")];
            foreach (string home in homes) new ReleaseUpdateStore(home).Configure(false);

            for (int round = 1; round <= 3; round++)
            {
                string same = Body(scratch, $"same-{round}.json", $"synthetic-collision:vault/once-{round}");
                CliHarness.Outcome[] collided = await Task.WhenAll(Write(estate, homes[0], same), Write(estate, homes[1], same));
                CliHarness.Outcome written = Assert.Single(collided, o => o.Code == 0);
                CliHarness.Outcome refused = Assert.Single(collided, o => o.Code != 0);
                Assert.Contains("operation.duplicate-identity", refused.Out + refused.Err, StringComparison.Ordinal);
                Assert.NotNull(written.Id);
            }

            CliHarness.Outcome[] separate = await Task.WhenAll(
                Write(estate, homes[0], Body(scratch, "a.json", "synthetic:a")), Write(estate, homes[1], Body(scratch, "b.json", "synthetic:b")));
            Assert.All(separate, o => Assert.True(o.Code == 0, o.Out + o.Err));

            Assert.Equal(5, estate.Load().OfType("operation").Count());
            Assert.Equal(0, CliHarness.Run("--estate", estate.Root, "records", "validate").Code);
        }
        finally
        {
            CliHarness.Delete(scratch);
        }
    }

    private static string Body(string directory, string name, string identity) => CliHarness.WriteJson(directory, name, JsonSerializer.Serialize(new
    {
        provider = "github-releases", action = "dispatch-release-workflow", identity,
        intent = "Recorded only, never dispatched.",
        target = new { repository = "Example/widget", workflow = "release.yml", @ref = "main", version = "0.2.0" },
        status = "not-started", evidence = Array.Empty<object>(),
    }));

    private static async Task<CliHarness.Outcome> Write(TestEstate estate, string home, string body)
    {
        var info = new ProcessStartInfo(CliHarness.BuiltExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--estate", estate.Root, "--json", "records", "new", "operation", "--by", estate.OperatorId, "--body", body })
            info.ArgumentList.Add(argument);
        info.Environment[VericairnHome.EnvironmentVariable] = home;
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
