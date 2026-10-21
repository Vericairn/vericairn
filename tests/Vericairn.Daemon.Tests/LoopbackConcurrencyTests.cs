// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class LoopbackConcurrencyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("invalid")]
    [InlineData("valid")]
    [InlineData("oversized")]
    [InlineData("cancelled")]
    public async Task EightPendingSignaturesBoundBodyReadsAndEveryExitReleasesItsSlot(string exit)
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        using var aborted = new CancellationTokenSource();
        var pipes = new List<Pipe>();
        var pending = new List<Task<Problem?>>();
        try
        {
            for (int i = 0; i < 8; i++)
            {
                var pipe = new Pipe();
                pipes.Add(pipe);
                DefaultHttpContext context = Request(harness, pipe.Reader.AsStream(leaveOpen: true));
                context.RequestAborted = aborted.Token;
                if (i == 0 && exit == "valid")
                {
                    Sign(harness, context, []);
                }

                pending.Add(harness.Host.Authenticator.VerifyAsync(context));
            }

            Assert.All(pending, task => Assert.False(task.IsCompleted));
            var unread = new MemoryStream(new byte[128 * 1024]);
            DefaultHttpContext excess = Request(harness, unread);
            BadHttpRequestException refusal = await Assert.ThrowsAsync<BadHttpRequestException>(() => harness.Host.Authenticator.VerifyAsync(excess));
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, refusal.StatusCode);
            Assert.Contains("concurrent", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(0, unread.Position);

            // The same refusal through the host completes the response and takes only the early-refusal discard budget.
            PipeReader original = excess.Request.BodyReader;
            bool dispatched = false;
            await harness.Host.HandleRequestAsync(excess, _ =>
            {
                dispatched = true;
                return Task.CompletedTask;
            });
            Assert.False(dispatched);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, excess.Response.StatusCode);
            Assert.Equal("close", excess.Response.Headers.Connection);
            long remaining = 0;
            while (true)
            {
                ReadResult read = await original.ReadAsync(TestContext.Current.CancellationToken);
                remaining += read.Buffer.Length;
                original.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }

            Assert.Equal(64 * 1024, remaining);
            await original.CompleteAsync();

            if (exit == "cancelled")
            {
                await aborted.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending[0]);
            }
            else if (exit == "oversized")
            {
                // FlushAsync may wait for the reader, so let completion observe the limit without a writer deadlock.
                pipes[0].Writer.Write(new byte[8 * 1024 * 1024 + 1]);
                await pipes[0].Writer.CompleteAsync();
                BadHttpRequestException oversized = await Assert.ThrowsAsync<BadHttpRequestException>(() => pending[0]);
                Assert.Equal(StatusCodes.Status413PayloadTooLarge, oversized.StatusCode);
            }
            else
            {
                await pipes[0].Writer.CompleteAsync();
                Problem? result = await pending[0];
                if (exit == "valid")
                {
                    Assert.Null(result);
                }
                else
                {
                    Assert.Equal("denied", result?.Code);
                }
            }

            DefaultHttpContext next = Request(harness, new MemoryStream());
            Sign(harness, next, []);
            Assert.Null(await harness.Host.Authenticator.VerifyAsync(next));
        }
        finally
        {
            await aborted.CancelAsync();
            foreach (Pipe pipe in pipes)
            {
                await pipe.Writer.CompleteAsync();
            }

            foreach (Task<Problem?> task in pending)
            {
                try
                {
                    await task;
                }
                catch (Exception exception) when (exception is OperationCanceledException or BadHttpRequestException)
                {
                    // Observe all pending work, including when a deliberately unfixed implementation fails an assertion.
                }
            }

            foreach (Pipe pipe in pipes)
            {
                await pipe.Reader.CompleteAsync();
            }
        }
    }

    [Fact]
    public async Task OrdinaryConcurrentLoopbackClientsCanReadAndWrite()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            using VericairnClient client = VericairnClient.Loopback(harness.Host.Endpoint, DeviceRequestSigner.Find(harness.OperatorDevice));
            Assert.Equal(harness.Estate.Estate.Id, client.Health().Estate);
            byte[] content = [(byte)i, 42];
            (ObjectStored? stored, Problem? problem) = client.ObjectPut(content);
            Assert.Null(problem);
            Assert.NotNull(stored);
            Assert.Equal(content, client.ObjectGet(stored.Hash));
        }, TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentHandshakesSucceedAtStartAndAfterRotation(bool localNetwork)
    {
        const int rounds = 12;
        const int clientsPerRound = 16;
        var failures = new List<string>();
        var serverFailures = new ConcurrentQueue<string>();
        int failedHandshakes = 0;
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(log: message =>
        {
            if (message.StartsWith("TLS handshake failed: ", StringComparison.Ordinal))
            {
                serverFailures.Enqueue(message);
            }
        });
        for (int round = 0; round < rounds; round++)
        {
            if (round == 1)
            {
                // Cover both creation and reload at startup, with no TLS client warming either certificate.
                await harness.RestartAsync();
            }
            else if (round > 1)
            {
                string previous = harness.Host.Endpoint.Loopback!.Certificate;
                Assert.NotEqual(previous, harness.Host.RotateCertificate().Certificate);
            }

            // Share the cold managed extension among the first handshakes without decoding its usages here.
            Assert.Single(harness.Host.Certificates!.Current.Extensions.OfType<X509EnhancedKeyUsageExtension>());

            LoopbackEndpoint endpoint = harness.Host.Endpoint.Loopback!;
            if (localNetwork)
            {
                endpoint = endpoint with { Port = harness.Host.Endpoint.LocalNetwork!.Port };
            }

            using var ready = new CountdownEvent(clientsPerRound);
            var start = TestTiming.Signal();
            Task[] clients = Enumerable.Range(0, clientsPerRound).Select(async _ =>
            {
                await using SslStream tls = await ConnectAsync(endpoint, async () =>
                {
                    if (ready.Signal())
                    {
                        start.SetResult();
                    }

                    await start.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
                });
                Assert.True(tls.IsAuthenticated);
                Assert.Equal(endpoint.Certificate, RuntimeIdentityScheme.CertificateFingerprint(tls.RemoteCertificate!.GetRawCertData()));
            }).ToArray();
            try
            {
                await Task.WhenAll(clients);
            }
            catch (Exception exception)
            {
                failedHandshakes += clients.Count(client => client.IsFaulted || client.IsCanceled);
                failures.Add($"Round {round}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        string measurement = $"{failures.Count}/{rounds} failed rounds; {failedHandshakes}/{rounds * clientsPerRound} failed handshakes.";
        output.WriteLine(measurement);
        foreach (string message in serverFailures.Take(5))
        {
            output.WriteLine(message);
        }

        Assert.True(failures.Count == 0, $"{measurement} {string.Join("; ", failures.Take(5))}");
    }

    [Fact]
    public async Task ConnectionsAreBoundedBeforeTlsAcceptsAnotherClient()
    {
        DaemonTestHome.Ensure();
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        var connections = new List<SslStream>();
        try
        {
            for (int i = 0; i < 32; i++)
            {
                connections.Add(await ConnectAsync(host.Endpoint.Loopback!));
            }

            await Assert.ThrowsAnyAsync<Exception>(async () => await (await ConnectAsync(host.Endpoint.Loopback!)).DisposeAsync());
        }
        finally
        {
            foreach (SslStream connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private static DefaultHttpContext Request(LocalNetworkHarness harness, Stream body)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Request.Method = "POST";
        context.Request.Path = "/v1/objects";
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/v1/objects";
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        context.Request.Headers[RequestSigning.DeviceHeader] = harness.OperatorDevice;
        context.Request.Headers[RequestSigning.SignedAtHeader] = RequestSigning.FormatTime(harness.Time.GetUtcNow());
        context.Request.Headers[RequestSigning.SignatureHeader] = Base64Url.EncodeToString(new byte[64]);
        return context;
    }

    private static void Sign(LocalNetworkHarness harness, HttpContext context, byte[] body)
    {
        string at = context.Request.Headers[RequestSigning.SignedAtHeader].ToString();
        byte[] digest = RequestSigning.Digest(context.Request.Method, context.Request.Path, at, body);
        context.Request.Headers[RequestSigning.SignatureHeader] = Base64Url.EncodeToString(DeviceRequestSigner.Find(harness.OperatorDevice)!.Sign(digest));
    }

    private static async Task<SslStream> ConnectAsync(LoopbackEndpoint endpoint, Func<Task>? beforeClientHello = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestTiming.HangTimeout);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, deadline.Token);
            tls = new SslStream(beforeClientHello is null
                ? new NetworkStream(socket, ownsSocket: true)
                : new ClientHelloStream(socket, beforeClientHello));

            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "vericairn-runtime",
                AllowTlsResume = false,
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && RuntimeIdentityScheme.CertificateFingerprint(certificate.GetRawCertData()) == endpoint.Certificate,
            }, deadline.Token);
            return tls;
        }
        catch
        {
            if (tls is not null)
            {
                await tls.DisposeAsync();
            }
            else
            {
                socket.Dispose();
            }

            throw;
        }
    }

    private sealed class ClientHelloStream(Socket socket, Func<Task> beforeClientHello) : NetworkStream(socket, ownsSocket: true)
    {
        private bool firstWrite = true;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (firstWrite)
            {
                firstWrite = false;
                // Release complete ClientHellos together, after every client has done its native TLS setup.
                await beforeClientHello().WaitAsync(cancellationToken);
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
