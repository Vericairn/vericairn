// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

internal static class PromptRoutes
{
    internal static void Map(IEndpointRouteBuilder app, IEstateApi service)
    {
        app.MapGet("/v1/prompts", async ([AsParameters] PromptQuery query, HttpContext context) =>
            Results.Json(await service.PromptsAsync(query, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
        app.MapPost("/v1/prompts", async (PromptSaveRequest request, HttpContext context) => Outcome(await service.PromptSaveAsync(request, context.RequestAborted).ConfigureAwait(false)));
        app.MapPost("/v1/prompts/preferences", async (PromptPreferenceRequest request, HttpContext context) => Outcome(await service.PromptPreferenceAsync(request, context.RequestAborted).ConfigureAwait(false)));
        app.MapPost("/v1/prompts/copy", async (PromptCopyRequest request, HttpContext context) => Outcome(await service.PromptCopyAsync(request, context.RequestAborted).ConfigureAwait(false)));
        app.MapPost("/v1/prompts/use", async (PromptUseRequest request, HttpContext context) =>
            Results.Json(await service.PromptUseAsync(request, context.RequestAborted).ConfigureAwait(false), ProtocolJson.Options));
    }

    private static IResult Outcome(WriteOutcome outcome) => Results.Json(outcome, ProtocolJson.Options,
        statusCode: outcome.Written is not null || outcome.Ok ? 200 : outcome.Problem?.Code == "not-found" ? 404 : 422);
}
