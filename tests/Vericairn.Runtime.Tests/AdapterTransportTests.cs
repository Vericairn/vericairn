// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Vericairn.Runtime.Representation;

namespace Vericairn.Runtime.Tests;

public class AdapterTransportTests
{
    [Theory]
    [InlineData("responses")]
    [InlineData("messages")]
    [InlineData("messages-array")]
    public async Task FixedUpstreamReceivesProtectedBytesAndUnchangedAuthenticationExactlyOnce(string shape)
    {
        using var upstream = new CountingAdapterUpstream();
        using var adapter = new LoopbackAdapter(upstream.Address, b => RequestReduction.Reduce(b, AdapterRepresentationTests.Original));
        Assert.True(adapter.Start());
        Assert.Equal("127.0.0.1", adapter.Address!.Host);
        string authentication = "Bearer " + Guid.NewGuid().ToString("N");
        string apiAuthentication = Guid.NewGuid().ToString("N");
        byte[] body = AdapterRepresentationTests.Request(shape);
        using HttpClient client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(adapter.Address, shape == "responses" ? "responses" : "messages")) { Content = Json(body) };
        request.Headers.TryAddWithoutValidation("Authorization", authentication);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiAuthentication);
        request.Headers.Host = "elsewhere.invalid";
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "elsewhere.invalid");
        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CountingAdapterUpstream.Received received = Assert.Single(upstream.Requests);
        Assert.Contains("Authorization: " + authentication + "\r\n", received.Headers, StringComparison.Ordinal);
        Assert.Contains("X-Api-Key: " + apiAuthentication + "\r\n", received.Headers, StringComparison.Ordinal);
        Assert.Contains("Host: " + upstream.Address.Authority + "\r\n", received.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere.invalid", received.Headers, StringComparison.Ordinal);
        Assert.Equal(RequestReduction.Reduce(body, AdapterRepresentationTests.Original).Body, received.Body);
        string measurement = adapter.Measurement().ToJsonString();
        Assert.DoesNotContain(authentication, measurement, StringComparison.Ordinal);
        Assert.DoesNotContain(apiAuthentication, measurement, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", measurement, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, adapter.Measurement()["requests"]!.GetValue<long>());
    }

    [Fact]
    public async Task ReducerFailureFallsBackBeforeOneDispatchAndDisablesFurtherReduction()
    {
        using var upstream = new CountingAdapterUpstream();
        int reductions = 0;
        string authentication = Guid.NewGuid().ToString("N");
        using var adapter = new LoopbackAdapter(upstream.Address, _ => { reductions++; throw new InvalidOperationException(authentication); });
        Assert.True(adapter.Start());
        using HttpClient client = Client();
        byte[] body = AdapterRepresentationTests.Request("responses");
        for (int i = 0; i < 2; i++)
        {
            using HttpResponseMessage response = await client.PostAsync(new Uri(adapter.Address!, "responses"), Json(body), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(2, upstream.Requests.Count);
        Assert.All(upstream.Requests, r => Assert.Equal(body, r.Body));
        Assert.Equal(1, reductions);
        Assert.Equal(2, adapter.Measurement()["fallbacks"]!.GetValue<long>());
        Assert.Equal("direct-request-fallback", adapter.Measurement()["state"]!.GetValue<string>());
        Assert.DoesNotContain(authentication, adapter.Measurement().ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResponseFailureAfterDispatchNeverSendsAgain()
    {
        using var upstream = new CountingAdapterUpstream(closeAfterRequest: true);
        using var adapter = new LoopbackAdapter(upstream.Address, b => new(b, 0, 0));
        Assert.True(adapter.Start());
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.PostAsync(new Uri(adapter.Address!, "responses"), Json("{}"u8.ToArray()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(upstream.Requests);
        Assert.Equal(1, adapter.Measurement()["requests"]!.GetValue<long>());
    }

    [Fact]
    public async Task RedirectsCannotSendAuthenticationToAnotherUpstream()
    {
        using var other = new CountingAdapterUpstream();
        using var upstream = new CountingAdapterUpstream(redirect: other.Address);
        using var adapter = new LoopbackAdapter(upstream.Address, b => new(b, 0, 0));
        Assert.True(adapter.Start());
        using HttpClient client = Client(); // Automatic redirects deliberately enabled at the caller.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Guid.NewGuid().ToString("N"));
        using HttpResponseMessage response = await client.PostAsync(new Uri(adapter.Address!, "responses"), Json("{}"u8.ToArray()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Single(upstream.Requests);
        Assert.Empty(other.Requests);
    }

    [Theory]
    [InlineData("http://elsewhere.invalid/responses")]
    [InlineData("//elsewhere.invalid/responses")]
    [InlineData("/api/../responses")]
    [InlineData("/api/%2e%2e/responses")]
    [InlineData("/outside/responses")]
    public async Task HostileTargetsAreRefusedBeforeAnyDispatch(string target)
    {
        using var upstream = new CountingAdapterUpstream();
        using var adapter = new LoopbackAdapter(upstream.Address, b => new(b, 0, 0));
        Assert.True(adapter.Start());
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, adapter.Address!.Port, TestContext.Current.CancellationToken);
        await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST " + target + " HTTP/1.1\r\nHost: elsewhere.invalid\r\nContent-Length: 0\r\n\r\n"), TestContext.Current.CancellationToken);
        using var response = new StreamReader(socket.GetStream(), Encoding.ASCII);
        Assert.Contains("400", (await response.ReadLineAsync(TestContext.Current.CancellationToken))!, StringComparison.Ordinal);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task UnclassifiedRequestsAreForwardedVerbatimAndTeardownClosesThePort()
    {
        using var upstream = new CountingAdapterUpstream();
        var adapter = new LoopbackAdapter(upstream.Address, b => RequestReduction.Reduce(b, AdapterRepresentationTests.Original));
        Assert.True(adapter.Start());
        Uri address = adapter.Address!;
        byte[] body = "{ \"unknown\" : \"exact  bytes\\u0061\" }"u8.ToArray();
        using HttpClient client = Client();
        using HttpResponseMessage response = await client.PostAsync(new Uri(address, "responses"), Json(body), TestContext.Current.CancellationToken);
        Assert.Equal(body, Assert.Single(upstream.Requests).Body);
        adapter.Dispose();
        using var socket = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => socket.ConnectAsync(IPAddress.Loopback, address.Port, TestContext.Current.CancellationToken).AsTask());
    }

    internal static HttpClient Client() => new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(15) };
    internal static ByteArrayContent Json(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }
}

internal sealed class CountingAdapterUpstream : IDisposable
{
    internal sealed record Received(string Headers, byte[] Body);
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly Task serving;
    internal ConcurrentQueue<Received> Requests { get; } = new();
    internal Uri Address { get; }

    internal CountingAdapterUpstream(bool closeAfterRequest = false, Uri? redirect = null)
    {
        TestPrerequisites.RequireLoopback();
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/api/");
        serving = Serve(closeAfterRequest, redirect);
    }

    private async Task Serve(bool closeAfterRequest, Uri? redirect)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(stop.Token);
                NetworkStream stream = client.GetStream();
                var header = new StringBuilder();
                var one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    await stream.ReadExactlyAsync(one, stop.Token);
                    header.Append((char)one[0]);
                }
                string text = header.ToString();
                string? length = text.Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                byte[] body = new byte[length is null ? 0 : int.Parse(length[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture)];
                await stream.ReadExactlyAsync(body, stop.Token);
                Requests.Enqueue(new(text, body));
                if (!closeAfterRequest)
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(redirect is null
                        ? "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"
                        : "HTTP/1.1 307 Temporary Redirect\r\nLocation: " + redirect.AbsoluteUri + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop.Token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException) { }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        serving.GetAwaiter().GetResult();
        stop.Dispose();
    }
}
