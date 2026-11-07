// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

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

/// <summary>What a request whose records only its own device signs may do with a record (0.9.2, MCP-03).</summary>
public enum DeviceSigningMode
{
    /// <summary>Write no record: a read, or a write that keeps live state only.</summary>
    Read,

    /// <summary>Prepare the one record the operation authors for the requesting device's signature, and write nothing.</summary>
    Prepare,

    /// <summary>Write the one record the operation authors only with the requesting device's signature over that plan's exact record.</summary>
    Sign,
}

/// <summary>
/// From 0.9.2 (MCP-03): a request whose records the requesting device signs itself, as network MCP's signed mode has it. Under it no
/// key on the runtime's machine signs and no record is written unsigned: an operation either writes nothing, prepares the one record
/// it authors for the requesting device and writes nothing, or writes that one record with the device's signature over the plan
/// it prepared, re-authored from the same request so that the record written is the one the device signed. The runtime stores
/// no other record while it holds.
/// </summary>
public sealed class DeviceSigning
{
    private int used;

    private DeviceSigning(DeviceSigningMode mode, string? plan, string? signature)
    {
        Mode = mode;
        Plan = plan;
        Signature = signature;
    }

    /// <summary>A request that writes no record.</summary>
    public static DeviceSigning Read() => new(DeviceSigningMode.Read, null, null);

    /// <summary>A request that prepares its one record for the requesting device's signature.</summary>
    public static DeviceSigning Prepare() => new(DeviceSigningMode.Prepare, null, null);

    /// <summary>A request that writes its one record with the requesting device's signature over a plan it prepared.</summary>
    public static DeviceSigning Sign(string plan, string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        return new(DeviceSigningMode.Sign, plan, signature);
    }

    /// <summary>What the request may do with a record.</summary>
    public DeviceSigningMode Mode { get; }

    /// <summary>The plan a signing request writes.</summary>
    public string? Plan { get; }

    /// <summary>The requesting device's signature of the plan's record-signing digest.</summary>
    public string? Signature { get; }

    /// <summary>The record a preparing request prepared, once it has; null when the operation authored none.</summary>
    public RecordPrepared? Prepared { get; internal set; }

    /// <summary>The time the request's record states: fixed once while preparing, and the plan's while signing.</summary>
    internal DateTimeOffset? At { get; set; }

    /// <summary>The content hash of the one record a signing request may store, once its signature verified.</summary>
    internal string? Authorized { get; set; }

    /// <summary>Takes the request's one record; false when it has authored one already.</summary>
    internal bool TakeRecord() => Interlocked.Exchange(ref used, 1) == 0;
}

public sealed partial class EstateService
{
    /// <summary>The most prepared records one device may have waiting for its signature at once, from 0.9.2.</summary>
    internal const int PreparedPerDevice = 32;

    /// <summary>The device-signing rule of the request in flight, or null for every other request.</summary>
    private DeviceSigning? CurrentDeviceSigning => requestDevice.Value is { Closed: false, Signing: { } signing } ? signing : null;

    /// <summary>
    /// The time a write's record states under device signing, so that preparing and signing author the same record: fixed to the
    /// whole second once while preparing, the plan's own while signing; null for every other request.
    /// </summary>
    private DateTimeOffset? DeviceSigningTime()
    {
        if (CurrentDeviceSigning is not { } signing)
        {
            return null;
        }

        if (signing.At is null)
        {
            if (signing.Mode == DeviceSigningMode.Sign)
            {
                signing.At = signing.Plan is not null && preparedRecords.TryGetValue(signing.Plan, out PreparedRecord? plan) ? plan.Draft.At : null;
            }
            else
            {
                DateTimeOffset now = PreparedClock.GetUtcNow();
                signing.At = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
            }
        }

        return signing.At;
    }

