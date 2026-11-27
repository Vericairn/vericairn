// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class DeviceRevocationReachApiTests
{
    private const string Route = "/v1/devices/{id}/revocation-reach";

    public DeviceRevocationReachApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task TheServingRuntimeReportsARevocationItAlreadyHeldAtItsJournalBaseline()
    {
        using var estate = new TestEstate(3);
        string operatorDevice;
        string contributorDevice;
        string suspected;
        RecordWritten revocation;
        using (EstateService writer = EstateService.Open(estate.Root))
        {
            operatorDevice = Admit(writer, estate, estate.OperatorId, "Administrator");
            contributorDevice = Admit(writer, estate, estate.BuilderId, "Contributor");
            suspected = Admit(writer, estate, estate.BuilderId, "Suspected device");
            WriteOutcome written = writer.DeviceRevoke(new(suspected, estate.OperatorId, "Suspected compromise."));
            Assert.True(written.Ok, written.Problem?.Message);
            revocation = written.Written!;
        }

        using EstateService server = EstateService.Open(estate.Root);
        string state = Path.Combine(estate.Root, "fixture-state");
        Directory.CreateDirectory(state);
        server.EnableEvents(state);
        server.DeriveEvents();
        string? epoch = server.RevocationJournalIdentity;
        Assert.NotNull(epoch);
        int records = estate.Load().Records.Count;
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var routes = new MemoryRoutes(services);
        RouteGroupBuilder group = routes.MapGroup("");
        group.AddEndpointFilter(new RequestScope(server, new LocalNetworkLedger(state)));
        ApiRoutes.Map(group, server, () => Assert.Fail("A reach read cannot shut down the runtime."));

        using var local = Client(null);
        using var administrator = Client(operatorDevice);
        using var contributor = Client(contributorDevice);
        AssertReport(local.DeviceRevocationReach(suspected));
        AssertReport(await administrator.DeviceRevocationReachAsync(suspected, TestContext.Current.CancellationToken));
        AssertReport(new CancellationBoundApi(administrator, TestContext.Current.CancellationToken).DeviceRevocationReach(suspected));
        Assert.Empty(administrator.DeviceRevocationReach(operatorDevice)!.Revocations);
        Assert.Null(await administrator.DeviceRevocationReachAsync(estate.BuilderId, TestContext.Current.CancellationToken));
        ClientRefusedException denied = await Assert.ThrowsAsync<ClientRefusedException>(
            () => contributor.DeviceRevocationReachAsync(suspected, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains("administer", denied.Message, StringComparison.Ordinal);
        Assert.False(denied.OutcomeUnknown);
        Assert.Empty(LocalNetworkLedger.Read(state));
        Assert.Equal(records, estate.Load().Records.Count);

        using JsonDocument discovery = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement responses = discovery.RootElement.GetProperty("paths").GetProperty(Route).GetProperty("get").GetProperty("responses");
        Assert.Equal("#/components/schemas/DeviceRevocationReachReport", responses.GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.True(responses.TryGetProperty("401", out _));
        Assert.True(responses.TryGetProperty("404", out _));

        VericairnClient Client(string? reader)
        {
            DeviceStanding? standing = reader is null ? null : server.Standing(reader);
            AuthenticatedDevice? authenticated = standing is null ? null : new(standing.Identity, standing.Principal, standing.PublicKey, LocalNetwork: true);
            return new VericairnClient(new HttpClient(new RouteTransport(routes, services, authenticated))
                { BaseAddress = new Uri("http://fixture.invalid") });
        }

        void AssertReport(DeviceRevocationReachReport? report)
        {
            Assert.NotNull(report);
            Assert.Equal(suspected, report.Device);
            Assert.Null(report.JournalProblem);
            DeviceRevocationReach row = Assert.Single(report.Revocations);
            Assert.Equal((revocation.Id, revocation.Hash), (row.Id, row.Hash));
            Assert.True(row.Counts);
            Assert.True(row.Current);
            RevocationObserver observation = Assert.Single(row.Observations, o => o.Kind == "server");
            Assert.Equal((epoch, "server-read"), (observation.Identity, observation.Evidence));
        }
    }

    private static string Admit(EstateService service, TestEstate estate, string principal, string name)
    {
        WriteOutcome created = service.DeviceCreate(new(principal, name, "file", ["local-network"]));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        WriteOutcome admitted = service.DeviceAdmit(new(device, estate.OperatorId, "Fixture admission."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        return device;
    }

    private sealed class MemoryRoutes(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(services);
    }

    // Invoke the actual route and its device scope over the SDK without binding a socket.
    private sealed class RouteTransport(MemoryRoutes routes, IServiceProvider services, AuthenticatedDevice? authenticated) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            string[] segments = request.RequestUri!.AbsolutePath.Split('/');
            Assert.Equal(["", "v1", "devices", segments[3], "revocation-reach"], segments);
            RouteEndpoint endpoint = Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
                e => e.RoutePattern.RawText == Route && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
            var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellationToken };
            context.Request.Method = request.Method.Method;
            context.Request.Path = request.RequestUri.AbsolutePath;
            context.Request.RouteValues["id"] = Uri.UnescapeDataString(segments[3]);
            context.SetEndpoint(endpoint);
            if (authenticated is not null) context.Items[RequestAuthenticator.DeviceItem] = authenticated;
            using var output = new MemoryStream();
            context.Response.Body = output;
            await endpoint.RequestDelegate!(context);
            return new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        }
    }
}
