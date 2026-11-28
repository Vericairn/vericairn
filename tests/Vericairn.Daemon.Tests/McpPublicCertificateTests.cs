// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class McpPublicCertificateTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnavailableCertificateLeavesTheLocalRuntimeRunning(bool expired)
    {
        TestPrerequisites.RequireLocalIpc();
        using var estate = new TestEstate(3);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        McpPublicEndpoint endpoint = Endpoint(estate, 8443);
        if (expired) WriteCertificate(endpoint, now.AddDays(-2), now.AddDays(-1));
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false,
            options: new DaemonOptions { Mcp = true, PublicMcp = endpoint }, cancellationToken: Stop);
        using var local = new VericairnClient(host.Endpoint);

        Assert.Null(host.Endpoint.PublicMcp);
        Assert.NotNull(await local.HealthAsync(Stop));
        Assert.Equal("unavailable", Assert.Single(local.AttentionGet(estate.OperatorId).Items,
            item => item.SubjectId == "public-mcp-certificate").State);
    }

    [Fact]
    public async Task ARunningCertificateRaisesAttentionThenOnlyItsListenerExpires()
    {
        TestPrerequisites.RequireLoopback();
        TestPrerequisites.RequireTlsCertificates();
        using var estate = new TestEstate(3);
        int port;
        using (var reserve = new TcpListener(IPAddress.Loopback, 0))
        {
            reserve.Start();
            port = ((IPEndPoint)reserve.LocalEndpoint).Port;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var clock = new MovableClock(now);
        McpPublicEndpoint endpoint = Endpoint(estate, port);
        string fingerprint = WriteCertificate(endpoint, now.AddDays(-1), now.AddDays(15));
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false,
            options: new DaemonOptions { Mcp = true, PublicMcp = endpoint, Time = clock }, cancellationToken: Stop);
        using var local = new VericairnClient(host.Endpoint);
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && RuntimeCertificate.Fingerprint(certificate) == fingerprint,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(endpoint.Issuer) };

        Assert.DoesNotContain(local.AttentionGet(estate.OperatorId).Items, item => item.SubjectId == "public-mcp-certificate");
        clock.Now = now.AddDays(2);
        Assert.Equal("expiring", Assert.Single(local.AttentionGet(estate.OperatorId).Items,
            item => item.SubjectId == "public-mcp-certificate").State);
        using (HttpResponseMessage answer = await http.GetAsync("/.well-known/oauth-authorization-server", Stop))
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        clock.Now = now.AddDays(16);
        using (HttpResponseMessage answer = await http.GetAsync("/.well-known/oauth-authorization-server", Stop))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        using var anotherConnection = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && RuntimeCertificate.Fingerprint(certificate) == fingerprint,
        }) { BaseAddress = new Uri(endpoint.Issuer) };
        await Assert.ThrowsAsync<HttpRequestException>(() => anotherConnection.GetAsync("/.well-known/oauth-authorization-server", Stop));

        Assert.NotNull(await local.HealthAsync(Stop));
        Assert.Equal("expired", Assert.Single(local.AttentionGet(estate.OperatorId).Items,
            item => item.SubjectId == "public-mcp-certificate").State);
    }

    [Fact]
    public void AttentionFollowsTheCertificateExpiry()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.Null(McpPublicEndpoint.ExpiryAttention(now.AddDays(15), now));
        Assert.Equal("expiring", McpPublicEndpoint.ExpiryAttention(now.AddDays(14), now)!.Value.State);
        Assert.Equal("expired", McpPublicEndpoint.ExpiryAttention(now, now)!.Value.State);
    }

    private static McpPublicEndpoint Endpoint(TestEstate estate, int port) =>
        new("localhost:" + port, Path.Combine(estate.Root, "public.pem"), Path.Combine(estate.Root, "key.pem"), "127.0.0.1:" + port);

    private static string WriteCertificate(McpPublicEndpoint endpoint, DateTimeOffset start, DateTimeOffset end)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 certificate = request.CreateSelfSigned(start, end);
        File.WriteAllText(endpoint.CertificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(endpoint.KeyPath, key.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(endpoint.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return RuntimeCertificate.Fingerprint(certificate);
    }
}
