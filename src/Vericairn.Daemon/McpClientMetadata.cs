// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Vericairn.Daemon;

internal sealed record McpClientMetadata(string Id, string Name, string[] Redirects, string? ApplicationType)
{
    private const int MetadataLimit = 16 * 1024;
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = ConnectAsync,
        ConnectTimeout = TimeSpan.FromSeconds(4),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxResponseHeadersLength = 16,
        SslOptions = new()
        {
            CertificateChainPolicy = new X509ChainPolicy
            {
                DisableCertificateDownloads = true,
                RevocationMode = X509RevocationMode.NoCheck,
            },
        },
    }) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = MetadataLimit };

    internal static bool IsDocumentId(string id)
    {
        const string Scheme = "https://";
        if (id.Length > 2048 || !id.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            || id.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')
            || !Uri.TryCreate(id, UriKind.Absolute, out Uri? uri)
            || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)
            || uri.Port <= 0 || uri.AbsolutePath == "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0
            || uri.IsLoopback || IPAddress.TryParse(uri.IdnHost, out IPAddress? address) && !IsPublic(address))
        {
            return false;
        }

        // Uri canonicalizes dot segments; reject them on the original string before fetching a different target.
        int path = id.IndexOf('/', Scheme.Length);
        return path >= 0 && !id[path..].Split('/').Any(segment => Uri.UnescapeDataString(segment) is "." or "..");
    }

    /// <summary>Fetches a bounded document from its HTTPS URL; public addresses are checked at connection time, with no redirects or proxies.</summary>
    internal static async Task<McpClientMetadata?> FetchAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsDocumentId(id)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, id);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using HttpResponseMessage result = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.StatusCode != HttpStatusCode.OK || !string.Equals(result.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)) return null;
            byte[] body = await result.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            return Parse(document.RootElement, id, dynamic: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static McpClientMetadata? Parse(JsonElement document, string id, bool dynamic)
    {
        if (document.ValueKind != JsonValueKind.Object || document.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != document.EnumerateObject().Count())
        {
            return null;
        }

        string? suppliedId = ReadString(document, "client_id");
        string? name = ReadString(document, "client_name");
        string? application = ReadString(document, "application_type");
        if (dynamic && !document.TryGetProperty("application_type", out _)) application = "web";
        if ((!dynamic && suppliedId != id) || (dynamic && application is not ("web" or "native"))
            || string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(c => char.IsControl(c))
            || !SupportsPublicClient(document, dynamic)
            || !document.TryGetProperty("redirect_uris", out JsonElement redirects) || redirects.ValueKind != JsonValueKind.Array
            || redirects.GetArrayLength() is < 1 or > 12)
        {
            return null;
        }

        var validated = new List<string>();
        foreach (JsonElement redirect in redirects.EnumerateArray())
        {
            string? text = redirect.ValueKind == JsonValueKind.String ? redirect.GetString() : null;
            if (text is null || !SafeRedirect(text, application)) return null;
            validated.Add(text);
        }

        return new McpClientMetadata(id, name, [.. validated], application);
    }

    private static bool SupportsPublicClient(JsonElement document, bool dynamic)
    {
        if (document.TryGetProperty("client_secret", out _) || document.TryGetProperty("client_secret_expires_at", out _)
            || document.TryGetProperty("token_endpoint_auth_method", out _) && ReadString(document, "token_endpoint_auth_method") != "none")
        {
            return false;
        }

        if (dynamic || !document.TryGetProperty("token_endpoint_auth_methods_supported", out JsonElement methods)) return true;
        if (methods.ValueKind != JsonValueKind.Array) return false;

        bool supportsNone = false;
        foreach (JsonElement method in methods.EnumerateArray())
        {
            if (method.ValueKind != JsonValueKind.String || method.GetString() is not string value
                || value.StartsWith("client_secret", StringComparison.Ordinal)) return false;
            supportsNone |= value == "none";
        }

        return supportsNone;
    }

    private static string? ReadString(JsonElement document, string name) =>
        document.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool SafeRedirect(string text, string? application)
    {
        if (text.Length > 2048 || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')
            || !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Scheme == "https" && uri.Host.Length > 0 || uri.Scheme == "http" && uri.IsLoopback && application != "web"))
        {
            return false;
        }

        return !QueryHelpers.ParseQuery(uri.Query).Keys.Any(key => key is "code" or "error" or "iss" or "state");
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublic(address)))
        {
            throw new HttpRequestException("A client metadata host must resolve only to publicly reachable addresses.");
        }

        foreach (IPAddress address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("No client metadata address could be reached.");
    }

    internal static bool IsPublic(IPAddress address)
    {
        byte[] b = McpEndpoint.Unscoped(address).GetAddressBytes();
        if (b.Length == 4)
        {
            return b[0] is > 0 and < 224 and not (10 or 127)
                && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 169 && b[1] == 254)
                && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && (b[1] is 0 or 168 || b[1] == 88 && b[2] == 99))
                && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100))
                && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        }

        return (b[0] & 0xe0) == 0x20
            && !(b[0] == 0x20 && b[1] == 0x02) // 6to4
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 2) // Special-purpose prefixes, including Teredo
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)
            && !(b[0] == 0x3f && b[1] == 0xff && b[2] < 0x10);
    }
}
