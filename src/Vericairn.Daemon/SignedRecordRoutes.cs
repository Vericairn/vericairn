// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>Records a device signs with its own key: the runtime prepares the exact record, and writes it with the device's signature.</summary>
internal static class SignedRecordRoutes
{
    internal static void Map(IEndpointRouteBuilder app, IEstateApi service)
    {
        app.MapPost("/v1/records/prepare", async (NewRecordRequest request, HttpContext context) =>
        {
            RecordPrepareResponse prepared = await service.PrepareRecordAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(prepared, ProtocolJson.Options, statusCode: prepared.Ok ? 200 : 422);
        });
        app.MapPost("/v1/records/signed", async (SignedRecordRequest request, HttpContext context) =>
        {
            WriteOutcome outcome = await service.NewSignedRecordAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(outcome, ProtocolJson.Options, statusCode: outcome.Written is not null || outcome.Ok ? 200 : 422);
        });
    }
}
