// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>The exact subject and context a person wants to continue with in Workbench. RequestId is retained across uncertain submissions.</summary>
public sealed record DesktopHandoffRequest(string By, string RequestId, AttentionReference Subject, string Task, string Reason, string? Draft = null,
    IReadOnlyList<AttentionReference>? Evidence = null);

/// <summary>A person's request to open one retained handoff in Workbench.</summary>
public sealed record DesktopHandoffOpenRequest(string By, string Id);

/// <summary>A retained context transfer, its separately observed opening, and the current state of its subject's revision.</summary>
public sealed record DesktopHandoff(string Id, string Estate, string? Node, string By, AttentionReference Subject, string Task, string Reason,
    string? Draft, IReadOnlyList<AttentionReference> Evidence, string PreparedAt, string? OpenedAt = null, string SubjectState = "current", string? SubjectStatus = null);

/// <summary>The retained handoffs available to the acting person.</summary>
public sealed record DesktopHandoffsResponse(IReadOnlyList<DesktopHandoff> Handoffs, Problem? Problem = null);

/// <summary>One observed handoff or the reason the operation could not be performed.</summary>
public sealed record DesktopHandoffResponse(DesktopHandoff? Handoff, Problem? Problem = null);

public partial interface IEstateApi
{
    /// <summary>Read a person's retained handoffs from this estate's runtime.</summary>
    DesktopHandoffsResponse DesktopHandoffs(string principal) => new([], new("unsupported", "This runtime does not support desktop handoffs.", []));

    /// <summary>Retain desktop context at an exact revision. Creates no authority.</summary>
    DesktopHandoffResponse PrepareDesktopHandoff(DesktopHandoffRequest request) => new(null, new("unsupported", "This runtime does not support desktop handoffs.", []));

    /// <summary>Observe the desktop opening of a retained handoff. Opening is independent of the work's outcome.</summary>
    DesktopHandoffResponse OpenDesktopHandoff(DesktopHandoffOpenRequest request) => new(null, new("unsupported", "This runtime does not support desktop handoffs.", []));

    /// <summary>Cancellable read of a person's handoffs.</summary>
    Task<DesktopHandoffsResponse> DesktopHandoffsAsync(string principal, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DesktopHandoffs), () => DesktopHandoffs(principal), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable preparation of desktop context.</summary>
    Task<DesktopHandoffResponse> PrepareDesktopHandoffAsync(DesktopHandoffRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PrepareDesktopHandoff), () => PrepareDesktopHandoff(request), true, this as IClientOperationLifetime, cancellationToken);

    /// <summary>Cancellable observation of desktop opening.</summary>
    Task<DesktopHandoffResponse> OpenDesktopHandoffAsync(DesktopHandoffOpenRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OpenDesktopHandoff), () => OpenDesktopHandoff(request), true, this as IClientOperationLifetime, cancellationToken);
}
