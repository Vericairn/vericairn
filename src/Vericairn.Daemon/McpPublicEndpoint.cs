// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace Vericairn.Daemon;

/// <summary>The published HTTPS authority, its local bind address, and a PEM certificate and key supplied by the owner.</summary>
public sealed record McpPublicEndpoint(string Authority, string CertificatePath, string KeyPath, string? Bind = null)
{
    /// <summary>The canonical OAuth issuer for this endpoint.</summary>
    [JsonIgnore]
    public string Issuer => Address().GetLeftPart(UriPartial.Authority);

    /// <summary>The address the public MCP listener binds.</summary>
    public IPEndPoint BindAddress()
    {
        Uri address = Address();
        if (Bind is not null)
        {
            if (!RuntimeSettings.TryParseAddress(Bind, out IPEndPoint? bound, out string? problem))
                throw new InvalidOperationException(problem);
            if (bound!.Port != address.Port) throw new InvalidOperationException("The public bind port must match the published port.");
            return bound;
        }

        return new IPEndPoint(IPAddress.TryParse(address.IdnHost, out IPAddress? ip) ? ip : IPAddress.Any, address.Port);
    }

    /// <summary>Loads the certificate and private key without importing it into the machine's user key store.</summary>
    public X509Certificate2 LoadCertificate(DateTimeOffset now)
    {
        Uri address = Address();
        using X509Certificate2 pem = X509Certificate2.CreateFromPemFile(CertificatePath, KeyPath);
        if (!pem.MatchesHostname(address.IdnHost, allowWildcards: true, allowCommonName: false)
            || pem.NotBefore.ToUniversalTime() > now.UtcDateTime || pem.NotAfter.ToUniversalTime() <= now.UtcDateTime)
        {
            throw new InvalidOperationException("The public MCP certificate must be current and name the published host in its subject alternative names.");
        }

        // On macOS, TLS requires the certificate's key in a keychain. Use the platform's temporary import for this host only.
        byte[] pfx = pem.Export(X509ContentType.Pkcs12);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, null, OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }

    internal static (string State, string Why)? ExpiryAttention(DateTimeOffset expires, DateTimeOffset now)
    {
        if (expires > now.AddDays(14)) return null;
        string date = Vericairn.Records.Model.RecordParser.FormatTimestamp(expires);
        return expires <= now
            ? ("expired", $"The public MCP certificate expired at {date}. Only the public MCP listener is unavailable.")
            : ("expiring", $"The public MCP certificate expires at {date}.");
    }

    internal X509Certificate2Collection LoadChain(X509Certificate2 leaf)
    {
        var chain = new X509Certificate2Collection();
        try
        {
            chain.ImportFromPemFile(CertificatePath);
            foreach (X509Certificate2 certificate in chain.Cast<X509Certificate2>().Where(c => c.RawData.AsSpan().SequenceEqual(leaf.RawData)).ToArray())
            {
                chain.Remove(certificate);
                certificate.Dispose();
            }

            return chain;
        }
        catch
        {
            foreach (X509Certificate2 certificate in chain) certificate.Dispose();
            throw;
        }
    }

    internal bool Matches(string host, int? port)
    {
        Uri address = Address();
        return (port ?? 443) == address.Port && host.Trim('[', ']').Equals(address.IdnHost, StringComparison.OrdinalIgnoreCase);
    }

    private Uri Address()
    {
        if (!Uri.TryCreate("https://" + Authority, UriKind.Absolute, out Uri? address)
            || address.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)
            || address.PathAndQuery != "/" || address.Fragment.Length != 0 || address.UserInfo.Length != 0
            || address.Port == 0 || Authority.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '?' or '#')
            || IPAddress.TryParse(address.IdnHost, out IPAddress? ip) && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)))
        {
            throw new InvalidOperationException("Give the public MCP endpoint as the certificate's host and port, for example mcp.example.com:443.");
        }

        // Require a written port so a configuration always says which port it intends to expose.
        int colon = Authority.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(Authority.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException("Give the public MCP endpoint's port explicitly.");
        }

        return address;
    }
}
