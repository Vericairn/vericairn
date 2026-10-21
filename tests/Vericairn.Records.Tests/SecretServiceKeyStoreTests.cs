// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Runtime.Versioning;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// Linux custody in the secret service through secret-tool. Skipped where none runs; continuous integration starts GNOME Keyring for
/// the Linux tests and sets VERICAIRN_EXPECT_SECRET_SERVICE, so there the test must run rather than skip.
/// </summary>
public class SecretServiceKeyStoreTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-secret-" + Ulid.NewUlid().Value);

    [Fact]
    [SupportedOSPlatform("linux")]
    public void SecretServiceCustodyCreatesSignsPromotesAndRemovesWithoutAKeyFile()
    {
        if (!OperatingSystem.IsLinux() || !SecretServiceKeyStore.Available())
        {
            Assert.False(Environment.GetEnvironmentVariable("VERICAIRN_EXPECT_SECRET_SERVICE") == "1", "Missing prerequisite: Linux secret-tool and an unlocked Secret Service session (such as GNOME Keyring), required by VERICAIRN_EXPECT_SECRET_SERVICE.");
            Assert.Skip("Secret-service custody needs Linux with a running secret service and secret-tool.");
        }

        var store = new SecretServiceKeyStore(home);
        Assert.Equal(SecretServiceCustody.Kind, store.Kind);
        string device = Ulid.NewUlid().Value;
        string revision = Ulid.NewUlid().Value;
        try
        {
            byte[] publicKey = store.Create(device);
            Assert.True(store.Has(device));
            Assert.Throws<InvalidOperationException>(() => store.Create(device));
            Assert.Equal(publicKey, store.PublicKey(device));
            byte[] digest = RecordSigning.Digest("sha256:" + new string('c', 64));
            Assert.True(RecordSigning.Verify(RecordSigning.EncodePublicKey(publicKey), digest, Base64Url.EncodeToString(store.Sign(device, digest))));

            // On disk there is only an empty, owner-only marker; the key itself is in the keyring.
            string marker = Path.Combine(home, "keys", device + ".secret");
            Assert.Equal(0, new FileInfo(marker).Length);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(marker));
            Assert.All(Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories), f => Assert.Equal(0, new FileInfo(f).Length));

            // A rotation's key takes the device's place, and the key it replaced leaves the keyring.
            byte[] next = store.Create(revision);
            store.Promote(revision, device);
            Assert.False(store.Has(revision));
            Assert.Equal(next, store.PublicKey(device));
            Assert.Throws<InvalidOperationException>(() => store.Promote(revision, device));
        }
        finally
        {
            store.Remove(device);
            store.Remove(revision);
        }

        Assert.False(store.Has(device));
        new SecretServiceKeyStore(Path.Combine(home, "never")).Remove(Ulid.NewUlid().Value);
    }

    [Fact]
    public void APersonsDeviceNeedsPresenceByDefaultWhereItsCustodyEnforcesIt()
    {
        Assert.True(DeviceKeyStores.PresenceByDefault(person: true, SecureEnclaveCustody.Kind));
        Assert.False(DeviceKeyStores.PresenceByDefault(person: false, SecureEnclaveCustody.Kind));
        Assert.False(DeviceKeyStores.PresenceByDefault(person: true, "file-owner-only"));
        Assert.False(DeviceKeyStores.PresenceByDefault(person: true, SecretServiceCustody.Kind));
        Assert.False(DeviceKeyStores.PresenceByDefault(person: true, "windows-cng"));

        // The default custody is one this platform has, and secret-service custody is named on Linux only.
        string strongest = DeviceKeyStores.Default().Kind;
        Assert.Contains(strongest, DeviceKeyStores.Format2Kinds);
        if (!OperatingSystem.IsLinux())
        {
            Assert.Throws<PlatformNotSupportedException>(() => DeviceKeyStores.Named("secret-service"));
        }
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
