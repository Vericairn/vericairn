// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

/// <summary>
/// The TLS client credential of a paired device on the local network, from 0.6.0: a certificate for a fresh P-256 key, under a
/// subject that names no device, issued, ECDSA with SHA-256, by the device's own key wherever that key is held. The runtime accepts
/// it only while the device's current key verifies it, so the key its pairing bound to the device record is what the client proves.
/// The TLS key never leaves memory and is replaced after a day; the device key signs once per credential.
/// </summary>
public sealed class LocalNetworkCredential : IDisposable
{
    /// <summary>How long a credential is valid.</summary>
    public static TimeSpan Validity { get; } = TimeSpan.FromHours(24);

    /// <summary>The subject and issuer of every credential, which names no device.</summary>
    public const string SubjectName = "CN=vericairn device";

    private static readonly TimeSpan RenewBefore = TimeSpan.FromHours(1);

    private readonly IRequestSigner signer;
    private readonly byte[] publicKey;
    private readonly object gate = new();
    private readonly List<X509Certificate2> issued = [];
    private X509Certificate2? current;

    /// <summary>A credential issued by a device's key, whose public key (SubjectPublicKeyInfo) the device record names.</summary>
    public LocalNetworkCredential(IRequestSigner signer, byte[] publicKey)
    {
        this.signer = signer ?? throw new ArgumentNullException(nameof(signer));
        this.publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
    }

    /// <summary>The certificate to present now, issued afresh when there is none or it expires within the hour.</summary>
    public X509Certificate2 Current()
    {
        lock (gate)
        {
            if (current is null || new DateTimeOffset(current.NotAfter.ToUniversalTime()) - DateTimeOffset.UtcNow < RenewBefore)
            {
                current = Issue(signer, publicKey, DateTimeOffset.UtcNow);
                issued.Add(current);
            }

            return current;
        }
    }

    /// <summary>
    /// Issues a client certificate for a new TLS key, signed by the device's key: the subject and issuer are <see cref="SubjectName"/>,
    /// the validity is a day from a few minutes ago, and its key is usable only for client authentication.
    /// </summary>
    public static X509Certificate2 Issue(IRequestSigner signer, byte[] publicKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(publicKey);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // The same name for every device: a TLS 1.2 handshake shows the client certificate in clear, and the device is named only
        // inside the connection, by the requests it signs. What ties the certificate to the device is its signature by the device's key.
        var name = new X500DistinguishedName(SubjectName);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using X509Certificate2 certificate = request.Create(name, new DeviceKeySignature(signer, publicKey), now.AddMinutes(-5), now.Add(Validity), serial);
        using X509Certificate2 withKey = certificate.CopyWithPrivateKey(key);

        // A round trip through PKCS#12 gives the key the form each platform's TLS needs for a client certificate (SChannel on
        // Windows does not use an ephemeral key); nothing is written to disk.
        byte[] pkcs12 = withKey.Export(X509ContentType.Pkcs12);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.DefaultKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            foreach (X509Certificate2 certificate in issued)
            {
                certificate.Dispose();
            }

            issued.Clear();
            current = null;
        }
    }

    /// <summary>Signs a certificate with the device's key: SHA-256 of the to-be-signed bytes, signed where the key is held, in the DER form X.509 carries.</summary>
    private sealed class DeviceKeySignature(IRequestSigner signer, byte[] publicKey) : X509SignatureGenerator
    {
        public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
        {
            if (hashAlgorithm != HashAlgorithmName.SHA256)
            {
                throw new ArgumentOutOfRangeException(nameof(hashAlgorithm), "A device key signs with SHA-256.");
            }

            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.10045.4.3.2");
            }

            return writer.Encode();
        }

        public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
        {
            ArgumentNullException.ThrowIfNull(data);
            byte[] signature = signer.Sign(SHA256.HashData(data));
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                writer.WriteInteger(new BigInteger(signature.AsSpan(0, 32), isUnsigned: true, isBigEndian: true));
                writer.WriteInteger(new BigInteger(signature.AsSpan(32, 32), isUnsigned: true, isBigEndian: true));
            }

            return writer.Encode();
        }

        protected override PublicKey BuildPublicKey() => PublicKey.CreateFromSubjectPublicKeyInfo(publicKey, out _);
    }
}
