// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>
/// From 0.9.5 (FIX-164): a command's unhandled exception is shown and never leaves <see cref="AsyncCommand.Execute"/>, the async void
/// handler a button calls, where it would end the app; on Android it closed Remote when Pair was tapped. A pairing the SDK refuses
/// before anything is sent says why.
/// </summary>
public sealed class RemoteCommandFailureTests(HeadlessFixture fixture) : IDisposable
{
    private readonly TestEstate estate = RemoteFixtures.Seeded();

    [Fact]
    public async Task AnUnexpectedFailureWhilePairingIsShownAndRemoteStaysOpen()
    {
        // What .NET's Android TLS threw for a cipher-suite list, before its release of the stream ended the process.
        using var connections = new FakeConnections(estate.Root) { PairFailure = new NotImplementedException("SafeDeleteSslContext") };
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections);
            model.Address = "192.168.1.20:7443";
            model.Code = "ABCD-EFGH";
            IReadOnlyList<Exception> escaped = RecordingContext.Run(() => model.PairCommand.Execute(null));

            Assert.Empty(escaped);
            Assert.True(model.Pairing);
            Assert.False(model.Busy);
            Assert.Empty(model.Estates);
            Assert.Equal("Not paired: the pairing stopped on an unexpected error, NotImplementedException: SafeDeleteSslContext.", model.PairingMessage);
            Assert.True(model.PairCommand.CanExecute(null));
            await Task.CompletedTask;
            return true;
        });
    }

    [Fact]
    public async Task ATlsSetupTheSdkRefusesIsShownAsRefused()
    {
        using var connections = new FakeConnections(estate.Root)
        {
            PairFailure = new ClientRefusedException("This device's TLS could not set up a connection to the runtime: the platform refused an option."),
        };
        await fixture.UiAsync(async () =>
        {
            var model = new RemoteViewModel(connections);
            model.Address = "192.168.1.20:7443";
            model.Code = "ABCD-EFGH";
            Assert.Empty(RecordingContext.Run(() => model.PairCommand.Execute(null)));
            Assert.Equal("Not paired (refused): This device's TLS could not set up a connection to the runtime: the platform refused an option.", model.PairingMessage);
            await Task.CompletedTask;
            return true;
        });
    }

    [Fact]
    public async Task AMistypedCodeIsRefusedWithAMessageBeforeAnythingIsSent()
    {
        DirectoryInfo phone = Directory.CreateTempSubdirectory("vericairn-test-phone-");
        try
        {
            var connections = new LocalNetworkConnections(phone.FullName, new FileKeyStore(phone.FullName));
            await fixture.UiAsync(async () =>
            {
                var model = new RemoteViewModel(connections);
                // A documentation address: the code is refused before any connection is tried.
                model.Address = "192.0.2.1:7443";
                model.Code = "ABCD-EFGH";
                await model.PairCommand.ExecuteAsync();

                Assert.True(model.Pairing);
                Assert.Equal($"Not paired (refused): That is not a pairing code: a code is {PairingProtocol.CodeLength} characters, shown in four groups of four.", model.PairingMessage);
                return true;
            });
        }
        finally
        {
            phone.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ACommandHandsWhatItDidNotHandleToItsPresenterAndExecuteAsyncStillThrows()
    {
        var shown = new List<Exception>();
        var failing = new AsyncCommand(() => throw new InvalidOperationException("at once"), () => true, shown.Add);
        var gate = new TaskCompletionSource();
        var later = new AsyncCommand(async () =>
        {
            await gate.Task.ConfigureAwait(true);
            throw new IOException("after a wait");
        }, () => true, shown.Add);

        IReadOnlyList<Exception> escaped = RecordingContext.Run(() =>
        {
            failing.Execute(null);
            later.Execute(null);
            gate.SetResult();
        });

        Assert.Empty(escaped);
        Assert.Collection(shown, e => Assert.Equal("at once", Assert.IsType<InvalidOperationException>(e).Message), e => Assert.Equal("after a wait", Assert.IsType<IOException>(e).Message));
        await Assert.ThrowsAsync<InvalidOperationException>(failing.ExecuteAsync);
    }

    public void Dispose() => estate.Dispose();

    /// <summary>
    /// A synchronization context that runs what is posted at once and keeps what it throws: an async void method posts its unhandled
    /// exception to the context it started on, which in an app ends the process.
    /// </summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly List<Exception> thrown = [];

        public static List<Exception> Run(Action action)
        {
            var context = new RecordingContext();
            SynchronizationContext? previous = Current;
            SetSynchronizationContext(context);
            try
            {
                action();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }

            return context.thrown;
        }

        public override void Post(SendOrPostCallback d, object? state) => Send(d, state);

        public override void Send(SendOrPostCallback d, object? state)
        {
            try
            {
                d(state);
            }
            catch (Exception exception)
            {
                thrown.Add(exception);
            }
        }
    }
}
