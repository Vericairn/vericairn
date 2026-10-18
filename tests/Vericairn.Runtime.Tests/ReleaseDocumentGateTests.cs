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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ADocumentInAnotherRepositorySubmitsWhenItsContentIsStillReviewed(bool covers, bool newerHead)
    {
        using var fixture = new Fixture(covers, otherRepository: true);
        string reviewed = fixture.Commit("Reviewed documentation.");
        fixture.Review(reviewed);
        string head = newerHead ? fixture.Commit("Reviewed documentation.") : reviewed;

        ReleaseResponse submitted = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(submitted.Ok, submitted.Message);
        RecordEnvelope intent = fixture.Service.Session.Load().OfType("operation").Single(r => EstateModel.BodyString(r, "status") == "not-started");
        Assert.Contains($"README.md at {head[..12]}", intent.Body.GetRawText(), StringComparison.Ordinal);
        fixture.Provider.CompleteRun("Example/widget", submitted.Run!, true, "1.0.0");
        ReleaseResponse observed = fixture.Service.ReleaseObserve(new ReleaseObserveRequest("1.0.0", "Example/widget", fixture.Estate.OperatorId));
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal(fixture.Provider.HeadSha, Assert.Single(fixture.Service.Releases()).Commit);
        Assert.Single(fixture.Provider.Dispatches);
        Assert.Equal(head, Git(fixture.Repository, "rev-parse", "HEAD"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AChangedDocumentInAnotherRepositoryRefusesBeforeIntent(bool covers, bool divergent)
    {
        using var fixture = new Fixture(covers, otherRepository: true);
        string first = fixture.Commit("Old documentation.");
        fixture.Review(fixture.Commit("Reviewed documentation."));
        if (divergent)
        {
            Git(fixture.Repository, "checkout", "-q", "-B", "main", first);
        }

        string changed = fixture.Commit("Changed documentation.");

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains("README.md changed after its review", response.Message, StringComparison.Ordinal);
        Assert.Contains(changed[..12], response.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Provider.Dispatches);
        Assert.Empty(fixture.Service.Records("operation"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADocumentInAnotherRepositoryWithoutAReviewRefusesBeforeIntent(bool covers)
    {
        using var fixture = new Fixture(covers, otherRepository: true);
        fixture.Commit("Unreviewed documentation.");

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains("README.md: no review has cleared its content", response.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Provider.Dispatches);
        Assert.Empty(fixture.Service.Records("operation"));
    }

    [Fact]
    public void AnUnregisteredOtherRepositoryIsReportedAsNotComparedAtItsOwnCommit()
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        File.Delete(EstateLocations.ClonesPath(fixture.Estate.Estate.Id));

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(response.Ok, response.Message);
        RecordEnvelope intent = fixture.Service.Session.Load().OfType("operation").Single(r => EstateModel.BodyString(r, "status") == "not-started");
        string evidence = intent.Body.GetProperty("evidence").GetRawText();
        Assert.Contains("README.md not compared, since no clone of its repository is registered here", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Provider.HeadSha, evidence, StringComparison.Ordinal);
        Assert.Single(fixture.Provider.Dispatches);
    }

    [Fact]
    public void OtherRepositoryCoveredPathsAreCheckedEvenWhenTheDocumentIsUnchanged()
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        File.WriteAllText(Path.Combine(fixture.Repository, "guide.md"), "New covered content.");
        string changed = fixture.Commit("Reviewed documentation.");

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains("its covered paths (README.md, guide.md) changed after the review", response.Message, StringComparison.Ordinal);
        Assert.Contains(changed[..12], response.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Provider.Dispatches);
        Assert.Empty(fixture.Service.Records("operation"));
    }

    [Fact]
    public void OtherRepositoryChangesAfterDispatchRefuseObservationUntilReviewed()
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        ReleaseResponse submitted = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(submitted.Ok, submitted.Message);
        string changed = fixture.Commit("Changed documentation.");
        fixture.Provider.CompleteRun("Example/widget", submitted.Run!, true, "1.0.0");
        var request = new ReleaseObserveRequest("1.0.0", "Example/widget", fixture.Estate.OperatorId);

        ReleaseResponse refused = fixture.Service.ReleaseObserve(request);
        Assert.False(refused.Ok);
        Assert.Contains("README.md changed after its review", refused.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Service.Records("release"));
        fixture.Review(changed);
        ReleaseResponse observed = fixture.Service.ReleaseObserve(request);
        Assert.True(observed.Ok, observed.Message);
        Assert.Single(fixture.Provider.Dispatches);
    }

    [Fact]
    public void OtherRepositoryDocumentsDoNotRequireTheProvidersReleaseCommit()
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        var registry = new ProviderRegistry();
        registry.Register(new MovingProvider(fixture.Provider, null));
        fixture.Service.Providers = registry;

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(response.Ok, response.Message);
        Assert.Single(fixture.Provider.Dispatches);
    }

    [Theory]
    [InlineData("Example/profile")]
    [InlineData("example/PROFILE")]
    public void RepositoryRevisionsAndRequestOverridesKeepTheExactReleaseCommitCheck(string destination)
    {
        using var fixture = new Fixture(otherRepository: true);
        string reviewed = fixture.Commit("Reviewed documentation.");
        fixture.Review(reviewed);
        string head = fixture.Commit("Unreviewed future documentation.");
        fixture.Provider.HeadSha = reviewed;
        fixture.Estate.Add("resource", fixture.Estate.OperatorId, fixture.Resource.Body.GetRawText(), supersedes: fixture.Resource.Id);
        RecordEnvelope product = fixture.Estate.Load().ById[fixture.Request.Product];
        RecordEnvelope revisedProduct = fixture.Estate.Add("product", fixture.Estate.OperatorId, product.Body.GetRawText(), supersedes: product.Id);

        ReleaseResponse submitted = fixture.Service.ReleaseSubmit(fixture.Request with { Product = revisedProduct.Id, Repository = destination });
        Assert.True(submitted.Ok, submitted.Message);
        RecordEnvelope intent = fixture.Service.Session.Load().OfType("operation").Single(r => EstateModel.BodyString(r, "status") == "not-started");
        Assert.Contains($"README.md at {reviewed[..12]}", intent.Body.GetRawText(), StringComparison.Ordinal);
        fixture.Provider.CompleteRun(destination, submitted.Run!, true, "1.0.0");
        ReleaseResponse observed = fixture.Service.ReleaseObserve(new ReleaseObserveRequest("1.0.0", destination, fixture.Estate.OperatorId));
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal(reviewed, Assert.Single(fixture.Service.Releases()).Commit);
        Assert.Equal(head, Git(fixture.Repository, "rev-parse", "HEAD"));
    }

    [Fact]
    public void AnUnreadableRegisteredOtherRepositoryRefusesInsteadOfSkippingComparison()
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        RepositoryClones.Register(fixture.Estate.Estate.Id, fixture.Resource.Id, Path.Combine(fixture.Scratch.Root, "missing"));

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains("has no readable HEAD commit", response.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Resource.Id, response.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Provider.Dispatches);
        Assert.Empty(fixture.Service.Records("operation"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOlderOtherRepositoryHeadUsesTheNewerReviewedCommit(bool covers)
    {
        using var fixture = new Fixture(covers, otherRepository: true);
        string behind = fixture.Commit("Old documentation.");
        string reviewed = fixture.Commit("Reviewed documentation.");
        fixture.Review(reviewed);
        Git(fixture.Repository, "checkout", "-q", "-B", "main", behind);

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(response.Ok, response.Message);
        RecordEnvelope intent = fixture.Service.Session.Load().OfType("operation").Single(r => EstateModel.BodyString(r, "status") == "not-started");
        Assert.Contains($"README.md at {reviewed[..12]}", intent.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(behind, Git(fixture.Repository, "rev-parse", "HEAD"));
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

        public Fixture(bool covers = true, bool otherRepository = false)
        {
            Repository = Path.Combine(Scratch.Root, "source");
            Directory.CreateDirectory(Repository);
            Git(Repository, "init", "-q", "-b", "main");
            DocumentSources.Register(new GitFileSource());
            RecordEnvelope product = Estate.Add("product", Estate.OperatorId, """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],"release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/widget"}}}}""");
            string location = otherRepository ? "Example/profile" : "Example/widget";
            Resource = Estate.Add("resource", Estate.OperatorId, $$"""{"kind":"repository","name":"Widget source","role":"Product source.","location":{"github":"{{location}}"},"visibility":"public"}""");
            RepositoryClones.Register(Estate.Estate.Id, Resource.Id, Repository);
            string covered = covers ? ",{\"on\":\"covers\",\"level\":\"refuse\",\"paths\":[\"README.md\",\"guide.md\"]}" : string.Empty;
            Declaration = Estate.Add("living-document", Estate.OperatorId, $$"""{"path":"README.md","repository":"{{Resource.Id}}","title":"Readme","kind":"authored","source":"vericairn.software/git-file","triggers":[{"on":"release","level":"refuse","product":"{{product.Id}}"}{{covered}}],"checks":["links-resolve"],"summary":"Documents the widget."}""");
            Service = EstateService.Open(Estate.Root);
            var registry = new ProviderRegistry();
            registry.Register(Provider);
            Service.Providers = registry;
            Request = new ReleaseSubmitRequest(product.Id, "1.0.0", "Example/widget", Estate.OperatorId);
            if (otherRepository)
            {
                string productRepository = Path.Combine(Scratch.Root, "product");
                Directory.CreateDirectory(productRepository);
                Git(productRepository, "init", "-q", "-b", "main");
                Git(productRepository, "commit", "-q", "--allow-empty", "-m", "Product source");
                Provider.HeadSha = Git(productRepository, "rev-parse", "HEAD");
            }
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