    /// <summary>
    /// Writes, prepares or refuses the one record an operation authors under device signing. Nothing on this machine signs it.
    /// Caller holds the gate.
    /// </summary>
    private WriteOutcome DeviceSignedWriteLocked(RecordDraft draft, EstateModel model, RequestDeviceScope device, DeviceSigning signing)
    {
        if (signing.Mode == DeviceSigningMode.Read)
        {
            return Refuse("This request writes no record: a network write is prepared for the requesting device and then written with its signature.");
        }

        if (!signing.TakeRecord())
        {
            return Refuse("A device-signed request prepares or writes one record; this operation authors more than one, and nothing more was written.");
        }

        if (Standing(model, device.Device) is not DeviceStanding standing || !string.Equals(standing.Principal, draft.By, StringComparison.Ordinal))
        {
            return WriteOutcome.Fail(new Problem("denied", $"Device {device.Device} signs only its own principal's records.", []));
        }

        if (signing.Mode == DeviceSigningMode.Prepare)
        {
            DateTimeOffset now = PreparedClock.GetUtcNow();
            DateTimeOffset at = signing.At ?? DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
            (RecordPrepared? prepared, Problem? problem) = PrepareDraftLocked(model, device.Device, draft with { At = at, Id = Ulid.NewUlid(at).Value }, now);
            if (prepared is null)
            {
                return WriteOutcome.Fail(problem!);
            }

            signing.Prepared = prepared;
            return WriteOutcome.Fail(new Problem("prepared", $"The record is prepared for device {device.Device}'s signature under plan {prepared.Plan}; nothing was written.", []));
        }

        if (string.IsNullOrEmpty(signing.Plan) || !preparedRecords.TryGetValue(signing.Plan, out PreparedRecord? plan))
        {
            return Refuse("No prepared record waits under this plan: it was written, lapsed or never made. Prepare it again.");
        }

        if (!string.Equals(plan.Device, device.Device, StringComparison.Ordinal))
        {
            return WriteOutcome.Fail(new Problem("denied", $"The plan was prepared for another device; device {device.Device} cannot sign it.", []));
        }

        // A plan is used once, whatever happens next: a signature is never applied to a second write.
        if (!preparedRecords.TryRemove(new KeyValuePair<string, PreparedRecord>(signing.Plan, plan)))
        {
            return Refuse("No prepared record waits under this plan: it was written, lapsed or never made. Prepare it again.");
        }

        if (plan.ExpiresAt <= PreparedClock.GetUtcNow())
        {
            return Refuse("The prepared record lapsed unsigned. Prepare it again.");
        }

        if (string.IsNullOrEmpty(signing.Signature) || !RecordSigning.Verify(device.PublicKey, RecordSigning.Digest(plan.Hash), signing.Signature))
        {
            return Refuse($"The signature does not verify with device {device.Device}'s key over the prepared record; nothing was written.");
        }

        if (DecisionBasisRefusal(draft) is string basis)
        {
            return Refuse(basis + " Nothing was written.");
        }

        // The request's arguments are authored again with the plan's identity and time: the record written is the one they yield
        // and the one the device signed, or nothing is written.
        RecordDraft signed = draft with { At = plan.Draft.At, Id = plan.Draft.Id, Format = plan.Draft.Format };
        signing.Authorized = plan.Hash;
        return WriteAuthoredLocked(signed, model, [new PresignedSigner(device.Device, plan.Hash, signing.Signature)]);
    }

