// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Providers;

/// <summary>A request to dispatch a workflow that builds a release.</summary>
/// <param name="Repository">owner/name.</param>
/// <param name="Workflow">Workflow file name.</param>
/// <param name="Ref">Branch or tag to run on.</param>
/// <param name="Inputs">Workflow inputs.</param>
public sealed record DispatchRequest(string Repository, string Workflow, string Ref, IReadOnlyDictionary<string, string> Inputs);

/// <summary>What a dispatch returned: a run id when the run could be identified, otherwise the operation is uncertain.</summary>
/// <param name="RunId">The run, or null.</param>
/// <param name="Url">Where to see it.</param>
/// <param name="Summary">What happened.</param>
/// <param name="At">When.</param>
public sealed record DispatchReceipt(string? RunId, string? Url, string Summary, string At);

/// <summary>A workflow run as observed.</summary>
/// <param name="RunId">Run id.</param>
/// <param name="Status">queued, in_progress or completed.</param>
/// <param name="Conclusion">success, failure, cancelled or another conclusion once completed.</param>
/// <param name="HeadSha">The commit the run built.</param>
/// <param name="Url">Where to see it.</param>
/// <param name="At">When observed.</param>
public sealed record RunObservation(string RunId, string Status, string? Conclusion, string HeadSha, string Url, string At);

/// <summary>One asset of a release as observed, with its hash when the release's checksum file names it.</summary>
/// <param name="Name">File name.</param>
/// <param name="Bytes">Size.</param>
/// <param name="Hash">sha256:hex, or null when no checksum names the asset.</param>
public sealed record ReleaseAsset(string Name, long Bytes, string? Hash);

/// <summary>A release as observed at its destination.</summary>
/// <param name="Tag">Tag.</param>
/// <param name="Draft">Whether it is still a draft.</param>
/// <param name="Url">Where to see it.</param>
/// <param name="TargetCommit">The commit the release was created from, when known.</param>
/// <param name="PublishedAt">When it was published, once it is.</param>
/// <param name="Assets">Assets with hashes.</param>
/// <param name="At">When observed.</param>
public sealed record ReleaseObservation(string Tag, bool Draft, string? Url, string? TargetCommit, string? PublishedAt, IReadOnlyList<ReleaseAsset> Assets, string At);

/// <summary>
/// The GitHub Releases provider contract, version 1: dispatch the release workflow, observe the run, observe the draft or
/// published release and its checksums. Publication itself is the operator's recorded manual step, so no implementation
/// publishes; the provider names the exact command instead. Agents hold no GitHub token: the implementation uses the user's
/// own GitHub CLI login on this machine.
/// </summary>
public interface IReleaseProvider
{
    /// <summary>The contract every implementation satisfies.</summary>
    const string Contract = "vericairn.provider.github-releases/1";

    /// <summary>What this implementation is.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>Dispatches the workflow; throws <see cref="ProviderRefusedException"/> before any effect, or <see cref="ProviderUncertainException"/> when an effect may have happened.</summary>
    DispatchReceipt Dispatch(DispatchRequest request);

    /// <summary>Observes a run, or null when no such run exists.</summary>
    RunObservation? ObserveRun(string repository, string runId);

    /// <summary>Observes a release by tag, or null when none exists.</summary>
    ReleaseObservation? ObserveRelease(string repository, string tag);

    /// <summary>The operator's manual publication command for a tag; the provider never runs it.</summary>
    string ManualPublicationCommand(string repository, string tag);
}

