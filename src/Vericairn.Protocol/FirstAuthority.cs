// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>
/// List the self-admissions that could be the estate's first authority, with the evidence for each, or name one of them in the
/// manifest's <c>firstAuthority</c> (0.9.3; CODE-72). Without a device or an admission it lists and writes nothing. With one and
/// <paramref name="Check"/> it shows the change and its plan hash, writing nothing; without <paramref name="Check"/> it writes the change,
/// refused unless <paramref name="By"/> is the charter's root operator and <paramref name="ExpectPlan"/> is the plan hash the check printed.
/// </summary>
/// <param name="By">The root operator the first charter names; required to write.</param>
/// <param name="Device">A device of the root operator's whose self-admission to name: its id, or the id of any revision of its line.</param>
/// <param name="Admission">The self-admission to name, by id; required when the device admitted itself more than once.</param>
/// <param name="Check">Show the change and its plan hash, writing nothing.</param>
/// <param name="ExpectPlan">The plan hash the check printed: the change is refused unless the estate still reads as that check read it.</param>
public sealed record FirstAuthorityRequest(string? By = null, string? Device = null, string? Admission = null, bool Check = false, string? ExpectPlan = null);

/// <summary>
/// One self-admission that could be the estate's first authority, with its evidence. Its stated time is a claim, never evidence of order:
/// a first authority is never chosen by date or id.
/// </summary>
/// <param name="Admission">The admission's id.</param>
/// <param name="Hash">The admission's content hash.</param>
/// <param name="At">The time the admission states, a claim.</param>
/// <param name="Path">The admission's repository-relative path.</param>
/// <param name="Device">The device record the admission names.</param>
/// <param name="Name">The device's name.</param>
/// <param name="Platform">The device's platform.</param>
/// <param name="Custody">The device's custody.</param>
/// <param name="Presence">Whether the device asks for its owner's presence at each signature: required or none.</param>
/// <param name="Principal">The principal the device record names.</param>
/// <param name="Nameable">Whether the command may name it: a device of the root operator's, which no witnessed history or settled root rules out.</param>
/// <param name="NotNameable">Why it may not be named; null when it may.</param>
/// <param name="Named">Whether the manifest's firstAuthority names it.</param>
/// <param name="Reads">Whether the estate reads it as its first authority now.</param>
/// <param name="WitnessedFirst">Whether, read as the first authority, the estate's counting history checkpoints witnessed it before every other candidate.</param>
/// <param name="Revoked">Whether, read as the first authority, its device's current admission is a revocation. A revoked device's self-admission stays nameable.</param>
/// <param name="Admits">How many other devices an admission that counts admits when it is read as the first authority.</param>
/// <param name="RecordsSigned">How many records its device signed.</param>
public sealed record FirstAuthorityCandidate(string Admission, string Hash, string At, string Path, string? Device, string? Name, string? Platform, string? Custody, string? Presence,
    string? Principal, bool Nameable, string? NotNameable, bool Named, bool Reads, bool WitnessedFirst, bool Revoked, int Admits, int RecordsSigned);

/// <summary>The change to the manifest that naming the first authority makes.</summary>
/// <param name="Manifest">The manifest's repository-relative path.</param>
/// <param name="Before">What its firstAuthority names now, a candidate or not; null when it names none.</param>
/// <param name="After">The admission it will name.</param>
public sealed record FirstAuthorityChange(string Manifest, string? Before, string After);

/// <summary>How the estate reads its first authority, the candidates with their evidence, and the change shown or made.</summary>
/// <param name="RootOperator">The root operator the first charter names; null when the copy reads no first charter.</param>
/// <param name="Named">What the manifest's firstAuthority names, a candidate or not; null when it names none.</param>
/// <param name="FirstAuthority">The admission the estate reads as its first authority; null when there is none.</param>
/// <param name="Adopted">Whether the estate has adopted history checkpoints.</param>
/// <param name="Unsettled">Whether witnessed history and the manifest disagree, so that no record counts until the copy is settled.</param>
/// <param name="Findings">The findings about the root of trust: manifest.first-authority, history.root-ambiguous and history.manifest-root.</param>
/// <param name="Candidates">Every self-admission that could be the first authority, by id.</param>
/// <param name="Change">The change shown or made; null for a listing.</param>
/// <param name="PlanHash">The hash of the change and of what it was judged from, which a write names in expectPlan; null for a listing.</param>
/// <param name="Applied">Whether the manifest was written.</param>
/// <param name="Remaining">
/// After writing, the errors about the root of trust the estate still reports, such as each other self-admission's
/// signature.self-admission, which the operator resolves by removing those record files; nothing here removes them.
/// </param>
public sealed record FirstAuthorityResponse(string? RootOperator, string? Named, string? FirstAuthority, bool Adopted, bool Unsettled, IReadOnlyList<FindingDto> Findings,
    IReadOnlyList<FirstAuthorityCandidate> Candidates, FirstAuthorityChange? Change, string? PlanHash, bool Applied, IReadOnlyList<FindingDto> Remaining);

public partial interface IEstateApi
{
    /// <summary>
    /// List the self-admissions that could be the estate's first authority with the evidence for each, or show or write the change
    /// naming one of the root operator's in the manifest (0.9.3). A machine-local act of the root operator at the runtime's local socket;
    /// refused for every device-signed request.
    /// </summary>
    (FirstAuthorityResponse? FirstAuthority, Problem? Problem) FirstAuthority(FirstAuthorityRequest request) =>
        (null, new Problem("unsupported", "This API implementation does not name the estate's first authority.", []));

    /// <summary>The cancellable first-authority request; a write unless it lists or checks.</summary>
    Task<(FirstAuthorityResponse? FirstAuthority, Problem? Problem)> FirstAuthorityAsync(FirstAuthorityRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(FirstAuthority), () => FirstAuthority(request), request is { Check: false } && (request.Device is not null || request.Admission is not null),
            this as IClientOperationLifetime, cancellationToken);
}
