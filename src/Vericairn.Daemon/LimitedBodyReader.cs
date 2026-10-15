// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Vericairn.Daemon;

/// <summary>
/// Limits bytes offered to authentication or binding without poisoning the underlying HTTP body reader. A refused remainder is
/// discarded through that original reader, never through this limit or a parser. The reader belongs to Kestrel, not this wrapper.
/// </summary>
internal sealed class LimitedBodyReader(PipeReader inner, long maximum) : PipeReader, IRequestBodyPipeFeature
{
    private ReadOnlySequence<byte> buffer;
    private long consumed;
    private bool completed;

    public bool IsFullyRead { get; private set; }

    public PipeReader Reader => this;

    public override void AdvanceTo(SequencePosition position) => AdvanceTo(position, position);

    public override void AdvanceTo(SequencePosition position, SequencePosition examined)
    {
        consumed += buffer.Slice(0, position).Length;
        IsFullyRead = completed && buffer.Slice(position).IsEmpty;
        inner.AdvanceTo(position, examined);
        buffer = default;
    }

    public override void CancelPendingRead() => inner.CancelPendingRead();

    public override void Complete(Exception? exception = null) { }

    public override ValueTask CompleteAsync(Exception? exception = null) => ValueTask.CompletedTask;

    public override bool TryRead(out ReadResult result)
    {
        if (!inner.TryRead(out result))
        {
            return false;
        }

        Check(result);
        return true;
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        ReadResult result = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        Check(result);
        return result;
    }

    private void Check(ReadResult result)
    {
        if (result.Buffer.Length > maximum - consumed)
        {
            // Release the read without consuming anything: cleanup can discard it, but no refused byte reaches the caller.
            inner.AdvanceTo(result.Buffer.Start, result.Buffer.Start);
            throw new BadHttpRequestException($"The request body exceeds its limit of {maximum} bytes.", StatusCodes.Status413PayloadTooLarge);
        }

        buffer = result.Buffer;
        completed = result.IsCompleted;
    }
}
