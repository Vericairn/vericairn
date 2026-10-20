// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class PolicyHashApiTests
{
    public PolicyHashApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void ContractDescribesPreparationAndItsHashOrProblemResponse()
    {
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = contract.RootElement.GetProperty("paths").GetProperty("/v1/estate-policies/hash").GetProperty("post");
        Assert.Contains("writes nothing", operation.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("PolicyHashRequest", operation.GetProperty("requestBody").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("PolicyHashResponse", operation.GetProperty("responses").GetProperty("200").GetRawText(), StringComparison.Ordinal);
        Assert.Contains("PolicyHashResponse", operation.GetProperty("responses").GetProperty("422").GetRawText(), StringComparison.Ordinal);
        Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
        JsonElement schemas = contract.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal("body", Assert.Single(schemas.GetProperty("PolicyHashRequest").GetProperty("required").EnumerateArray()).GetString());
        JsonElement properties = schemas.GetProperty("PolicyHashResponse").GetProperty("properties");
        Assert.True(properties.GetProperty("definitionHash").GetProperty("nullable").GetBoolean());
        Assert.True(properties.GetProperty("problem").GetProperty("nullable").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApiAndBothSdkMethodsReturnTheEmbeddedHashWithoutWriting(bool duty)
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        using var http = new HttpClient(new PolicyAdoptionApiTests.RouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        var request = new PolicyHashRequest(PolicyFixture.Element(fixture.PolicyBody(duty: duty)));
        Dictionary<string, string> before = Snapshot(fixture.Estate.Root);
        PolicyHashResponse expected = service.PolicyHash(request);
        Assert.Null(expected.Problem);
        Assert.True(ContentHash.IsValid(expected.DefinitionHash));
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/estate-policies/hash", UriKind.Relative), request, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<PolicyHashResponse>(ProtocolJson.Options, TestContext.Current.CancellationToken));
        Assert.Equal(expected, sdk.PolicyHash(request));
        Assert.Equal(expected, await sdk.PolicyHashAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal(expected, new CancellationBoundApi(sdk, TestContext.Current.CancellationToken).PolicyHash(request));
        Assert.Equal(before, Snapshot(fixture.Estate.Root));
    }

    [Theory]
    [InlineData("{", HttpStatusCode.BadRequest)]
    [InlineData("{}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("{\"body\":null}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("{\"body\":[]}", HttpStatusCode.UnprocessableEntity)]
    [InlineData("{\"body\":{\"type\":\"estate-policy\",\"body\":{}}}", HttpStatusCode.UnprocessableEntity)]
    public async Task InvalidRequestsCannotReturnAHash(string json, HttpStatusCode status)
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        using var http = new HttpClient(new PolicyAdoptionApiTests.RouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        Dictionary<string, string> before = Snapshot(fixture.Estate.Root);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(new Uri("/v1/estate-policies/hash", UriKind.Relative), content, TestContext.Current.CancellationToken);
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.UnprocessableEntity)
        {
            PolicyHashResponse? result = await response.Content.ReadFromJsonAsync<PolicyHashResponse>(ProtocolJson.Options, TestContext.Current.CancellationToken);
            Assert.NotNull(result);
            Assert.Null(result.DefinitionHash);
            Assert.Equal("invalid", result.Problem!.Code);
            Assert.Contains("prepared policy or duty body", result.Problem.Message, StringComparison.Ordinal);
        }
        Assert.Equal(before, Snapshot(fixture.Estate.Root));
    }

    [Fact]
    public async Task SdkPreservesAHashRefusalAcrossBothMethods()
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        using var http = new HttpClient(new PolicyAdoptionApiTests.RouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        var request = new PolicyHashRequest(JsonSerializer.SerializeToElement(new { }));
        PolicyHashResponse result = sdk.PolicyHash(request);
        PolicyHashResponse asynchronous = await sdk.PolicyHashAsync(request, TestContext.Current.CancellationToken);
        Assert.Null(result.DefinitionHash);
        Assert.Null(asynchronous.DefinitionHash);
        Assert.Equal("invalid", result.Problem!.Code);
        Assert.Equal(result.Problem.Message, asynchronous.Problem!.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeviceRequestsNeedNoWriteAuthoritySigningKeyOrWriteLedger(bool localNetwork)
    {
        using var fixture = new PolicyFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-policy-hash-ledger-" + Guid.NewGuid().ToString("N"));
        int writes = 0;
        var scope = new RequestScope(service, new LocalNetworkLedger(scratch), beforeWrite: () => writes++);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/estate-policies/hash";
        context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", fixture.Estate.BuilderId, "public-key", localNetwork);
        var request = new PolicyHashRequest(PolicyFixture.Element(fixture.PolicyBody()));
        Assert.Empty(service.PolicyAccess(fixture.Estate.BuilderId));
        Dictionary<string, string> before = Snapshot(fixture.Estate.Root);
        try
        {
            object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request),
                _ => ValueTask.FromResult<object?>(service.PolicyHash(request)));
            Assert.Null(Assert.IsType<PolicyHashResponse>(result).Problem);
            Assert.Equal(0, writes);
            Assert.False(Directory.Exists(scratch));
            Assert.Equal(before, Snapshot(fixture.Estate.Root));
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(root, path), path => ContentHash.Sha256(File.ReadAllBytes(path)));
}
