// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;

namespace Vericairn.Daemon.Tests;

public sealed class McpMetadataAddressTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("8.8.8.8");
    private static readonly IPAddress OtherPublicAddress = IPAddress.Parse("2606:4700::1");

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("::1")]
    [InlineData("2002:a9fe:a9fe::1")]
    [InlineData("2001:0:a9fe:a9fe::1")]
    public async Task MixedDnsAnswersWithNonPublicAddressesRefuseTheWholeConnection(string reserved)
    {
        var endpoint = new DnsEndPoint("client.example", 443);
        Task<IPAddress[]> Resolve(string name, CancellationToken _)
        {
            Assert.Equal(endpoint.Host, name);
            return Task.FromResult<IPAddress[]>([PublicAddress, IPAddress.Parse(reserved)]);
        }

        HttpRequestException refusal = await Assert.ThrowsAsync<HttpRequestException>(() =>
            McpClientMetadata.ConnectAsync(endpoint, Resolve, (_, _) => throw new InvalidOperationException("No socket should be attempted."),
                TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("must resolve only to publicly reachable addresses", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectUsesTheReviewedAddressesEvenIfDnsChanges()
    {
        int resolutions = 0;
        var endpoints = new List<IPEndPoint>();
        Task<IPAddress[]> Resolve(string _, CancellationToken __) =>
            Task.FromResult<IPAddress[]>(++resolutions == 1 ? [PublicAddress, OtherPublicAddress] : [IPAddress.Loopback]);

        ValueTask<Stream> ConnectTo(IPEndPoint endpoint, CancellationToken _)
        {
            endpoints.Add(endpoint);
            if (endpoints.Count == 1) throw new SocketException((int)SocketError.ConnectionRefused);
            return ValueTask.FromResult(Stream.Null);
        }

        await using Stream result = await McpClientMetadata.ConnectAsync(new DnsEndPoint("client.example", 8443),
            Resolve, ConnectTo, TestContext.Current.CancellationToken);
        Assert.Same(Stream.Null, result);
        Assert.Equal(1, resolutions);
        Assert.Collection(endpoints,
            endpoint => Assert.Equal(new IPEndPoint(PublicAddress, 8443), endpoint),
            endpoint => Assert.Equal(new IPEndPoint(OtherPublicAddress, 8443), endpoint));
    }

    [Theory]
    [InlineData("https://2130706433/client.json")]
    [InlineData("https://0x7f.1/client.json")]
    [InlineData("https://127.1/client.json")]
    [InlineData("https://[::ffff:127.0.0.1]/client.json")]
    [InlineData("https://169.254.169.254/client.json")]
    [InlineData("https://client.example/a/%2E%2E/client.json")]
    [InlineData("https://client.example/client.json?query=1")]
    [InlineData("https://client.example/client.json#fragment")]
    [InlineData("https://user@client.example/client.json")]
    public void MetadataUrlRejectsNonPublicAddressesAndAmbiguousTargets(string url) =>
        Assert.False(McpClientMetadata.IsDocumentId(url));
}
