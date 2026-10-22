// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed partial class ReleasePolicyGateTests
{
    [Fact]
    public void PassingPreflightLeavesRecordsObjectsJournalsAndLiveStateByteForByteUnchanged()
    {
        using var f = new Fixture();
        f.Duty();
        int signatures = 0;
        f.Api.BeforeSigningRecord = () => signatures++;
        string live = Path.GetDirectoryName(f.Api.Session.Live.Path)!;
        var estateBefore = BytesOf(f.Records.Estate.Root);
        var liveBefore = BytesOf(live);

        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);

        Assert.True(check.Ok, check.Message);
        Assert.Equal("checked", check.Status);
        Assert.Equal(Fixture.Commit, check.Check!.Commit);
        Assert.Equal(f.Product.ComputedHash, check.Check.Expected);
        Assert.Equal(EstateService.Version, check.Check.ClientBuild);
        Assert.Equal(EstateService.Version, check.Check.RuntimeBuild);
        Assert.Empty(check.Check.Objections);
        Assert.True(check.Check.BasisBytes > 0);
        Assert.Null(check.Operation);
        Assert.Null(check.BasisEvidence);
        Assert.Equal(0, signatures);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Equal(estateBefore, BytesOf(f.Records.Estate.Root));
        Assert.Equal(liveBefore, BytesOf(live));

        ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
        Assert.True(submitted.Ok, submitted.Message);
        Assert.True(signatures > 0);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(check.Check.BasisBytes, f.Api.Session.Objects.Read(Assert.Single(submitted.BasisEvidence!))!.LongLength);
    }

    [Fact]
    public void CheckAggregatesSetupDocumentsPoliciesRevisionBuildAndCloneObjectionsWithoutSigning()
    {
        using var f = new Fixture();
        f.Duty();
        f.Clock.Now = f.Due;
        f.Records.Add("living-document", new()
        {
            ["pathPattern"] = "docs/generated/{month}/{date}-{slug}-{id}.md", ["title"] = "Generated guide", ["kind"] = "generated",
            ["generator"] = new JsonObject { ["id"] = "example.documents/uninstalled", ["inputs"] = new JsonArray("format=1", "audience=Internal", "selection=all-revisions") },
            ["source"] = "estate-file", ["triggers"] = new JsonArray(), ["checks"] = new JsonArray("matches-generator"), ["summary"] = "Required fixture verification.",
        });
        f.Api.Providers.Register(new CheckedSource(false, new string('f', 40)));
        f.Api.BeforeSigningRecord = () => Assert.Fail("A refused check or submission must not sign.");
        ReleaseSubmitRequest request = f.Request with { Expected = "stale", Provider = "uninstalled", ClientBuild = "unknown" };
        var before = BytesOf(f.Records.Estate.Root);

        ReleaseResponse check = f.Api.ReleaseCheck(request);
        ReleaseResponse submit = f.Api.ReleaseSubmit(request);

        Assert.False(check.Ok);
        Assert.False(submit.Ok);
        string[] codes = check.Check!.Objections.Select(o => o.Code).ToArray();
        foreach (string code in new[] { "release.setup", "document.release-hold", "policy.unmet", "release.expected", "release.client-build", "release.clone-dirty", "release.default-head" })
            Assert.Contains(code, codes);
        Assert.All(check.Check.Objections, o => Assert.False(string.IsNullOrWhiteSpace(o.NextAction)));
        Assert.Equal(check.Problem!.Findings, submit.Problem!.Findings);
        Assert.Equal(before, BytesOf(f.Records.Estate.Root));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void MissingSigningDeviceAndAuthorityAreReportedWithoutPresenceOrAnIntent()
    {
        using var f = new Fixture();
        f.Api.BeforeSigningRecord = () => Assert.Fail("Preflight never enters the signing boundary.");
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request with { By = f.Records.Estate.BuilderId });
        Assert.False(check.Ok);
        Assert.Contains(check.Check!.Objections, o => o.Code == "release.signature");
        Assert.Contains(check.Check.Objections, o => o.Code.Contains("authority", StringComparison.Ordinal) || o.Code.Contains("author", StringComparison.Ordinal));
        Assert.Empty(f.Api.Session.Read().OfType("operation"));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void APassingCheckDoesNotAuthorizeSubmissionAfterTheProductChanges()
    {
        using var f = new Fixture();
        ReleaseSubmitRequest request = f.Request;
        Assert.True(f.Api.ReleaseCheck(request).Ok);
        f.Records.Add("product", Fixture.ProductBody("widget"), supersedes: f.Product.Id);
        ReleaseResponse refused = f.Api.ReleaseSubmit(request);
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, o => o.Code == "release.product");
        Assert.Empty(f.Api.Session.Read().OfType("operation"));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Theory]
    [InlineData("prepared", "dirty", "release.clone-dirty")]
    [InlineData("before-dispatch", "dirty", "release.clone-dirty")]
    [InlineData("prepared", "head", "release.default-head")]
    [InlineData("before-dispatch", "head", "release.default-head")]
    public void ChangedSourcePreconditionsAreRecheckedBeforeIntentAndDispatch(string boundary, string change, string code)
    {
        using var f = new Fixture();
        Assert.True(f.Api.ReleaseCheck(f.Request).Ok);
        f.Api.ReleaseBoundary = stage =>
        {
            if (stage == boundary) f.Api.Providers.Register(new CheckedSource(change != "dirty", change == "head" ? new string('f', 40) : Fixture.Commit));
        };

        ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);

        Assert.False(response.Ok);
        Assert.Contains(response.Problem!.Findings, finding => finding.Code == code);
        Assert.Equal(0, f.Provider.Calls);
        if (boundary == "prepared") Assert.Empty(f.Api.Session.Read().OfType("operation"));
        else Assert.Equal("canceled", EstateModel.BodyString(Assert.Single(f.Api.Session.Read().Current("operation")), "status"));
    }

    [Fact]
    public void AnUnresolvedIdentityIsReportedWithoutSigningOrRewritingItsOriginalIntent()
    {
        using var f = new Fixture();
        Assert.True(f.Api.ReleaseSubmit(f.Request).Ok);
        var before = BytesOf(f.Records.Estate.Root);
        f.Api.BeforeSigningRecord = () => Assert.Fail("An unresolved identity must be inspected without signing.");

        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        ReleaseResponse submit = f.Api.ReleaseSubmit(f.Request);

        Assert.False(check.Ok);
        Assert.Contains(check.Check!.Objections, o => o.Code == "release.identity");
        Assert.Equal(check.Problem!.Findings, submit.Problem!.Findings);
        Assert.Equal(before, BytesOf(f.Records.Estate.Root));
        Assert.Equal(1, f.Provider.Calls);
    }

    [Fact]
    public void ExistingRecordSignatureFindingsRemainObjections()
    {
        using var f = new Fixture();
        RecordEnvelope policy = f.Duty();
        string path = Path.Combine(f.Records.Estate.Root, policy.ExpectedPath);
        JsonObject envelope = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        envelope["signatures"] = new JsonArray();
        File.WriteAllBytes(path, CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(envelope)));
        var before = BytesOf(f.Records.Estate.Root);

        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        ReleaseResponse submit = f.Api.ReleaseSubmit(f.Request);

        Assert.False(check.Ok);
        Assert.Contains(check.Check!.Objections, o => o.Code == "signature.required" && o.Location == policy.ExpectedPath);
        Assert.Equal(check.Problem!.Findings, submit.Problem!.Findings);
        Assert.Equal(before, BytesOf(f.Records.Estate.Root));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void OversizedSourceObjectsAreReportedBeforeAnyBasisObjectIsRetained()
    {
        using var f = new Fixture();
        f.Records.Estate.WriteFile("docs/large.md", new string('x', checked((int)EstateLayout.MaxObjectBytes + 1)));
        var before = BytesOf(f.Records.Estate.Root);
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        ReleaseResponse submit = f.Api.ReleaseSubmit(f.Request);
        Assert.False(check.Ok);
        Assert.False(submit.Ok);
        Assert.Contains(check.Check!.Objections, o => o.Code == "release.basis-file-size");
        Assert.Equal(check.Problem!.Findings, submit.Problem!.Findings);
        Assert.Empty(f.Api.Session.Objects.List());
        Assert.Equal(before, BytesOf(f.Records.Estate.Root));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public void OversizedSerializedBasisIsMeasuredAndRefusedWithoutRetainingEvenSmallFiles()
    {
        using var f = new Fixture();
        RecordEnvelope resource = Assert.Single(f.Api.Session.Read().Current("resource"));
        JsonObject definition = f.Records.PolicyBody("source");
        definition["level"] = "remind";
        definition["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-10));
        definition["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = resource.Id, ["selectors"] = new JsonArray("docs/**") };
        definition["rules"] = new JsonArray(new JsonObject { ["key"] = "revision", ["kind"] = "vericairn.estate/revision-line", ["version"] = 1,
            ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        f.Records.Policy(definition);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new LargeInventory();
            ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
            ReleaseResponse submit = f.Api.ReleaseSubmit(f.Request);
            Assert.False(check.Ok);
            Assert.False(submit.Ok);
            Assert.True(check.Check!.BasisBytes > EstateLayout.MaxObjectBytes);
            Assert.Contains(check.Check.Objections, o => o.Code == "release.basis-size");
            Assert.Contains(submit.Problem!.Findings, o => o.Code == "release.basis-size");
            Assert.Empty(f.Api.Session.Objects.List());
            Assert.Empty(f.Api.Session.Read().OfType("operation"));
            Assert.Equal(0, f.Provider.Calls);
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    [Fact]
    public async Task ReadOnlyMcpAndSyncAsyncSdkCarryTheExactCheckAndNeverUseTheSubmitRoute()
    {
        using var f = new Fixture();
        f.Duty();
        f.Clock.Now = f.Due;
        f.Api.BeforeSigningRecord = () => Assert.Fail("No client may sign a check.");
        var before = BytesOf(f.Records.Estate.Root);
        ReleaseResponse expected = f.Api.ReleaseCheck(f.Request);
        var registry = new ToolRegistry(f.Api, null);
        ToolDefinition tool = Assert.Single(registry.Visible(), t => t.Name == "release_submit_check");
        Assert.False(tool.Writes);
        ToolCallResult result = await registry.CallAsync(tool.Name, JsonSerializer.SerializeToNode(f.Request, ProtocolJson.Options)!.AsObject(), TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(JsonSerializer.Serialize(expected, ProtocolJson.Options), result.Text);
        using var client = new VericairnClient(new HttpClient(new CheckHandler(f.Api, f.Request)) { BaseAddress = new Uri("http://fixture.invalid") });
        Assert.Equal(JsonSerializer.Serialize(expected, ProtocolJson.Options), JsonSerializer.Serialize(client.ReleaseCheck(f.Request), ProtocolJson.Options));
        Assert.Equal(JsonSerializer.Serialize(expected, ProtocolJson.Options), JsonSerializer.Serialize(await client.ReleaseCheckAsync(f.Request, TestContext.Current.CancellationToken), ProtocolJson.Options));
        Assert.Equal(before, BytesOf(f.Records.Estate.Root));
        Assert.Equal(0, f.Provider.Calls);
    }

    private static KeyValuePair<string, string>[] BytesOf(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => new KeyValuePair<string, string>(Path.GetRelativePath(root, path), ContentHash.Sha256File(path))).OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();

    private sealed class CheckedSource(bool clean, string head) : ISourceAdapter
    {
        public ProviderDescriptor Descriptor { get; } = new("fixture-source", ISourceAdapter.Contract, "implemented", [], []);
        public bool IsRepository(string path) => true;
        public RepositoryObservation Inspect(string path) => throw new InvalidOperationException("Unexpected source inspection.");
        public ReleaseSourceObservation? InspectRelease(string path) => new(clean, "main", head);
    }

    private sealed class LargeInventory : IPolicyRepositorySource
    {
        private readonly ImmutableArray<PolicyFile> files = [.. Enumerable.Range(0, 40000).Select(i =>
            new PolicyFile("docs/" + new string('a', 180) + "/file-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".md", ContentHash.Sha256([]), []))];
        public PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit) => new(repository.Id, commit!, PolicyBasis.RepositoryContract, true, files);
    }

    private sealed class CheckHandler(EstateService service, ReleaseSubmitRequest expected) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/releases/check", request.RequestUri!.AbsolutePath);
            var submitted = await request.Content!.ReadFromJsonAsync<ReleaseSubmitRequest>(ProtocolJson.Options, cancellationToken);
            Assert.Equal(expected, submitted);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(service.ReleaseCheck(submitted!), options: ProtocolJson.Options) };
        }
    }
}
