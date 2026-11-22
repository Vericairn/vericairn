// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json.Nodes;
using Vericairn.Providers.GitHub;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (WATCH-06): the GitHub Discussions progress target against a recorded GitHub GraphQL API, with no network: it finds the
/// target's discussion by its marker, reports whether only the repository's members may read it, creates it once, replaces its text,
/// comments, refuses before any effect what it cannot do completely, and calls uncertain what it sent and could not confirm.
/// </summary>
public sealed class GitHubDiscussionsTargetTests
{
    private const string Marker = "vericairn progress-target status 0123456789abcdef";
    private const string OutsideText = "a reviewer's private remark";

    private static ProgressTargetSpec Spec(string? known = null, string repository = "Example/widget", string category = "Announcements") =>
        new("status", new Dictionary<string, string> { ["repository"] = repository, ["category"] = category }, "Progress of Example", Marker, known);

    [Fact]
    public void ItsDescriptorDeclaresTheContractAndSettings()
    {
        var target = new GitHubDiscussionsTarget(new RecordedGitHub().Run);
        Assert.Equal((GitHubDiscussionsTarget.AdapterId, IProgressTargetAdapter.Contract), (target.Adapter, target.Descriptor.Contract));
        Assert.Equal([("repository", true, (string?)null), ("category", true, "Announcements")], target.Descriptor.DeclaredSettings.Select(s => (s.Name, s.Required, s.Default)));
        Assert.Equal(["github"], target.Descriptor.ServedLocations);
    }

    [Theory]
    [InlineData("PRIVATE", true)]
    [InlineData("PUBLIC", false)]
    [InlineData("INTERNAL", false)]
    public void ItFindsTheTargetsDiscussionByItsMarkerAcrossPagesAndSaysWhoMayReadIt(string visibility, bool readOnlyByMembers)
    {
        var gh = new RecordedGitHub { Visibility = visibility };
        for (int i = 0; i < 120; i++) gh.Discussions.Add(RecordedGitHub.Discussion($"D_{i}", "Another post.", viewer: true));
        // A discussion someone else wrote carries the marker too: it is never taken for the target's.
        gh.Discussions[60] = RecordedGitHub.Discussion("D_60", "Copied.\n<!-- " + Marker + " -->\n", viewer: false);
        gh.Discussions[110] = RecordedGitHub.Discussion("D_110", "# Progress\n\n<!-- " + Marker + " -->\n", viewer: true);

        ProgressTargetDestination destination = new GitHubDiscussionsTarget(gh.Run).Locate(Spec());

        Assert.Equal(readOnlyByMembers, destination.Private);
        Assert.Equal(("D_110", "https://github.com/Example/widget/discussions/110", "# Progress\n\n<!-- " + Marker + " -->\n"), (destination.Place!.Id, destination.Place.Url, destination.Place.Body));
        // The repository is read once, then each page by its cursor in the named category; nothing is written.
        Assert.Equal(["repository", "page:", "page:p1", "page:p2"], gh.Calls.Select(RecordedGitHub.Describe));
        Assert.All(gh.Calls.Skip(1), call => Assert.Contains("category=C_1", call));

        gh.Discussions.Clear();
        Assert.Null(new GitHubDiscussionsTarget(gh.Run).Locate(Spec()).Place);
    }

    [Fact]
    public void AKnownDiscussionStandsOnlyWhileItIsThisLoginsInTheNamedRepositoryAndCategory()
    {
        var gh = new RecordedGitHub();
        gh.Discussions.Add(RecordedGitHub.Discussion("D_7", "<!-- " + Marker + " -->", viewer: true));
        gh.Node = RecordedGitHub.Known("D_7", "Example/widget", "C_1");
        Assert.Equal("D_7", new GitHubDiscussionsTarget(gh.Run).Locate(Spec(known: "D_7")).Place!.Id);
        Assert.Equal(["repository", "node"], gh.Calls.Select(RecordedGitHub.Describe));

        // Moved to another category, deleted, or another repository's: it is looked for again by its marker.
        foreach (JsonObject? node in new[] { RecordedGitHub.Known("D_7", "Example/widget", "C_2"), null, RecordedGitHub.Known("D_7", "Example/other", "C_1") })
        {
            gh.Calls.Clear();
            gh.Node = node;
            gh.NodeFails = node is null;
            Assert.Equal("D_7", new GitHubDiscussionsTarget(gh.Run).Locate(Spec(known: "D_7")).Place!.Id);
            Assert.Equal(["repository", "node", "page:"], gh.Calls.Select(RecordedGitHub.Describe));
        }
    }

