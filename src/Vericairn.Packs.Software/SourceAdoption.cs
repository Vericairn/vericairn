// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Runtime.Providers;

namespace Vericairn.Packs.Software;

/// <summary>Turns a read-only repository observation into the records that adopt and observe a repository, with no machine path in either.</summary>
public static partial class SourceAdoption
{
    /// <summary>The owner/name of a GitHub remote, or null when the remote is not GitHub.</summary>
    public static string? GitHubRepository(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return null;
        }

        Match match = GitHubRemote().Match(remote.Trim());
        return match.Success ? match.Groups["owner"].Value + "/" + match.Groups["name"].Value : null;
    }

    /// <summary>True when a remote is a network address rather than a path on this machine.</summary>
    public static bool IsNetworkRemote(string? remote) =>
        !string.IsNullOrWhiteSpace(remote) && (remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || remote.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) || remote.StartsWith("git://", StringComparison.OrdinalIgnoreCase) || GitRemoteScp().IsMatch(remote));

    /// <summary>The resource record body for an adopted repository: identified by its GitHub repository, its https remote or its root commit, never by a path.</summary>
    public static JsonObject ResourceBody(string name, string role, string visibility, RepositoryObservation observation, string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(visibility);
        ArgumentNullException.ThrowIfNull(observation);
        var location = new JsonObject { ["rootCommit"] = observation.RootCommit };
        string? github = GitHubRepository(observation.Remote);
        if (github is not null)
        {
            location["github"] = github;
        }
        else if (observation.Remote is not null && observation.Remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            location["url"] = observation.Remote;
        }

        var body = new JsonObject
        {
            ["kind"] = "repository",
            ["name"] = name,
            ["role"] = role,
            ["location"] = location,
            ["visibility"] = visibility,
        };
        if (!string.IsNullOrWhiteSpace(notes))
        {
            body["notes"] = notes;
        }

        return body;
    }

    /// <summary>The observation record body: commit, branch, remote, working-tree state, tags and recent history as flat facts.</summary>
    public static JsonObject ObservationBody(RepositoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var facts = new JsonObject
        {
            ["commit"] = observation.Commit,
            ["rootCommit"] = observation.RootCommit,
            ["clean"] = observation.Clean,
            ["modified"] = observation.Modified,
            ["untracked"] = observation.Untracked,
            ["tags"] = new JsonArray(observation.Tags.Select(t => (JsonNode)t).ToArray()),
            ["recent"] = new JsonArray(observation.Recent.Select(c => (JsonNode)$"{c.Commit} {c.At} {c.Summary}").ToArray()),
        };
        if (observation.Branch is not null)
        {
            facts["branch"] = observation.Branch;
        }

        var limitations = new List<string> { "Observed on one machine's working tree at the as-of time; another clone or a later moment may differ." };
        if (IsNetworkRemote(observation.Remote))
        {
            facts["remote"] = observation.Remote;
        }
        else if (observation.Remote is not null)
        {
            limitations.Add("The origin remote is a path on this machine and was not recorded; the repository is identified by its root commit.");
        }
        else
        {
            limitations.Add("The repository has no origin remote; it is identified by its root commit.");
        }

        string state = observation.Clean ? "clean working tree" : $"{observation.Modified} modified and {observation.Untracked} untracked files preserved";
        return new JsonObject
        {
            ["kind"] = "repository",
            ["asOf"] = observation.AsOf,
            ["summary"] = $"At {observation.Commit[..12]}{(observation.Branch is null ? " (detached)" : " on " + observation.Branch)}; {state}; {observation.Tags.Count} tags.",
            ["facts"] = facts,
            ["limitations"] = new JsonArray(limitations.Select(l => (JsonNode)l).ToArray()),
        };
    }

    [GeneratedRegex(@"^(?:https?://github\.com/|git@github\.com:|ssh://git@github\.com/)(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)/(?<name>[A-Za-z0-9._-]+?)(?:\.git)?/?$")]
    private static partial Regex GitHubRemote();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:")]
    private static partial Regex GitRemoteScp();
}
