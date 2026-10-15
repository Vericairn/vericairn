// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Runtime.Versioning;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// macOS custody: keys generated inside the Secure Enclave and kept as enclave-wrapped blobs through the vericairn-enclave helper.
/// Skipped where there is no helper or no enclave (other platforms, and the virtual Macs continuous integration runs on); a key
/// without presence is used, so the test never asks anyone for Touch ID or a password.
/// </summary>
public class SecureEnclaveKeyStoreTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-enclave-" + Ulid.NewUlid().Value);

    [Fact]
    [SupportedOSPlatform("macos")]
    public void SecureEnclaveCustodyCreatesSignsVerifiesAndRemovesABlobUsableOnlyHere()
    {
        if (!OperatingSystem.IsMacOS() || !SecureEnclaveKeyStore.Available())
        {
            Assert.Skip("Secure Enclave custody needs a Mac with an enclave and the vericairn-enclave helper (VERICAIRN_ENCLAVE_HELPER).");
        }

        var store = new SecureEnclaveKeyStore(home);
        Assert.Equal("macos-secure-enclave", store.Kind);
        Assert.False(store.Presence);
        string id = Ulid.NewUlid().Value;
        Assert.False(store.Has(id));
        byte[] publicKey = store.Create(id);
        Assert.True(store.Has(id));
        Assert.Throws<IOException>(() => store.Create(id));
        string encoded = RecordSigning.EncodePublicKey(publicKey);
        Assert.True(RecordSigning.IsPublicKey(encoded));
        Assert.Equal(publicKey, store.PublicKey(id));

        // The signature is Vericairn's own scheme: ECDSA P-256 over the 32 digest bytes, IEEE P1363, verified as any device's is.
        byte[] digest = RecordSigning.Digest("sha256:" + new string('a', 64));
        byte[] signature = store.Sign(id, digest);
        Assert.Equal(RecordSigning.SignatureBytes, signature.Length);
        Assert.True(RecordSigning.Verify(encoded, digest, Base64Url.EncodeToString(signature)));
        string blob = Path.Combine(home, "keys", id + ".se");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(blob));

        store.Remove(id);
        Assert.False(store.Has(id));
    }

    [Fact]
    public void CustodyNeverClaimsPresenceItCannotEnforce()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => DeviceKeyStores.Named("file", presence: true));
        Assert.Contains("cannot enforce", refused.Message, StringComparison.Ordinal);
        Assert.Contains(SecureEnclaveCustody.Kind, DeviceKeyStores.PresenceKinds);
        Assert.DoesNotContain("file-owner-only", DeviceKeyStores.PresenceKinds);
        Assert.Contains(SecureEnclaveCustody.Kind, DeviceKeyStores.Format2Kinds);
        Assert.DoesNotContain(SecureEnclaveCustody.Kind, DeviceKeyStores.Kinds);
    }

    public void Dispose()
    {
        if (Directory.Exists(home))
        {
            Directory.Delete(home, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
