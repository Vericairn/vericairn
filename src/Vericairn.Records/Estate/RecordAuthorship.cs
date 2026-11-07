// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// Whether a record's envelope author names a principal the estate knew at the record's time. Without a signature, <c>by</c> is the
/// author's claim; this rule makes the claim name a principal, never a signer. From 0.9.2 the write boundary refuses a new record
/// whose author does not resolve; a record already in the estate keeps its reading, and validation reports it as information.
/// </summary>
internal static class RecordAuthorship
{
    /// <summary>The finding's code.</summary>
    internal const string Code = "record.author";

    /// <summary>The information that a record's author does not resolve to a principal known at its time; null when it does.</summary>
    internal static Finding? Unresolved(EstateModel model, RecordEnvelope record)
    {
        // A principal record that names itself is the principal; principal.self-authored judges whether it may name itself.
        if (record.Type == "principal" && record.By == record.Id) return null;
        string reason;
        if (!model.ById.TryGetValue(record.By, out RecordEnvelope? named))
        {
            reason = $"Author {record.By} names no record in this estate, so no principal.";
        }
        else if (named.Type != "principal")
        {
            reason = $"Author {record.By} names a record of type {named.Type}, not a principal.";
        }
        else if (string.CompareOrdinal(named.At, record.At) > 0)
        {
            reason = $"Author {record.By} is a principal recorded at {named.At}, after this record's time {record.At}.";
        }
        else
        {
            return null;
        }

        return new(Severity.Info, Code, record.ExpectedPath + "#by", reason
            + " From 0.9.2 a new record's author must be a principal known at its time; a record already in the estate keeps its reading.");
    }
}
