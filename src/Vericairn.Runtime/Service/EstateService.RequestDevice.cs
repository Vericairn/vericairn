// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private readonly AsyncLocal<RequestDeviceScope?> requestDevice = new();

    /// <summary>
    /// Carries only the device identity, the key that authenticated this request and any required profile to the write path. Each
    /// write checks them against the records under the estate's gate. Local-socket and embedded operations carry no such scope.
    /// </summary>
    public RequestDeviceScope ForDeviceRequest(string device, string publicKey, string? profile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);
        var scope = new RequestDeviceScope(this, requestDevice.Value, device, publicKey, profile);
        requestDevice.Value = scope;
        return scope;
    }

    /// <summary>Rechecks the requesting device in the model loaded under the gate, before anything is written. Caller holds the gate.</summary>
    private Problem? RequestDeviceRefusal(EstateModel model)
    {
        RequestDeviceScope? request = requestDevice.Value;
        if (request is null || request.Closed)
        {
            return null;
        }

        if (request.Refusal is not null)
        {
            return request.Refusal;
        }

        DeviceStanding? standing = Standing(model, request.Device);
        if (standing is null || standing.Admission != "admitted")
        {
            return request.Deny($"Device {request.Device} is {standing?.Admission ?? "unknown"} now; the request cannot write after its device loses admission or is revoked.");
        }

        if (!string.Equals(standing.PublicKey, request.PublicKey, StringComparison.Ordinal))
        {
            return request.Deny($"Device {request.Device} no longer holds the key that authenticated this request; sign the request afresh with its current key.");
        }

        return request.Profile is string profile && !standing.Profiles.Contains(profile, StringComparer.Ordinal)
            ? request.Deny($"Device {request.Device} is no longer permitted the {profile} profile; the request cannot write.")
            : null;
    }

    /// <summary>The requesting device for one operation, flowing across awaits; disposing it restores the caller's scope.</summary>
    public sealed class RequestDeviceScope : IDisposable
    {
        private readonly EstateService service;
        private readonly RequestDeviceScope? previous;
        private int closed;

        internal RequestDeviceScope(EstateService service, RequestDeviceScope? previous, string device, string publicKey, string? profile)
        {
            this.service = service;
            this.previous = previous;
            Device = device;
            PublicKey = publicKey;
            Profile = profile;
        }

        internal string Device { get; }

        internal string PublicKey { get; }

        internal string? Profile { get; }

        // A background operation may have captured the execution context. Once the request ends it is the runtime's work;
        // it must not retain the completed request's credential, nor lend it to another request.
        internal bool Closed => Volatile.Read(ref closed) != 0;

        /// <summary>The write's refusal, when the device lost authority; the host answers it as an authentication refusal.</summary>
        public Problem? Refusal { get; private set; }

        internal Problem Deny(string message) => Refusal ??= new Problem("denied", message, []);

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0)
            {
                service.requestDevice.Value = previous;
            }
        }
    }
}
