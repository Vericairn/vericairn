// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Workers;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class EfficiencyAdapterCommandTests
{
    [Fact]
    public void OnlyTheOperatorOutsideARunCanNameTheAdapterAndAllowedLaunchers()
    {
        _ = CliHarness.Run("--help"); // Initializes the harness's isolated home before any configuration access.
        string? previousRun = Environment.GetEnvironmentVariable("VERICAIRN_RUN");
        try
        {
            Environment.SetEnvironmentVariable("VERICAIRN_RUN", null);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--adapter", "local-lossless").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--adapter", "local-lossless", "--allow-launcher", "generic").Code);
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--allow-launcher", "codex").Code);
            CliHarness.Outcome allowed = CliHarness.Run("--json", "worker", "configure", "--adapter", "local-lossless", "--allow-launcher", "codex", "--allow-launcher", "claude-code");
            Assert.Equal(0, allowed.Code);
            Assert.Equal("local-lossless", allowed.Json.GetProperty("adapter").GetString());
            Assert.Equal(2, allowed.Json.GetProperty("launchers").GetArrayLength());
            Assert.Equal(2, CliHarness.Run("worker", "configure", "--adapter", "local-lossless", "--allow-launcher", "codex", "--launcher", "codex").Code);
            Environment.SetEnvironmentVariable("VERICAIRN_RUN", "test-run");
            CliHarness.Outcome refused = CliHarness.Run("worker", "configure", "--adapter", "disabled");
            Assert.Equal(2, refused.Code);
            Assert.Contains("Only the operator", refused.Err, StringComparison.Ordinal);
            Assert.Equal("local-lossless", new WorkerConfigurationStore().ReadAdapter()!.Adapter);
            Environment.SetEnvironmentVariable("VERICAIRN_RUN", null);
            Assert.Equal(0, CliHarness.Run("worker", "configure", "--adapter", "disabled").Code);
            Assert.Empty(new WorkerConfigurationStore().ReadAdapter()!.Launchers);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VERICAIRN_RUN", previousRun);
            new WorkerConfigurationStore().WriteAdapter(new());
        }
    }
}
