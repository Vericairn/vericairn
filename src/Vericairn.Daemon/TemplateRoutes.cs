// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

internal static class TemplateRoutes
{
    internal static void Map(IEndpointRouteBuilder app, IEstateApi service)
    {
        app.MapPost("/v1/templates/render", async (TemplateRenderRequest request, HttpContext context) =>
            Results.Json(await service.TemplateRenderAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet("/v1/templates", async Task<IResult> (HttpContext context) =>
            Results.Json(await service.TemplatesAsync(context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet("/v1/documents/{id}/sections", async (string id, string? commit, HttpContext context) =>
            Results.Json(await service.DocumentSectionsAsync(Uri.UnescapeDataString(id), commit, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet("/v1/documents/{id}/sections/{section}/brief", async (string id, string section, long? maxBytes, string? commit, HttpContext context) =>
            Results.Json(await service.SectionBriefAsync(Uri.UnescapeDataString(id), Uri.UnescapeDataString(section), maxBytes, commit, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapPost("/v1/documents/new", async (DocumentNewRequest request, HttpContext context) =>
            Results.Json(await service.DocumentNewAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapPost("/v1/documents/adopt", async (DocumentAdoptRequest request, HttpContext context) =>
            Results.Json(await service.DocumentAdoptAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
    }
}
