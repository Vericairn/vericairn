// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Buffers.Text;
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
            Assert.DoesNotContain(Directory.EnumerateFiles(VericairnHome.Path(), "*", SearchOption.AllDirectories), f => f.Contains(id, StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => store.Create(id));
        }
        finally
        {
            store.Remove(id);
        }

        Assert.False(store.Has(id));
    }
}