    [Fact]
    public void ItCreatesTheDiscussionOnceReplacesItsTextAndComments()
    {
        var gh = new RecordedGitHub();
        var target = new GitHubDiscussionsTarget(gh.Run);
        string text = "# Progress of Example\n\n| W-1 | Order W-1 | planned |\n\n<!-- " + Marker + " -->\n";

        ProgressTargetPlace created = target.Create(Spec(), text);
        Assert.Equal(("D_new", "https://github.com/Example/widget/discussions/7"), (created.Id, created.Url));
        string[] create = gh.Calls[^1];
        Assert.Contains("repository=R_1", create);
        Assert.Contains("category=C_1", create);
        Assert.Contains("title=Progress of Example", create);
        // The text goes in an owner-only file that is gone once gh has answered, never on the command line.
        Assert.Equal(text, gh.Bodies[^1]);
        Assert.DoesNotContain(create, a => a.Contains("Order W-1", StringComparison.Ordinal));
        Assert.False(File.Exists(gh.Files[^1]));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, gh.Modes[^1]);

        ProgressTargetPlace replaced = target.Replace(Spec(), created, text + "more\n");
        Assert.Equal("D_new", replaced.Id);
        Assert.Contains("id=D_new", gh.Calls[^1]);
        Assert.Equal(text + "more\n", gh.Bodies[^1]);
        Assert.Equal("https://github.com/Example/widget/discussions/7#discussioncomment-1", target.Comment(Spec(), created, "Example 0.2.0 is now candidate."));
        Assert.Equal("Example 0.2.0 is now candidate.", gh.Bodies[^1]);
        Assert.Equal(["repository", "create", "replace", "comment"], gh.Calls.Select(RecordedGitHub.Describe));
    }

    [Theory]
    [InlineData("setting", "must be a GitHub owner/name")]
    [InlineData("dot", "must be a GitHub owner/name")]
    [InlineData("category-setting", "must name a discussion category")]
    [InlineData("missing", "not a repository the gh login can read")]
    [InlineData("renamed", "under another name")]
    [InlineData("off", "Discussions turned off")]
    [InlineData("category", "no one discussion category named")]
    [InlineData("not-started", "could not be started")]
    [InlineData("read", "could not be read completely")]
    [InlineData("unreadable", "unreadable answer")]
    [InlineData("pages", "more than 1000 discussions")]
    public void WhatItCannotDoCompletelyIsRefusedBeforeAnyEffect(string failure, string reason)
    {
        var gh = new RecordedGitHub { Failure = failure };
        if (failure == "pages") for (int i = 0; i < 1001; i++) gh.Discussions.Add(RecordedGitHub.Discussion($"D_{i}", "Another post.", viewer: true));
        ProgressTargetSpec spec = failure switch
        {
            "setting" => Spec(repository: "not a repository"),
            "dot" => Spec(repository: "Example/.."),
            "category-setting" => Spec(category: " "),
            _ => Spec(),
        };
        var target = new GitHubDiscussionsTarget(gh.Run);
        // Creating reads the repository and category as locating does; only locating reads the category's pages.
        foreach (Action act in failure == "pages" ? [() => target.Locate(spec)] : new Action[] { () => target.Locate(spec), () => target.Create(spec, "text") })
        {
            ProviderRefusedException refused = Assert.Throws<ProviderRefusedException>(act);
            Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
            Assert.Contains("nothing was sent", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(OutsideText, refused.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(gh.Calls, c => RecordedGitHub.Describe(c) is "create" or "replace" or "comment");
    }

    [Fact]
    public void ATextLongerThanADiscussionHoldsIsRefusedBeforeAnythingIsCalled()
    {
        var gh = new RecordedGitHub();
        var target = new GitHubDiscussionsTarget(gh.Run);
        string text = new('x', GitHubDiscussionsTarget.MaxBody + 1);
        var place = new ProgressTargetPlace("D_1", "https://github.com/Example/widget/discussions/1");
        foreach (Action act in new Action[] { () => target.Create(Spec(), text), () => target.Replace(Spec(), place, text), () => target.Comment(Spec(), place, text) })
            Assert.Contains("longer than GitHub keeps", Assert.Throws<ProviderRefusedException>(act).Message, StringComparison.Ordinal);
        Assert.Empty(gh.Calls);
        Assert.Throws<ProviderRefusedException>(() => target.Replace(Spec(), place with { Id = "D 1; drop" }, "text"));
        Assert.Empty(gh.Calls);
    }

    [Theory]
    [InlineData("mutation", "may have taken effect")]
    [InlineData("receipt", "receipt was unreadable")]
    [InlineData("answer", "answered unreadably")]
    public void WhatItSentAndCouldNotConfirmIsUncertain(string failure, string reason)
    {
        var gh = new RecordedGitHub { MutationFailure = failure };
        var target = new GitHubDiscussionsTarget(gh.Run);
        var place = new ProgressTargetPlace("D_1", "https://github.com/Example/widget/discussions/1");
        foreach (Action act in new Action[] { () => target.Create(Spec(), "text"), () => target.Replace(Spec(), place, "text"), () => target.Comment(Spec(), place, "text") })
        {
            ProviderUncertainException uncertain = Assert.Throws<ProviderUncertainException>(act);
            Assert.Contains(reason, uncertain.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(OutsideText, uncertain.Message, StringComparison.Ordinal);
        }

        Assert.All(gh.Files, f => Assert.False(File.Exists(f)));
    }

    /// <summary>GitHub's GraphQL API as recorded for one repository, answering gh's invocations; it reaches no network.</summary>
    private sealed class RecordedGitHub
    {
        public string Visibility { get; init; } = "PRIVATE";

        public string? Failure { get; init; }

        public string? MutationFailure { get; init; }

        public List<JsonObject> Discussions { get; } = [];

        public JsonObject? Node { get; set; }

        public bool NodeFails { get; set; }

        public List<string[]> Calls { get; } = [];

        public List<string> Bodies { get; } = [];

        public List<string> Files { get; } = [];

        public List<UnixFileMode> Modes { get; } = [];

        public static JsonObject Discussion(string id, string body, bool viewer) =>
            new() { ["id"] = id, ["url"] = "https://github.com/Example/widget/discussions/" + id[2..], ["body"] = body, ["viewerDidAuthor"] = viewer };

        public static JsonObject Known(string id, string repository, string category) => new()
        {
            ["id"] = id, ["url"] = "https://github.com/" + repository + "/discussions/" + id[2..], ["body"] = "<!-- " + Marker + " -->", ["viewerDidAuthor"] = true,
            ["repository"] = new JsonObject { ["nameWithOwner"] = repository }, ["category"] = new JsonObject { ["id"] = category },
        };

        public static string Describe(string[] call)
        {
            string query = call.FirstOrDefault(a => a.StartsWith("query=", StringComparison.Ordinal)) ?? string.Empty;
            return query.Contains("createDiscussion", StringComparison.Ordinal) ? "create"
                : query.Contains("updateDiscussion", StringComparison.Ordinal) ? "replace"
                : query.Contains("addDiscussionComment", StringComparison.Ordinal) ? "comment"
                : query.Contains("node(id:", StringComparison.Ordinal) ? "node"
                : query.Contains("discussions(first:", StringComparison.Ordinal) ? "page:" + (Variable(call, "after") ?? string.Empty)
                : "repository";
        }

        public ProcessResult Run(ProcessStartInfo info)
        {
            string[] call = [.. info.ArgumentList];
            Assert.Equal("gh", info.FileName);
            Assert.Equal("1", info.Environment["GH_PROMPT_DISABLED"]);
            Assert.Equal(["api", "graphql"], call[..2]);
            Calls.Add(call);
            if (Failure == "not-started") return new(ProcessOutcome.NotStarted, false, null, string.Empty, string.Empty);
            if (Failure == "read") return new(ProcessOutcome.Exited, true, 1, string.Empty, OutsideText);
            if (Failure == "unreadable") return Ok("{\"data\":");
            string kind = Describe(call);
            if (kind is "create" or "replace" or "comment")
            {
                string file = Variable(call, "body")![1..];
                Files.Add(file);
                Bodies.Add(File.ReadAllText(file));
                if (!OperatingSystem.IsWindows()) Modes.Add(File.GetUnixFileMode(file));
                if (MutationFailure == "mutation") return new(ProcessOutcome.Exited, true, 1, string.Empty, OutsideText);
                if (MutationFailure == "answer") return Ok("not json " + OutsideText);
                if (MutationFailure == "receipt") return Ok("""{"data":{"createDiscussion":null,"updateDiscussion":null,"addDiscussionComment":null}}""");
                return Ok(kind switch
                {
                    "create" => """{"data":{"createDiscussion":{"discussion":{"id":"D_new","url":"https://github.com/Example/widget/discussions/7"}}}}""",
                    "replace" => "{\"data\":{\"updateDiscussion\":{\"discussion\":{\"id\":\"" + Variable(call, "id") + "\",\"url\":\"https://github.com/Example/widget/discussions/7\"}}}}",
                    _ => """{"data":{"addDiscussionComment":{"comment":{"url":"https://github.com/Example/widget/discussions/7#discussioncomment-1"}}}}""",
                });
            }

            if (kind == "node")
            {
                return NodeFails ? new(ProcessOutcome.Exited, true, 1, string.Empty, OutsideText) : Ok(new JsonObject { ["data"] = new JsonObject { ["node"] = Node?.DeepClone() } }.ToJsonString());
            }

            if (kind.StartsWith("page:", StringComparison.Ordinal))
            {
                Assert.Equal("Example", Variable(call, "owner"));
                int start = Variable(call, "after") is string after ? int.Parse(after[1..], System.Globalization.CultureInfo.InvariantCulture) * GitHubDiscussionsTarget.PageSize : 0;
                List<JsonObject> page = [.. Discussions.Skip(start).Take(GitHubDiscussionsTarget.PageSize)];
                bool more = start + GitHubDiscussionsTarget.PageSize < Discussions.Count;
                int next = start / GitHubDiscussionsTarget.PageSize + 1;
                return Ok(new JsonObject
                {
                    ["data"] = new JsonObject
                    {
                        ["repository"] = new JsonObject
                        {
                            ["discussions"] = new JsonObject
                            {
                                ["pageInfo"] = new JsonObject { ["hasNextPage"] = more, ["endCursor"] = more ? "p" + next : null },
                                ["nodes"] = new JsonArray([.. page.Select(d => (JsonNode)d.DeepClone())]),
                            },
                        },
                    },
                }.ToJsonString());
            }

            Assert.Equal(("Example", "widget"), (Variable(call, "owner"), Variable(call, "name")));
            return Ok(Failure switch
            {
                "missing" => """{"data":{"repository":null}}""",
                _ => new JsonObject
                {
                    ["data"] = new JsonObject
                    {
                        ["repository"] = new JsonObject
                        {
                            ["id"] = "R_1", ["nameWithOwner"] = Failure == "renamed" ? "Example/renamed" : "Example/widget", ["visibility"] = Visibility,
                            ["hasDiscussionsEnabled"] = Failure != "off",
                            ["discussionCategories"] = new JsonObject
                            {
                                ["nodes"] = new JsonArray(
                                    new JsonObject { ["id"] = "C_1", ["name"] = Failure == "category" ? "News" : "Announcements" },
                                    new JsonObject { ["id"] = "C_2", ["name"] = "General" }),
                            },
                        },
                    },
                }.ToJsonString(),
            });
        }

        private static string? Variable(string[] call, string name) =>
            call.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

        private static ProcessResult Ok(string output) => new(ProcessOutcome.Exited, true, 0, output, OutsideText);
    }
}