    /// <summary>
    /// Prepares a draft whose id and time are fixed for a device's signature: the authoring checks every write runs, with no
    /// signature, and the plan kept for that device alone until it lapses. Writes nothing. Caller holds the gate.
    /// </summary>
    private (RecordPrepared? Prepared, Problem? Problem) PrepareDraftLocked(EstateModel model, string device, RecordDraft draft, DateTimeOffset now)
    {
        draft = draft with { Format = draft.Format ?? Math.Min(model.Manifest?.RecordFormat ?? session.Manifest.RecordFormat, RecordFormats.Current) };
        if (IsPolicyWrite(draft) || IsDutyEvidence(draft, model))
        {
            return (null, new Problem("refused", "Policy writes and duty evidence are authored on the runtime's machine, with their expected hashes; they are not prepared for a device's signature.", []));
        }

        // A device cannot observe the estate's repository: a change set it states must be the one the runtime observes now.
        if (DecisionBasisRefusal(draft) is string basis)
        {
            return (null, new Problem("refused", basis, []));
        }

        // The same authoring path as every write, with no signature: the exact record the device will sign, or why there is none.
        (AuthoredRecord? record, string? refusal) = AuthorUnsignedLocked(draft, model);
        if (record is null)
        {
            return (null, new Problem("refused", refusal!, []));
        }

        if (record.Envelope.Type == "decision" && DecisionRecordTimeProblem(EstateModel.BodyString(record.Envelope, "decidedAt"),
            record.Envelope.At, record.RelativePath) is { } timeProblem)
        {
            return (null, timeProblem);
        }

        if (record.Envelope.Type == "acceptance" && AcceptanceType.BasisOutcomeFinding(model, record.Envelope, record.RelativePath) is { } outcome)
        {
            return (null, new Problem("rejected", outcome.Message, [new FindingDto("error", outcome.Code, outcome.Location, outcome.Message)]));
        }

        if (record.Envelope.Type == "result" && ResultType.OutputSchemeFinding(record.Envelope, record.RelativePath) is { } scheme)
        {
            return (null, new Problem("rejected", scheme.Message, [new FindingDto("error", scheme.Code, scheme.Location, scheme.Message)]));
        }

        if (RecordAuthorship.Unresolved(model, record.Envelope) is { } author)
        {
            return (null, new Problem("rejected", author.Message, [new FindingDto("error", author.Code, author.Location, author.Message)]));
        }

        draft = draft with { At = record.Envelope.AtTime };
        foreach ((string id, PreparedRecord _) in preparedRecords.Where(p => p.Value.ExpiresAt <= now).ToArray())
        {
            preparedRecords.TryRemove(id, out _);
        }

        if (preparedRecords.Count >= PreparedLimit)
        {
            return (null, new Problem("unavailable", "Too many records await their devices' signatures; sign or let some lapse first.", []));
        }

        // From 0.9.2 one device holds a bounded share, so a device preparing without signing never keeps another's plans out.
        if (preparedRecords.Values.Count(p => p.Device == device) >= PreparedPerDevice)
        {
            return (null, new Problem("unavailable", $"Device {device} has {PreparedPerDevice} records awaiting its signature; sign or let some lapse first.", []));
        }

        string plan = Ulid.NewUlid(now).Value;
        DateTimeOffset expires = now + PreparedLifetime;
        preparedRecords[plan] = new PreparedRecord(device, draft, record.Envelope.ComputedHash, expires);
        using JsonDocument unsigned = JsonDocument.Parse(record.Bytes);
        return (new RecordPrepared(plan, record.Envelope.Id, record.Envelope.At, record.Envelope.ComputedHash, RecordParser.FormatTimestamp(expires), unsigned.RootElement.Clone()), null);
    }

    /// <summary>
    /// Why the records a batch would store may not be stored under device signing, or null: only the one record whose signature
    /// verified for the requesting device's plan, signed by that device alone. Caller holds the gate.
    /// </summary>
    private Problem? DeviceSignedStoreRefusal(IReadOnlyList<AuthoredRecord> records)
    {
        if (requestDevice.Value is not { Closed: false, Signing: { } signing } device)
        {
            return null;
        }

        bool allowed = records.Count == 1 && signing.Authorized is string hash && records[0].Envelope.ComputedHash == hash
            && records[0].Envelope.Signatures.Count == 1 && records[0].Envelope.Signatures[0].Device == device.Device;
        return allowed
            ? null
            : new Problem("denied", "A device-signed request stores only the one record its device signed over the plan it prepared; nothing was written.", []);
    }

    /// <summary>The findings without the one that says a never-stored candidate record carries no signature.</summary>
    private static FindingList WithoutCandidateSignature(FindingList findings, AuthoredRecord candidate)
    {
        var kept = new FindingList();
        foreach (Finding finding in findings.Where(f => !(f.Code == "signature.required" && f.Location == candidate.RelativePath && candidate.Envelope.Signatures.Count == 0)))
        {
            kept.Add(finding);
        }

        return kept;
    }

    /// <summary>The kind of a principal by its id, from 0.9.2: human, agent or service; null when no current principal has that id.</summary>
    public string? PrincipalKind(string principal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        lock (gate)
        {
            EstateModel model = session.Read();
            return model.CurrentPrincipal(principal) is RecordEnvelope current ? EstateModel.BodyString(current, "kind") : null;
        }
    }
}
