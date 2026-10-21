// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseDocumentGateTests
{
    private static readonly string[] GitIdentity = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void AnUnavailableStrictGeneratorRefusesReleaseWithoutAuthoredTriggersOrPins()
    {
        using var f = new PolicyFixture();
        RecordEnvelope product = f.Add("product", new JsonObject
        {
            ["name"] = "Example", ["slug"] = "example", ["summary"] = "Test product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Test edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Test surface." }),
            ["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = "github-releases", ["settings"] = new JsonObject { ["repository"] = "Example/example" } } },
        });
        f.Add("living-document", new JsonObject
        {
            ["pathPattern"] = "docs/generated/{month}/{date}-{slug}-{id}.md", ["title"] = "Generated decisions",
            ["kind"] = "generated", ["generator"] = new JsonObject { ["id"] = "example.documents/uninstalled", ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
            ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["checks"] = new JsonArray("matches-generator"), ["summary"] = "Test mandatory verification.",
        });
        f.AssertValid();
        using var service = EstateService.Open(f.Estate.Root);
        var provider = new FakeReleaseProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        service.Providers = registry;

        ReleaseResponse response = service.ReleaseSubmit(new ReleaseSubmitRequest(product.Id, "1.0.0", "Example/example", f.Estate.OperatorId));

        Assert.False(response.Ok);
        Assert.Contains("generator", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.Dispatches);
        Assert.Empty(service.Records("operation"));
        Assert.Empty(service.Records("document-revision"));
    }
    [Theory]
    [InlineData("behind", false)]
    [InlineData("ahead", false)]
    [InlineData("exact", false)]
    [InlineData("behind", true)]
    [InlineData("ahead", true)]
    [InlineData("exact", true)]
    public void SubmissionComparesThePreparedCommitRegardlessOfCloneHead(string position, bool covers)
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
    public void AnAbsentPreparedCommitCannotFallBackToTheReviewedCloneHeadOrCallTheProvider()
    {
        using var fixture = new Fixture();
        fixture.Review(fixture.Commit("Reviewed documentation."));
        var registry = new ProviderRegistry();
        registry.Register(new MovingProvider(fixture.Provider, null));
        fixture.Service.Providers = registry;
        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request with { Commit = null });
        Assert.False(response.Ok);
        Assert.Contains("exact release commit is unavailable", response.Message, StringComparison.Ordinal);
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
        ReleaseResponse submitted = fixture.Service.ReleaseSubmit(fixture.Request with { Commit = reviewed });
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
    public void AnUnregisteredRepositoryHoldsItsRefuseLevelDocumentBeforeIntent()
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        // The de-registration step 0.7.3's and 0.8.0's submissions used: the machine's clone registration removed.
        File.Delete(EstateLocations.ClonesPath(fixture.Estate.Estate.Id));

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.False(response.Ok);
        Assert.Contains("README.md: no clone of Widget source", response.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn sources clone <path> --resource " + fixture.Resource.Id, response.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Provider.Dispatches);
        Assert.Empty(fixture.Service.Records("operation"));
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("expired", false)]
    [InlineData("other-version", false)]
    [InlineData("no-expiry", false)]
    [InlineData("unsigned", false)]
    [InlineData("not-operator", false)]
    public void OnlyTheOperatorsSignedExpiringGrantForThisVersionExceptsAnUncomparedDocument(string grant, bool excepted)
    {
        using var fixture = new Fixture(otherRepository: true);
        fixture.Review(fixture.Commit("Reviewed documentation."));
        File.Delete(EstateLocations.ClonesPath(fixture.Estate.Estate.Id));
        string scope = ReleaseCoordinationService.UncomparedScope(fixture.Service.Session.Load(), fixture.Declaration, grant == "other-version" ? "1.0.1" : "1.0.0");
        string? expires = grant switch { "expired" => "2026-10-14T00:00:00Z", "no-expiry" => null, _ => "2030-01-01T00:00:00Z" };
        RecordEnvelope recorded = Grant(fixture, scope, expires, sign: grant != "unsigned", byOperator: grant != "not-operator");

        ReleaseResponse response = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.True(excepted == response.Ok, response.Message);
        if (excepted)
        {
            string evidence = fixture.Service.Session.Load().OfType("operation").Single(r => EstateModel.BodyString(r, "status") == "not-started").Body.GetProperty("evidence").GetRawText();
            Assert.Contains("README.md not compared", evidence, StringComparison.Ordinal);
            Assert.Contains("under signed grant " + recorded.Id, evidence, StringComparison.Ordinal);
            Assert.Single(fixture.Provider.Dispatches);
        }
        else
        {
            Assert.Contains("README.md: no clone of Widget source", response.Message, StringComparison.Ordinal);
            Assert.Empty(fixture.Provider.Dispatches);
            Assert.Empty(fixture.Service.Records("operation"));
        }
    }

    // A grant of one scope to the operator, by the operator or by the builder, signed with a fixture device or not.
    private static RecordEnvelope Grant(Fixture fixture, string scope, string? expires, bool sign, bool byOperator)
    {
        var store = new FileKeyStore(Path.Combine(fixture.Scratch.Root, "grant-keys"));
        string op = fixture.Estate.OperatorId;
        StoreSigner operatorSigner = Device(fixture, store, op, null);
        StoreSigner signer = byOperator ? operatorSigner : Device(fixture, store, fixture.Estate.BuilderId, operatorSigner);
        RecordEnvelope decision = fixture.Estate.Add("decision", op, $$"""{"title":"Release without comparing one document","statement":"Except README.md's comparison for this release only.","summary":"A bounded test exception.","synthesis":"Test evidence.","effects":["One uncompared document."],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["publication"]}""", signer: operatorSigner);
        string expiry = expires is null ? "" : $$""","expires":"{{expires}}" """.TrimEnd();
        return fixture.Estate.Add("authority", byOperator ? op : fixture.Estate.BuilderId, $$"""{"grantee":"{{op}}","scope":["{{scope}}"],"reserved":["publication"],"conditions":["Only while this machine cannot register the repository's clone."],"basis":"{{decision.Id}}"{{expiry}}}""",
            signer: sign ? signer : null);
    }

    // A principal's signing device, admitted by the operator's device, or by itself as the operator's first device.
    private static StoreSigner Device(Fixture fixture, FileKeyStore store, string principal, StoreSigner? admitter)
    {
        string id = Ulid.NewUlid().Value;
        string key = RecordSigning.EncodePublicKey(store.Create(id));
        var signer = new StoreSigner(store, id);
        RecordEnvelope device = fixture.Estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"Test signing device","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{key}}","profiles":["embedded"],"presence":"none","purpose":"signing"}""", id: id, signer: signer);
        fixture.Estate.Add("admission", fixture.Estate.OperatorId, $$"""{"principal":"{{principal}}","status":"admitted","note":"Test signing device."}""", subject: device.Reference, signer: admitter ?? signer);
        return signer;
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
        private readonly ReleaseSubmitRequest request;
        public ReleaseSubmitRequest Request => request with { Commit = Provider.HeadSha };

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
            request = new ReleaseSubmitRequest(product.Id, "1.0.0", "Example/widget", Estate.OperatorId);
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
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds), "Git did not exit.");
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }
}
