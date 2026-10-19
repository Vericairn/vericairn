// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

internal static class CompanionRoutes
{
    internal static void Map(IEndpointRouteBuilder app, IEstateApi service)
    {
        app.MapGet("/v1/companions", async (HttpContext context) =>
            Results.Json(await service.CompanionsAsync(context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet("/v1/companions/render", async ([AsParameters] CompanionRequest request, HttpContext context) =>
            Results.Json(await service.CompanionRenderAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet("/v1/companions/sources", async ([AsParameters] CompanionRequest request, HttpContext context) =>
            Results.Json(await service.CompanionSourcesAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapGet("/v1/companions/compare", async ([AsParameters] CompanionCompareRequest request, HttpContext context) =>
            Results.Json(await service.CompanionCompareAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
    }
}
