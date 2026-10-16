// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

/// <summary>
/// One HTTP/1.1 exchange written and read exactly on a stream the SDK set up itself, for the steps that must happen on a connection
/// before, or instead of, the HTTP client's own requests: the runtime identity's proof, and the pairing exchange.
/// </summary>
internal static class RawHttp
{
    private const int MaxHeader = 16 * 1024;
    private const int MaxBody = 64 * 1024;

    /// <summary>Sends a JSON body by POST, UTF-8 without a byte order mark, and reads the answer.</summary>
    public static async Task<(int Status, byte[] Body)> PostJsonAsync<T>(Stream stream, string host, string target, T value, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, ProtocolJson.Options);
        string head = $"POST {target} HTTP/1.1\r\nHost: {host}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The problem a refusal carries, or null when its body is none.</summary>
    public static Problem? ProblemOf(byte[] body)
    {
        try
        {
            return body.Length == 0 ? null : JsonSerializer.Deserialize<Problem>(body, ProtocolJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one HTTP/1.1 response exactly, so the connection is left clean for the requests that follow.</summary>
    public static async Task<(int Status, byte[] Body)> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        string head = await ReadHeadAsync(stream, cancellationToken).ConfigureAwait(false);
        string[] lines = head.Split("\r\n");
        string[] statusLine = lines[0].Split(' ', 3);
        if (statusLine.Length < 2 || !statusLine[0].StartsWith("HTTP/1.", StringComparison.Ordinal) || !int.TryParse(statusLine[1], NumberStyles.None, CultureInfo.InvariantCulture, out int status))
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is not HTTP/1.1.");
        }

        long? length = null;
        bool chunked = false;
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
            {
                length = parsed;
            }
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                chunked = true;
            }
        }

        if (chunked)
        {
            using var body = new MemoryStream();
            while (true)
            {
                string size = (await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false)).Split(';')[0].Trim();
                if (!int.TryParse(size, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int chunk) || chunk < 0 || body.Length + chunk > MaxBody)
                {
                    throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is malformed.");
                }

                if (chunk == 0)
                {
                    while ((await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false)).Length > 0)
                    {
                        // Trailers are ignored.
                    }

                    return (status, body.ToArray());
                }

                body.Write(await ReadExactlyAsync(stream, chunk, cancellationToken).ConfigureAwait(false));
                await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        if (length is null)
        {
            return (status, []);
        }

        if (length > MaxBody)
        {
            throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is too long for an identity proof.");
        }

        return (status, await ReadExactlyAsync(stream, (int)length.Value, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new List<byte>(512);
        byte[] one = new byte[1];
        while (head.Count < MaxHeader)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime closed the connection before answering.");
            }

            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
            {
                return Encoding.ASCII.GetString(head.ToArray(), 0, n - 4);
            }
        }

        throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer has no end of its headers.");
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new List<byte>(16);
        byte[] one = new byte[1];
        while (line.Count < MaxHeader)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime closed the connection while answering.");
            }

            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
            }

            line.Add(one[0]);
        }

        throw new RuntimeConnectionException(ConnectionStates.Unavailable, "The runtime's answer is malformed.");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }
}
