// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;

namespace Vericairn.Protocol;

/// <summary>
/// The runtime's answer to a client's nonce on a connection, from 0.6.0: its identity key's signature over the estate, the
/// fingerprint of the TLS certificate it presented on that connection, the fingerprint of the client certificate that connection
/// presented, and the nonce. A paired device accepts a connection only when the identity it pinned at pairing signed the
/// certificate its own handshake showed, for its own client certificate.
/// </summary>
/// <param name="Estate">The estate's id.</param>
/// <param name="Identity">The runtime identity's public key, base64url SubjectPublicKeyInfo.</param>
/// <param name="Certificate">The fingerprint of the runtime certificate on this connection, sha256:hex.</param>
/// <param name="Client">The fingerprint of the client certificate on this connection, sha256:hex: the proof answers that connection only.</param>
/// <param name="Nonce">The client's nonce, echoed.</param>
/// <param name="Signature">ECDSA P-256 signature by the identity key over the proof's digest, IEEE P1363, base64url.</param>
public sealed record RuntimeIdentityProof(string Estate, string Identity, string Certificate, string Client, string Nonce, string Signature);

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
/// by TLS, signs SHA-256 over the scheme, the estate, the runtime certificate's fingerprint, the asking connection's client
/// certificate's fingerprint and the client's nonce joined by line feeds. Rotating the certificate keeps the identity, so paired
/// devices keep working; the identity signs only the current certificate, so a replaced one is refused by every client; and a proof
/// one connection asks for verifies for no other client.
/// </summary>
public static class RuntimeIdentityScheme
{
    /// <summary>The scheme name bound into every proof.</summary>
    public const string Scheme = "vericairn-runtime-identity/1";

    /// <summary>The route a client asks for a proof on, with the query parameter nonce.</summary>
    public const string Path = "/v1/runtime/identity";

    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    private const string NamedP256Oid = "1.2.840.10045.3.1.7";

    /// <summary>The digest the identity key signs.</summary>
    public static byte[] Digest(string estate, string certificate, string client, string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(estate);
        ArgumentException.ThrowIfNullOrEmpty(certificate);
        ArgumentException.ThrowIfNullOrEmpty(client);
        ArgumentException.ThrowIfNullOrEmpty(nonce);
        return SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Scheme, estate, certificate, client, nonce)));
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
    /// True when the proof answers this nonce for this estate, names the certificate the client's handshake showed and the client
    /// certificate it presented, and verifies with the identity the client pinned. The identity is read only in the form of every key
    /// Vericairn makes (<see cref="IsNamedP256"/>): one in any other form verifies nothing and is not handed to the platform. Any error
    /// importing the identity or verifying the signature is a refusal, false like any other, except the fatal ones (out of memory,
    /// stack overflow, access violation).
    /// </summary>
    public static bool Verify(RuntimeIdentityProof? proof, string identity, string estate, string certificate, string client, string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        if (proof is null || proof.Identity != identity || proof.Estate != estate || proof.Certificate != certificate || proof.Client != client || proof.Nonce != nonce
            || string.IsNullOrEmpty(proof.Signature) || !Base64Url.IsValid(proof.Signature))
        {
            return false;
        }

        try
        {
            byte[] subjectPublicKeyInfo = Base64Url.DecodeFromChars(identity);
            if (!IsNamedP256(subjectPublicKeyInfo))
            {
                return false;
            }

            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            byte[] signature = Base64Url.DecodeFromChars(proof.Signature);
            return signature.Length == 64 && key.VerifyHash(Digest(estate, certificate, client, nonce), signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the bytes begin with the only form of SubjectPublicKeyInfo a runtime identity takes, read as DER: the algorithm
    /// id-ecPublicKey with the named curve P-256 as its only parameter, and an uncompressed point of 65 bytes. Every key Vericairn makes
    /// has this form. Read before any import, so an identity in another form (explicit curve parameters, another named curve, a
    /// compressed point, another algorithm) is refused the same way on every platform, without asking the platform or the framework's
    /// own reader, whose answers for such keys differ: some platforms take the P-256 key written with its parameters in full, others do
    /// not. Bytes after the structure, and the bit string's count of unused bits, are not read. This assembly references no other
    /// Vericairn assembly, so this is its own copy of the records' check of a key's form, written to the same rules; a test holds the
    /// two to the same answers.
    /// </summary>
    private static bool IsNamedP256(byte[] subjectPublicKeyInfo)
    {
        try
        {
            AsnReader info = new AsnReader(subjectPublicKeyInfo, AsnEncodingRules.DER).ReadSequence();
            AsnReader algorithm = info.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcPublicKeyOid || algorithm.ReadObjectIdentifier() != NamedP256Oid)
            {
                return false;
            }

            algorithm.ThrowIfNotEmpty();
            byte[] point = info.ReadBitString(out _);
            info.ThrowIfNotEmpty();
            return point.Length == 65 && point[0] == 0x04;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return false;
        }
    }
}
