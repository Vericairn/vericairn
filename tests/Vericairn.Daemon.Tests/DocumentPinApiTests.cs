// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DocumentPinApiTests
{
    public DocumentPinApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void ContractDescribesExactBytesExpectedPinAndReceipt()
    {
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = contract.RootElement.GetProperty("paths").GetProperty("/v1/documents/pin").GetProperty("post");
        Assert.Contains("exact estate document bytes", operation.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DocumentPinRequest", operation.GetProperty("requestBody").GetRawText(), StringComparison.Ordinal);
        foreach (string code in new[] { "200", "422" })
            Assert.Contains("DocumentPinResponse", operation.GetProperty("responses").GetProperty(code).GetRawText(), StringComparison.Ordinal);
        JsonElement schema = contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DocumentPinRequest");
        Assert.Equal(["path", "by", "summary"], schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        Assert.True(schema.GetProperty("properties").TryGetProperty("expected", out _));
        Assert.True(RequestScope.Covers("POST /v1/documents/pin"));
        Assert.True(RequestScope.Covers(typeof(DocumentPinRequest)));
    }

    [Fact(Timeout = 30000)]
    public async Task ApiAndSynchronousAsyncAndBoundSdkPinTheRuntimeBytesAndReturnRefusals()
    {
        using var estate = new TestEstate();
        using EstateService service = EstateService.Open(estate.Root);
        using var http = new HttpClient(new PinRouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        const string path = "docs/guide.md";
        estate.WriteFile(path, "# API\r\n");
        var request = new DocumentPinRequest(path, estate.OperatorId, "Initial pin.", "Guide");
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/documents/pin", UriKind.Relative), request, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DocumentPinResponse first = (await response.Content.ReadFromJsonAsync<DocumentPinResponse>(ProtocolJson.Options, TestContext.Current.CancellationToken))!;
        AssertPin(first, 1, null, "# API\r\n");
        DocumentPinResponse unchanged = sdk.DocumentPin(request with { Expected = first.Written!.Id });
        Assert.False(unchanged.Ok);
        Assert.Null(unchanged.Written);
        Assert.Null(unchanged.Revision);
        Assert.Contains("unchanged bytes", unchanged.Problem!.Message, StringComparison.Ordinal);
        Assert.Single(service.Session.Load().OfType("document-revision"));

        estate.WriteFile(path, "# SDK\n");
        DocumentPinResponse second = sdk.DocumentPin(request with { Summary = "Synchronous SDK.", Expected = first.Written.Id });
        AssertPin(second, 2, first.Written.Id, "# SDK\n");
        estate.WriteFile(path, "# Async\n");
        DocumentPinResponse third = await sdk.DocumentPinAsync(request with { Summary = "Asynchronous SDK.", Expected = second.Written!.Id }, TestContext.Current.CancellationToken);
        AssertPin(third, 3, second.Written.Id, "# Async\n");
        estate.WriteFile(path, "# Bound\n");
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        DocumentPinResponse fourth = bound.DocumentPin(request with { Summary = "Bound SDK.", Expected = third.Written!.Id });
        AssertPin(fourth, 4, third.Written.Id, "# Bound\n");
        foreach (DocumentPinResponse stale in new[]
        {
            sdk.DocumentPin(request with { Expected = first.Written.Id }),
            await sdk.DocumentPinAsync(request with { Expected = first.Written.Id }, TestContext.Current.CancellationToken),
            bound.DocumentPin(request with { Expected = first.Written.Id }),
        })
        {
            Assert.False(stale.Ok);
            Assert.Null(stale.Written);
            Assert.Contains(fourth.Written!.Id, stale.Problem!.Message, StringComparison.Ordinal);
        }
        using HttpResponseMessage staleResponse = await http.PostAsJsonAsync(new Uri("/v1/documents/pin", UriKind.Relative), request with { Expected = first.Written.Id }, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, staleResponse.StatusCode);
        Assert.Equal(4, service.Session.Load().OfType("document-revision").Count());
        Assert.Equal(0, service.Check().Errors);

        void AssertPin(DocumentPinResponse result, long revision, string? supersedes, string content)
        {
            Assert.True(result.Ok, result.Problem?.Message);
            Assert.Equal(revision, result.Revision);
            JsonElement envelope = service.Record(result.Written!.Id)!.Envelope;
            Assert.Equal(supersedes, envelope.TryGetProperty("supersedes", out JsonElement previous) ? previous.GetString() : null);
            JsonElement body = envelope.GetProperty("body");
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            Assert.Equal(ContentHash.Sha256(bytes), body.GetProperty("hash").GetString());
            Assert.Equal(bytes.Length, body.GetProperty("bytes").GetInt64());
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(estate.Root, path)));
            Assert.Equal("Guide", body.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task GeneratedDocumentsAndForkedPinsAreRefusedThroughTheSdkWithNoNewRecords()
    {
        using (var generated = new GeneratedWritingFixture())
        using (EstateService service = EstateService.Open(generated.Estate.Root))
        using (var sdk = new VericairnClient(new HttpClient(new PinRouteHandler(service)) { BaseAddress = new Uri("http://fixture.invalid") }))
        {
            var before = Records(generated.Estate);
            foreach (string path in generated.GeneratedPaths)
            {
                DocumentPinResponse refused = await sdk.DocumentPinAsync(new(path, generated.Estate.OperatorId, "Must refuse generated output."), TestContext.Current.CancellationToken);
                Assert.False(refused.Ok);
                Assert.Null(refused.Written);
                Assert.Contains("generated document", refused.Problem!.Message, StringComparison.Ordinal);
            }
            Assert.Equal(before, Records(generated.Estate));
        }

        using var estate = new TestEstate();
        estate.WriteFile("docs/guide.md", "# First\n");
        using EstateService forkService = EstateService.Open(estate.Root);
        using var client = new VericairnClient(new HttpClient(new PinRouteHandler(forkService)) { BaseAddress = new Uri("http://fixture.invalid") });
        var request = new DocumentPinRequest("docs/guide.md", estate.OperatorId, "First pin.");
        DocumentPinResponse first = client.DocumentPin(request);
        Assert.True(first.Ok, first.Problem?.Message);
        RecordEnvelope initial = forkService.Session.Load().ById[first.Written!.Id];
        estate.SetClock(initial.AtTime);
        estate.WriteFile("docs/guide.md", "# Branches\n");
        string body = JsonSerializer.Serialize(new
        {
            path = "docs/guide.md", title = "Guide", hash = ContentHash.Sha256File(Path.Combine(estate.Root, "docs", "guide.md")),
            bytes = 11, mediaType = "text/markdown", revision = 2, summary = "A branch pin.",
        });
        RecordEnvelope a = estate.Add("document-revision", estate.OperatorId, body, supersedes: initial.Id);
        RecordEnvelope b = estate.Add("document-revision", estate.OperatorId, body, supersedes: initial.Id);
        var original = Records(estate);
        foreach (DocumentPinResponse refused in new[]
        {
            client.DocumentPin(request with { Expected = a.Id }),
            await client.DocumentPinAsync(request with { Expected = b.Id }, TestContext.Current.CancellationToken),
        })
        {
            Assert.False(refused.Ok);
            Assert.Null(refused.Written);
            Assert.Contains("forked pins", refused.Problem!.Message, StringComparison.Ordinal);
            foreach (string id in new[] { initial.Id, a.Id, b.Id }) Assert.Contains(id, refused.Problem.Message, StringComparison.Ordinal);
        }
        Assert.Equal(original, Records(estate));
    }

    private static Dictionary<string, string> Records(TestEstate estate) => Directory.EnumerateFiles(Path.Combine(estate.Root, "records"), "*.json", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(estate.Root, p), ContentHash.Sha256File, StringComparer.Ordinal);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAuthenticatedDeviceCannotPinAsAnotherPrincipal(bool localNetwork)
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-pin-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var scope = new RequestScope(service, new LocalNetworkLedger(scratch));
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/v1/documents/pin";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", estate.BuilderId, "public-key", localNetwork);
            var request = new DocumentPinRequest("docs/guide.md", estate.OperatorId, "Another principal.");
            bool reached = false;
            object? response = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, request), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(response).StatusCode);
            Assert.Empty(service.Session.Load().OfType("document-revision"));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    // Exercise the real API binding and response delegates without a host, file watchers or sockets.
    private sealed class PinRouteHandler : HttpMessageHandler
    {
        private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        private readonly RouteEndpoint[] endpoints;

        public PinRouteHandler(IEstateApi api)
        {
            var routes = new RouteBuilder(services);
            ApiRoutes.Map(routes, api, () => throw new InvalidOperationException("This fixture has no host to shut down."));
            endpoints = routes.DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = request.RequestUri!.AbsolutePath;
            byte[] bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = request.Content?.Headers.ContentType?.ToString();
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyFeature(request.Content is not null));
            using var output = new MemoryStream();
            context.Response.Body = output;
            var matched = endpoints.Select(e => (Endpoint: e, Values: new RouteValueDictionary()))
                .Where(pair => pair.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(request.Method.Method, StringComparer.Ordinal)
                    && new TemplateMatcher(TemplateParser.Parse(pair.Endpoint.RoutePattern.RawText!), new()).TryMatch(context.Request.Path, pair.Values));
            var route = Assert.Single(matched);
            context.Request.RouteValues = route.Values;
            context.SetEndpoint(route.Endpoint);
            await route.Endpoint.RequestDelegate!(context).ConfigureAwait(false);
            return new((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) services.Dispose();
            base.Dispose(disposing);
        }

        private sealed class RouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider => services;
            public ICollection<EndpointDataSource> DataSources { get; } = [];
            public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
        }

        private sealed class BodyFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
        {
            public bool CanHaveBody => canHaveBody;
        }
    }
}
