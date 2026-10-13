// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Software;

/// <summary>What running a check command observed.</summary>
/// <param name="Command">The command line as run.</param>
/// <param name="ExitCode">Exit code; null when the command did not finish.</param>
/// <param name="DurationMs">Elapsed time.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
/// <param name="Output">Combined output bytes.</param>
/// <param name="Outcome">passed, failed or unknown.</param>
/// <param name="Summary">What was observed.</param>
public sealed record CheckRun(string Command, int? ExitCode, long DurationMs, string StartedAt, string FinishedAt, byte[] Output, string Outcome, string Summary)
{
    /// <summary>sha256:hex of the output.</summary>
    public string OutputHash => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Output));

    /// <summary>The last part of the output, for a summary.</summary>
    public string OutputTail(int characters = 400)
    {
        string text = Encoding.UTF8.GetString(Output).Trim();
        return text.Length <= characters ? text : "…" + text[^characters..];
    }
}

/// <summary>Runs checks and shapes verification records: the exact commit, the method, the environment, the observed result; a claim to inspect, never permission.</summary>
public static class Checks
{
    /// <summary>Runs a command in a directory and observes its exit code and output; a command that does not finish is unknown, not failed.</summary>
    public static CheckRun Run(string workingDirectory, IReadOnlyList<string> command, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
        {
            throw new ArgumentException("A command names at least the program.", nameof(command));
        }

        var info = new ProcessStartInfo(command[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = workingDirectory };
        foreach (string argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        string line = string.Join(' ', command);
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using Process process = Process.Start(info) ?? throw new ProviderRefusedException($"{command[0]} did not start.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            bool finished = process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
            if (!finished)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            byte[] output = Encoding.UTF8.GetBytes(standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult());
            stopwatch.Stop();
            DateTimeOffset ended = DateTimeOffset.UtcNow;
            if (!finished)
            {
                return new CheckRun(line, null, stopwatch.ElapsedMilliseconds, RecordParser.FormatTimestamp(started), RecordParser.FormatTimestamp(ended), output, "unknown", $"{line} did not finish within {timeout.TotalSeconds:F0} seconds and was stopped; its result is unknown.");
            }

            string outcome = process.ExitCode == 0 ? "passed" : "failed";
            return new CheckRun(line, process.ExitCode, stopwatch.ElapsedMilliseconds, RecordParser.FormatTimestamp(started), RecordParser.FormatTimestamp(ended), output, outcome, $"{line} exited {process.ExitCode} after {stopwatch.Elapsed.TotalSeconds:F1} seconds.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ProviderRefusedException($"{command[0]} is not on the PATH or cannot be started: {exception.Message}", exception);
        }
    }

    /// <summary>Facts about where a check ran that are the same on every clone: platform and runtime, never a path or a user name.</summary>
    public static IReadOnlyDictionary<string, string> LocalEnvironment(IReadOnlyDictionary<string, string>? extra = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["platform"] = Records.Signing.VericairnHome.Platform(),
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ["os"] = RuntimeInformation.OSDescription,
            ["vericairn"] = Service.EstateService.Version,
        };
        foreach ((string key, string value) in extra ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            environment[key] = value;
        }

        return environment;
    }

    /// <summary>One piece of evidence of a verification.</summary>
    /// <param name="Summary">What it is.</param>
    /// <param name="Hash">Its hash, when stored.</param>
    /// <param name="Location">Where it is, when in the repository or at a URL.</param>
    public sealed record Evidence(string Summary, string? Hash = null, string? Location = null);

    /// <summary>The verification record body.</summary>
    public static JsonObject VerificationBody(string method, string commit, string? command, IReadOnlyDictionary<string, string> environment, string role, string outcome, string summary, int? exitCode, long? durationMs, string? startedAt, string? finishedAt, IReadOnlyList<Evidence> evidence, IReadOnlyList<string> limitations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(limitations);
        var environmentObject = new JsonObject();
        foreach ((string key, string value) in environment.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            environmentObject[key] = value;
        }

        var body = new JsonObject
        {
            ["method"] = method,
            ["commit"] = commit,
            ["environment"] = environmentObject,
            ["role"] = role,
            ["outcome"] = outcome,
            ["summary"] = summary,
            ["evidence"] = new JsonArray(evidence.Select(e =>
            {
                var item = new JsonObject { ["summary"] = e.Summary };
                if (e.Hash is not null)
                {
                    item["hash"] = e.Hash;
                }

                if (e.Location is not null)
                {
                    item["location"] = e.Location;
                }

                return (JsonNode)item;
            }).ToArray()),
            ["limitations"] = new JsonArray(limitations.Select(l => (JsonNode)l).ToArray()),
        };
        if (command is not null)
        {
            body["command"] = command;
        }

        if (exitCode is not null)
        {
            body["exitCode"] = exitCode.Value;
        }

        if (durationMs is not null)
        {
            body["durationMs"] = durationMs.Value;
        }

        if (startedAt is not null)
        {
            body["startedAt"] = startedAt;
        }

        if (finishedAt is not null)
        {
            body["finishedAt"] = finishedAt;
        }

        return body;
    }

    /// <summary>The verification body for a workflow run observed at its host: automation's claim, with the run as evidence.</summary>
    public static JsonObject VerificationOfRun(RunObservation run, string workflow)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflow);
        string outcome = run.Status != "completed" ? "unknown" : run.Conclusion == "success" ? "passed" : run.Conclusion is null ? "unknown" : "failed";
        string summary = run.Status != "completed"
            ? $"Run {run.RunId} of {workflow} is {run.Status.Replace('_', ' ')}; its result is not yet known."
            : $"Run {run.RunId} of {workflow} completed with conclusion {run.Conclusion ?? "none"}.";
        return VerificationBody(
            "github-actions",
            run.HeadSha,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["runner"] = "github-hosted", ["workflow"] = workflow },
            "automation",
            outcome,
            summary,
            null,
            null,
            null,
            run.Status == "completed" ? run.At : null,
            [new Evidence($"Workflow run {run.RunId}", null, run.Url)],
            ["Observed through the host's own report of the run; the logs were not inspected here.", "A run that passed on one platform is no evidence for another; the workflow's own matrix says which ran."]);
    }
}
