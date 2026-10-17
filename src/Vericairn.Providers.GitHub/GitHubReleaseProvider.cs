// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Providers.GitHub;

/// <summary>The GitHub provider for releases, over the GitHub CLI on the PATH, logged in by the user; Vericairn never sees or stores the token.</summary>
public sealed class GitHubReleaseProvider : IReleaseProvider
{
    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Runs the GitHub CLI with a two-minute deadline and bounded output for each command.</summary>
    public GitHubReleaseProvider() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(2)))
    {
    }

    internal GitHubReleaseProvider(Func<ProcessStartInfo, ProcessResult> run)
    {
        this.run = run;
    }

    /// <summary>The provider id recorded on every operation this implementation dispatches.</summary>
    public const string ProviderId = "github-releases";

    /// <inheritdoc />
    public string Provider => ProviderId;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(
        "github-releases-gh",
        IReleaseProvider.Contract,
        "implemented",
        ["dispatch", "observe-run", "observe-release"],
        ["Requires the GitHub CLI on the PATH and a login of the user's own; agents hold no token.", "Identifies a dispatched run by listing recent runs of the workflow on the ref; when none appears within thirty seconds the operation is uncertain.", "Never publishes; publication is the operator's manual step."],
        [
            new ProviderSetting("repository", "GitHub owner/name of the repository whose workflow builds the release.", true),
            new ProviderSetting("workflow", "The workflow file that builds the release into a draft.", false, "release.yml"),
            new ProviderSetting("ref", "The branch or tag the workflow runs on.", false, "main"),
            new ProviderSetting("checksums", "The release asset that lists every other asset's SHA-256.", false, "SHA256SUMS"),
        ],
        "repository",
        ["github"]);

    /// <inheritdoc />
    public DispatchReceipt Dispatch(DispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Gh(out string auth, "auth", "status") != 0)
        {
            throw new ProviderRefusedException("The GitHub CLI is not logged in on this machine; run 'gh auth login' yourself. Vericairn stores no token.");
        }

        DateTimeOffset dispatchedAt = DateTimeOffset.UtcNow;
        string workflow = request.Setting("workflow", "release.yml");
        string reference = request.Setting("ref", "main");
        List<string> arguments = ["workflow", "run", workflow, "-R", request.Destination, "--ref", reference];
        foreach ((string key, string value) in request.Inputs.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            arguments.Add("-f");
            arguments.Add($"{key}={value}");
        }

        int code = Gh(out string output, mayHaveEffect: true, [.. arguments]);
        if (code != 0)
        {
            string text = output.Trim();
            if (text.Contains("could not find", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP 422", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProviderRefusedException($"GitHub refused the dispatch before any run started: {text}");
            }

            throw new ProviderUncertainException($"The dispatch command failed after the request may have been sent: {text}");
        }

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(3000);
                if (Gh(out string runs, "run", "list", "-R", request.Destination, "--workflow", workflow, "--event", "workflow_dispatch", "--branch", reference, "-L", "10", "--json", "databaseId,createdAt,url,status") == 0)
                {
                    using JsonDocument document = JsonDocument.Parse(runs);
                    foreach (JsonElement run in document.RootElement.EnumerateArray())
                    {
                        if (run.TryGetProperty("createdAt", out JsonElement created) && DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) && at >= dispatchedAt.AddSeconds(-5))
                        {
                            string id = run.GetProperty("databaseId").GetInt64().ToString(CultureInfo.InvariantCulture);
                            return new DispatchReceipt(id, run.GetProperty("url").GetString(), $"Workflow {workflow} dispatched on {reference}; run {id} started.", RecordParser.FormatTimestamp(dispatchedAt));
                        }
                    }
                }
            }

            return new DispatchReceipt(null, null, $"Workflow {workflow} was dispatched on {reference} but no run was identified within thirty seconds; observe the workflow's runs before any retry.", RecordParser.FormatTimestamp(dispatchedAt));
        }
        catch (Exception exception) when (exception is ProviderRefusedException or JsonException)
        {
            // The workflow request already succeeded. A later observation failure cannot mean no effect happened.
            throw new ProviderUncertainException("The workflow was dispatched, but its run could not be identified; observe and reconcile before any retry.", exception);
        }
    }

    /// <inheritdoc />
    public RunObservation? ObserveRun(string destination, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (Gh(out string output, "run", "view", runId, "-R", destination, "--json", "databaseId,status,conclusion,headSha,url") != 0)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        string? conclusion = root.TryGetProperty("conclusion", out JsonElement c) && c.ValueKind == JsonValueKind.String && c.GetString()!.Length > 0 ? c.GetString() : null;
        return new RunObservation(runId, root.GetProperty("status").GetString() ?? "unknown", conclusion, root.GetProperty("headSha").GetString() ?? string.Empty, root.GetProperty("url").GetString() ?? string.Empty, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), "github-actions", new Dictionary<string, string>(StringComparer.Ordinal) { ["runner"] = "github-hosted" });
    }

    /// <inheritdoc />
    public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentNullException.ThrowIfNull(settings);
        string checksums = settings.TryGetValue("checksums", out string? named) && !string.IsNullOrWhiteSpace(named) ? named : "SHA256SUMS";
        string repository = destination;
        if (Gh(out string output, "release", "view", tag, "-R", repository, "--json", "tagName,isDraft,url,targetCommitish,publishedAt,assets") != 0)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ReadAsset(repository, tag, checksums) is byte[] listed)
        {
            using var reader = new StreamReader(new MemoryStream(listed), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (reader.ReadLine() is string line)
            {
                string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].Length == 64)
                {
                    sums[parts[1].TrimStart('*', ' ')] = "sha256:" + parts[0].ToLowerInvariant();
                }
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
    public string ManualPublicationCommand(string destination, string tag) => $"gh release edit {tag} -R {destination} --draft=false --latest";

    /// <inheritdoc />
    public byte[]? ReadAsset(string destination, string tag, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.IndexOfAny(['/', '\\']) >= 0 || name is "." or "..")
        {
            return null;
        }

        string directory = Path.Combine(Path.GetTempPath(), "vericairn-asset-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, name);
            return Gh(out _, "release", "download", tag, "-R", destination, "-p", name, "-D", directory, "--clobber") == 0 && File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private int Gh(out string output, params string[] arguments) => Gh(out output, mayHaveEffect: false, arguments);

    private int Gh(out string output, bool mayHaveEffect, params string[] arguments)
    {
        var info = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GH_PROMPT_DISABLED"] = "1";
        info.Environment["NO_COLOR"] = "1";
        ProcessResult result = run(info);
        if (result.Outcome != ProcessOutcome.Exited)
        {
            string message = result.Outcome == ProcessOutcome.NotStarted
                ? "The GitHub CLI (gh) could not be started; install it on the PATH and log in yourself."
                : $"gh {result.Description}.";
            if (mayHaveEffect && result.Started)
            {
                throw new ProviderUncertainException(message + " The workflow request may have been sent; observe and reconcile before any retry.");
            }

            throw new ProviderRefusedException(message);
        }

        output = result.ExitCode == 0 ? result.StandardOutput : result.StandardError + result.StandardOutput;
        return result.ExitCode!.Value;
    }
}
