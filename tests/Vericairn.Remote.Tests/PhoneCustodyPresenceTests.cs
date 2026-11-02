// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Remote;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

/// <summary>
/// Custody stays honest about presence: the iOS simulator's owner-only file cannot require it, so a simulator build holds no presence
/// key and signs no decision, and no store that cannot enforce presence is accepted as a presence store.
/// </summary>
public sealed class PhoneCustodyPresenceTests
{
    [Fact]
    public async Task TheSimulatorsFileCustodyClaimsNoPresenceAndSignsNoDecision()
    {
        Assert.DoesNotContain("file-owner-only", DeviceKeyStores.PresenceKinds);
        Assert.Contains(DeviceKeyStores.IosSecureEnclaveKind, DeviceKeyStores.PresenceKinds);
        Assert.Contains(DeviceKeyStores.AndroidKeystoreKind, DeviceKeyStores.PresenceKinds);
        DirectoryInfo home = Directory.CreateTempSubdirectory("vericairn-simulator-custody-");
        try
        {
            var file = new FileKeyStore(home.FullName);
            Assert.Throws<ArgumentException>(() => new LocalNetworkConnections(home.FullName, file, file));

            // What the iOS head builds on the simulator: the request key in a file, and no presence store.
            var simulator = new LocalNetworkConnections(home.FullName, file);
            var connection = new RuntimeConnection("01M5JZ0000000000000000TEST", "192.168.1.20:7443", "estate", "Test Estate", "identity", "device", "key", "principal", null, ["local-network"], "2026-11-01T12:00:00Z")
            {
                PresenceDevice = "presence", PresenceDevicePublicKey = "presence-key",
            };
            Assert.False(simulator.HasPresenceKey(connection));
            var request = new NewRecordRequest("acceptance", "principal", JsonSerializer.SerializeToElement(new { statement = "Accept." }));
            WriteOutcome outcome = await simulator.WriteSignedAsync(connection, request, _ => throw new InvalidOperationException("Nothing is prepared."), _ => throw new InvalidOperationException("Nothing is signed."),
                TestContext.Current.CancellationToken);
            Assert.Equal("presence-required", outcome.Problem?.Code);
            Assert.Null(outcome.Written);
            PairingException refused = await Assert.ThrowsAsync<PairingException>(() => simulator.AddPresenceKeyAsync(connection, "ABCD-EFGH-JKMN-PQRS", "Simulator", TestContext.Current.CancellationToken));
            Assert.Contains("cannot require your presence", refused.Message, StringComparison.Ordinal);

            // The SDK never signs as a presence device with a custody that cannot enforce presence; it asks the runtime nothing.
            IEstateApi untouched = DispatchProxy.Create<IEstateApi, Untouched>();
            WriteOutcome sdk = await DeviceSignedRecords.WriteWithPresenceAsync(untouched, request, file, "device", cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("refused", sdk.Problem?.Code);
            Assert.Contains("cannot enforce", sdk.Problem!.Message, StringComparison.Ordinal);
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy generates a subclass at runtime.")]
    public class Untouched : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("The runtime was asked " + targetMethod?.Name + ".");
    }
}
