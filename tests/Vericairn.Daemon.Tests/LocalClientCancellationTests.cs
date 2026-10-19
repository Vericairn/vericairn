// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using Vericairn.Protocol;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Sdk;
using Vericairn.TestSupport;

namespace Vericairn.Daemon.Tests;

public sealed class LocalClientCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SilentLoopbackPeerPreservesDeadlineAndCallerCancellation(bool pairing, bool cancel)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string home = Path.Combine(Path.GetTempPath(), "vericairn-client-test-" + Ulid.NewUlid().Value);
        try
        {
            var keys = new FileKeyStore(home);
            string device = Ulid.NewUlid().Value;
            string publicKey = RecordSigning.EncodePublicKey(keys.Create(device));
            string address = $"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            var connection = new RuntimeConnection(Ulid.NewUlid().Value, address, Ulid.NewUlid().Value, null,
                publicKey, device, publicKey, Ulid.NewUlid().Value, null, ["local-network"], "2026-10-18T00:00:00Z");
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            // Caller cancellation is triggered by acceptance, independently of elapsed startup time.
            TimeSpan timeout = cancel ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(100);
            Task operation = pairing
                ? RuntimePairing.PairAsync(address, PairingProtocol.NewCode(), new PairingOptions("Test device") { KeyStore = keys, Timeout = timeout }, stop.Token)
                : new RuntimeConnections(home, keys).ProbeAsync(connection, timeout, cancellationToken: stop.Token);
            try
            {
                using TcpClient? accepted = await SilentPeer.AcceptUntilCompletedAsync(
                    token => listener.AcceptTcpClientAsync(token).AsTask(), operation, TestContext.Current.CancellationToken);
                if (cancel)
                {
                    Assert.NotNull(accepted);
                    stop.Cancel();
                }
                Exception failure = cancel
                    ? await Assert.ThrowsAsync<ClientCancelledException>(() => operation)
                    : await Assert.ThrowsAsync<ClientTimeoutException>(() => operation);
                // Neither connection setup, a silent handshake nor a read-only probe has sent a pairing confirmation or another effect.
                Assert.False(((IClientOperationFailure)failure).OutcomeUnknown);
                Assert.Equal(cancel ? "cancelled" : "timed-out", ClientFailure.FromException(failure)?.Code);
            }
            finally
            {
                stop.Cancel();
                _ = operation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
