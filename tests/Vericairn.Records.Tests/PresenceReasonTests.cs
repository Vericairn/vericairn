// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-124: the reason a presence prompt shows names which signature of how many it is and the record's type, key and purpose, in one
/// bounded sentence that never carries a machine path; the Secure Enclave custody passes it to its helper, whose system dialog shows it.
/// </summary>
public sealed class PresenceReasonTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-presence-reason-" + Ulid.NewUlid().Value);

    [Fact]
    public void AReasonIsOneBoundedSentenceWithoutControlCharactersOrMachinePaths()
    {
        Assert.Equal(PresenceReason.Default, PresenceReason.Text(null));
        Assert.Equal(PresenceReason.Default, PresenceReason.Text(" \n\t "));
        Assert.Equal("sign Vericairn signature 2 of 5: operation branch operator/01M79NZXPA4D3WJDMGCQ1K82CG, records the push",
            PresenceReason.Text("sign Vericairn signature 2 of 5: operation branch operator/01M79NZXPA4D3WJDMGCQ1K82CG, records the push"));
        Assert.Equal("sign it from [path] and [path] and [path] and [path]", PresenceReason.Text("sign it from /home/someone/estate and ~/keys and C:\\keys and share\\key"));
        Assert.Equal("one line only", PresenceReason.Text("one\nline\r\u0007only"));
        string bounded = PresenceReason.Text("sign " + new string('x', 400));
        Assert.Equal(PresenceReason.MaxLength, bounded.Length);
        Assert.EndsWith("…", bounded, StringComparison.Ordinal);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void TheEnclaveCustodyPassesTheBoundedReasonToItsHelperForTheSystemDialog()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave custody and its helper exist on macOS only.");
        Directory.CreateDirectory(home);
        string captured = Path.Combine(home, "reason.txt");
        string helper = Path.Combine(home, "helper.sh");
        // A stand-in for the helper: it keeps the reason its dialog would show and answers with a signature of the right size.
        File.WriteAllText(helper, $"#!/bin/sh\nprintf '%s' \"$4\" > '{captured}'\necho {new string('A', 86)}==\n");
        File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var store = new SecureEnclaveKeyStore(home, helper: helper);
        byte[] digest = RecordSigning.Digest("sha256:" + new string('c', 64));

        Assert.Equal(RecordSigning.SignatureBytes, store.Sign(Ulid.NewUlid().Value, digest, "sign Vericairn signature 1 of 2: acceptance FIX-124, accepts result 01M at its exact hash").Length);
        Assert.Equal("sign Vericairn signature 1 of 2: acceptance FIX-124, accepts result 01M at its exact hash", File.ReadAllText(captured));

        store.Sign(Ulid.NewUlid().Value, digest, "sign from /opt/vericairn/keys " + new string('y', 300));
        string reason = File.ReadAllText(captured);
        Assert.Equal(PresenceReason.MaxLength, reason.Length);
        Assert.StartsWith("sign from [path] yyy", reason, StringComparison.Ordinal);

        store.Sign(Ulid.NewUlid().Value, digest);
        Assert.Equal(PresenceReason.Default, File.ReadAllText(captured));
    }

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }
}
