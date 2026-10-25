// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>Every open request's identity, with at most three complete requests delivered at once.</summary>
public sealed record OperatorRequestsResponse(string Text, JsonElement Json, bool HasErrors);

/// <summary>Create or supersede a draft; revisions require the expected predecessor hash.</summary>
public sealed record OperatorRequestWriteRequest(string By, JsonElement Body, string Subject, string? Supersedes = null, string? Expected = null);

/// <summary>Carry an exact open ask into a request, retaining its words and original requester. Body supplies kind, destination and content.</summary>
public sealed record OperatorRequestCarryRequest(string By, string Expected, JsonElement Body, string Subject);

/// <summary>An attributed verbatim citation of the addressed operator's words.</summary>
public sealed record OperatorRequestWords(string By, string Statement, string Source, string At);

/// <summary>Close an exact request revision, optionally pinning its hash too. A dismissal needs a reason; an agent needs OperatorWords.</summary>
public sealed record OperatorRequestCloseRequest(string By, string? Expected = null, string? Reason = null, string? Link = null,
    JsonElement? Evidence = null, OperatorRequestWords? OperatorWords = null);

public partial interface IEstateApi
{
    /// <summary>Read the complete open request queue without authoring or dispatching anything.</summary>
    OperatorRequestsResponse OperatorRequests(string? principal = null) => throw new NotSupportedException("Operator requests are not supported by this API implementation.");

    /// <summary>Record a request or revised draft, with exact content and subject pins.</summary>
    WriteOutcome OperatorRequestWrite(OperatorRequestWriteRequest request) => WriteOutcome.Fail(new("unsupported", "Operator requests are not supported by this API implementation.", []));

    /// <summary>Carry an open ask into one request; the ask and its decision semantics are preserved.</summary>
    WriteOutcome OperatorRequestCarry(string ask, OperatorRequestCarryRequest request) => WriteOutcome.Fail(new("unsupported", "Operator requests are not supported by this API implementation.", []));

    /// <summary>Sign one done outcome, preserving the exact request and any evidence.</summary>
    WriteOutcome OperatorRequestDone(string id, OperatorRequestCloseRequest request) => WriteOutcome.Fail(new("unsupported", "Operator requests are not supported by this API implementation.", []));

    /// <summary>Sign one dismissed outcome with the operator's reason.</summary>
    WriteOutcome OperatorRequestDismiss(string id, OperatorRequestCloseRequest request) => WriteOutcome.Fail(new("unsupported", "Operator requests are not supported by this API implementation.", []));

    /// <summary>Cancellable request queue read.</summary>
    Task<OperatorRequestsResponse> OperatorRequestsAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorRequests), () => OperatorRequests(principal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable request authoring; cancellation after submission does not undo the write.</summary>
    Task<WriteOutcome> OperatorRequestWriteAsync(OperatorRequestWriteRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorRequestWrite), () => OperatorRequestWrite(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable carry-over; cancellation after submission does not undo the write.</summary>
    Task<WriteOutcome> OperatorRequestCarryAsync(string ask, OperatorRequestCarryRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorRequestCarry), () => OperatorRequestCarry(ask, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable done outcome; cancellation after submission does not undo the write.</summary>
    Task<WriteOutcome> OperatorRequestDoneAsync(string id, OperatorRequestCloseRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorRequestDone), () => OperatorRequestDone(id, request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable dismissed outcome; cancellation after submission does not undo the write.</summary>
    Task<WriteOutcome> OperatorRequestDismissAsync(string id, OperatorRequestCloseRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorRequestDismiss), () => OperatorRequestDismiss(id, request), true, this as IClientOperationLifetime, cancellationToken);
}
