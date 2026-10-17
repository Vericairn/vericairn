// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Sdk;

namespace Vericairn.Daemon;

/// <summary>
/// The runtime's own certificate for its TLS profiles: self-signed, ECDSA P-256, kept with its key in an owner-only PKCS#12 file
/// in the owner-only directory of the estate's live state on this machine, replaced on a schedule before it expires and at once on
/// demand. Loopback clients pin its SHA-256 fingerprint from the endpoint file; paired devices accept it when the runtime identity
/// signs its fingerprint. No trust store is consulted. While a daemon presents it, its key is also where <see cref="KeyStorage"/> says.
/// </summary>
public static class RuntimeCertificate
{
    /// <summary>File name beside the live store.</summary>
    public const string FileName = "runtime-certificate.p12";

    /// <summary>How long a certificate is valid.</summary>
    public static TimeSpan Validity { get; } = TimeSpan.FromDays(180);

    /// <summary>A certificate with less than this left is replaced: at the next start, and by a running daemon on its schedule.</summary>
    public static TimeSpan RenewBefore { get; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How the key is loaded while a daemon presents the certificate, as for a client's credential (<see cref="LocalNetworkCredential.KeyStorage"/>):
    /// in memory on Linux, in a temporary keychain on macOS, in the user's key store on Windows, until the daemon stops.
    /// </summary>
    public static X509KeyStorageFlags KeyStorage => LocalNetworkCredential.KeyStorage;

    /// <summary>Loads the certificate kept in a directory, creating or renewing it when absent, unreadable or close to expiry.</summary>
    public static X509Certificate2 Load(string directory, string subject = "vericairn runtime")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        OwnerOnly(directory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            try
            {
                X509Certificate2 existing = X509CertificateLoader.LoadPkcs12FromFile(path, null, KeyStorage);
                if (existing.HasPrivateKey && !IsDue(existing, DateTimeOffset.UtcNow))
                {
                    return Prepare(existing);
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
        OwnerOnly(directory);
        return Create(Path.Combine(directory, FileName), subject);
    }

    /// <summary>Creates a directory, or keeps the one there, readable and writable by its owner only on Linux and macOS.</summary>
    public static void OwnerOnly(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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
            // Written beside the file, owner-only from the moment it exists (a file left from an interrupted write is removed, never
            // reused with its mode), and moved over it, so the old certificate is gone the moment the new one is there.
            string temporary = path + ".tmp";
            File.Delete(temporary);
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                create.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, create))
            {
                stream.Write(pkcs12);
            }

            File.Move(temporary, path, overwrite: true);
            return Prepare(X509CertificateLoader.LoadPkcs12(pkcs12, null, KeyStorage));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    /// <summary>Decodes the managed extension caches on the loading thread, before any TLS connection can receive this instance.</summary>
    private static X509Certificate2 Prepare(X509Certificate2 certificate)
    {
        try
        {
            // X509Certificate2.Extensions and its typed extensions decode lazily. In particular, concurrent first reads of
            // EnhancedKeyUsages can see an incompletely populated collection and make Kestrel reject Server Authentication.
            // Do this at both PKCS#12 load sites, including reloads and replacements, before RuntimeCertificates publishes it.
            foreach (X509Extension extension in certificate.Extensions)
            {
                switch (extension)
                {
                    case X509EnhancedKeyUsageExtension usage:
                        _ = usage.EnhancedKeyUsages;
                        break;
                    case X509KeyUsageExtension usage:
                        _ = usage.KeyUsages;
                        break;
                    case X509BasicConstraintsExtension constraints:
                        _ = constraints.CertificateAuthority;
                        break;
                    case X509SubjectAlternativeNameExtension names:
                        _ = names.EnumerateDnsNames().ToArray();
                        _ = names.EnumerateIPAddresses().ToArray();
                        break;
                    case X509SubjectKeyIdentifierExtension subject:
                        _ = subject.SubjectKeyIdentifier;
                        break;
                    case X509AuthorityKeyIdentifierExtension authority:
                        _ = authority.KeyIdentifier;
                        break;
                    case X509AuthorityInformationAccessExtension access:
                        _ = access.EnumerateCAIssuersUris().ToArray();
                        _ = access.EnumerateOcspUris().ToArray();
                        break;
                }
            }

            return certificate;
        }
        catch
        {
            certificate.Dispose();
            throw;
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
