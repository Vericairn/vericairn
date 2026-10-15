// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Daemon;

/// <summary>
/// The runtime's own certificate for its TLS profiles: self-signed, ECDSA P-256, kept owner-only beside the estate's live state on
/// this machine, renewed before it expires. Loopback clients pin its SHA-256 fingerprint from the endpoint file; paired devices
/// accept it when the runtime identity signs its fingerprint. No trust store is consulted.
/// </summary>
public static class RuntimeCertificate
{
    /// <summary>File name beside the live store.</summary>
    public const string FileName = "runtime-certificate.p12";

    /// <summary>How long a certificate is valid.</summary>
    public static TimeSpan Validity { get; } = TimeSpan.FromDays(180);

    /// <summary>A certificate with less than this left is replaced at the next start.</summary>
    public static TimeSpan RenewBefore { get; } = TimeSpan.FromDays(30);

    /// <summary>Loads the certificate kept in a directory, creating or renewing it when absent, unreadable or close to expiry.</summary>
    public static X509Certificate2 Load(string directory, string subject = "vericairn runtime")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            try
            {
                X509Certificate2 existing = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.DefaultKeySet);
                if (existing.HasPrivateKey && existing.NotAfter.ToUniversalTime() - DateTime.UtcNow > RenewBefore)
                {
                    return existing;
                }

                existing.Dispose();
            }
            catch (CryptographicException)
            {
                // Unreadable: replaced below.
            }

            File.Delete(path);
        }

        return Create(path, subject);
    }

    /// <summary>The fingerprint clients pin: sha256:hex of the certificate bytes.</summary>
    public static string Fingerprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return "sha256:" + Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256));
    }

    private static X509Certificate2 Create(string path, string subject)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={subject}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 created = request.CreateSelfSigned(now.AddMinutes(-5), now.Add(Validity));
        byte[] pkcs12 = created.Export(X509ContentType.Pkcs12);
        try
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(pkcs12);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.DefaultKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }
}

/// <summary>The certificate a running daemon presents to its TLS connections.</summary>
internal sealed class RuntimeCertificates : IDisposable
{
    private readonly X509Certificate2 current;

    public RuntimeCertificates(string directory)
    {
        current = RuntimeCertificate.Load(directory);
    }

    /// <summary>The certificate presented to new connections.</summary>
    public X509Certificate2 Current => current;

    /// <summary>Its fingerprint.</summary>
    public string Fingerprint => RuntimeCertificate.Fingerprint(Current);

    public void Dispose() => current.Dispose();
}