/// <summary>The GitHub Releases provider over the GitHub CLI on the PATH, logged in by the user; Vericairn never sees or stores the token.</summary>
public sealed class GhReleaseProvider : IReleaseProvider
{
    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "github-releases-gh",
        IReleaseProvider.Contract,
        "implemented",
        ["dispatch", "observe-run", "observe-release"],
        ["Requires the GitHub CLI on the PATH and a login of the user's own; agents hold no token.", "Identifies a dispatched run by listing recent runs of the workflow on the ref; when none appears within thirty seconds the operation is uncertain.", "Never publishes; publication is the operator's manual step."]);

    /// <inheritdoc />
    public DispatchReceipt Dispatch(DispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Gh(out string auth, "auth", "status") != 0)
        {
            throw new ProviderRefusedException("The GitHub CLI is not logged in on this machine; run 'gh auth login' yourself. Vericairn stores no token.");
        }

        DateTimeOffset dispatchedAt = DateTimeOffset.UtcNow;
        List<string> arguments = ["workflow", "run", request.Workflow, "-R", request.Repository, "--ref", request.Ref];
        foreach ((string key, string value) in request.Inputs.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            arguments.Add("-f");
            arguments.Add($"{key}={value}");
        }

        int code = Gh(out string output, [.. arguments]);
        if (code != 0)
        {
            string text = output.Trim();
            if (text.Contains("could not find", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 422", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProviderRefusedException($"GitHub refused the dispatch before any run started: {text}");
            }

            throw new ProviderUncertainException($"The dispatch command failed after the request may have been sent: {text}");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Thread.Sleep(3000);
            if (Gh(out string runs, "run", "list", "-R", request.Repository, "--workflow", request.Workflow, "--event", "workflow_dispatch", "--branch", request.Ref, "-L", "10", "--json", "databaseId,createdAt,url,status") == 0)
            {
                using JsonDocument document = JsonDocument.Parse(runs);
                foreach (JsonElement run in document.RootElement.EnumerateArray())
                {
                    if (run.TryGetProperty("createdAt", out JsonElement created) && DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) && at >= dispatchedAt.AddSeconds(-5))
                    {
                        string id = run.GetProperty("databaseId").GetInt64().ToString(CultureInfo.InvariantCulture);
                        return new DispatchReceipt(id, run.GetProperty("url").GetString(), $"Workflow {request.Workflow} dispatched on {request.Ref}; run {id} started.", RecordParser.FormatTimestamp(dispatchedAt));
                    }
                }
            }
        }

        return new DispatchReceipt(null, null, $"Workflow {request.Workflow} was dispatched on {request.Ref} but no run was identified within thirty seconds; observe the workflow's runs before any retry.", RecordParser.FormatTimestamp(dispatchedAt));
    }

    /// <inheritdoc />
    public RunObservation? ObserveRun(string repository, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (Gh(out string output, "run", "view", runId, "-R", repository, "--json", "databaseId,status,conclusion,headSha,url") != 0)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        string? conclusion = root.TryGetProperty("conclusion", out JsonElement c) && c.ValueKind == JsonValueKind.String && c.GetString()!.Length > 0 ? c.GetString() : null;
        return new RunObservation(runId, root.GetProperty("status").GetString() ?? "unknown", conclusion, root.GetProperty("headSha").GetString() ?? string.Empty, root.GetProperty("url").GetString() ?? string.Empty, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public ReleaseObservation? ObserveRelease(string repository, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (Gh(out string output, "release", "view", tag, "-R", repository, "--json", "tagName,isDraft,url,targetCommitish,publishedAt,assets") != 0)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        string directory = Path.Combine(Path.GetTempPath(), "vericairn-sums-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            if (Gh(out _, "release", "download", tag, "-R", repository, "-p", "SHA256SUMS", "-D", directory, "--clobber") == 0 && File.Exists(Path.Combine(directory, "SHA256SUMS")))
            {
                foreach (string line in File.ReadAllLines(Path.Combine(directory, "SHA256SUMS")))
                {
                    string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && parts[0].Length == 64)
                    {
                        sums[parts[1].TrimStart('*', ' ')] = "sha256:" + parts[0].ToLowerInvariant();
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        var assets = new List<ReleaseAsset>();
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            assets.Add(new ReleaseAsset(name, asset.GetProperty("size").GetInt64(), sums.TryGetValue(name, out string? hash) ? hash : null));
        }

        string? publishedAt = root.TryGetProperty("publishedAt", out JsonElement p) && p.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ? RecordParser.FormatTimestamp(at) : null;
        string? target = root.TryGetProperty("targetCommitish", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return new ReleaseObservation(root.GetProperty("tagName").GetString() ?? tag, root.GetProperty("isDraft").GetBoolean(), root.GetProperty("url").GetString(), target, publishedAt, assets.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(), RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public string ManualPublicationCommand(string repository, string tag) => $"gh release edit {tag} -R {repository} --draft=false --latest";

    private static int Gh(out string output, params string[] arguments)
    {
        var info = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GH_PROMPT_DISABLED"] = "1";
        info.Environment["NO_COLOR"] = "1";
        try
        {
            using Process process = Process.Start(info) ?? throw new ProviderRefusedException("gh did not start.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                throw new ProviderUncertainException("gh did not answer within two minutes.");
            }

            output = process.ExitCode == 0 ? standardOutput : standardError + standardOutput;
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ProviderRefusedException("The GitHub CLI (gh) is not on the PATH; install it and log in yourself.", exception);
        }
    }
}

/// <summary>An in-memory GitHub Releases provider for conformance tests and journeys: it behaves as GitHub would, under the test's control, and qualifies no live service.</summary>
public sealed class FakeReleaseProvider : IReleaseProvider
{
    private readonly Dictionary<string, RunObservation> runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReleaseObservation> releases = new(StringComparer.Ordinal);
    private readonly List<DispatchRequest> dispatches = [];
    private int nextRun = 1000;

    /// <summary>How the next dispatch behaves.</summary>
    public enum DispatchBehavior
    {
        /// <summary>The run starts and is identified.</summary>
        Normal,

        /// <summary>Refused before any effect: not logged in.</summary>
        RefuseAuthentication,

        /// <summary>The request may have been sent but no run was identified.</summary>
        Uncertain,
    }

    /// <summary>Behavior of the next dispatch.</summary>
    public DispatchBehavior NextDispatch { get; set; } = DispatchBehavior.Normal;

    /// <summary>The commit the fake runs build.</summary>
    public string HeadSha { get; set; } = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Every dispatch requested.</summary>
    public IReadOnlyList<DispatchRequest> Dispatches => dispatches;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("github-releases-fake", IReleaseProvider.Contract, "implemented", ["dispatch", "observe-run", "observe-release"], ["Simulates GitHub in memory; qualifies no live service."]);

    /// <inheritdoc />
    public DispatchReceipt Dispatch(DispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
        switch (NextDispatch)
        {
            case DispatchBehavior.RefuseAuthentication:
                NextDispatch = DispatchBehavior.Normal;
                throw new ProviderRefusedException("The GitHub CLI is not logged in on this machine; run 'gh auth login' yourself. Vericairn stores no token.");
            case DispatchBehavior.Uncertain:
                NextDispatch = DispatchBehavior.Normal;
                dispatches.Add(request);
                return new DispatchReceipt(null, null, "The dispatch was sent but no run was identified within thirty seconds; observe the workflow's runs before any retry.", at);
            default:
                dispatches.Add(request);
                string id = (nextRun++).ToString(CultureInfo.InvariantCulture);
                string url = $"https://github.com/{request.Repository}/actions/runs/{id}";
                runs[request.Repository + "#" + id] = new RunObservation(id, "in_progress", null, HeadSha, url, at);
                return new DispatchReceipt(id, url, $"Workflow {request.Workflow} dispatched on {request.Ref}; run {id} started.", at);
        }
    }

    /// <summary>Completes a run; on success a draft release with distributables, SBOM, notices and checksums appears.</summary>
    public void CompleteRun(string repository, string runId, bool success, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        RunObservation run = runs[repository + "#" + runId];
        runs[repository + "#" + runId] = run with { Status = "completed", Conclusion = success ? "success" : "failure", At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
        if (!success)
        {
            return;
        }

        string tag = "v" + version;
        var assets = new List<ReleaseAsset>();
        int n = 1;
        foreach (string platform in new[] { "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64" })
        {
            string suffix = platform.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz";
            assets.Add(new ReleaseAsset($"vericairn-{version}-{platform}{suffix}", 1000 + n, "sha256:" + new string((char)('0' + n), 64)));
            assets.Add(new ReleaseAsset($"vericairn-workbench-{version}-{platform}{suffix}", 2000 + n, "sha256:" + new string('f', 60) + n.ToString("D4", CultureInfo.InvariantCulture)));
            n++;
        }

        assets.Add(new ReleaseAsset($"vericairn-{version}-sbom.cdx.json", 500, "sha256:" + new string('a', 64)));
        assets.Add(new ReleaseAsset($"vericairn-{version}-THIRD-PARTY-NOTICES.md", 400, "sha256:" + new string('b', 64)));
        assets.Add(new ReleaseAsset("SHA256SUMS", 300, null));
        releases[repository + "#" + tag] = new ReleaseObservation(tag, true, $"https://github.com/{repository}/releases/tag/{tag}", run.HeadSha, null, assets.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(), RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
    }

    /// <summary>What the operator's manual step does at GitHub: the draft becomes public.</summary>
    public void Publish(string repository, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ReleaseObservation release = releases[repository + "#" + tag];
        releases[repository + "#" + tag] = release with { Draft = false, PublishedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
    }

    /// <inheritdoc />
    public RunObservation? ObserveRun(string repository, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return runs.TryGetValue(repository + "#" + runId, out RunObservation? run) ? run with { At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) } : null;
    }

    /// <inheritdoc />
    public ReleaseObservation? ObserveRelease(string repository, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return releases.TryGetValue(repository + "#" + tag, out ReleaseObservation? release) ? release with { At = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) } : null;
    }

    /// <inheritdoc />
    public string ManualPublicationCommand(string repository, string tag) => $"gh release edit {tag} -R {repository} --draft=false --latest";
}
