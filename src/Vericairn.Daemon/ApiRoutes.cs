// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vericairn.Protocol;

namespace Vericairn.Daemon;

/// <summary>The routes of API version 1, one per operation of <see cref="IEstateApi"/>; the OpenAPI document in the protocol describes them.</summary>
public static class ApiRoutes
{
    /// <summary>Maps every route onto a service.</summary>
    public static void Map(IEndpointRouteBuilder app, IEstateApi service, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(shutdown);
        string v = ApiVersion.Prefix;
        app.MapGet($"{v}/health", () => Results.Json(service.Health(), ProtocolJson.Options));
        app.MapGet($"{v}/status", () => Results.Json(service.Status(), ProtocolJson.Options));
        app.MapGet($"{v}/validate", () => Results.Json(service.Validate(), ProtocolJson.Options));
        app.MapGet($"{v}/check", () => Results.Json(service.Check(), ProtocolJson.Options));
        app.MapGet($"{v}/records/{{id}}", (string id) => service.Record(id) is RecordResponse record ? Results.Json(record, ProtocolJson.Options) : Results.NotFound());
        app.MapPost($"{v}/records", (NewRecordRequest request) => Outcome(service.NewRecord(request)));
        app.MapGet($"{v}/decisions", () => Results.Json(service.Decisions(), ProtocolJson.Options));
        app.MapGet($"{v}/decisions/{{id}}", (string id) => service.Decision(id) is DecisionResponse decision ? Results.Json(decision, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/work/{{key}}/brief", (string key) => service.Brief(key) is BriefResponse brief ? Results.Json(brief, ProtocolJson.Options) : Results.NotFound());
        app.MapGet($"{v}/work/{{key}}", (string key) => service.Observe(key) is ObserveResponse observe ? Results.Json(observe, ProtocolJson.Options) : Results.NotFound());
        app.MapPost($"{v}/work/assign", (AssignRequest request) => Outcome(service.Assign(request)));
        app.MapPost($"{v}/work/attempt", (AttemptRequest request) => Outcome(service.Attempt(request)));
        app.MapPost($"{v}/work/result", (ResultRequest request) => Outcome(service.Result(request)));
        app.MapPost($"{v}/work/amend", (AmendRequest request) => Outcome(service.Amend(request)));
        app.MapGet($"{v}/devices", () => Results.Json(service.Devices(), ProtocolJson.Options));
        app.MapPost($"{v}/devices", (DeviceCreateRequest request) => Outcome(service.DeviceCreate(request)));
        app.MapPost($"{v}/devices/admit", (AdmitRequest request) => Outcome(service.DeviceAdmit(request)));
        app.MapPost($"{v}/devices/revoke", (RevokeRequest request) => Outcome(service.DeviceRevoke(request)));
        app.MapGet($"{v}/releases", () => Results.Json(service.Releases(), ProtocolJson.Options));
        app.MapPost($"{v}/estate/upgrade", (UpgradeRequest request) =>
        {
            (UpgradeResponse? plan, Problem? problem) = service.Upgrade(request);
            return plan is not null ? Results.Json(plan, ProtocolJson.Options) : Results.Json(problem, ProtocolJson.Options, statusCode: StatusCodes.Status409Conflict);
        });
        app.MapPost($"{v}/estate/export", (ExportRequest request) =>
        {
            (ExportResponse? export, Problem? problem) = service.Export(request);
            return export is not null ? Results.Json(export, ProtocolJson.Options) : Results.Json(problem, ProtocolJson.Options, statusCode: StatusCodes.Status409Conflict);
        });
        app.MapGet($"{v}/reconcile", () => Results.Json(service.Reconcile(), ProtocolJson.Options));
        app.MapGet($"{v}/recover/inspect", () => Results.Text(service.RecoverInspect(), "text/plain; charset=utf-8"));
        app.MapGet($"{v}/objects", () => Results.Json(service.Objects(), ProtocolJson.Options));
        app.MapPost($"{v}/objects", async (HttpRequest request) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer).ConfigureAwait(false);
            (ObjectStored? stored, Problem? problem) = service.ObjectPut(buffer.ToArray());
            return stored is not null ? Results.Json(stored, ProtocolJson.Options) : Results.Json(problem, ProtocolJson.Options, statusCode: StatusCodes.Status413PayloadTooLarge);
        });
        app.MapGet($"{v}/objects/verify", () => Results.Json(service.ObjectsVerify(), ProtocolJson.Options));
        app.MapGet($"{v}/objects/{{hash}}", (string hash) => service.ObjectGet(hash) is byte[] bytes ? Results.Bytes(bytes, "application/octet-stream") : Results.NotFound());
        app.MapGet(OpenApiDocument.Path, () => Results.Text(OpenApiDocument.Json, "application/json; charset=utf-8"));
        app.MapPost($"{v}/shutdown", () =>
        {
            shutdown();
            return Results.Accepted();
        });
    }

    private static IResult Outcome(WriteOutcome outcome) =>
        outcome.Ok
            ? Results.Json(outcome, ProtocolJson.Options)
            : Results.Json(outcome, ProtocolJson.Options, statusCode: outcome.Problem?.Code == "not-found" ? StatusCodes.Status404NotFound : StatusCodes.Status422UnprocessableEntity);
}
