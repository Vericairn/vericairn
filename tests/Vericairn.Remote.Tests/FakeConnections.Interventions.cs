// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Remote.Tests;

internal sealed partial class FakeConnections
{
    private WriteOutcome WriteSignedIntervention(RuntimeConnection connection, NewRecordRequest request,
        Func<RecordPrepared, bool> review, Func<RecordPrepared, bool> beforeSend)
    {
        BeforeApiCall?.Invoke(nameof(IEstateApi.PrepareRecordAsync));
        RecordPrepareResponse result;
        using (service.ForDeviceRequest(connection.Device, connection.DevicePublicKey))
            result = service.PrepareRecord(request);
        if (result.Prepared is not { } prepared)
            return WriteOutcome.Fail(result.Problem ?? new("refused", "The runtime prepared no intervention.", []));
        if (!review(prepared))
            return WriteOutcome.Fail(new("declined", "The review declined the prepared intervention.", []));
        var signature = new StoreSigner(PresenceKeys, connection.PresenceDevice!).Sign(RecordSigning.Digest(prepared.Hash));
        if (!beforeSend(prepared))
            return WriteOutcome.Fail(new("declined", "The signature was discarded.", []));
        BeforeApiCall?.Invoke(nameof(IEstateApi.NewSignedRecordAsync));
        WriteOutcome written;
        using (service.ForDeviceRequest(connection.Device, connection.DevicePublicKey))
            written = service.NewSignedRecord(new(prepared.Plan, signature.Signature) { Device = connection.PresenceDevice });
        if (LoseRunStopResponse && request.Type == "run-stop-request") throw new HttpRequestException("The signed response was lost.");
        return written;
    }
}
