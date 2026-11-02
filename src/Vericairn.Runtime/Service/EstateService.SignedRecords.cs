// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    private const int PreparedLimit = 256;
    private readonly ConcurrentDictionary<string, PreparedRecord> preparedRecords = new(StringComparer.Ordinal);

    /// <summary>How long a prepared record waits for its device's signature: room for the person to review it and answer a presence prompt.</summary>
    internal static TimeSpan PreparedLifetime { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The clock prepared records lapse by; a test moves it.</summary>
    internal TimeProvider PreparedClock { get; set; } = TimeProvider.System;

    /// <inheritdoc />
    public RecordPrepareResponse PrepareRecord(NewRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (requestDevice.Value is not RequestDeviceScope device || device.Closed)
            return Unprepared("refused", "A record is prepared for the device whose request asks for it, so only a device-signed request can prepare one.");
        if (RecordTypeRegistry.Find(request.Type) is null) return Unprepared("refused", $"Unknown record type '{request.Type}'.");
        if (!Ulid.IsValid(request.By)) return Unprepared("refused", "The authoring principal must be a ULID.");
        if (request.At is not null || request.Id is not null) return Unprepared("refused", "The runtime fixes a prepared record's id and time.");
        RecordReference? subject = null;
        if (request.Subject is not null && !TryParseSubject(request.Subject, out subject)) return Unprepared("refused", "The subject must be type:id or type:id:hash.");

        DateTimeOffset now = PreparedClock.GetUtcNow();
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is Problem denied) return new(false, Problem: denied);
            if (Standing(model, device.Device) is not DeviceStanding standing || !string.Equals(standing.Principal, request.By, StringComparison.Ordinal))
                return Unprepared("denied", $"Device {device.Device} signs only its own principal's records.");

            var draft = new RecordDraft(request.Type, request.By, request.Body, subject, request.Supersedes, now, Ulid.NewUlid(now).Value,
                Math.Min(model.Manifest?.RecordFormat ?? session.Manifest.RecordFormat, RecordFormats.Current));
            if (IsPolicyWrite(draft) || IsDutyEvidence(draft, model))
                return Unprepared("refused", "Policy writes and duty evidence are authored on the runtime's machine, with their expected hashes; they are not prepared for a device's signature.");
            // A device cannot observe the estate's repository: a change set it states must be the one the runtime observes now.
            if (DecisionBasisRefusal(draft) is string basis) return Unprepared("refused", basis);

            // The same authoring path as every write, with no signature: the exact record the device will sign, or why there is none.
            (AuthoredRecord? record, string? refusal) = AuthorLocked(draft, model, []);
            if (record is null) return Unprepared("refused", refusal!);
            draft = draft with { At = record.Envelope.AtTime };

            foreach ((string id, PreparedRecord lapsed) in preparedRecords.Where(p => p.Value.ExpiresAt <= now).ToArray()) preparedRecords.TryRemove(id, out _);
            if (preparedRecords.Count >= PreparedLimit) return Unprepared("unavailable", "Too many records await their devices' signatures; sign or let some lapse first.");
            string plan = Ulid.NewUlid(now).Value;
            DateTimeOffset expires = now + PreparedLifetime;
            preparedRecords[plan] = new PreparedRecord(device.Device, draft, record.Envelope.ComputedHash, expires);
            using JsonDocument unsigned = JsonDocument.Parse(record.Bytes);
            return new(true, new RecordPrepared(plan, record.Envelope.Id, record.Envelope.At, record.Envelope.ComputedHash, RecordParser.FormatTimestamp(expires), unsigned.RootElement.Clone()));
        }
    }

    /// <inheritdoc />
    public WriteOutcome NewSignedRecord(SignedRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (requestDevice.Value is not RequestDeviceScope device || device.Closed)
            return Refuse("Only the device that prepared a record signs it, so only a device-signed request can write one.");
        if (string.IsNullOrEmpty(request.Plan) || !preparedRecords.TryGetValue(request.Plan, out PreparedRecord? plan))
            return Refuse("No prepared record waits under this plan: it was written, lapsed or never made. Prepare it again.");
        if (!string.Equals(plan.Device, device.Device, StringComparison.Ordinal))
            return WriteOutcome.Fail(new Problem("denied", $"The plan was prepared for another device; device {device.Device} cannot sign it.", []));
        // Its own device uses a plan once, whatever happens next: a signature is never applied to a second write.
        if (!preparedRecords.TryRemove(new KeyValuePair<string, PreparedRecord>(request.Plan, plan)))
            return Refuse("No prepared record waits under this plan: it was written, lapsed or never made. Prepare it again.");
        if (plan.ExpiresAt <= PreparedClock.GetUtcNow()) return Refuse("The prepared record lapsed unsigned. Prepare it again.");

        lock (gate)
        {
            EstateModel model = session.Load();
            string signer = string.IsNullOrEmpty(request.Device) ? device.Device : request.Device;
            string key = device.PublicKey;
            if (!string.Equals(signer, device.Device, StringComparison.Ordinal))
            {
                // Another device signs only when it is its principal's, admitted now, and each of its signatures needs its owner's
                // presence: a phone's presence key, never a second unattended key standing in for the requesting one.
                if (Standing(model, device.Device) is not DeviceStanding requester || Standing(model, signer) is not DeviceStanding signing
                    || signing.Admission != "admitted" || !string.Equals(signing.Principal, requester.Principal, StringComparison.Ordinal))
                    return WriteOutcome.Fail(new Problem("denied", $"Device {signer} is not an admitted device of device {device.Device}'s principal, so it cannot sign this plan.", []));
                if (EstateModel.BodyString(model.Latest(model.ById[signing.Identity]), "presence") != "required")
                    return Refuse($"Device {signer} does not need its owner's presence at each signature, so it cannot sign a plan another device prepared.");
                // The record's signature names the device's identity, whichever of its record ids the request gave.
                signer = signing.Identity;
                key = signing.PublicKey;
            }

            if (string.IsNullOrEmpty(request.Signature) || !RecordSigning.Verify(key, RecordSigning.Digest(plan.Hash), request.Signature))
                return Refuse($"The signature does not verify with device {signer}'s key over the prepared record; nothing was written.");

            // The basis a decision states is observed again: the repository may have moved while the person reviewed and signed.
            if (DecisionBasisRefusal(plan.Draft) is string basis) return Refuse(basis + " Nothing was written.");

            // The write rechecks the device under the gate, re-authors the same record and judges the estate with it, as every write does.
            return WriteLocked(plan.Draft, model, [new PresignedSigner(signer, plan.Hash, request.Signature)]);
        }
    }

    private static RecordPrepareResponse Unprepared(string code, string message) => new(false, Problem: new Problem(code, message, []));

    private sealed record PreparedRecord(string Device, RecordDraft Draft, string Hash, DateTimeOffset ExpiresAt);
}
