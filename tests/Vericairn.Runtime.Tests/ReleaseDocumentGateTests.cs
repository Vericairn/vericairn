// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseDocumentGateTests
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];
    [Theory]
    [InlineData("behind", false)]
    [InlineData("ahead", false)]
    [InlineData("exact", false)]
    [InlineData("behind", true)]
    [InlineData("ahead", true)]
    [InlineData("exact", true)]
    public void SubmissionComparesTheProvidersCommitRegardlessOfCloneHead(string position, bool covers)
    {
        using var fixture = new Fixture(covers);
        string first = fixture.Commit("Old documentation.");
        string target = fixture.Commit("Reviewed documentation.");
        fixture.Review(target);
        string head = position switch
        {
            "behind" => first,
            "ahead" => fixture.Commit("Unreviewed future documentation."),
            _ => target,
        };
        Git(fixture.Repository, "checkout", "-q", "-B", "main", head);
        fixture.Provider.HeadSha = target;

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(response.Ok, response.Message);
        Assert.Single(fixture.Provider.Dispatches);
        RecordEnvelope intent = fixture.Service.Session.Load().OfType("operation").Single(r => EstateModel.BodyString(r, "status") == "not-started");
        Assert.Contains($"README.md at {target[..12]}", intent.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(head, Git(fixture.Repository, "rev-parse", "HEAD"));
    }

    [Fact]
    public void AMissingReleaseCommitRefusesBeforeIntentAndNamesTheCloneToFetch()
    {
        using var fixture = new Fixture();
        fixture.Commit("Reviewed documentation.");
        string behind = Path.Combine(fixture.Scratch.Root, "behind");
        Git(fixture.Scratch.Root, "clone", "-q", "--no-local", fixture.Repository, behind);
        // The bytes are unchanged, so reading the old HEAD would incorrectly admit this release.
        string target = fixture.Commit("Reviewed documentation.");
        fixture.Review(target);
        RepositoryClones.Register(fixture.Estate.Estate.Id, fixture.Resource.Id, behind);
        fixture.Provider.HeadSha = target;

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains(target, response.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Resource.Id, response.Message, StringComparison.Ordinal);
        Assert.Contains("Widget source", response.Message, StringComparison.Ordinal);
        Assert.Contains("fetch", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Provider.Dispatches);
        Assert.Empty(fixture.Service.Records("operation"));
    }

    [Fact]
    public void AnUnresolvedProviderCommitCannotFallBackToTheReviewedCloneHead()
    {
        using var fixture = new Fixture();
        fixture.Review(fixture.Commit("Reviewed documentation."));
        var registry = new ProviderRegistry();
        registry.Register(new MovingProvider(fixture.Provider, null));
        fixture.Service.Providers = registry;
        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains("could not resolve", response.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Service.Records("operation"));
        Assert.Empty(fixture.Provider.Dispatches);
    }

    [Fact]
    public void ABranchMovingBeforeDispatchIsCheckedAtTheCommitActuallyBuiltBeforeACandidateIsRecorded()
    {
        using var fixture = new Fixture();
        string reviewed = fixture.Commit("Reviewed documentation.");
        fixture.Review(reviewed);
        string built = fixture.Commit("Unreviewed documentation.");
        fixture.Provider.HeadSha = built;
        var registry = new ProviderRegistry();
        registry.Register(new MovingProvider(fixture.Provider, reviewed));
        fixture.Service.Providers = registry;
        ReleaseResponse submitted = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(submitted.Ok, submitted.Message);
        fixture.Provider.CompleteRun("Example/widget", submitted.Run!, true, "1.0.0");
        var request = new ReleaseObserveRequest("1.0.0", "Example/widget", fixture.Estate.OperatorId);

        ReleaseResponse refused = fixture.Service.ReleaseObserve(request);
        Assert.False(refused.Ok);
        Assert.Contains(built, refused.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Service.Records("release"));
        Assert.Equal("dispatched", EstateModel.BodyString(Assert.Single(fixture.Service.Session.Load().Current("operation")), "status"));

        fixture.Review(built);
        ReleaseResponse observed = fixture.Service.ReleaseObserve(request);
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal(built, Assert.Single(fixture.Service.Releases()).Commit);
        Assert.Single(fixture.Provider.Dispatches);
    }

    private sealed class MovingProvider(FakeReleaseProvider provider, string? resolved) : IReleaseProvider
    {
        public string Provider => provider.Provider;
        public ProviderDescriptor Descriptor => provider.Descriptor;
        public string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) => resolved;
        public DispatchReceipt Dispatch(DispatchRequest request) => provider.Dispatch(request);
        public RunObservation? ObserveRun(string destination, string runId) => provider.ObserveRun(destination, runId);
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) => provider.ObserveRelease(destination, tag, settings);
        public string ManualPublicationCommand(string destination, string tag) => provider.ManualPublicationCommand(destination, tag);
    }

    private sealed class Fixture : IDisposable
    {
        public TestEstate Estate { get; } = new(recordFormat: 2);
        public TestEstate Scratch { get; } = new();
        public string Repository { get; }
        public RecordEnvelope Resource { get; }
        public RecordEnvelope Declaration { get; }
        public EstateService Service { get; }
        public FakeReleaseProvider Provider { get; } = new();
        public ReleaseSubmitRequest Request { get; }

        public Fixture(bool covers = true)
        {
            Repository = Path.Combine(Scratch.Root, "source");
            Directory.CreateDirectory(Repository);
            Git(Repository, "init", "-q", "-b", "main");
            DocumentSources.Register(new GitFileSource());
            RecordEnvelope product = Estate.Add("product", Estate.OperatorId, """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],"release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/widget"}}}}""");
            Resource = Estate.Add("resource", Estate.OperatorId, """{"kind":"repository","name":"Widget source","role":"Product source.","location":{"github":"Example/widget"},"visibility":"public"}""");
            RepositoryClones.Register(Estate.Estate.Id, Resource.Id, Repository);
            string covered = covers ? ",{\"on\":\"covers\",\"level\":\"refuse\",\"paths\":[\"README.md\"]}" : string.Empty;
            Declaration = Estate.Add("living-document", Estate.OperatorId, $$"""{"path":"README.md","repository":"{{Resource.Id}}","title":"Readme","kind":"authored","source":"vericairn.software/git-file","triggers":[{"on":"release","level":"refuse","product":"{{product.Id}}"}{{covered}}],"checks":["links-resolve"],"summary":"Documents the widget."}""");
            Service = EstateService.Open(Estate.Root);
            var registry = new ProviderRegistry();
            registry.Register(Provider);
            Service.Providers = registry;
            Request = new ReleaseSubmitRequest(product.Id, "1.0.0", "Example/widget", Estate.OperatorId);
        }

        public string Commit(string content)
        {
            File.WriteAllText(Path.Combine(Repository, "README.md"), content);
            Git(Repository, "add", ".");
            Git(Repository, "commit", "-q", "--allow-empty", "-m", "Document the widget");
            return Git(Repository, "rev-parse", "HEAD");
        }

        public void Review(string commit)
        {
            WriteOutcome review = Service.DocumentReview(Declaration.Id, new DocumentReviewRequest(Estate.OperatorId, "current", "Read the release documentation.", Commit: commit));
            Assert.True(review.Ok, System.Text.Json.JsonSerializer.Serialize(review.Problem));
        }

        public void Dispose()
        {
            Service.Dispose();
            Estate.Dispose();
            WorkspaceCleanup.Delete(Scratch.Root);
        }
    }

    private static string Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in GitIdentity.Concat(arguments)) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10000), "Git did not exit.");
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }
}
