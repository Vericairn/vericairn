// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class McpServerTemporaryDirectoryTests
{
    private static readonly StringComparison Comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    [Fact]
    public void TheServerIsGivenTheWorkersTemporaryDirectoryWhenTheWorkerNamesNone()
    {
        string run = RunTemporary();
        var harness = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = "/usr/bin" };
        var server = new Dictionary<string, string>(StringComparer.Ordinal);
        RunDirectory.UseTemporary(harness, server, run);
        string resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        foreach (string name in RunDirectory.TemporaryVariables)
        {
            Assert.Equal(run, harness[name]);
            Assert.Equal(resolved, server[name]);
        }
    }

    [Fact]
    public void TheWorkersOwnTemporaryDirectoryIsKeptForTheServer()
    {
        string run = RunTemporary();
        string shared = Path.Combine(Path.GetTempPath(), "vericairn-worker-" + Guid.NewGuid().ToString("N"));
        var harness = RunDirectory.TemporaryVariables.ToDictionary(n => n, _ => shared, StringComparer.Ordinal);
        var server = new Dictionary<string, string>(StringComparer.Ordinal);
        RunDirectory.UseTemporary(harness, server, run);
        foreach (string name in RunDirectory.TemporaryVariables)
        {
            Assert.Equal(shared, server[name]);
            Assert.Equal(run, harness[name]);
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("inside")]
    [InlineData("empty")]
    [InlineData("relative")]
    public void TheServerIsNeverGivenTheRunsDirectoryOrAnUnusableValue(string kind)
    {
        string run = RunTemporary();
        string value = kind switch
        {
            "run" => run,
            "inside" => Path.Combine(run, "nested"),
            "empty" => "",
            _ => "relative-temporary",
        };
        var harness = RunDirectory.TemporaryVariables.ToDictionary(n => n, _ => value, StringComparer.Ordinal);
        var server = new Dictionary<string, string>(StringComparer.Ordinal);
        RunDirectory.UseTemporary(harness, server, run);
        foreach (string name in RunDirectory.TemporaryVariables)
        {
            Assert.True(Path.IsPathFullyQualified(server[name]), server[name]);
            Assert.False(Within(server[name], run), server[name]);
        }
    }

    [Fact]
    public void DispatchNamesTheServersTemporaryDirectoryWhenTheWorkerEnvironmentHasNone()
    {
        using var run = new RunEstate();
        var environment = new Dictionary<string, string>(run.Service.WorkerOptions.Environment!, StringComparer.Ordinal);
        foreach (string name in RunDirectory.TemporaryVariables) environment.Remove(name);
        run.Service.WorkerOptions = run.Service.WorkerOptions with { Environment = environment };
        string report = run.Report("no-temporary");
        File.WriteAllText(report + ".command", "mkdir -p \"$TMPDIR/x\"");
        RunResponse started = run.Start("codex", "watch-codex-command-input", report);
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("observed-success", settled.Status);

        var directory = new RunDirectory(run.Service.Session.Manifest.Estate, started.Run);
        using JsonDocument launched = JsonDocument.Parse(File.ReadLines(report).Last());
        Assert.Equal(directory.Temporary, launched.RootElement.GetProperty("environment").GetProperty("TMPDIR").GetString());
        using JsonDocument mcp = JsonDocument.Parse(File.ReadAllText(directory.McpConfiguration));
        JsonElement server = mcp.RootElement.GetProperty("mcpServers").GetProperty("vericairn").GetProperty("env");
        foreach (string name in RunDirectory.TemporaryVariables)
        {
            string? value = server.GetProperty(name).GetString();
            Assert.False(string.IsNullOrEmpty(value));
            Assert.False(Within(value!, directory.Temporary), value);
        }
        Assert.Equal(0, run.Service.Check().Errors);
    }

    private static string RunTemporary() => Path.Combine(Path.GetTempPath(), "vericairn-run-" + Guid.NewGuid().ToString("N"), "tmp");

    private static bool Within(string path, string run)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(run));
        return full.Equals(root, Comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    }
}
