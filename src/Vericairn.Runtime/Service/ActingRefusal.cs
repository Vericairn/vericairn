// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>
/// From 0.9.4 (FIX-159): the one refusal every acting surface calls before it acts, whether or not it records first, read from the estate
/// as loaded under the caller's gate: release submission, run admission and launch, goal step, and every path on which a device signs a
/// record on this machine or prepares one for a device elsewhere to sign. While the estate reports an error it names the finding, and
/// nothing is signed, recorded, prepared, started or dispatched.
/// </summary>
/// <remarks>
/// An act with an effect outside the estate's records (a release's dispatch, a run's workspace, setup and launch, a goal step's assignment
/// and dispatch) is refused while the estate reports any error. A record a device signs is refused before it is signed while the estate
/// leaves records unread (<c>record.duplicate-id</c>, <c>record.duplicate-unsettled</c>), which no record can clear, since only removing
/// files does; while it reports another error, the write boundary judges the estate with the signed record, as before, and stores it only
/// when it clears every error, as a reconciliation or an acknowledgement does, so that an estate can still be repaired by its records.
/// Stopping a run, which contains an effect, is never refused.
/// </remarks>
internal static class ActingRefusal
{
    /// <summary>The errors of records left unread, which only removing files clears.</summary>
    internal static readonly string[] UnreadCodes = ["record.duplicate-id", "record.duplicate-unsettled"];

    /// <summary>The refusal of an act with an effect outside the records while the estate reports any error; null while it reports none.</summary>
    /// <param name="model">The estate as loaded under the caller's gate.</param>
    /// <param name="act">What would have been done, for the message, as "submitting the release".</param>
    internal static Problem? BeforeActing(EstateModel model, string act) => Refusal(model, act, record: false);

    /// <summary>The refusal of a record before a device signs it, or before it is prepared for one, while the estate leaves records unread.</summary>
    /// <param name="model">The estate as loaded under the caller's gate.</param>
    /// <param name="act">What would have been done, for the message, as "signing the decision record".</param>
    internal static Problem? BeforeSigning(EstateModel model, string act) => Refusal(model, act, record: true);

    private static Problem? Refusal(EstateModel model, string act, bool record)
    {
        ArgumentNullException.ThrowIfNull(model);
        List<Finding> errors = [.. model.Findings.Where(f => f.Severity == Severity.Error && (!record || UnreadCodes.Contains(f.Code, StringComparer.Ordinal)))];
        if (errors.Count == 0)
        {
            return null;
        }

        Finding first = errors[0];
        string count = errors.Count == 1 ? "an error" : errors.Count.ToString(CultureInfo.InvariantCulture) + " errors";
        string because = record
            ? "records it leaves unread can be read in no record's place, and only removing all but one record carrying each identifier reads them again"
            : "nothing acts on an estate that reports an error; vericairn estate check lists them";
        return new Problem("refused", $"Refused before {act}: the estate reports {count}, {first.Code} at {first.Location}: {first.Message} Nothing was signed, recorded or started: {because}.",
            [.. errors.Select(f => new FindingDto("error", f.Code, f.Location, f.Message))]);
    }
}
