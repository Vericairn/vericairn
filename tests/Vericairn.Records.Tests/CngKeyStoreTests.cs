// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Runtime.Versioning;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>Windows custody: keys live in the software key storage provider, non-exportable, with no key file to copy. Skipped elsewhere.</summary>
public class CngKeyStoreTests
{
    [Fact]
    public void CngCustodyCreatesSignsVerifiesAndRemovesWithoutAKeyFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("CNG custody exists on Windows only; the file store covers other platforms.");
        }

        IDeviceKeyStore store = DeviceKeyStores.Named("cng");
        Assert.Equal("windows-cng", store.Kind);
        Assert.Equal("windows-cng", DeviceKeyStores.Default().Kind);
        string id = Ulid.NewUlid().Value;
        Assert.False(store.Has(id));
        byte[] publicKey = store.Create(id);
        try
        {
            Assert.True(store.Has(id));
            Assert.True(RecordSigning.IsPublicKey(RecordSigning.EncodePublicKey(publicKey)));
            Assert.Equal(RecordSigning.EncodePublicKey(publicKey), RecordSigning.EncodePublicKey(store.PublicKey(id)!));
            byte[] digest = RecordSigning.Digest("sha256:" + new string('3', 64));
            byte[] signature = store.Sign(id, digest);
            Assert.Equal(RecordSigning.SignatureBytes, signature.Length);
            Assert.True(RecordSigning.Verify(RecordSigning.EncodePublicKey(publicKey), digest, Base64Url.EncodeToString(signature)));
            if (Directory.Exists(VericairnHome.Path()))
            {
                Assert.DoesNotContain(Directory.EnumerateFiles(VericairnHome.Path(), "*", SearchOption.AllDirectories), f => f.Contains(id, StringComparison.Ordinal));
            }
            Assert.Throws<InvalidOperationException>(() => store.Create(id));
        }
        finally
        {
            store.Remove(id);
        }

        Assert.False(store.Has(id));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ARotatedCngKeyTakesTheDevicesNameAndTheReplacedOneIsDeleted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("CNG custody exists on Windows only; the file store covers other platforms.");
        }

        string home = Path.Combine(Path.GetTempPath(), "vericairn-cng-" + Ulid.NewUlid().Value);
        var store = new CngKeyStore(home);
        string device = Ulid.NewUlid().Value;
        string revision = Ulid.NewUlid().Value;
        store.Create(device);
        byte[] next = store.Create(revision);
        try
        {
            // The new key keeps its CNG name; the device's alias names it, and the key it replaced is deleted.
            store.Promote(revision, device);
            Assert.Equal(next, store.PublicKey(device));
            byte[] digest = RecordSigning.Digest("sha256:" + new string('4', 64));
            Assert.True(RecordSigning.Verify(RecordSigning.EncodePublicKey(next), digest, Base64Url.EncodeToString(store.Sign(device, digest))));
            Assert.True(File.Exists(Path.Combine(home, "keys", device + ".cng")));
            Assert.Throws<InvalidOperationException>(() => store.Promote(Ulid.NewUlid().Value, device));
        }
        finally
        {
            store.Remove(device);
            Directory.Delete(home, recursive: true);
        }

        Assert.False(store.Has(device));
        Assert.False(store.Has(revision));
    }
}
