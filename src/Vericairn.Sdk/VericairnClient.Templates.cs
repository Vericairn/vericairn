// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public TemplateRenderResponse TemplateRender(TemplateRenderRequest request) => Post<TemplateRenderRequest, TemplateRenderResponse>("/v1/templates/render", request)!;
    /// <inheritdoc />
    public Task<TemplateRenderResponse> TemplateRenderAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default) => RunOperationAsync(nameof(TemplateRender),
        async token => (await PostAsync<TemplateRenderRequest, TemplateRenderResponse>("/v1/templates/render", request, token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(TemplateRender)), cancellationToken);
    /// <inheritdoc />
    public TemplatesResponse Templates() => Get<TemplatesResponse>("/v1/templates")!;
    /// <inheritdoc />
    public DocumentSectionsResponse DocumentSections(string declaration, string? commit = null) => Get<DocumentSectionsResponse>(SectionsPath(declaration, commit))!;
    /// <inheritdoc />
    public SectionBriefResponse SectionBrief(string declaration, string section, long? maxBytes = null, string? commit = null) => Get<SectionBriefResponse>(SectionBriefPath(declaration, section, maxBytes, commit))!;
    /// <inheritdoc />
    public DocumentNewResponse DocumentNew(DocumentNewRequest request) => Post<DocumentNewRequest, DocumentNewResponse>("/v1/documents/new", request)!;
    /// <inheritdoc />
    public Task<TemplatesResponse> TemplatesAsync(CancellationToken cancellationToken = default) => RunOperationAsync(nameof(Templates),
        async token => (await GetAsync<TemplatesResponse>("/v1/templates", cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(Templates)), cancellationToken);
    /// <inheritdoc />
    public Task<DocumentSectionsResponse> DocumentSectionsAsync(string declaration, string? commit = null, CancellationToken cancellationToken = default) => RunOperationAsync(nameof(DocumentSections),
        async token => (await GetAsync<DocumentSectionsResponse>(SectionsPath(declaration, commit), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(DocumentSections)), cancellationToken);
    /// <inheritdoc />
    public Task<SectionBriefResponse> SectionBriefAsync(string declaration, string section, long? maxBytes = null, string? commit = null, CancellationToken cancellationToken = default) => RunOperationAsync(nameof(SectionBrief),
        async token => (await GetAsync<SectionBriefResponse>(SectionBriefPath(declaration, section, maxBytes, commit), cancellationToken: token).ConfigureAwait(false))!, false, OperationTimeout?.Invoke(nameof(SectionBrief)), cancellationToken);
    /// <inheritdoc />
    public Task<DocumentNewResponse> DocumentNewAsync(DocumentNewRequest request, CancellationToken cancellationToken = default) => RunOperationAsync(nameof(DocumentNew),
        async token => (await PostAsync<DocumentNewRequest, DocumentNewResponse>("/v1/documents/new", request, token).ConfigureAwait(false))!, true, OperationTimeout?.Invoke(nameof(DocumentNew)), cancellationToken);

    private static string SectionsPath(string declaration, string? commit) => "/v1/documents/" + Uri.EscapeDataString(declaration) + "/sections"
        + (commit is null ? "" : "?commit=" + Uri.EscapeDataString(commit));
    private static string SectionBriefPath(string declaration, string section, long? maxBytes, string? commit) =>
        "/v1/documents/" + Uri.EscapeDataString(declaration) + "/sections/" + Uri.EscapeDataString(section) + "/brief?"
        + string.Join('&', new Dictionary<string, string?> { ["maxBytes"] = maxBytes?.ToString(CultureInfo.InvariantCulture), ["commit"] = commit }
            .Where(p => p.Value is not null).Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value!)));
}
