// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>List documents still due for a publication and prepare front-document patches without writing.</summary>
    DocumentPassResponse DocumentPass(string version, string? product = null) =>
        new(version, null, null, null, null, [], [], [], [], new("unsupported", "Documentation passes are unavailable.", []));

    /// <summary>Assign exact displayed documents through an ordinary work order and assignment; this clears no document.</summary>
    DocumentPassAssignResponse DocumentPassAssign(string version, DocumentPassAssignRequest request) =>
        new([], new("unsupported", "Documentation pass assignments are unavailable.", []));

    /// <summary>The cancellable documentation pass read.</summary>
    Task<DocumentPassResponse> DocumentPassAsync(string version, string? product = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DocumentPass), () => DocumentPass(version, product), false, this as IClientOperationLifetime, cancellationToken);

    /// <summary>The cancellable documentation assignment write.</summary>
    Task<DocumentPassAssignResponse> DocumentPassAssignAsync(string version, DocumentPassAssignRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DocumentPassAssign), () => DocumentPassAssign(version, request), true, this as IClientOperationLifetime, cancellationToken);
}
