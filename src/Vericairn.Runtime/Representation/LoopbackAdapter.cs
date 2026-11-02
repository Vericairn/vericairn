// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Representation;

namespace Vericairn.Runtime.Representation;

/// <summary>A run-owned forwarding boundary. Reduction finishes before the only upstream connection is opened.</summary>
internal sealed class LoopbackAdapter : IDisposable
{
    private const int MaxBody = 64 * 1024 * 1024;
    private readonly Uri upstream;
    private readonly Func<byte[], RequestReduction.Result> reduce;
    private readonly Func<Uri, CancellationToken, Task<Stream>>? connect;
    private readonly Func<byte[], long>? measure;
    private TcpListener? listener;
    private readonly CancellationTokenSource stopped = new();
    private readonly object gate = new();
    private readonly HashSet<Task> requests = [];
    private Task? accept;
    private bool failed;
    private long count, fallbacks, before, after;

    internal LoopbackAdapter(Uri upstream, Func<byte[], RequestReduction.Result> reduce, Func<Uri, CancellationToken, Task<Stream>>? connect = null, Func<byte[], long>? measure = null)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(reduce);
        if (!upstream.IsAbsoluteUri || upstream.Scheme != "https" && !(upstream.Scheme == "http" && upstream.Host == "127.0.0.1")
            || upstream.UserInfo.Length != 0 || upstream.Query.Length != 0 || upstream.Fragment.Length != 0
            || !upstream.AbsolutePath.EndsWith('/')) throw new ArgumentException("The harness upstream must be a fixed HTTPS base with no user information, query or fragment.", nameof(upstream));
        this.upstream = upstream;
        this.reduce = reduce;
        this.connect = connect;
        this.measure = measure;
    }

    internal Uri? Address { get; private set; }

    internal bool Start(Action? beforeStart = null)
    {
        try
        {
            beforeStart?.Invoke();
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + upstream.AbsolutePath);
            accept = Accept();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            listener?.Stop();
            failed = true;
            return false;
        }
    }

    internal JsonObject Measurement()
    {
        lock (gate) return new JsonObject
        {
            ["name"] = RepresentationContract.LocalLossless,
            ["state"] = Address is null ? "direct-start-fallback" : failed ? "direct-request-fallback" : "engaged",
            ["address"] = Address is null ? null : Address.GetLeftPart(UriPartial.Authority),
            ["requests"] = count, ["fallbacks"] = fallbacks,
            ["eligibleBeforeBytes"] = before, ["eligibleAfterBytes"] = after,
            ["tokens"] = "unknown",
        };
    }

    private async Task Accept()
    {
        try
        {
            while (!stopped.IsCancellationRequested)
            {
                TcpClient client = await listener!.AcceptTcpClientAsync(stopped.Token).ConfigureAwait(false);
                Task task = Handle(client);
                lock (gate) requests.Add(task);
                _ = task.ContinueWith(t => { lock (gate) requests.Remove(t); }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token))
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            CancellationToken token = deadline.Token;
            await Forward(client.GetStream(), token).ConfigureAwait(false);
        }
    }

    internal async Task Forward(Stream incoming, CancellationToken token)
    {
        bool sent = false;
        bool responseStarted = false;
        try
        {
            string[] headers = (await Headers(incoming, token).ConfigureAwait(false)).Split("\r\n", StringSplitOptions.None);
            string[] request = headers[0].Split(' ');
            if (request.Length != 3 || request[2] != "HTTP/1.1" || request[0] is not ("POST" or "GET") || !Target(request[1]))
            { await Refuse(incoming, 400, token).ConfigureAwait(false); return; }
            var fields = new List<(string Name, string Value, string Raw)>();
            foreach (string line in headers.Skip(1).Where(h => h.Length > 0))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || line[..colon].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
                    || line.Any(c => c < ' ' && c != '\t' || c == 127)) throw new InvalidDataException();
                fields.Add((line[..colon], line[(colon + 1)..].Trim(), line));
            }
            string[] Values(string name) => fields.Where(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(f => f.Value).ToArray();
            string[] lengths = Values("Content-Length"), encodings = Values("Transfer-Encoding");
            if (lengths.Length > 1 || encodings.Length > 1 || lengths.Length > 0 && encodings.Length > 0 || Values("Expect").Length > 0
                || Values("Upgrade").Length > 0 || Values("Trailer").Length > 0) throw new InvalidDataException();
            byte[] body;
            if (encodings.Length == 1)
            {
                if (!encodings[0].Equals("chunked", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                body = await Chunks(incoming, token).ConfigureAwait(false);
            }
            else
            {
                long length = 0;
                if (lengths.Length == 1 && !long.TryParse(lengths[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length)) throw new InvalidDataException();
                if (length is < 0 or > MaxBody) throw new InvalidDataException();
                body = new byte[(int)length];
                await incoming.ReadExactlyAsync(body, token).ConfigureAwait(false);
            }
            RequestReduction.Result result = new(body, 0, 0);
            bool fallback;
            lock (gate) fallback = failed;
            if (Values("Content-Encoding").Length == 0 && Values("Content-Type") is [string type] && type.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    long eligible = measure?.Invoke(body) ?? 0;
                    result = new(body, eligible, eligible);
                    if (!fallback) result = reduce(body);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                { lock (gate) failed = true; fallback = true; }
            }
            if (fallback) result = new(body, result.BeforeBytes, result.BeforeBytes);

            // Nothing below this line retries. Even a failed response after a complete write is an uncertain send.
            using TcpClient? connection = connect is null ? new TcpClient() : null;
            sent = true;
            if (connection is not null) await connection.ConnectAsync(upstream.Host, upstream.Port, token).ConfigureAwait(false);
            await using Stream outgoing = connect is not null ? await connect(upstream, token).ConfigureAwait(false)
                : upstream.Scheme == "https" ? new SslStream(connection!.GetStream(), leaveInnerStreamOpen: false) : connection!.GetStream();
            if (outgoing is SslStream tls) await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = upstream.IdnHost }, token).ConfigureAwait(false);
            var head = new StringBuilder(request[0] + " " + request[1] + " HTTP/1.1\r\nHost: " + upstream.Authority + "\r\nConnection: close\r\n");
            foreach (var field in fields)
            {
                // Authentication lines are copied verbatim and never placed in a diagnostic or retained object.
                if (field.Name.Equals("Host", StringComparison.OrdinalIgnoreCase) || field.Name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || field.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) || field.Name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Equals("X-Original-URL", StringComparison.OrdinalIgnoreCase) || field.Name.Equals("X-Rewrite-URL", StringComparison.OrdinalIgnoreCase)) continue;
                head.Append(field.Raw).Append("\r\n");
            }
            head.Append("Content-Length: ").Append(result.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            await outgoing.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), token).ConfigureAwait(false);
            await outgoing.WriteAsync(result.Body, token).ConfigureAwait(false);
            lock (gate) { count++; if (fallback) fallbacks++; before += result.BeforeBytes; after += result.AfterBytes; }
            string response = await Headers(outgoing, token).ConfigureAwait(false);
            string[] status = response.Split("\r\n", 2, StringSplitOptions.None)[0].Split(' ');
            if (status.Length < 2 || !int.TryParse(status[1], out int code) || code is >= 300 and < 400 || code is < 200 or > 599)
            { await Refuse(incoming, 502, token).ConfigureAwait(false); return; }
            await incoming.WriteAsync(Encoding.Latin1.GetBytes(response), token).ConfigureAwait(false);
            responseStarted = true;
            await outgoing.CopyToAsync(incoming, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Do not include exception text: transports can include headers or request content in it.
            try { if (!responseStarted) await Refuse(incoming, sent ? 502 : 400, token).ConfigureAwait(false); }
            catch (Exception ignored) when (ignored is not OutOfMemoryException and not StackOverflowException) { }
        }
    }

    private bool Target(string target)
    {
        string path = target.Split('?', 2)[0];
        if (!path.StartsWith(upstream.AbsolutePath, StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\') || path.Contains('%') || path.Split('/').Any(p => p is "." or "..")
            || target.Any(c => c <= ' ' || c >= 127 || c == '#')) return false;
        return Uri.TryCreate(upstream, target, out Uri? uri) && uri.Scheme == upstream.Scheme && uri.Host == upstream.Host && uri.Port == upstream.Port
            && uri.AbsolutePath.StartsWith(upstream.AbsolutePath, StringComparison.Ordinal);
    }

    private static async Task<string> Headers(Stream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var one = new byte[1];
        int tail = 0;
        while (bytes.Length < 65536)
        {
            await stream.ReadExactlyAsync(one, token).ConfigureAwait(false);
            bytes.WriteByte(one[0]);
            tail = (tail << 8) | one[0];
            if (tail == 0x0d0a0d0a) return Encoding.Latin1.GetString(bytes.ToArray());
        }
        throw new InvalidDataException();
    }

    private static async Task<byte[]> Chunks(Stream stream, CancellationToken token)
    {
        using var result = new MemoryStream();
        while (true)
        {
            var line = new StringBuilder();
            var one = new byte[1];
            do
            {
                await stream.ReadExactlyAsync(one, token).ConfigureAwait(false);
                line.Append((char)one[0]);
                if (line.Length > 80) throw new InvalidDataException();
            } while (one[0] != '\n');
            string size = line.ToString();
            if (!size.EndsWith("\r\n", StringComparison.Ordinal) || !int.TryParse(size[..^2], System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out int length) || length < 0 || result.Length + length > MaxBody) throw new InvalidDataException();
            var chunk = new byte[length];
            await stream.ReadExactlyAsync(chunk, token).ConfigureAwait(false);
            var end = new byte[2];
            await stream.ReadExactlyAsync(end, token).ConfigureAwait(false);
            if (end[0] != '\r' || end[1] != '\n') throw new InvalidDataException();
            if (length == 0) return result.ToArray();
            result.Write(chunk);
        }
    }

    private static ValueTask Refuse(Stream stream, int code, CancellationToken token) => stream.WriteAsync(
        Encoding.ASCII.GetBytes("HTTP/1.1 " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Refused\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"), token);

    public void Dispose()
    {
        stopped.Cancel();
        listener?.Stop();
        accept?.GetAwaiter().GetResult();
        Task[] pending;
        lock (gate) pending = requests.ToArray();
        Task.WhenAll(pending).GetAwaiter().GetResult();
        stopped.Dispose();
    }
}
