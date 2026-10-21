// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Something other than the runtime answering at an address on 127.0.0.1: a TLS server presenting the certificate a test gives it
/// (a stranger's, or a stolen runtime certificate with its key) that answers a client's identity request with whatever the test
/// supplies for the nonce, for example a genuine proof relayed from the runtime. It records the request line of every request it
/// receives, so a test can see that a client sent nothing after a proof it refused.
/// </summary>
internal sealed class Impostor : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 certificate;
    private readonly Func<string, string> answer;
    private readonly CancellationTokenSource stop = new();
    private readonly List<Task> handlers = [];
    private readonly List<string> requests = [];
    private readonly Task loop;
    private int disposed;

    public Impostor(X509Certificate2 certificate, Func<string, string> answer)
    {
        this.certificate = certificate;
        this.answer = answer;
        listener.Start();
        loop = Task.Run(AcceptAsync);
    }

    /// <summary>Where it listens.</summary>
    public string Address => $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>The request line of every request received.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (requests)
            {
                return [.. requests];
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await stop.CancelAsync();
        listener.Stop();
        await loop;
        Task[] running;
        lock (handlers)
        {
            running = [.. handlers];
        }

        await Task.WhenAll(running).WaitAsync(TestTiming.HangTimeout);
        stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (handlers)
            {
                handlers.Add(Task.Run(() => ServeAsync(client)));
            }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var tls = new SslStream(client.GetStream());
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TestTiming.HangTimeout);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, ClientCertificateRequired = false, AllowTlsResume = false }, timeout.Token);
                while (await ReadRequestLineAsync(tls, timeout.Token) is string line)
                {
                    lock (requests)
                    {
                        requests.Add(line);
                    }

                    int start = line.IndexOf("nonce=", StringComparison.Ordinal);
                    string nonce = start < 0 ? string.Empty : line[(start + "nonce=".Length)..].Split(' ')[0];
                    byte[] body = Encoding.UTF8.GetBytes(answer(nonce));
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n");
                    await tls.WriteAsync(head, timeout.Token);
                    await tls.WriteAsync(body, timeout.Token);
                    await tls.FlushAsync(timeout.Token);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException or ObjectDisposedException)
            {
                // The client went away, or refused what it was shown.
            }
        }
    }

    /// <summary>Reads one request's head and returns its first line; null when the client closed the connection.</summary>
    private static async Task<string?> ReadRequestLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>();
        byte[] one = new byte[1];
        while (head.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(one, cancellationToken) == 0)
            {
                return null;
            }

            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
            {
                return Encoding.ASCII.GetString([.. head]).Split("\r\n")[0];
            }
        }

        return null;
    }
}
