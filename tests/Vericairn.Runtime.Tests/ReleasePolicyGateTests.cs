// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class ReleasePolicyGateTests
{
    [Theory]
    [InlineData("intent")]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void CrossedDeadlineRefusesAtEveryAdmissionBoundaryWithoutProviderCalls(string boundary)
    {
        using var f = new Fixture();
        f.Duty();
        Assert.Equal(0, f.Api.Policies().Holds);
        if (boundary == "intent") f.Clock.Now = f.Due;
        else f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = f.Due; };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Contains(response.Problem!.Findings, finding => finding.Code == "policy.unmet" && finding.Message.Contains("deadline", StringComparison.Ordinal));
        Assert.Equal("submission", response.Policies!.Basis.Origin);
        Assert.Equal(1, response.Policies.Holds);
        Assert.Equal(0, f.Provider.Calls);
        if (boundary == "before-dispatch")
        {
            Assert.Equal("canceled", response.Status);
            Assert.Equal("canceled", EstateModel.BodyString(Assert.Single(f.Api.Session.Load().Current("operation")), "status"));
            Assert.True(response.BasisEvidence!.Count >= 2);
        }
        else Assert.Empty(f.Api.Records("operation"));
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void APolicyAddedAfterPreparationIsReevaluatedBeforeAnyProviderCall(string boundary)
    {
        using var f = new Fixture();
        f.Api.ReleaseBoundary = stage =>
        {
            if (stage != boundary) return;
            JsonObject policy = f.Records.PolicyBody("new-hold", duty: false);
            policy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
            f.Records.Policy(policy);
        };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Contains(response.Problem!.Findings, finding => finding.Code == "policy.unavailable");
        Assert.Equal(0, f.Provider.Calls);
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("before-dispatch")]
    public void AnExactSubmissionExceptionCannotOutliveItsExpiry(string boundary)
    {
        using var f = new Fixture();
        RecordEnvelope policy = f.Duty();
        f.Records.Estate.SetClock(f.Due.AddSeconds(1));
        RecordEnvelope candidate = f.Candidate();
        JsonObject exception = f.Records.ExceptionBody(policy);
        DateTimeOffset expiry = f.Due.AddMinutes(10);
        exception["expiresAt"] = RecordParser.FormatTimestamp(expiry);
        exception["targets"]![0]!["submission"] = new JsonObject { ["release"] = PolicyFixture.Ref(candidate), ["product"] = f.Product.Id, ["version"] = "1.0.0" };
        f.Records.Add("policy-exception", exception, subject: policy.Reference);
        f.Clock.Now = expiry.AddSeconds(-1);
        f.Records.AssertValid();
        f.Api.ReleaseBoundary = stage => { if (stage == boundary) f.Clock.Now = expiry; };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Contains(response.Policies!.Findings, finding => finding.ReleaseHold && finding.Disposition != "excepted");
    }

    [Fact]
    public void BuiltInDocumentPolicyAndPreparationObjectionsAreReportedTogether()
    {
        using var f = new Fixture();
        f.Duty();
        f.Clock.Now = f.Due;
        f.Records.Add("living-document", new JsonObject
        {
            ["pathPattern"] = "docs/generated/{month}/{date}-{slug}-{id}.md", ["title"] = "Generated decisions", ["kind"] = "generated",
            ["generator"] = new JsonObject { ["id"] = "example.documents/uninstalled", ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
            ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["checks"] = new JsonArray("matches-generator"), ["summary"] = "Mandatory fixture verification.",
        });
        Directory.CreateDirectory(Path.Combine(f.Records.Estate.Root, "docs"));
        File.WriteAllBytes(Path.Combine(f.Records.Estate.Root, "docs", "prose.md"), [0xff]);
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request with { Expected = "stale" });
        Assert.False(response.Ok);
        string[] codes = response.Problem!.Findings.Select(finding => finding.Code).ToArray();
        Assert.Contains("layout.encoding", codes);
        Assert.Contains("document.release-hold", codes);
        Assert.Contains("policy.unmet", codes);
        Assert.Contains("release.expected", codes);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Api.Records("operation"));
        ReleaseResponse unavailableProvider = f.Api.ReleaseSubmit(f.Request with { Provider = "uninstalled" });
        Assert.Contains(unavailableProvider.Problem!.Findings, finding => finding.Code == "release.setup");
        Assert.Contains(unavailableProvider.Problem.Findings, finding => finding.Code == "policy.unmet");
        Assert.Contains(unavailableProvider.Problem.Findings, finding => finding.Code == "document.release-hold");
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task APassingSubmissionRetainsExactInputsAndRechecksWithoutHashingItsOwnIntent()
    {
        using var f = new Fixture();
        f.Duty();
        Directory.CreateDirectory(Path.Combine(f.Records.Estate.Root, "docs"));
        File.WriteAllText(Path.Combine(f.Records.Estate.Root, "docs", "evidence.md"), "Retained admission source.\n");
        f.Api.ReleaseBoundary = stage => { if (stage == "before-dispatch") f.Clock.Now = f.Clock.Now.AddSeconds(1); };
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(Fixture.Commit, f.Provider.Dispatched!.ExpectedCommit);
        Assert.Equal(2, response.BasisEvidence!.Count);
        RecordEnvelope intent = f.Api.Session.Load().OfType("operation").Single(o => EstateModel.BodyString(o, "status") == "not-started");
        string? recordDigest = null;
        foreach (string hash in response.BasisEvidence)
        {
            using JsonDocument evidence = JsonDocument.Parse(f.Api.Session.Objects.Read(hash)!);
            JsonElement root = evidence.RootElement;
            Assert.Equal("vericairn.release-basis/1", root.GetProperty("format").GetString());
            JsonElement basis = root.GetProperty("basis");
            Assert.Equal(Fixture.Commit, basis.GetProperty("submission").GetProperty("commit").GetString());
            Assert.Equal("submission", basis.GetProperty("origin").GetString());
            Assert.DoesNotContain(root.GetProperty("records").EnumerateArray(), r => r.GetProperty("id").GetString() == intent.Id);
            string digest = basis.GetProperty("recordDigest").GetString()!;
            Assert.True(recordDigest is null || recordDigest == digest);
            recordDigest = digest;
            int before = f.Api.Session.Load().Records.Count;
            ReleaseBasisResponse retained = f.Api.ReleaseBasis(hash)!;
            Assert.NotNull(retained);
            Assert.Equal(digest, retained.Evidence.Basis.RecordDigest);
            Assert.Equal(Fixture.Commit, retained.Evidence.Basis.Submission!.Commit);
            Assert.Equal("Retained admission source.\n", System.Text.Encoding.UTF8.GetString(Assert.Single(retained.Evidence.Repositories.SelectMany(r => r.Files), file => file.Path == "docs/evidence.md").Bytes.ToArray()));
            using var client = new VericairnClient(new HttpClient(new BasisHandler(retained)) { BaseAddress = new Uri("http://fixture.invalid") });
            Assert.Equal(JsonSerializer.Serialize(retained, ProtocolJson.Options), JsonSerializer.Serialize(client.ReleaseBasis(hash), ProtocolJson.Options));
            Assert.Equal(JsonSerializer.Serialize(retained, ProtocolJson.Options), JsonSerializer.Serialize(await client.ReleaseBasisAsync(hash, TestContext.Current.CancellationToken), ProtocolJson.Options));
            ToolCallResult read = await new ToolRegistry(f.Api, null).CallAsync("release_basis_get", new() { ["hash"] = hash }, TestContext.Current.CancellationToken);
            Assert.False(read.IsError, read.Text);
            Assert.Equal(JsonSerializer.Serialize(retained, ProtocolJson.Options), read.Text);
            Assert.Equal(before, f.Api.Session.Load().Records.Count);
        }
        Assert.Null(f.Api.ReleaseBasis("unavailable"));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    private sealed class BasisHandler(ReleaseBasisResponse basis) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/release-bases/" + Uri.EscapeDataString(basis.Hash), request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(basis, options: ProtocolJson.Options) });
        }
    }

    [Fact]
    public void OtherProductAndRepositoryHoldsStayVisibleButDoNotHoldThisRelease()
    {
        using var f = new Fixture();
        RecordEnvelope other = f.Records.Add("product", Fixture.ProductBody("other"));
        JsonObject productPolicy = f.Records.PolicyBody("other-product", duty: false);
        productPolicy["appliesTo"] = new JsonObject { ["kind"] = "product", ["product"] = other.Id };
        productPolicy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        f.Records.Policy(productPolicy);
        RecordEnvelope repository = f.Records.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Other source", ["role"] = "Another product's source.", ["location"] = new JsonObject { ["github"] = "Example/other" }, ["visibility"] = "public" });
        JsonObject repositoryPolicy = f.Records.PolicyBody("other-repository", duty: false);
        repositoryPolicy["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository.Id, ["selectors"] = new JsonArray("docs/**") };
        repositoryPolicy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        f.Records.Policy(repositoryPolicy);
        f.Records.AssertValid();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.True(response.Ok, response.Message);
        Assert.All(response.Policies!.Findings, finding => Assert.False(finding.Enforced));
        Assert.Equal(1, f.Provider.Calls);
    }

    [Fact]
    public void GenericRecordAuthoringCannotUseAnOldTimeOrSkipReleaseHolds()
    {
        using var f = new Fixture();
        f.Duty();
        f.Clock.Now = f.Due;
        JsonElement body = PolicyFixture.Element(ReleaseFlow.IntentBody(f.Provider.Provider, "Example/widget", new Dictionary<string, string> { ["repository"] = "Example/widget", ["ref"] = Fixture.Commit }, "1.0.0", f.Product.Id));
        var request = new NewRecordRequest("operation", f.Records.Estate.OperatorId, body, "product:" + f.Product.Id, Expected: PolicyBasis.Hash(body));
        Assert.False(f.Api.NewRecord(request with { At = RecordParser.FormatTimestamp(f.Due.AddSeconds(-1)) }).Ok);
        WriteOutcome refused = f.Api.NewRecord(request);
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, finding => finding.Code == "policy.unmet");
        Assert.Empty(f.Api.Records("operation"));
        Assert.Equal(0, f.Provider.Calls);
        RecordEnvelope candidate = f.Candidate();
        f.Records.Add("acceptance", new JsonObject { ["statement"] = "Fixture acceptance.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        JsonObject published = PolicyFixture.Body(candidate);
        published["status"] = "published";
        published["publishedAt"] = RecordParser.FormatTimestamp(f.Clock.Now);
        published["url"] = "https://example.invalid/release";
        WriteOutcome publication = f.Api.NewRecord(new("release", f.Records.Estate.OperatorId, PolicyFixture.Element(published), "product:" + f.Product.Id, candidate.Id, Expected: candidate.ComputedHash));
        Assert.False(publication.Ok);
        Assert.Contains(publication.Problem!.Findings, finding => finding.Code == "policy.unmet");
        Assert.Single(f.Api.Records("release"));
    }

    [Fact]
    public void ARefuseRepositoryPolicyWithNoRegisteredCloneRemainsUnavailable()
    {
        using var f = new Fixture();
        RecordEnvelope repository = f.Records.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Release source", ["role"] = "The selected source.", ["location"] = new JsonObject { ["github"] = "Example/widget" }, ["visibility"] = "public" });
        JsonObject policy = f.Records.PolicyBody("source", duty: false);
        policy["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = repository.Id, ["selectors"] = new JsonArray("docs/**") };
        policy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        policy["rules"] = new JsonArray(new JsonObject { ["key"] = "revision", ["kind"] = "vericairn.estate/revision-line", ["version"] = 1, ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        f.Records.Policy(policy);
        f.Records.AssertValid();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.False(response.Ok);
        Assert.Contains(response.Policies!.Findings, finding => finding.ReleaseHold && finding.Verdict == "unavailable");
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Api.Records("operation"));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", false)]
    public void ExplicitWorkPolicyAppliesOnlyToTheReleaseThatCarriesTheWork(string version, bool held)
    {
        using var f = new Fixture();
        RecordEnvelope carrier = f.Records.Add("work-order", new JsonObject
        {
            ["key"] = "REL-1", ["kind"] = "ordinary", ["title"] = "A fixture release", ["intent"] = "Carry the selected work.", ["scope"] = new JsonArray("fixture"),
            ["exitQuestion"] = "Is the release complete?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("release"),
            ["releasePlan"] = new JsonObject { ["product"] = f.Product.Id, ["version"] = version },
        });
        JsonObject policy = f.Records.PolicyBody("explicit-work", duty: false);
        policy["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-1));
        policy["appliesTo"] = new JsonObject { ["kind"] = "work-orders", ["workOrders"] = new JsonArray(carrier.Id) };
        f.Records.Policy(policy);
        f.Records.AssertValid();
        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
        Assert.Equal(!held, response.Ok);
        Assert.Equal(held ? 0 : 1, f.Provider.Calls);
    }

    private sealed class Clock(DateTimeOffset at) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = at;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Commit = "0123456789abcdef0123456789abcdef01234567";
        internal PolicyFixture Records { get; } = new();
        internal EstateService Api { get; }
        internal CountingProvider Provider { get; } = new();
        internal RecordEnvelope Product { get; }
        internal Clock Clock { get; }
        internal DateTimeOffset Due { get; }
        internal ReleaseSubmitRequest Request => new(Product.Id, "1.0.0", "Example/widget", Records.Estate.OperatorId, Commit: Commit, Expected: Product.ComputedHash);

        internal Fixture()
        {
            Product = Records.Add("product", ProductBody("widget"));
            Api = EstateService.Open(Records.Estate.Root);
            RecordWritten device = Api.DeviceCreate(new(Records.Estate.OperatorId, "Fixture release writer", "file")).Written!;
            RecordEnvelope held = Api.Session.Load().ById[device.Id];
            Records.Estate.SetClock(held.AtTime);
            Records.Add("admission", new JsonObject { ["principal"] = Records.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Admit the isolated fixture writer." }, subject: held.Reference);
            Clock = new(Records.Estate.Clock.AddMinutes(1));
            Due = Clock.Now.AddSeconds(30);
            Api.PolicyClock = Clock;
            var registry = new ProviderRegistry();
            registry.Register(Provider);
            Api.Providers = registry;
        }

        internal RecordEnvelope Duty()
        {
            JsonObject policy = Records.PolicyBody();
            policy["startsAt"] = RecordParser.FormatTimestamp(Clock.Now.AddSeconds(-10));
            policy["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = RecordParser.FormatTimestamp(Due);
            policy["rules"]![0]!["parameters"]!["graceDays"] = 0;
            return Records.Policy(policy);
        }

        internal RecordEnvelope Candidate() => Records.Add("release", new JsonObject
        {
            ["version"] = "1.0.0", ["repository"] = "Example/widget", ["commit"] = Commit, ["status"] = "candidate", ["artifacts"] = new JsonArray(),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(),
            ["signing"] = "fixture", ["limitations"] = new JsonArray(),
        }, subject: Product.Reference);

        internal static JsonObject ProductBody(string slug) => new()
        {
            ["name"] = slug, ["slug"] = slug, ["summary"] = "A fixture product.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture interface." }),
            ["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = "github-releases", ["settings"] = new JsonObject { ["repository"] = "Example/" + slug } } },
        };

        public void Dispose() { Api.Dispose(); Records.Dispose(); }
    }

    private sealed class CountingProvider : IReleaseProvider
    {
        internal int Calls { get; private set; }
        internal DispatchRequest? Dispatched { get; private set; }
        public string Provider => "github-releases";
        public ProviderDescriptor Descriptor { get; } = new FakeReleaseProvider().Descriptor;
        public string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) { Calls++; throw new InvalidOperationException("Admission must not resolve through the provider."); }
        public DispatchReceipt Dispatch(DispatchRequest request)
        {
            Calls++;
            Dispatched = request;
            return new("fixture-run", "https://example.invalid/fixture-run", "Fixture dispatch.", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1)));
        }
        public RunObservation? ObserveRun(string destination, string runId) { Calls++; return null; }
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) { Calls++; return null; }
        public string ManualPublicationCommand(string destination, string tag) { Calls++; return "Manual fixture publication."; }
    }
}
