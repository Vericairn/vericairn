// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// What a device-signed request may do once the runtime has bound it, from 0.6.0: it acts for its device's own principal only. The
/// check reads the request object the operation will receive, after binding, never the body a second time, so no encoding, spelling
/// or repetition in the body can make the check and the operation read different principals. A request of a type this does not know
/// is refused, so a new operation is held to a principal before it can be reached by a device. Requests over the local socket carry
/// no device, and the operating system's identity of their caller stands for them as before.
/// </summary>
internal sealed class RequestScope(EstateService service, LocalNetworkLedger ledger) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!context.HttpContext.Items.TryGetValue(RequestAuthenticator.DeviceItem, out object? item) || item is not AuthenticatedDevice device)
        {
            return await next(context).ConfigureAwait(false);
        }

        string operation = RequestAuthenticator.Operation(context.HttpContext);
        if (Refusal(operation, device) is string machine)
        {
            return Denied(context.HttpContext, device, machine);
        }

        foreach (object? argument in context.Arguments)
        {
            if (Refusal(argument, device) is string refusal)
            {
                return Denied(context.HttpContext, device, refusal);
            }
        }

        bool write = HttpMethods.IsPost(context.HttpContext.Request.Method);
        if (!device.LocalNetwork || !write)
        {
            return await next(context).ConfigureAwait(false);
        }

        // A write a paired device asks for is signed by a device of its principal that this machine holds: never by one whose every
        // signature needs its owner's presence here, which would ask the person at this machine to approve what someone else sent.
        if (service.PresenceRefusal(device.Principal) is string presence)
        {
            return Denied(context.HttpContext, device, presence);
        }

        object? result = await next(context).ConfigureAwait(false);
        Keep(device, operation, result);
        return result;
    }

    /// <summary>Keeps a write a paired device made: which device, for whom, what operation, and the record it produced.</summary>
    private void Keep(AuthenticatedDevice device, string operation, object? result)
    {
        int status = result is IStatusCodeHttpResult { StatusCode: int code } ? code : StatusCodes.Status200OK;
        if (status >= 300)
        {
            return;
        }

        RecordWritten? written = result is IValueHttpResult { Value: WriteOutcome { Written: RecordWritten record } } ? record : null;
        ledger.Append(new LocalNetworkWrite(Records.Model.RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), device.Identity, device.Principal, operation, written?.Id, written?.Hash, written?.Path));
    }

    /// <summary>
    /// The operations a device-signed request may ask only for an administrator: stopping the runtime and replacing its certificate or
    /// identity. Over the local network they are refused before this, as operations on the runtime's own machine.
    /// </summary>
    private string? Refusal(string operation, AuthenticatedDevice device) =>
        operation is "POST /v1/shutdown" or "POST /v1/runtime/certificate" && !service.Administers(device.Principal)
            ? $"{operation} is an administrator's: device {device.Identity} acts for {device.Principal}, which is neither an operator nor holds the 'administer' scope. On the runtime's machine its operating-system user reaches it over the local socket."
            : null;

    /// <summary>
    /// Each request type an operation binds, with how it names the principal acting. Every request type of the protocol is here: a
    /// test holds the table to the protocol, and a device-signed request of a type that is not is refused.
    /// </summary>
    private static readonly Dictionary<Type, Func<RequestScope, object, AuthenticatedDevice, string?>> Rules = new()
    {
        [typeof(NewRecordRequest)] = (_, r, d) => Acting(((NewRecordRequest)r).By, d) ?? DeviceRecord(((NewRecordRequest)r).Type, d),
        [typeof(AssignRequest)] = (_, r, d) => Acting(((AssignRequest)r).By, d),
        [typeof(AttemptRequest)] = (_, r, d) => Acting(((AttemptRequest)r).By, d),
        [typeof(ResultRequest)] = (_, r, d) => Acting(((ResultRequest)r).By, d),
        [typeof(AmendRequest)] = (_, r, d) => Acting(((AmendRequest)r).By, d),
        [typeof(DeviceCreateRequest)] = (_, r, d) => Acting(((DeviceCreateRequest)r).Principal, d),
        [typeof(AdmitRequest)] = (_, r, d) => Acting(((AdmitRequest)r).By, d) ?? Recovery(((AdmitRequest)r).Recovery, d),
        [typeof(RevokeRequest)] = (_, r, d) => Acting(((RevokeRequest)r).By, d) ?? Recovery(((RevokeRequest)r).Recovery, d),
        [typeof(DeviceRecoveryRequest)] = (_, r, d) => Acting(((DeviceRecoveryRequest)r).Principal, d),
        [typeof(DeviceRenameRequest)] = (scope, r, d) => scope.OwnDevice(((DeviceRenameRequest)r).Device, d),
        [typeof(DeviceRotateRequest)] = (scope, r, d) => scope.OwnDevice(((DeviceRotateRequest)r).Device, d),
        [typeof(DocumentReviewRequest)] = (_, r, d) => Acting(((DocumentReviewRequest)r).By, d),
        [typeof(GoalProposeRequest)] = (_, r, d) => Acting(((GoalProposeRequest)r).By, d),
        [typeof(GoalActionRequest)] = (_, r, d) => Acting(((GoalActionRequest)r).By, d),
        [typeof(GoalEvaluateRequest)] = (_, r, d) => Acting(((GoalEvaluateRequest)r).By, d),
        [typeof(GoalAcceptRequest)] = (_, r, d) => Acting(((GoalAcceptRequest)r).By, d),
        [typeof(ReleaseSubmitRequest)] = (_, r, d) => Acting(((ReleaseSubmitRequest)r).By, d),
        [typeof(ReleaseObserveRequest)] = (_, r, d) => Acting(((ReleaseObserveRequest)r).By, d),
        [typeof(UpgradeRequest)] = (_, r, d) => ((UpgradeRequest)r).Check ? Optional(((UpgradeRequest)r).By, d) : Acting(((UpgradeRequest)r).By, d) ?? Upgrade(d),
        [typeof(ExportRequest)] = (_, r, d) => Optional(((ExportRequest)r).By, d),
        [typeof(RunStartRequest)] = (_, r, d) => Acting(((RunStartRequest)r).By, d),
        [typeof(RunStopRequest)] = (_, r, d) => Acting(((RunStopRequest)r).By, d),
        [typeof(PairingIssueRequest)] = (_, r, d) => Acting(((PairingIssueRequest)r).By, d),

        // No principal acts in these: the rotation's authority is the operation's, and the pairing exchange is the code's.
        [typeof(CertificateRotateRequest)] = (_, _, _) => null,
        [typeof(PairingHello)] = (_, _, _) => null,
        [typeof(PairingConfirm)] = (_, _, _) => null,
    };

    /// <summary>Whether a request type is one this holds to a principal; a device-signed request of any other protocol type is refused.</summary>
    internal static bool Covers(Type type) => Rules.ContainsKey(type);

    /// <summary>Why a bound argument may not proceed for the device, or null.</summary>
    private string? Refusal(object? argument, AuthenticatedDevice device)
    {
        if (argument is null or string or HttpContext or HttpRequest)
        {
            return null;
        }

        Type type = argument.GetType();
        if (Rules.TryGetValue(type, out Func<RequestScope, object, AuthenticatedDevice, string?>? rule))
        {
            return rule(this, argument, device);
        }

        return type.Namespace == typeof(Problem).Namespace
            ? $"A device-signed {type.Name} names no principal the runtime can hold it to, so it is refused."
            : null;
    }

    /// <summary>The principal a request names as acting must be the device's.</summary>
    private static string? Acting(string? acting, AuthenticatedDevice device) =>
        string.IsNullOrEmpty(acting)
            ? $"This request names no principal acting; a request signed by device {device.Identity} acts for {device.Principal}."
            : Optional(acting, device);

    /// <summary>A principal the request may leave out, but when it names one it is the device's.</summary>
    private static string? Optional(string? acting, AuthenticatedDevice device) =>
        acting is null || string.Equals(acting, device.Principal, StringComparison.Ordinal)
            ? null
            : $"Device {device.Identity} acts for its own principal {device.Principal}; this request names {acting} as acting. A principal's requests are signed by its own device{(device.LocalNetwork ? string.Empty : ", or sent over the local socket by the runtime machine's user")}.";

    /// <summary>A device renames or rotates only devices of its own principal.</summary>
    private string? OwnDevice(string? named, AuthenticatedDevice device)
    {
        if (string.IsNullOrEmpty(named) || service.Standing(named) is not DeviceStanding standing)
        {
            return null;
        }

        return string.Equals(standing.Principal, device.Principal, StringComparison.Ordinal)
            ? null
            : $"Device {standing.Identity} is a device of {standing.Principal}; a request signed by device {device.Identity} reaches only the devices of {device.Principal}.";
    }

    /// <summary>A device record is written on the machine that holds its key; over the local network it is refused, as /v1/devices and its renames are.</summary>
    private static string? DeviceRecord(string? type, AuthenticatedDevice device) =>
        device.LocalNetwork && string.Equals(type, "device", StringComparison.Ordinal)
            ? "A device record is written on the machine that holds its key, and is refused over the local network."
            : null;

    /// <summary>Applying an upgrade rewrites the estate's pin and applies an irreversible migration on the runtime's machine; over the local network only its plan is given.</summary>
    private static string? Upgrade(AuthenticatedDevice device) =>
        device.LocalNetwork
            ? "Applying an upgrade acts on the runtime's own machine and is refused over the local network; its plan (check) is not."
            : null;

    /// <summary>A recovery key is a file on the runtime's machine, used there only.</summary>
    private static string? Recovery(string? recovery, AuthenticatedDevice device) =>
        device.LocalNetwork && recovery is not null
            ? "A recovery key is a file on the runtime's own machine and is used there only; it is refused over the local network."
            : null;

    private static IResult Denied(HttpContext context, AuthenticatedDevice device, string message)
    {
        if (device.LocalNetwork)
        {
            context.Response.Headers.Connection = "close";
        }

        return Results.Content(JsonSerializer.Serialize(new Problem("denied", message, []), ProtocolJson.Options), "application/json", Encoding.UTF8, StatusCodes.Status401Unauthorized);
    }
}
