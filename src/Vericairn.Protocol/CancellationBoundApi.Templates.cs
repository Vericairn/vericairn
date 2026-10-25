// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

public sealed partial class CancellationBoundApi
{
    /// <inheritdoc />
    public TemplatesResponse Templates() => inner.TemplatesAsync(cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DocumentSectionsResponse DocumentSections(string declaration, string? commit = null) => inner.DocumentSectionsAsync(declaration, commit, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public SectionBriefResponse SectionBrief(string declaration, string section, long? maxBytes = null, string? commit = null) => inner.SectionBriefAsync(declaration, section, maxBytes, commit, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public DocumentNewResponse DocumentNew(DocumentNewRequest request) => inner.DocumentNewAsync(request, cancellationToken).GetAwaiter().GetResult();
    /// <inheritdoc />
    public Task<TemplatesResponse> TemplatesAsync(CancellationToken cancellationToken = default) => ForwardAsync(inner.TemplatesAsync, cancellationToken);
    /// <inheritdoc />
    public Task<DocumentSectionsResponse> DocumentSectionsAsync(string declaration, string? commit = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DocumentSectionsAsync(declaration, commit, token), cancellationToken);
    /// <inheritdoc />
    public Task<SectionBriefResponse> SectionBriefAsync(string declaration, string section, long? maxBytes = null, string? commit = null, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.SectionBriefAsync(declaration, section, maxBytes, commit, token), cancellationToken);
    /// <inheritdoc />
    public Task<DocumentNewResponse> DocumentNewAsync(DocumentNewRequest request, CancellationToken cancellationToken = default) => ForwardAsync(token => inner.DocumentNewAsync(request, token), cancellationToken);
}
