// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Daemon;

/// <summary>
/// The runtime's own certificate for its TLS profiles: self-signed, ECDSA P-256, kept owner-only beside the estate's live state on
/// this machine, replaced on a schedule before it expires and at once on demand. Loopback clients pin its SHA-256 fingerprint from
/// the endpoint file; paired devices accept it when the runtime identity signs its fingerprint. No trust store is consulted.
/// </summary>
public static class RuntimeCertificate
{
    /// <summary>File name beside the live store.</summary>
    public const string FileName = "runtime-certificate.p12";

    /// <summary>How long a certificate is valid.</summary>
    public static TimeSpan Validity { get; } = TimeSpan.FromDays(180);

    /// <summary>A certificate with less than this left is replaced: at the next start, and by a running daemon on its schedule.</summary>
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
                if (existing.HasPrivateKey && !IsDue(existing, DateTimeOffset.UtcNow))
                {
                    return existing;
                }

                existing.Dispose();
            }
            catch (CryptographicException)
            {
                // Unreadable: replaced below.
            }
        }

        return Create(path, subject);
    }

    /// <summary>Replaces the certificate kept in a directory with a new one now; the file is swapped whole, so a reader sees one or the other.</summary>
    public static X509Certificate2 Replace(string directory, string subject = "vericairn runtime")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        return Create(Path.Combine(directory, FileName), subject);
    }

    /// <summary>Whether a certificate is due for replacement at a moment: it expires within <see cref="RenewBefore"/>.</summary>
    public static bool IsDue(X509Certificate2 certificate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new DateTimeOffset(certificate.NotAfter.ToUniversalTime()) - now <= RenewBefore;
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
            // Written beside the file and moved over it, so the old certificate is gone the moment the new one is there.
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(pkcs12);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, path, overwrite: true);
            return X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.DefaultKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }
}

/// <summary>The certificate a running daemon presents, swapped whole on rotation; replaced certificates are kept until the daemon stops, since connections that began with one may still use it.</summary>
internal sealed class RuntimeCertificates : IDisposable
{
    private readonly string directory;
    private readonly object gate = new();
    private readonly List<X509Certificate2> replaced = [];
    private X509Certificate2 current;

    public RuntimeCertificates(string directory)
    {
        this.directory = directory;
        current = RuntimeCertificate.Load(directory);
    }

    /// <summary>The certificate presented to new connections.</summary>
    public X509Certificate2 Current => Volatile.Read(ref current);

    /// <summary>Its fingerprint.</summary>
    public string Fingerprint => RuntimeCertificate.Fingerprint(Current);

    /// <summary>Replaces the certificate; returns the replaced one's fingerprint and the new certificate.</summary>
    public (string Previous, X509Certificate2 Next) Rotate()
    {
        lock (gate)
        {
            X509Certificate2 next = RuntimeCertificate.Replace(directory);
            X509Certificate2 previous = current;
            Volatile.Write(ref current, next);
            replaced.Add(previous);
            return (RuntimeCertificate.Fingerprint(previous), next);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            current.Dispose();
            foreach (X509Certificate2 certificate in replaced)
            {
                certificate.Dispose();
            }

            replaced.Clear();
        }
    }
}
