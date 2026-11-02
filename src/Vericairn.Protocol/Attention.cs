// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>A notice and its independent presentation state. Revision identifies the observed item, never an approval.</summary>
public sealed record AttentionNotice(string Kind, string Subject, string Why, string State, string AsOf, string NextAction,
    string SubjectType, string SubjectId, string Revision, string? AcknowledgedAt = null)
{
    /// <summary>The notice's stable identity within a principal's attention.</summary>
    public string Key => Kind + ":" + SubjectType + ":" + SubjectId;

    /// <summary>Acknowledgment never changes the subject's state or removes the notice.</summary>
    public string AttentionState => AcknowledgedAt is null ? "unacknowledged" : "acknowledged";

    /// <summary>The exact subject to open when the source notice concerns another record.</summary>
    public AttentionReference? Target { get; init; }

    /// <summary>The exact evaluated condition behind a policy notice; absent on other notices and older runtimes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PolicyAttention? Policy { get; init; }
}

/// <summary>An exact condition with its basis digest, time and origin, plus operator preparation. The policy read carries the full basis inventory.</summary>
public sealed record PolicyAttention(PolicyFindingDto Finding, string Origin, IReadOnlyList<OperatorDutyDto> Operator);

/// <summary>One principal's complete current attention, including acknowledged items.</summary>
public sealed record AttentionResponse(string Principal, string AsOf, IReadOnlyList<AttentionNotice> Items);

/// <summary>Acknowledge only the exact observed notice for the acting principal, without a record or a signature.</summary>
public sealed record AttentionAcknowledgeRequest(string By, string Kind, string SubjectType, string SubjectId, string Expected);

/// <summary>The presentation state stored, or a refusal; this response never contains a written record.</summary>
public sealed record AttentionAcknowledgeResponse(AttentionNotice? Item, Problem? Problem = null)
{
    /// <summary>True only when the runtime confirmed the acknowledgment.</summary>
    public bool Ok => Item is not null && Problem is null;
}

public partial interface IEstateApi
{
    /// <summary>Reads attention for a principal, defaulting to the requesting device's principal or the local operator.</summary>
    AttentionResponse AttentionGet(string? principal = null) => throw new NotSupportedException("Attention presentation state is unavailable.");

    /// <summary>Stores unsigned presentation state only, shared by the principal's devices through this runtime.</summary>
    AttentionAcknowledgeResponse AttentionAcknowledge(AttentionAcknowledgeRequest request) => throw new NotSupportedException("Attention acknowledgment is unavailable.");

    /// <summary>The cancellable read bridge.</summary>
    Task<AttentionResponse> AttentionGetAsync(string? principal = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(AttentionGet), () => AttentionGet(principal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable write bridge. Interruption does not imply that presentation state was stored.</summary>
    Task<AttentionAcknowledgeResponse> AttentionAcknowledgeAsync(AttentionAcknowledgeRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(AttentionAcknowledge), () => AttentionAcknowledge(request), true, this as IClientOperationLifetime, cancellationToken);
}
