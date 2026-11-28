// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Connections;

namespace Vericairn.Daemon;

/// <summary>A Kestrel listener backed only by admitted relay streams, with no socket another process can dial directly.</summary>
internal sealed class RelayListener : IConnectionListenerFactory, IConnectionListenerFactorySelector, IConnectionListener
{
    internal static readonly object TicketDevice = new();
    private readonly Channel<RelayConnection> connections = Channel.CreateBounded<RelayConnection>(DaemonHost.ConcurrentConnections);

    public EndPoint EndPoint { get; } = new RelayEndPoint();

    public bool CanBind(EndPoint endpoint) => ReferenceEquals(endpoint, EndPoint);

    public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default) =>
        CanBind(endpoint) ? ValueTask.FromResult<IConnectionListener>(this) : throw new NotSupportedException();

    public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await connections.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        connections.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await UnbindAsync().ConfigureAwait(false);
        while (connections.Reader.TryRead(out RelayConnection? remaining))
        {
            await remaining.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async Task ServeAsync(Stream stream, string device, CancellationToken cancellationToken)
    {
        var connection = new RelayConnection(stream, device, cancellationToken);
        using CancellationTokenRegistration stop = cancellationToken.Register(() => connection.Abort());
        if (!connections.Writer.TryWrite(connection))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return;
        }

        await connection.Ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class RelayEndPoint : EndPoint
    {
        public override string ToString() => "relay";
    }

    private sealed class RelayConnection : DefaultConnectionContext
    {
        private readonly Stream stream;

        internal RelayConnection(Stream stream, string device, CancellationToken stop)
            : base(Guid.NewGuid().ToString("N"))
        {
            this.stream = stream;
            Items[TicketDevice] = device;
            ConnectionClosed = stop;
            Transport = new ScreenedTransport(PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true)),
                PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)));
        }

        internal TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Abort(ConnectionAbortedException abortReason) => stream.Dispose();

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await Transport.Input.CompleteAsync().ConfigureAwait(false);
                await Transport.Output.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
                Ended.TrySetResult();
            }
        }
    }
}
