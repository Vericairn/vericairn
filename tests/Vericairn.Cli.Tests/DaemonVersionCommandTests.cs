// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class DaemonVersionCommandTests
{
    private static readonly string[] UpgradeRequests = ["/v1/health", "/v1/estate/upgrade"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UpgradeWarnsBeforeCheckOrApplyAndNeverStopsTheDaemon(bool check, bool json)
    {
        CliHarness.Ok("estate", "upgrade", "--help");
        using var estate = new TestEstate();
        var requests = new List<string>();
        using var client = Client(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/v1/health") return Json(Health());
            Assert.Equal("/v1/estate/upgrade", request.RequestUri.AbsolutePath);
            UpgradeRequest upgrade = request.Content!.ReadFromJsonAsync<UpgradeRequest>(ProtocolJson.Options).GetAwaiter().GetResult()!;
            Assert.Equal(check, upgrade.Check);
            Assert.Equal("0.8.2", upgrade.Version);
            return Json(new UpgradeResponse("release 0.8.0", "release 0.8.2", 3, 3, [], "release", new Dictionary<string, string>(), !check, null));
        });
        string[] arguments = ["--estate", estate.Root, "estate", "upgrade", "--to", "0.8.2", .. check ? new[] { "--check" } : new[] { "--by", estate.OperatorId }, .. json ? new[] { "--json" } : []];
        CliHarness.Outcome result = CliHarness.RunWithApi(client, null, arguments);
        Assert.Equal(0, result.Code);
        AssertWarning(result.Err);
        Assert.DoesNotContain("warning:", result.Out, StringComparison.Ordinal);
        if (json) Assert.Equal(!check, result.Json.GetProperty("applied").GetBoolean());
        else Assert.Contains(check ? "would upgrade" : "upgraded", result.Out, StringComparison.Ordinal);
        Assert.Equal(UpgradeRequests, requests);
        Assert.Equal("0.8.0", client.Health().Vericairn);
    }

    [Fact]
    public void UnknownOperationNamesTheServingReleaseInsteadOfAnUnreadableDiagnostic()
    {
        CliHarness.Ok("estate", "upgrade", "--help");
        using var estate = new TestEstate();
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health" ? Json(Health()) : new(HttpStatusCode.NotFound));
        CliHarness.Outcome result = CliHarness.RunWithApi(client, null, "--estate", estate.Root, "estate", "upgrade", "--to", "0.8.2", "--check");
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Out);
        AssertWarning(result.Err);
        Assert.Contains("does not provide POST /v1/estate/upgrade", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic could not be read", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingReleaseKeepsJsonAndErrorStreamsUnchanged()
    {
        CliHarness.Ok("estate", "upgrade", "--help");
        using var estate = new TestEstate();
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health"
            ? Json(Health() with { Vericairn = ClientRelease + "+different-build" })
            : Json(new UpgradeResponse("release 0.8.0", "release 0.8.2", 3, 3, [], "release", new Dictionary<string, string>(), false, null)));
        CliHarness.Outcome result = CliHarness.RunWithApi(client, null, "--estate", estate.Root, "--json", "estate", "upgrade", "--to", "0.8.2", "--check");
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Err);
        Assert.Equal("release 0.8.2", result.Json.GetProperty("toPin").GetString());
    }

    [Fact]
    public void ASuccessfulReadStillWarnsWithoutChangingItsJson()
    {
        CliHarness.Ok("objects", "list", "--help");
        using var estate = new TestEstate();
        using var client = Client(request => request.RequestUri!.AbsolutePath == "/v1/health" ? Json(Health()) : Json(Array.Empty<ObjectRow>()));
        CliHarness.Outcome result = CliHarness.RunWithApi(client, null, "--estate", estate.Root, "--json", "objects", "list");
        Assert.Equal(0, result.Code);
        Assert.Equal(0, result.Json.GetArrayLength());
        AssertWarning(result.Err);
    }

    private static string ClientRelease => typeof(VericairnClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    private static HealthResponse Health() => new(ApiVersion.Current, "0.8.0", "estate", "root", "repository-synced", 3, "now", 1);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: ProtocolJson.Options) };
    private static VericairnClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new HttpClient(new Handler(response)) { BaseAddress = new("http://fixture.invalid") });

    private static void AssertWarning(string text)
    {
        Assert.Contains("warning:", text, StringComparison.Ordinal);
        Assert.Contains("Vericairn " + ClientRelease, text, StringComparison.Ordinal);
        Assert.Contains("Vericairn 0.8.0", text, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime stop", text, StringComparison.Ordinal);
        Assert.Contains("vericairn runtime start", text, StringComparison.Ordinal);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage result = response(request);
            result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }
}
