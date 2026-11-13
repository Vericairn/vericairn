// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Naming the estate's first authority in its manifest (0.9.3; CODE-72): <c>vericairn estate first-authority</c>, which replaces the hand
/// edit of <c>firstAuthority</c> that an estate holding several self-admissions of the root operator's device needed. It lists the
/// candidates with their evidence and names one of the root operator's own devices, showing the change and its plan hash before it
/// writes. It changes nothing in how the root of trust is judged: it writes the field the estate already reads, refuses wherever that
/// would move a first authority that works or where witnessed history speaks to the root, and leaves every other self-admission, still
/// an error, for the operator to remove.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>The errors about the root of trust a write reports as remaining.</summary>
    private static readonly string[] RootOfTrustCodes = ["manifest.first-authority", "history.root-ambiguous", "history.manifest-root", "signature.self-admission"];

    /// <inheritdoc />
    public (FirstAuthorityResponse? FirstAuthority, Problem? Problem) FirstAuthority(FirstAuthorityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool names = request.Device is not null || request.Admission is not null;
        lock (gate)
        {
            // The manifest is no record and nothing signs it, so the act is the root operator's at the runtime's local socket, where the
            // operating system's identity of the caller stands for it, as it stood for the hand edit this replaces.
            if (requestDevice.Value is { Closed: false })
                return Refused("Naming the estate's first authority writes its manifest on the runtime's own machine and is the root operator's act over the local socket there; a device-signed request neither lists the candidates nor names one.");
            if (request.Device is not null && request.Admission is not null)
                return Refused("Name the first authority by --device or by --admission, not both.");
            if (!names && request.ExpectPlan is not null)
                return Refused("--expect-plan names a change: pass it with the --device or --admission its check was shown for.");
            // The root operator and the first authority are read from the manifest, so one that does not read cleanly lists and names nothing.
            if (ManifestRefusal(ReadManifest(), out byte[] manifest) is { } unread) return Refused(unread);

            FirstAuthorityReading reading = EstateTrust.ReadFirstAuthority(session.Root, session.Files(), manifest, session.ObjectReader);
            if (!names) return (Response(reading, null, null, applied: false, []), null);

            EstateModel model = session.Load();
            (FirstAuthorityEvidence? target, string? refusal) = Target(model, reading, request);
            if (target is null) return Refused(refusal!);
            if (Refusal(reading, target) is { } settled) return Refused(settled);

            var change = new FirstAuthorityChange(EstateLayout.ManifestPath, reading.Named, target.Admission.Id);
            string plan = PlanHash(manifest, reading, target);
            if (request.Check) return (Response(reading, change, plan, applied: false, []), null);

            if (request.By is null || !Ulid.IsValid(request.By))
                return Refused($"Naming the first authority is the root operator's act: pass --by {reading.RootOperator ?? "<root operator id>"}.");
            if (request.By != reading.RootOperator)
                return Refused($"Naming the estate's first authority is the root operator's act, and the first charter names {reading.RootOperator} as root operator, not {request.By}. Nothing was changed.");
            if (request.ExpectPlan is null)
                return Refused("Read the change with vericairn estate first-authority --check and the same --device or --admission, then pass the plan hash it prints with --expect-plan, so that what is written is what you read. Nothing was changed.");
            if (!SamePlan(request.ExpectPlan, plan))
                return Refused("The estate no longer reads as the check you named: its manifest, its root operator or any candidate's evidence the check showed changed since, or the hash is another change's. Read vericairn estate first-authority --check again. Nothing was changed.");
            if (session.IsReadOnly) return Refused("This copy is opened read-only; nothing was changed.");

            return UnderWriteLock(() =>
            {
                // Judged again under the working tree's write lock, from the files and the manifest as they are now.
                if (ManifestRefusal(ReadManifest(), out byte[] current) is { } changed) return Refused(changed);
                FirstAuthorityReading now = EstateTrust.ReadFirstAuthority(session.Root, session.Files(), current, session.ObjectReader);
                (FirstAuthorityEvidence? again, string? refused) = Target(session.Load(), now, request);
                if (again is null) return Refused(refused!);
                if (Refusal(now, again) is { } stopped) return Refused(stopped);
                if (!SamePlan(request.ExpectPlan, PlanHash(current, now, again)))
                    return Refused("Another writer changed the estate since the check you named; nothing was changed. Read vericairn estate first-authority --check again.");
                byte[] written;
                try
                {
                    written = EstateManifest.WithFirstAuthority(current, again.Admission.Id);
                    WriteManifest(written);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
                {
                    return Refused("The manifest could not be written: " + exception.Message);
                }

                // What remains for the operator: every error about the root of trust, and every error the estate now reports on the admission named.
                FirstAuthorityReading after = EstateTrust.ReadFirstAuthority(session.Root, session.Files(), written, session.ObjectReader);
                string path = again.Admission.ExpectedPath;
                List<FindingDto> remaining = [.. session.Load().Findings.Sorted()
                    .Where(f => f.Severity == Severity.Error && (RootOfTrustCodes.Contains(f.Code) || f.Location.StartsWith(path, StringComparison.Ordinal)))
                    .Select(f => new FindingDto("error", f.Code, f.Location, f.Message))];
                return (Response(after, change, plan, applied: true, remaining), null);
            }, problem => ((FirstAuthorityResponse?)null, (Problem?)problem));
        }

        static (FirstAuthorityResponse?, Problem?) Refused(string message) => (null, new Problem("refused", message, []));
    }

    /// <summary>
    /// Why the manifest's bytes cannot be read from, or null with the bytes: missing, unreadable, or not parsing without findings, such
    /// as a key it does not know, a key twice or a form that is not canonical. The estate's first charter, and so its root operator, are
    /// read from the manifest, and a model read without one falls back to the charter with the lowest id, so nothing is listed or named
    /// from a manifest that does not read cleanly.
    /// </summary>
    private static string? ManifestRefusal(byte[]? bytes, out byte[] manifest)
    {
        manifest = bytes ?? [];
        if (bytes is null) return "This estate's manifest (estate/manifest.json) cannot be read. Nothing was listed or changed.";
        var findings = new FindingList();
        EstateManifest? parsed = EstateManifest.Parse(bytes, findings);
        if (parsed is not null && findings.Count == 0) return null;
        string why = findings.Count == 0 ? "it does not parse" : string.Join("; ", findings.Select(f => f.Code + ": " + f.Message));
        return $"This estate's manifest (estate/manifest.json) does not read cleanly ({why}). The estate's root operator and first authority are read from it, so this command lists and names nothing until it does: restore it, then run vericairn estate check. Nothing was listed or changed.";
    }

    /// <summary>The manifest's bytes, or null when it cannot be read.</summary>
    private byte[]? ReadManifest()
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(session.Root, EstateLayout.ManifestPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Replaces the manifest through a file written and flushed beside it, as naming the first authority at its self-admission does.</summary>
    private void WriteManifest(byte[] bytes)
    {
        string path = Path.Combine(session.Root, EstateLayout.ManifestPath);
        string temporary = path + ".naming";
        File.Delete(temporary);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// The candidate a request names, or why it names none: an admission that is no self-admission of the root operator's device that
    /// could be the first authority, a device with no such admission or with more than one, or a copy that reads no first charter or
    /// reads unsettled, where naming a first authority cannot settle which history belongs.
    /// </summary>
    private static (FirstAuthorityEvidence? Target, string? Refusal) Target(EstateModel model, FirstAuthorityReading reading, FirstAuthorityRequest request)
    {
        if (reading.RootOperator is null)
            return (null, "This copy reads no first charter, so it has no root operator whose device could be named. " + (reading.Unsettled ? Unsettled : "Its manifest names no charter that is here.") + " Nothing was changed.");
        if (reading.Unsettled) return (null, Unsettled + " Nothing was changed.");
        string candidates = reading.Candidates.Count == 0 ? "the estate holds none" : "the candidates are " + string.Join(", ", reading.Candidates.Select(c => c.Admission.Id));
        if (request.Admission is { } id)
        {
            if (reading.Candidates.FirstOrDefault(c => c.Admission.Id == id) is { } named) return (named, null);
            if (!model.ById.TryGetValue(id, out RecordEnvelope? record)) return (null, $"No record {id} is in this copy; {candidates}. Nothing was changed.");
            string why = record.Type != "admission" ? $"{id} is a {record.Type} record, not an admission"
                : record.By != reading.RootOperator ? $"admission {id} is by {record.By}, not by the root operator {reading.RootOperator}"
                : EstateModel.BodyString(record, "status") != "admitted" ? $"admission {id} records a revocation"
                : record.Subject?.Id is { } subject && model.Trust.Recovers(subject) ? $"admission {id} admits a recovery device, which is never the first authority"
                : $"admission {id} is not signed by the device it admits";
            return (null, $"The first authority is the root operator's admission of its own device, signed by that device, and {why}; {candidates}. Nothing was changed.");
        }

        string device = request.Device!;
        if (!model.ById.TryGetValue(device, out RecordEnvelope? deviceRecord) || deviceRecord.Type != "device")
            return (null, $"No device record {device} is in this copy; {candidates}. Nothing was changed.");
        string identity = model.Trust.IdentityOf(deviceRecord);
        List<FirstAuthorityEvidence> matches = [.. reading.Candidates.Where(c => c.Identity == identity)];
        if (matches.Count == 1) return (matches[0], null);
        if (matches.Count > 1)
            return (null, $"Device {identity} admitted itself more than once ({string.Join(", ", matches.Select(c => c.Admission.Id))}); name one with --admission. Nothing was changed.");
        string principal = EstateModel.BodyString(deviceRecord, "principal") ?? "no principal";
        string reason = principal != reading.RootOperator ? $"device {identity} is a device of {principal}, not of the root operator {reading.RootOperator}"
            : model.Trust.Recovers(deviceRecord.Id) ? $"device {identity} is a recovery device, which is never the first authority"
            : $"device {identity} has no admission of itself by the root operator, signed by itself";
        return (null, $"The first authority is the root operator's admission of its own device, signed by that device, and {reason}; {candidates}. Nothing was changed.");
    }

    private const string Unsettled = "This copy reads unsettled (history.root-ambiguous): its counting history checkpoints and its manifest disagree on the estate's root, and naming a first authority cannot tell which history belongs. Compare the copy with a head known elsewhere (vericairn estate check --expect <checkpoint>) and remove what does not belong.";

    /// <summary>
    /// Why a candidate may not be named, or null when it may: it admits a device of another principal than the root operator; the
    /// estate's first authority works already, named by the manifest or settled by adopted history, which the command never moves; or
    /// witnessed history speaks to the root, some candidate being witnessed first by counting history checkpoints when it is read as the
    /// first authority. The manifest decides only where history is silent, and history that witnesses a candidate under its own reading
    /// can be history added to the copy with that candidate, so the command names none, the witnessed one included, until the copy has been
    /// compared with a head known elsewhere and what does not belong removed. A revoked device stays nameable: the identity design reads
    /// the root as the first self-admitted device, revoked or not.
    /// </summary>
    private static string? Refusal(FirstAuthorityReading reading, FirstAuthorityEvidence target)
    {
        string id = target.Admission.Id;
        if (target.Principal != reading.RootOperator)
            return $"Admission {id} admits device {target.Identity} of {target.Principal ?? "no principal"}, and the first authority is one of the root operator's own devices ({reading.RootOperator}). Nothing was changed.";
        // A signature that names the device without verifying under its key is one anyone able to add a file can write.
        if (target.SignatureProblem is { } problem)
            return $"Admission {id}'s signature does not verify, read as the first authority, as the estate verifies every signature ({problem}): naming it would make a self-admission its device never signed the estate's first authority. Remove its record file from the copy. Nothing was changed.";
        if (reading.FirstAuthority is { } working && (reading.Adopted || reading.Named == working.Id))
        {
            string settledBy = reading.Named == working.Id ? "the manifest names it" + (reading.Adopted ? " and the estate's history checkpoints confirm it" : string.Empty) : "the estate's adopted history settles it";
            return working.Id == id
                ? $"Admission {id} is the estate's first authority already: {settledBy}. Nothing was changed."
                : $"The estate's first authority is admission {working.Id}, and it works: {settledBy}. This command never moves a working first authority. Nothing was changed.";
        }

        List<FirstAuthorityEvidence> witnessed = [.. reading.Candidates.Where(c => c.WitnessedFirst)];
        if (witnessed.Count > 0)
            return $"Witnessed history speaks to the first authority: read as the first authority, {(witnessed.Count == 1 ? "admission " + witnessed[0].Admission.Id + " is" : "admissions " + string.Join(", ", witnessed.Select(c => c.Admission.Id)) + " are each")} witnessed before every other by history checkpoints that count. The manifest decides only where history is silent, and history that witnesses a self-admission under its own reading can have been added to this copy with it, so this command names none, that one included. Compare the copy with a head known elsewhere (vericairn estate check --expect <checkpoint>) and remove the records that do not belong. Nothing was changed.";
        return null;
    }

    /// <summary>
    /// The hash of a change and of everything it was judged from and shows: the manifest's bytes, the target, and the response the check
    /// prints for it, with the root operator, what the manifest names and the estate reads, the findings and every candidate's evidence,
    /// whether it is nameable and whether its signature verifies.
    /// </summary>
    private static string PlanHash(byte[] manifest, FirstAuthorityReading reading, FirstAuthorityEvidence target)
    {
        var change = new FirstAuthorityChange(EstateLayout.ManifestPath, reading.Named, target.Admission.Id);
        var text = new StringBuilder();
        text.Append("vericairn.first-authority-plan/2\n");
        text.Append("manifest ").Append(ContentHash.Sha256(manifest)).Append('\n');
        text.Append("name ").Append(target.Admission.Id).Append(' ').Append(target.Admission.ComputedHash).Append('\n');
        text.Append(JsonSerializer.Serialize(Response(reading, change, null, applied: false, []), ProtocolJson.Options));
        return ContentHash.Sha256(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>Whether a plan hash named, with or without its prefix, is the one computed.</summary>
    private static bool SamePlan(string named, string plan)
    {
        string value = named.Trim();
        if (!value.StartsWith(ContentHash.Prefix, StringComparison.Ordinal)) value = ContentHash.Prefix + value;
        return string.Equals(value, plan, StringComparison.OrdinalIgnoreCase);
    }

    private static FirstAuthorityResponse Response(FirstAuthorityReading reading, FirstAuthorityChange? change, string? plan, bool applied, IReadOnlyList<FindingDto> remaining) =>
        new(reading.RootOperator, reading.Named, reading.FirstAuthority?.Id, reading.Adopted, reading.Unsettled,
            [.. reading.RootFindings.Select(f => new FindingDto(f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message))],
            [.. reading.Candidates.Select(c => Candidate(reading, c))], change, plan, applied, remaining);

    private static FirstAuthorityCandidate Candidate(FirstAuthorityReading reading, FirstAuthorityEvidence c)
    {
        string? refusal = reading.Unsettled ? Unsettled : Refusal(reading, c);
        string? Field(string name) => c.Device is null ? null : EstateModel.BodyString(c.Device, name);
        return new FirstAuthorityCandidate(c.Admission.Id, c.Admission.ComputedHash, c.Admission.At, c.Admission.ExpectedPath, c.Device?.Id, Field("name"), Field("platform"),
            Field("custody"), Field("presence"), c.Principal, refusal is null, refusal?.Replace(" Nothing was changed.", string.Empty, StringComparison.Ordinal), c.Named, c.Reads,
            c.WitnessedFirst, c.Revoked, c.Admits, c.RecordsSigned, c.SignatureProblem is null, c.SignatureProblem);
    }
}
