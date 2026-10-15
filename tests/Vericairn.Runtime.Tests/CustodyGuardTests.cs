// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The custody precondition as a guard on launch (the estate's design/goals.md § 10): the worker launches nothing while a device of
/// an operator, or of the goal's sponsor, has its key held on this machine in custody that signs without its owner's presence.
/// </summary>
public class CustodyGuardTests
{
    [Fact]
    public void TheCustodyGuardNamesEveryDeviceOfAnOperatorOrTheSponsorThatSignsHereWithoutPresence()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope sponsor = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Sponsor","roles":["contributor"]}""");
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, $$"""{"title":"Widget","objective":"Ship it.","completion":[{"evidence":"It ships.","check":"Read it."}],"constraints":[],"kind":"finite","sponsor":"{{sponsor.Id}}","budget":{"wallMinutes":60,"attemptsPerStep":3},"plan":{"policy":"proposed-by-planner","approval":"sponsor-approves"},"inputs":[]}""");
        string presence = Device(estate, estate.OperatorId, "Operator's enclave key", "macos-secure-enclave", "required");
        string file = Device(estate, estate.OperatorId, "Operator's old laptop", "file-owner-only", "none");
        string sponsors = Device(estate, sponsor.Id, "Sponsor's laptop", "linux-secret-service", "none");
        string agents = Device(estate, estate.BuilderId, "Builder's device", "file-owner-only", "none");
        string elsewhere = Device(estate, estate.OperatorId, "Operator's desktop", "windows-cng", "none");
        EstateModel model = estate.Load();
        IReadOnlyList<(string Principal, string Role)> guarded = CustodyGuard.Guarded(model, goal, estate.BuilderId);
        Assert.Equal([estate.OperatorId, sponsor.Id], guarded.Select(g => g.Principal));

        // An enclave key with presence signs nothing unattended; a file key, a secret-service key or a CNG key here does.
        IReadOnlyList<IDeviceKeyStore> stores = [new HeldStore("macos-secure-enclave", presence), new HeldStore("file-owner-only", file, agents), new HeldStore("linux-secret-service", sponsors)];
        IReadOnlyList<string> refusals = CustodyGuard.Refusals(model, guarded, stores, "someone");
        Assert.Equal(2, refusals.Count);
        Assert.Contains(refusals, r => r.StartsWith($"Device {file} (Operator's old laptop, not admitted) of Test Operator ({estate.OperatorId}), an operator: this machine holds its key in file-owner-only custody without its owner's presence", StringComparison.Ordinal));
        Assert.Contains(refusals, r => r.StartsWith($"Device {sponsors} (Sponsor's laptop, not admitted) of Sponsor ({sponsor.Id}), the sponsor of the goal the run is charged to", StringComparison.Ordinal) && r.Contains("the operating-system user someone", StringComparison.Ordinal));
        Assert.DoesNotContain(refusals, r => r.Contains(presence, StringComparison.Ordinal) || r.Contains(agents, StringComparison.Ordinal) || r.Contains(elsewhere, StringComparison.Ordinal));

        // A record that claims presence counts for nothing when the key is held where presence cannot be enforced.
        IReadOnlyList<string> copied = CustodyGuard.Refusals(model, guarded, [new HeldStore("file-owner-only", presence)], "someone");
        Assert.Contains("its record requires its owner's presence, but this machine holds its key in file-owner-only custody, which cannot enforce it", Assert.Single(copied), StringComparison.Ordinal);
    }

    private static string Device(TestEstate estate, string principal, string name, string custody, string presence) =>
        estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"{{name}}","platform":"macos","custody":"{{custody}}","algorithm":"ecdsa-p256-sha256","publicKey":"not-a-key","profiles":["embedded"],"presence":"{{presence}}","purpose":"signing"}""").Id;

    /// <summary>A key store that holds the keys of the devices it names, of one custody; it signs nothing.</summary>
    private sealed class HeldStore(string kind, params string[] held) : IDeviceKeyStore
    {
        public string Kind => kind;

        public bool Has(string deviceId) => held.Contains(deviceId, StringComparer.Ordinal);

        public byte[] Create(string deviceId) => throw new NotSupportedException();

        public byte[]? PublicKey(string deviceId) => null;

        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest) => throw new NotSupportedException();

        public void Remove(string deviceId) => throw new NotSupportedException();

        public void Promote(string keyId, string deviceId) => throw new NotSupportedException();
    }
}
