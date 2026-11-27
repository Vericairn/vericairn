// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Providers.GitHub;
using Vericairn.Runtime.Progress;
using Vericairn.Runtime.Providers;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (WATCH-06): vericairn progress target sets a target on this machine, refreshes it through the adapter its configuration
/// chooses, and follows the daemon's event journal to keep it equal, refreshing and commenting only on the events its triggers name. The
/// GitHub Discussions adapter is replaced by an in-memory one under its id, so nothing reaches a network.
/// </summary>
[Collection("cli")]
public sealed class ProgressTargetCommandTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Resource = """{"kind":"repository","name":"Widget","role":"source","location":{"github":"Example/widget"},"visibility":"public"}""";

    private readonly string root = CliHarness.TempDirectory("progress-target");
    private readonly string scratch = CliHarness.TempDirectory("progress-target-scratch");
    private readonly InMemoryTarget target = new();
    private readonly IProgressTargetAdapter? previous;
    private readonly string op;

    public ProgressTargetCommandTests()
    {
        previous = ProviderRegistry.Current.Register(target);
        op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Progress journey.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
    }

    public void Dispose()
    {
        ProviderRegistry.Current.Register(previous ?? new GitHubDiscussionsTarget());
        Directory.Delete(scratch, recursive: true);
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void ATargetIsSetOnThisMachineRefreshedThroughItsAdapterAndRemoved()
    {
        // A configuration names its adapter's settings; one its adapter does not take, or a trigger that cannot be read, changes nothing.
        CliHarness.Outcome unknown = CliHarness.Run("--estate", root, "progress", "target", "set", "status", "--adapter", "elsewhere", "--form", "private");
        Assert.Equal(2, unknown.Code);
        Assert.Contains("No progress-target adapter 'elsewhere' is available in this build", unknown.Err, StringComparison.Ordinal);
        Assert.Contains("requires the setting 'repository'", CliHarness.Run("--estate", root, "progress", "target", "set", "status", "--adapter", "github-discussions", "--form", "private").Err, StringComparison.Ordinal);
        Assert.Contains("'records' is neither an event kind", CliHarness.Run("--estate", root, "progress", "target", "set", "status", "--adapter", "github-discussions", "--setting", "repository=Example/widget", "--form", "private", "--refresh", "records").Err, StringComparison.Ordinal);
        Assert.Contains("No progress target is configured", CliHarness.Ok("--estate", root, "progress", "target", "list").Out, StringComparison.Ordinal);

        JsonElement set = CliHarness.Ok("--estate", root, "--json", "progress", "target", "set", "status", "--adapter", "github-discussions", "--setting", "repository=Example/widget", "--form", "private").Json;
        JsonElement configured = set.GetProperty("targets")[0];
        Assert.Equal(("status", "github-discussions", "private"), (configured.GetProperty("name").GetString(), configured.GetProperty("adapter").GetString(), configured.GetProperty("form").GetString()));
        Assert.Equal(["record type=release when=state-changed", "record type=operation when=merged", "record type=acceptance,result", "step to=person when=waiting"],
            configured.GetProperty("refresh").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(["step to=operator when=waiting", "record type=release when=state-changed"], configured.GetProperty("comment").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("comments on: step to=operator when=waiting; record type=release when=state-changed", CliHarness.Ok("--estate", root, "progress", "target", "list").Out, StringComparison.Ordinal);

        // A dry run composes the text and sends nothing.
        CliHarness.Outcome dry = CliHarness.Ok("--estate", root, "progress", "target", "refresh", "status", "--dry-run");
        Assert.StartsWith("status: would-create (private form)\n", dry.Out, StringComparison.Ordinal);
        Assert.Contains("\n# Progress of Example\n", dry.Out, StringComparison.Ordinal);
        Assert.Null(target.Body);

        // The first refresh creates the place and names every record the text carries; the next finds it equal and sends nothing.
        JsonElement created = CliHarness.Ok("--estate", root, "--json", "progress", "target", "refresh", "status").Json;
        Assert.Equal(("created", "https://example.invalid/discussions/1", "private"), (created.GetProperty("action").GetString(), created.GetProperty("url").GetString(), created.GetProperty("form").GetString()));
        Assert.Contains(created.GetProperty("carried").EnumerateArray(), r => r.GetProperty("type").GetString() == "estate");
        Assert.Equal(new Dictionary<string, string> { ["repository"] = "Example/widget", ["category"] = "Announcements" }, target.Settings);
        Assert.StartsWith("status: unchanged https://example.invalid/discussions/1 (private form)\n", CliHarness.Ok("--estate", root, "progress", "target", "refresh", "status").Out, StringComparison.Ordinal);
        Assert.Equal("https://example.invalid/discussions/1", CliHarness.Ok("--estate", root, "--json", "progress", "target", "list").Json.GetProperty("targets")[0].GetProperty("place").GetProperty("url").GetString());

        // A destination anyone may read takes the public form whatever the configuration says.
        target.Private = false;
        CliHarness.Outcome open = CliHarness.Ok("--estate", root, "progress", "target", "refresh", "status");
        Assert.Contains("(public form)", open.Out, StringComparison.Ordinal);
        Assert.Contains("readable beyond its own members", open.Out, StringComparison.Ordinal);

        // Following needs the daemon, which keeps the event journal.
        CliHarness.Outcome follow = CliHarness.Run("--estate", root, "progress", "target", "follow", "status", "--count", "1");
        Assert.Equal(2, follow.Code);
        Assert.Contains("the daemon keeps the event journal", follow.Err, StringComparison.Ordinal);

        Assert.Contains("the place it kept stays where it is", CliHarness.Ok("--estate", root, "progress", "target", "remove", "status").Out, StringComparison.Ordinal);
        Assert.Equal(2, CliHarness.Run("--estate", root, "progress", "target", "refresh", "status").Code);
        Assert.Equal(2, CliHarness.Run("--estate", root, "progress", "target", "remove", "status").Code);
    }

    [Fact]
    public void ACommentWithoutAnOutcomeIsListedWithItsOriginalDestination()
    {
        CliHarness.Ok("--estate", root, "progress", "target", "set", "status", "--adapter", "github-discussions",
            "--setting", "repository=Example/current", "--form", "public");
        const string key = "ask:example";
        const string hash = "sha256:example";
        const string place = "https://example.invalid/original";
        var pending = new ProgressTargetComment("github-discussions", hash, "private", "D_1", place);
        ProgressTargetStore.Of(root).Update("status", state => state with { Comments = state.Comments.Add(key, pending) });

        string list = CliHarness.Ok("--estate", root, "progress", "target", "list").Out;
        Assert.Contains("uncertain comment ask:example", list, StringComparison.Ordinal);
        Assert.Contains(place, list, StringComparison.Ordinal);
        JsonElement comments = CliHarness.Ok("--estate", root, "--json", "progress", "target", "list").Json
            .GetProperty("targets")[0].GetProperty("uncertainComments");
        JsonElement original = Assert.Single(comments.EnumerateArray());
        Assert.Equal((key, hash, "private", "uncertain"),
            (original.GetProperty("key").GetString(), original.GetProperty("payloadHash").GetString(),
                original.GetProperty("form").GetString(), original.GetProperty("outcome").GetString()));
        Assert.Equal(place, original.GetProperty("place").GetProperty("url").GetString());
        Assert.Null(ProgressTargetStore.Of(root).State("status").Comments[key].Outcome);
        Assert.Empty(target.Comments);
    }

    [Fact]
    public async Task FollowingKeepsTheTargetEqualAndCommentsOnlyOnTheEventsItsTriggersName()
    {
        CliHarness.RequireDaemonTransport(tls: false);
        CliHarness.Ok("--estate", root, "progress", "target", "set", "status", "--adapter", "github-discussions", "--setting", "repository=Example/widget", "--form", "private",
            "--refresh", "record type=resource", "--comment", "record type=resource change=written");
        await using DaemonHost host = await DaemonHost.StartAsync(root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);

        // Three outcomes: the refresh once the stream is open, then for one resource the refresh and the comment its triggers name.
        Task<CliHarness.Outcome> following = Task.Run(() => CliHarness.Run("--estate", root, "progress", "target", "follow", "status", "--count", "3"), TestContext.Current.CancellationToken);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TestTiming.HangTimeout);
        await target.Created.Task.WaitAsync(guard.Token);
        // A record no trigger names is followed by nothing.
        WriteOutcome other = client.NewRecord(new NewRecordRequest("principal", op, JsonDocument.Parse("""{"kind":"human","name":"Reviewer","roles":["reviewer"]}""").RootElement.Clone()));
        Assert.True(other.Ok, other.Problem?.Message);
        WriteOutcome resource = client.NewRecord(new NewRecordRequest("resource", op, JsonDocument.Parse(Resource).RootElement.Clone()));
        Assert.True(resource.Ok, resource.Problem?.Message);

        CliHarness.Outcome outcome = await following.WaitAsync(guard.Token);
        Assert.True(outcome.Code == 0, outcome.Err);
        string[] lines = [.. outcome.Out.Split('\n').Where(l => l.StartsWith("status: ", StringComparison.Ordinal))];
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("status: created https://example.invalid/discussions/1", lines[0], StringComparison.Ordinal);
        Assert.Matches("^status: (unchanged|replaced) ", lines[1]);
        Assert.StartsWith("status: commented https://example.invalid/discussions/1#comment-1", lines[2], StringComparison.Ordinal);
        Assert.Equal([$"record written: resource {resource.Written!.Id}."], target.Comments);
    }

    /// <summary>An in-memory progress target under the GitHub Discussions adapter's id: one place, its text and comments.</summary>
    private sealed class InMemoryTarget : IProgressTargetAdapter
    {
        private readonly GitHubDiscussionsTarget real = new();

        public string Adapter => GitHubDiscussionsTarget.AdapterId;

        public ProviderDescriptor Descriptor => real.Descriptor;

        public bool Private { get; set; } = true;

        public string? Body { get; private set; }

        public IReadOnlyDictionary<string, string>? Settings { get; private set; }

        public List<string> Comments { get; } = [];

        public TaskCompletionSource Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProgressTargetDestination Locate(ProgressTargetSpec target)
        {
            Settings = target.Settings;
            return new(Private, Body is null ? null : new ProgressTargetPlace("D_1", "https://example.invalid/discussions/1", Body));
        }

        public ProgressTargetPlace Create(ProgressTargetSpec target, string body)
        {
            Body = body;
            Created.TrySetResult();
            return new("D_1", "https://example.invalid/discussions/1");
        }

        public ProgressTargetPlace Replace(ProgressTargetSpec target, ProgressTargetPlace place, string body)
        {
            Body = body;
            return place with { Body = null };
        }

        public string Comment(ProgressTargetSpec target, ProgressTargetPlace place, string body)
        {
            Comments.Add(body);
            return $"https://example.invalid/discussions/1#comment-{Comments.Count}";
        }
    }
}
