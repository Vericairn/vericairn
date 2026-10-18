// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Providers.GitHub;

/// <summary>Pull requests over bounded gh processes using the user's own login. No credential or diagnostic output is retained.</summary>
public sealed class GitHubPullRequestProvider : IPullRequestProvider
{
    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each invocation has a two-minute deadline and bounded output.</summary>
    public GitHubPullRequestProvider() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(2))) { }

    internal GitHubPullRequestProvider(Func<ProcessStartInfo, ProcessResult> run) => this.run = run;

    /// <inheritdoc />
    public string Provider => "github-pull-requests";

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("github-pull-requests-gh", IPullRequestProvider.Contract, "implemented",
        ["open", "observe", "merge", "observe-target"],
        ["Requires the user's own gh login. Fork pull requests are refused. Check names, including matrix jobs, must be declared.",
         "Observes once per request; pending runs require another observation. An unconfirmed merge remains uncertain."], Locations: ["github"]);

    /// <inheritdoc />
    public string PushRemote(string destination)
    {
        Destination(destination);
        return "https://github.com/" + destination + ".git";
    }

    /// <inheritdoc />
    public ChangeReceipt Open(string destination, string branch, string target, string head, string title, string body)
    {
        Destination(destination);
        Commit(head);
        using JsonDocument resolved = Read("api", $"repos/{destination}/commits/{Uri.EscapeDataString(branch)}");
        if (Text(resolved.RootElement, "sha") != head) throw new ProviderRefusedException("The pushed branch is not at the expected head.");
        Effect("pr", "create", "--repo", destination, "--head", branch, "--base", target, "--title", title, "--body", body);
        try
        {
            PullRequestObservation? pull = Observe(destination, branch);
            if (pull is null || !pull.Complete || pull.State != "open" || pull.Head != head || pull.Branch != branch || pull.Target != target)
                throw new ProviderUncertainException("The opening was sent but its exact pull request was not confirmed.");
            return new(pull.Number);
        }
        catch (ProviderRefusedException)
        {
            throw new ProviderUncertainException("The opening was sent but its receipt could not be observed.");
        }
    }

    /// <inheritdoc />
    public PullRequestObservation? Observe(string destination, string numberOrBranch)
    {
        Destination(destination);
        using JsonDocument document = Read("pr", "view", numberOrBranch, "--repo", destination, "--json",
            "number,headRefOid,headRefName,baseRefName,state,mergeCommit,isCrossRepository");
        try
        {
            JsonElement p = document.RootElement;
            string head = Text(p, "headRefOid");
            Commit(head);
            List<ChangeCheck> checks = CommitChecks(destination, head);
            string? merged = p.TryGetProperty("mergeCommit", out JsonElement m) && m.ValueKind == JsonValueKind.Object ? Text(m, "oid") : null;
            return new(p.GetProperty("number").GetInt64().ToString(CultureInfo.InvariantCulture), Text(p, "headRefName"), Text(p, "baseRefName"),
                head, Text(p, "state").ToLowerInvariant(), merged, checks, !p.GetProperty("isCrossRepository").GetBoolean());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new ProviderRefusedException("The pull-request observation was incomplete.");
        }
    }

    /// <inheritdoc />
    public ChangeReceipt Merge(string destination, string number, string head)
    {
        Destination(destination);
        Commit(head);
        if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long n) || n <= 0)
            throw new ProviderRefusedException("The pull request must be named by its positive number.");
        // The synchronous merge endpoint compares sha atomically. Unlike the interactive merge command,
        // it cannot turn this request into auto-merge or enqueue a later merge when a queue is required.
        Effect("api", $"repos/{destination}/pulls/{number}/merge", "--method", "PUT", "--raw-field", "sha=" + head,
            "--raw-field", "merge_method=merge");
        try
        {
            PullRequestObservation? observed = Observe(destination, number);
            return observed is { State: "merged", MergeCommit: not null, Complete: true } && observed.Head == head
                ? new(number, observed.MergeCommit) : new(number);
        }
        catch (ProviderRefusedException)
        {
            throw new ProviderUncertainException("The merge was requested but its commit could not be confirmed.");
        }
    }

    /// <inheritdoc />
    public TargetObservation ObserveTarget(string destination, string branch, string commit)
    {
        Destination(destination);
        Commit(commit);
        using JsonDocument pages = Pages($"repos/{destination}/actions/runs?head_sha={commit}&branch={Uri.EscapeDataString(branch)}&event=push&per_page=100");
        List<ChangeCheck> checks = [];
        try
        {
            foreach (JsonElement workflow in Items(pages, "workflow_runs"))
            {
                string head = Text(workflow, "head_sha");
                if (Text(workflow, "head_branch") != branch || Text(workflow, "event") != "push")
                    return new(branch, commit, checks, false);
                checks.Add(Check("workflow:" + Text(workflow, "name"), head, Status(Text(workflow, "status")), Conclusion(Text(workflow, "conclusion"))));
                string id = workflow.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
                using JsonDocument jobs = Pages($"repos/{destination}/actions/runs/{id}/jobs?filter=latest&per_page=100");
                foreach (JsonElement job in Items(jobs, "jobs"))
                    checks.Add(Check(Text(job, "name"), Text(job, "head_sha"), Status(Text(job, "status")), Conclusion(Text(job, "conclusion"))));
            }
            return new(branch, commit, checks);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new ProviderRefusedException("The target run observation was incomplete.");
        }
    }

    private List<ChangeCheck> CommitChecks(string destination, string head)
    {
        List<ChangeCheck> checks = [];
        using JsonDocument runs = Pages($"repos/{destination}/commits/{head}/check-runs?filter=latest&per_page=100");
        foreach (JsonElement check in Items(runs, "check_runs"))
            checks.Add(Check(Text(check, "name"), Text(check, "head_sha"), Status(Text(check, "status")), Conclusion(Text(check, "conclusion"))));
        using JsonDocument statuses = Pages($"repos/{destination}/commits/{head}/status?per_page=100");
        _ = Items(statuses, "statuses");
        foreach (JsonElement page in statuses.RootElement.EnumerateArray())
            foreach (JsonElement check in page.GetProperty("statuses").EnumerateArray())
            {
                string state = Text(check, "state");
                checks.Add(Check(Text(check, "context"), Text(page, "sha"), state == "pending" ? "in_progress" : "completed",
                    state == "success" ? "success" : state is "failure" or "error" ? "failure" : "unknown"));
            }
        return checks;
    }

    private static List<JsonElement> Items(JsonDocument pages, string property)
    {
        if (pages.RootElement.ValueKind != JsonValueKind.Array || pages.RootElement.GetArrayLength() == 0)
            throw new ProviderRefusedException("The host's paginated inventory is incomplete.");
        var items = new List<JsonElement>();
        int expected = 0;
        foreach (JsonElement page in pages.RootElement.EnumerateArray())
        {
            if (!page.TryGetProperty("total_count", out JsonElement total) || !total.TryGetInt32(out int count) || count < 0
                || !page.TryGetProperty(property, out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                throw new ProviderRefusedException("The host's paginated inventory is incomplete.");
            expected = Math.Max(expected, count);
            items.AddRange(list.EnumerateArray());
        }
        if (items.Count != expected) throw new ProviderRefusedException("The host's inventory changed or pagination was incomplete.");
        return items;
    }

    private JsonDocument Pages(string route) => Read("api", route, "--paginate", "--slurp");

    private static ChangeCheck Check(string name, string head, string status, string? conclusion)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || !Patterns.Commit.IsMatch(head))
            throw new ProviderRefusedException("GitHub returned incomplete check metadata; no successful check is inferred.");
        return new(name, head, status, conclusion);
    }

    private JsonDocument Read(params string[] arguments)
    {
        ProcessResult result = Invoke(arguments);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderRefusedException("GitHub could not be observed completely; check gh installation, login and repository access.");
        try { return JsonDocument.Parse(result.StandardOutput); }
        catch (JsonException) { throw new ProviderRefusedException("GitHub returned an unreadable observation."); }
    }

    private void Effect(params string[] arguments)
    {
        ProcessResult result = Invoke(arguments);
        if (!result.Started) throw new ProviderRefusedException("The GitHub CLI could not be started; no request was sent.");
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode != 0)
            throw new ProviderUncertainException("The GitHub request may have taken effect; observe it before any retry.");
    }

    private ProcessResult Invoke(string[] arguments)
    {
        var info = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        return run(info);
    }

    private static void Destination(string destination)
    {
        if (!Patterns.GitHubRepository.IsMatch(destination)) throw new ProviderRefusedException("The destination must be a GitHub owner/name.");
    }

    private static void Commit(string head)
    {
        if (!Patterns.Commit.IsMatch(head)) throw new ProviderRefusedException("An exact commit is required.");
    }

    private static string Text(JsonElement element, string key) => element.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
    private static string Status(string value) => value is "queued" or "in_progress" or "completed" ? value : "unknown";
    private static string? Conclusion(string value) => value.Length == 0 ? null : value is "success" or "failure" or "cancelled" or "skipped" or "neutral" or "timed_out" or "action_required" or "stale" or "startup_failure" ? value : "unknown";
}
