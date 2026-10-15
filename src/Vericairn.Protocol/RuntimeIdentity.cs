// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Protocol;

/// <summary>
/// The runtime's answer to a client's nonce on a connection, from 0.6.0: its identity key's signature over the estate, the
/// fingerprint of the TLS certificate it presented on that connection and the nonce. A paired device accepts a connection only
/// when the identity it pinned at pairing signed the certificate its own handshake showed.
/// </summary>
/// <param name="Estate">The estate's id.</param>
/// <param name="Identity">The runtime identity's public key, base64url SubjectPublicKeyInfo.</param>
/// <param name="Certificate">The fingerprint of the runtime certificate on this connection, sha256:hex.</param>
/// <param name="Nonce">The client's nonce, echoed.</param>
/// <param name="Signature">ECDSA P-256 signature by the identity key over the proof's digest, IEEE P1363, base64url.</param>
public sealed record RuntimeIdentityProof(string Estate, string Identity, string Certificate, string Nonce, string Signature);

/// <summary>Replace the runtime certificate now; from 0.6.0.</summary>
/// <param name="Compromised">The certificate's key may be known to someone else: every open connection is closed too, so nothing continues under it.</param>
/// <param name="Reason">Why, for the daemon's log.</param>
/// <param name="Identity">The runtime identity's key may be known to someone else: it is replaced as well, and every paired device pairs again; implies Compromised.</param>
public sealed record CertificateRotateRequest(bool Compromised = false, string? Reason = null, bool Identity = false);

/// <summary>A rotation of the runtime certificate.</summary>
/// <param name="Certificate">The new certificate's fingerprint, sha256:hex.</param>
/// <param name="Previous">The replaced certificate's fingerprint.</param>
/// <param name="NotAfter">When the new certificate expires; it is replaced before then.</param>
/// <param name="Compromised">Whether the rotation was for a compromise, which closed the open connections.</param>
/// <param name="Closed">How many open connections were closed.</param>
/// <param name="Identity">The new runtime identity's fingerprint when the identity was replaced too; null otherwise.</param>
public sealed record CertificateRotated(string Certificate, string Previous, string NotAfter, bool Compromised, int Closed, string? Identity = null);

/// <summary>
/// The runtime identity scheme, version 1: a long-lived P-256 key of the runtime, separate from its TLS certificate and never used
/// by TLS, signs SHA-256 over the scheme, the estate, the certificate fingerprint and the client's nonce joined by line feeds.
/// Rotating the certificate keeps the identity, so paired devices keep working; the identity signs only the current certificate,
/// so a replaced one is refused by every client.
/// </summary>
public static class RuntimeIdentityScheme
{
    /// <summary>The scheme name bound into every proof.</summary>
    public const string Scheme = "vericairn-runtime-identity/1";

    /// <summary>The route a client asks for a proof on, with the query parameter nonce.</summary>
    public const string Path = "/v1/runtime/identity";

    /// <summary>The digest the identity key signs.</summary>
    public static byte[] Digest(string estate, string certificate, string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(estate);
        ArgumentException.ThrowIfNullOrEmpty(certificate);
        ArgumentException.ThrowIfNullOrEmpty(nonce);
        return SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Scheme, estate, certificate, nonce)));
    }

    /// <summary>The fingerprint of a public key: sha256:hex of its SubjectPublicKeyInfo bytes.</summary>
    public static string Fingerprint(string publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(publicKey);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Base64Url.DecodeFromChars(publicKey)));
    }

    /// <summary>The fingerprint of a certificate as clients pin and compare it: sha256:hex of its DER bytes.</summary>
    public static string CertificateFingerprint(ReadOnlySpan<byte> certificate) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(certificate));

    /// <summary>
    /// True when the proof answers this nonce for this estate, names the certificate the client's handshake showed, and verifies
    /// with the identity the client pinned.
    /// </summary>
    public static bool Verify(RuntimeIdentityProof? proof, string identity, string estate, string certificate, string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        if (proof is null || proof.Identity != identity || proof.Estate != estate || proof.Certificate != certificate || proof.Nonce != nonce
            || string.IsNullOrEmpty(proof.Signature) || !Base64Url.IsValid(proof.Signature))
        {
            return false;
        }

        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Base64Url.DecodeFromChars(identity), out _);
            byte[] signature = Base64Url.DecodeFromChars(proof.Signature);
            return signature.Length == 64 && key.VerifyHash(Digest(estate, certificate, nonce), signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
