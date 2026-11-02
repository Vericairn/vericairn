// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Sdk;

/// <summary>
/// Writes a record signed by this device's own key: the runtime prepares the exact record, this device's key signs its digest, and the
/// runtime verifies the signature against this admitted device before writing. A paired phone uses it so that its person's decision is
/// signed on the phone, never by a device on the runtime's machine.
/// </summary>
public static class DeviceSignedRecords
{
    /// <summary>Prepares, shows the prepared record to <paramref name="review"/> when given, signs with the device's key and writes.</summary>
    /// <param name="api">A client whose requests the same device signs.</param>
    /// <param name="request">The record to write; its id and time are the runtime's.</param>
    /// <param name="keys">The store holding the device's key.</param>
    /// <param name="device">The device's identity.</param>
    /// <param name="review">Sees the exact record before it is signed and returns false to sign nothing.</param>
    /// <param name="cancellationToken">Stops waiting; a stopped observation does not undo an effect.</param>
    public static async Task<WriteOutcome> WriteAsync(IEstateApi api, NewRecordRequest request, IDeviceKeyStore keys, string device,
        Func<RecordPrepared, bool>? review = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(keys);
        return await WriteCoreAsync(api, request, keys, device, named: false, review, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// From 0.9.0 (REMOTE-11): prepares with the client's own device and signs with its presence device, another admitted device of the
    /// same principal whose every signature needs its owner's presence. The signed request names that device; the runtime verifies the
    /// signature with its key and writes the record with its signature. Requests stay signed by the client's device.
    /// </summary>
    /// <param name="api">A client whose requests the paired device signs.</param>
    /// <param name="request">The record to write; its id and time are the runtime's.</param>
    /// <param name="presenceKeys">The store holding the presence device's key.</param>
    /// <param name="presenceDevice">The presence device's identity.</param>
    /// <param name="review">Sees the exact record before it is signed and returns false to sign nothing; the presence prompt follows it.</param>
    /// <param name="beforeSend">
    /// Called once the presence key has signed and before the signature is sent; returns false to send nothing. A caller that must keep
    /// the record's identity before it can leave keeps it here, so a declined or failed presence prompt leaves nothing to reconcile.
    /// </param>
    /// <param name="cancellationToken">Stops waiting; a stopped observation does not undo an effect.</param>
    /// <returns>The write's outcome; a <c>not-signed</c> problem when the presence key did not sign, in which case nothing was sent.</returns>
    public static async Task<WriteOutcome> WriteWithPresenceAsync(IEstateApi api, NewRecordRequest request, IDeviceKeyStore presenceKeys, string presenceDevice,
        Func<RecordPrepared, bool>? review = null, Func<RecordPrepared, bool>? beforeSend = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(presenceKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(presenceDevice);
        if (DeviceKeyStores.PresenceKinds.IndexOf(presenceKeys.Kind) < 0)
        {
            return WriteOutcome.Fail(new Problem("refused", $"{presenceKeys.Kind} custody cannot enforce its owner's presence, so it does not sign as a presence device. Nothing was prepared or signed.", []));
        }

        return await WriteCoreAsync(api, request, presenceKeys, presenceDevice, named: true, review, beforeSend, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WriteOutcome> WriteCoreAsync(IEstateApi api, NewRecordRequest request, IDeviceKeyStore keys, string device, bool named,
        Func<RecordPrepared, bool>? review, Func<RecordPrepared, bool>? beforeSend, CancellationToken cancellationToken)
    {
        RecordPrepareResponse prepared = await api.PrepareRecordAsync(request, cancellationToken).ConfigureAwait(false);
        if (!prepared.Ok || prepared.Prepared is not RecordPrepared plan)
        {
            return WriteOutcome.Fail(prepared.Problem ?? new Problem("refused", "The runtime prepared no record.", []));
        }

        if (review is not null && !review(plan))
        {
            return WriteOutcome.Fail(new Problem("declined", "Nothing was signed or written.", []));
        }

        RecordSignature signature;
        if (!named)
        {
            signature = new StoreSigner(keys, device).Sign(RecordSigning.Digest(plan.Hash));
        }
        else
        {
            try
            {
                // The prompt for the owner's presence is here. Whatever stops it, nothing has been sent, so it is reported as such.
                signature = new StoreSigner(keys, device).Sign(RecordSigning.Digest(plan.Hash));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return WriteOutcome.Fail(new Problem("not-signed", $"The presence key did not sign: {exception.Message} Nothing was signed or sent.", []));
            }
        }

        if (beforeSend is not null && !beforeSend(plan))
        {
            return WriteOutcome.Fail(new Problem("declined", "The signature was discarded; nothing was sent or written.", []));
        }

        return await api.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, signature.Signature) { Device = named ? device : null }, cancellationToken).ConfigureAwait(false);
    }
}
