// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The evidence about one self-admission that could be the estate's first authority (CODE-72), for the root operator choosing the one the
/// manifest names: what the admission and its device say, and how the estate reads when it is read as the first authority. The stated
/// time of the admission is a claim, never evidence of order.
/// </summary>
/// <param name="Admission">The root operator's admission of its own device, signed by that device.</param>
/// <param name="Device">The device record the admission names, or null when it names none.</param>
/// <param name="Identity">That device's identity: the id of the first record of its line.</param>
/// <param name="Principal">The principal that device record names.</param>
/// <param name="Named">Whether the manifest's <c>firstAuthority</c> names this admission.</param>
/// <param name="Reads">Whether the estate reads this admission as its first authority now.</param>
/// <param name="WitnessedFirst">
/// Whether, read as the first authority, the estate has adopted history checkpoints and its counting checkpoints witnessed this admission
/// before every other self-admission that could be the first authority.
/// </param>
/// <param name="Revoked">Whether, read as the first authority, the current admission of its device is a revocation.</param>
/// <param name="Admits">How many other devices an admission that counts admits when this one is read as the first authority.</param>
/// <param name="RecordsSigned">How many records a device of its device's identity signed.</param>
/// <param name="SignatureProblem">
/// Why the admission's signature does not verify when it is read as the first authority, as the estate verifies every signature: under
/// its device's key at its stated time, by a device of its author, over the hash its content has; null when it verifies. A
/// self-admission is a candidate when a signature names a device of the line it admits, whether or not that signature verifies.
/// </param>
internal sealed record FirstAuthorityEvidence(RecordEnvelope Admission, RecordEnvelope? Device, string? Identity, string? Principal, bool Named, bool Reads, bool WitnessedFirst, bool Revoked,
    int Admits, int RecordsSigned, string? SignatureProblem = null);

/// <summary>
/// How the estate reads its root of trust now, and the evidence for each self-admission that could be its first authority (CODE-72).
/// </summary>
/// <param name="RootOperator">The root operator the first charter names; null when no charter is read as the first.</param>
/// <param name="Charter">The first charter's id.</param>
/// <param name="Named">What the manifest's <c>firstAuthority</c> names, a candidate or not; null when it names none.</param>
/// <param name="FirstAuthority">The estate's first authority as it reads now; null when there is none.</param>
/// <param name="Adopted">Whether the estate has adopted history checkpoints.</param>
/// <param name="Unsettled">Whether witnessed history and the manifest disagree, so that no record counts until the copy is settled.</param>
/// <param name="RootFindings">The findings about the root of trust: <c>manifest.first-authority</c>, <c>history.root-ambiguous</c> and <c>history.manifest-root</c>.</param>
/// <param name="Candidates">Every self-admission that could be the first authority, by id, with its evidence.</param>
internal sealed record FirstAuthorityReading(string? RootOperator, string? Charter, string? Named, RecordEnvelope? FirstAuthority, bool Adopted, bool Unsettled,
    IReadOnlyList<Finding> RootFindings, IReadOnlyList<FirstAuthorityEvidence> Candidates);

internal sealed partial class EstateTrust
{
    /// <summary>
    /// Reads the estate's root of trust from its record files and objects as the loader judges it, with the evidence for each
    /// self-admission that could be its first authority (<see cref="AuthorityCandidates"/>), for <c>vericairn estate first-authority</c>
    /// (CODE-72). Each candidate is also judged as the first authority, as <see cref="UnderCharter"/> judges it, to tell whether witnessed
    /// history names it first, whether its device stands revoked, how many devices rest on it and whether its own signature verifies as
    /// that judgment verifies it. Nothing here changes how the root of trust is judged: the reading is the loader's, and each judgment as
    /// the first authority is one the estate already makes when the manifest names a candidate. It costs a judgment of the estate for
    /// each candidate, so it serves that command alone. The manifest is read from the bytes given, which the command has checked.
    /// </summary>
    internal static FirstAuthorityReading ReadFirstAuthority(string root, IReadOnlyList<RecordFile> files, byte[] manifestBytes, Func<string, byte[]?>? objects)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(manifestBytes);
        // Judged from a model that holds every record, as the loader judges one before it keeps only the records that count.
        EstateModel model = EstateLoader.Load(root, files, null, manifestBytes, objects, judge: false);
        EstateManifest? manifest = model.Manifest;
        EstateTrust trust = Build(model, manifest, model.ObjectReader, model.FilePaths);
        bool history = model.OfType("history-checkpoint").Any();
        var evidence = new List<FirstAuthorityEvidence>();
        foreach (RecordEnvelope candidate in trust.AuthorityCandidates)
        {
            RecordEnvelope? device = candidate.Subject?.Id is { } subject && model.ById.TryGetValue(subject, out RecordEnvelope? named) && named.Type == "device" ? named : null;
            EstateTrust judged = ReferenceEquals(candidate, trust.FirstAuthority) && !trust.Unsettled
                ? trust
                : new EstateTrust(model, manifest, model.ObjectReader, model.FilePaths, trust.FirstEstate, authorityGiven: true, authority: candidate);
            string? identity = device is null ? null : judged.IdentityOf(device);
            bool revoked = device is not null && judged.AdmissionOf(device.Id) is { } current && EstateModel.BodyString(current, "status") == "revoked";
            int admits = judged.admissionRecords
                .Where(a => a.Id != candidate.Id && EstateModel.BodyString(a, "status") == "admitted" && a.Subject?.Id is { } admitted && model.ById.TryGetValue(admitted, out RecordEnvelope? other) &&
                    other.Type == "device" && judged.IdentityOf(other) != identity && judged.AdmissionCounts(a, Mode.Anchors))
                .Select(a => judged.IdentityOf(model.ById[a.Subject!.Id])).Distinct(StringComparer.Ordinal).Count();
            int signed = identity is null ? 0 : model.Records.Count(r => r.Signatures.Any(s => model.ById.TryGetValue(s.Device, out RecordEnvelope? signer) && signer.Type == "device" &&
                judged.IdentityOf(signer) == identity));
            // Its own signatures, verified as the judgment with it as the first authority verifies every signature.
            string? problem = candidate.DeclaredHash != candidate.ComputedHash
                ? "its content does not match the hash its envelope declares"
                : judged.Verification(candidate, Mode.Anchors).FirstOrDefault(f => f.Severity == Severity.Error) is { } failed ? failed.Code + ": " + failed.Message : null;
            evidence.Add(new FirstAuthorityEvidence(candidate, device, identity, device is null ? null : EstateModel.BodyString(device, "principal"), manifest?.FirstAuthority == candidate.Id,
                ReferenceEquals(candidate, trust.FirstAuthority), history && judged.WitnessedFirst(candidate, trust.AuthorityCandidates), revoked, admits, signed, problem));
        }

        return new FirstAuthorityReading(trust.RootOperator, trust.FirstEstate?.Id, manifest?.FirstAuthority, trust.FirstAuthority, trust.Adopted, trust.Unsettled,
            [.. trust.RootFindings], evidence);
    }
}
