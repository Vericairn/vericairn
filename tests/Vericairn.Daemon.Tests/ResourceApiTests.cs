// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class ResourceApiTests
{
    public ResourceApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void ContractCoversRoutesRequestFieldsAndDefaultMembers()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (Type type in new[] { typeof(ResourceRequest), typeof(ResourceEntry), typeof(ResourcesResponse), typeof(ResourceResponse) })
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
                schemas.GetProperty(type.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        foreach (string method in new[] { "Resources", "ResourcesAsync", "ResourceRead", "ResourceReadAsync" })
        {
            Assert.False(typeof(IEstateApi).GetMethod(method)!.IsAbstract);
            Assert.NotNull(typeof(CancellationBoundApi).GetMethod(method));
            Assert.NotNull(typeof(VericairnClient).GetMethod(method));
        }
        Assert.True(RequestScope.Covers(typeof(ResourceRequest)));
        Assert.True(RequestScope.Covers("GET /v1/resources"));
        Assert.True(RequestScope.Covers("POST /v1/resources/read"));
    }

    [Fact]
    public async Task RealRoutesSdkAndBoundApiKeepAllRepresentationsAndHashes()
    {
        using var estate = new TestEstate(recordFormat: 2);
        var order = estate.Add("work-order", estate.OperatorId, """{"key":"R-1","kind":"ordinary","title":"Reads","intent":"Read originals.","scope":["reads"],"exitQuestion":"Exact?","inputs":[],"deliverables":["result"]}""");
        byte[] original = [0, 255, 13, 10, 65];
        Directory.CreateDirectory(Path.Combine(estate.Root, "docs"));
        File.WriteAllBytes(Path.Combine(estate.Root, "docs/original.dat"), original);
        estate.Add("document-revision", estate.OperatorId, $$"""{"path":"docs/original.dat","title":"Original","hash":"{{ContentHash.Sha256(original)}}","bytes":{{original.Length}},"mediaType":"application/octet-stream","revision":1,"summary":"Original."}""");
        using EstateService api = EstateService.Open(estate.Root);
        api.CommunicationClock = new ClientClock(estate.Clock);
        using var http = new HttpClient(new WorkNewApiTests.WorkRouteHandler(api)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        ResourcesResponse listing = await bound.ResourcesAsync(TestContext.Current.CancellationToken);
        Assert.Null(listing.Problem);
        Assert.Equal(api.Resources().Resources, listing.Resources);
        Assert.Equal(listing.Resources, sdk.Resources().Resources);
        Assert.Equal(listing.Resources, bound.Resources().Resources);
        foreach (ResourceEntry entry in listing.Resources)
        {
            ResourceResponse expected = api.ResourceRead(entry.Request);
            ResourceResponse read = await bound.ResourceReadAsync(entry.Request, TestContext.Current.CancellationToken);
            Assert.Null(read.Problem);
            Assert.Equal(expected.Content, read.Content);
            Assert.Equal(expected.Hash, read.Hash);
            Assert.Equal(expected.Content, sdk.ResourceRead(entry.Request).Content);
            Assert.Equal(expected.Content, bound.ResourceRead(entry.Request).Content);
        }
        Assert.Equal(CanonicalJson.SerializePrettyUtf8(api.Record(order.Id)!.Envelope), sdk.ResourceRead(new("record", order.Id)).Content);
        Assert.Equal(Encoding.UTF8.GetBytes(api.Context(order.Id).Text), sdk.ResourceRead(new("context", order.Id, Form: "text")).Content);
        using HttpResponseMessage refused = await http.PostAsJsonAsync(new Uri("/v1/resources/read", UriKind.Relative), new { kind = "record", key = order.Id, authority = "operator" }, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task ResourcePostIsAReadWithTheSameDeviceScopeAndNoWriteLedger()
    {
        using var writer = new PromptWriter();
        var device = writer.Api.Devices().Single(d => d.Principal == writer.Builder && d.Name == "Prompt fixture writer");
        var standing = writer.Api.Standing(device.Id)!;
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-resource-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            int writes = 0;
            var scope = new RequestScope(writer.Api, new LocalNetworkLedger(scratch), beforeWrite: () => writes++);
            var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = "/v1/resources/read";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(standing.Identity, writer.Builder, standing.PublicKey, false);
            var request = new ResourceRequest("record", writer.Operator);
            object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ => ValueTask.FromResult<object?>(writer.Api.ResourceRead(request)));
            Assert.Null(Assert.IsType<ResourceResponse>(result).Problem);
            Assert.Equal(0, writes);
            Assert.Empty(Directory.GetFiles(scratch, "*", SearchOption.AllDirectories));
            Assert.True(writer.Api.DeviceRevoke(new(device.Id, writer.Operator, "Withdraw reader.")).Ok);
            result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ => ValueTask.FromResult<object?>(writer.Api.ResourceRead(request)));
            ResourceResponse denied = Assert.IsType<ResourceResponse>(result);
            Assert.Equal("denied", denied.Problem!.Code);
            Assert.Null(denied.Content); Assert.Null(denied.Hash);
            Assert.Equal(0, writes);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
