// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public partial interface IEstateApi
{
    /// <summary>Lists installed and estate-retained template versions without selecting one.</summary>
    TemplatesResponse Templates() => new([], new Problem("unsupported", "Template reads are unavailable.", []));
    /// <summary>Reads each section's state and evidence at an optional exact repository commit.</summary>
    DocumentSectionsResponse DocumentSections(string declaration, string? commit = null) => new(null, null, [], new Problem("unsupported", "Section reads are unavailable.", []));
    /// <summary>Reads a complete measured section brief; a limit never silently truncates a source.</summary>
    SectionBriefResponse SectionBrief(string declaration, string section, long? maxBytes = null, string? commit = null) => new(null, new Problem("unsupported", "Section briefs are unavailable.", []));
    /// <summary>Creates an estate document and its pinned living declaration within ordinary authoring authority.</summary>
    DocumentNewResponse DocumentNew(DocumentNewRequest request) => new(null, null, null, new Problem("unsupported", "Template instantiation is unavailable.", []));
    /// <summary>Cancellable catalogue read.</summary>
    Task<TemplatesResponse> TemplatesAsync(CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(Templates), Templates, false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>Cancellable section read.</summary>
    Task<DocumentSectionsResponse> DocumentSectionsAsync(string declaration, string? commit = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DocumentSections), () => DocumentSections(declaration, commit), false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>Cancellable complete brief read.</summary>
    Task<SectionBriefResponse> SectionBriefAsync(string declaration, string section, long? maxBytes = null, string? commit = null, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(SectionBrief), () => SectionBrief(declaration, section, maxBytes, commit), false, this as IClientOperationLifetime, cancellationToken);
    /// <summary>Cancellable instantiation write; an interrupted write must be inspected before retrying.</summary>
    Task<DocumentNewResponse> DocumentNewAsync(DocumentNewRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(DocumentNew), () => DocumentNew(request), true, this as IClientOperationLifetime, cancellationToken);
}
