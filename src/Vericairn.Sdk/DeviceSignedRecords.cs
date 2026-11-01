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
        RecordPrepareResponse prepared = await api.PrepareRecordAsync(request, cancellationToken).ConfigureAwait(false);
        if (!prepared.Ok || prepared.Prepared is not RecordPrepared plan)
        {
            return WriteOutcome.Fail(prepared.Problem ?? new Problem("refused", "The runtime prepared no record.", []));
        }

        if (review is not null && !review(plan))
        {
            return WriteOutcome.Fail(new Problem("declined", "Nothing was signed or written.", []));
        }

        RecordSignature signature = new StoreSigner(keys, device).Sign(RecordSigning.Digest(plan.Hash));
        return await api.NewSignedRecordAsync(new SignedRecordRequest(plan.Plan, signature.Signature), cancellationToken).ConfigureAwait(false);
    }
}
