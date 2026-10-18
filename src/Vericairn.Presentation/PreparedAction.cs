// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Presentation;

/// <summary>What a surface finds when it returns to a prepared action: the cohesion contract's three states.</summary>
public enum PreparedState
{
    /// <summary>The subject is the current revision and nothing new is on record about it; the preparation can be sent as it is.</summary>
    Reusable,

    /// <summary>The subject is still current but new records about it appeared since the preparation; the draft is kept and must be reconsidered.</summary>
    NeedsReview,

    /// <summary>The subject was superseded or is gone; the draft is kept for reference and cannot be sent.</summary>
    Withdrawn,
}

/// <summary>A consequential request pinned to the exact subject it was prepared against, with the facts about that subject at the time.</summary>
public sealed record PreparedAction(string Kind, string SubjectType, string SubjectId, string SubjectHash, string Facts)
{
    /// <summary>The subject as the runtime wants it: type:id:hash.</summary>
    public string Subject => $"{SubjectType}:{SubjectId}:{SubjectHash}";

    /// <summary>Compares the pinned subject and facts with what the runtime holds now.</summary>
    public static PreparedState Check(PreparedAction prepared, RecordResponse? current, string facts)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (current is null || !current.Current || current.Hash != prepared.SubjectHash)
        {
            return PreparedState.Withdrawn;
        }

        return facts == prepared.Facts ? PreparedState.Reusable : PreparedState.NeedsReview;
    }

    /// <summary>The label of a state.</summary>
    public static string Label(PreparedState state) => state switch
    {
        PreparedState.Reusable => "reusable: the subject is unchanged and nothing new is on record about it",
        PreparedState.NeedsReview => "needs review: new records about the subject appeared since this was prepared; your draft is kept and nothing was sent",
        _ => "withdrawn: the subject was superseded or is gone; your draft is kept and cannot be sent",
    };
}

/// <summary>The facts about a subject that a prepared action is compared against: the current records that name it as their subject.</summary>
public static class SubjectFacts
{
    private static readonly string[] AboutTypes = ["review", "acceptance", "amendment", "work-status", "verification", "retirement", "attempt", "result"];

    /// <summary>A fingerprint of the records about a subject; equal strings mean no new fact.</summary>
    public static string About(IEstateApi api, string subjectId)
    {
        ArgumentNullException.ThrowIfNull(api);
        var ids = new List<string>();
        foreach (string type in AboutTypes)
        {
            foreach (RecordResponse record in api.Records(type))
            {
                if (record.Envelope.TryGetProperty("subject", out JsonElement subject) && subject.ValueKind == JsonValueKind.Object && subject.TryGetProperty("id", out JsonElement id) && id.GetString() == subjectId)
                {
                    ids.Add(record.Id);
                }
            }
        }

        ids.Sort(StringComparer.Ordinal);
        return string.Join(",", ids);
    }
    /// <summary>A cancellable fingerprint read for a prepared action, with the same ordering as the synchronous form.</summary>
    public static async Task<string> AboutAsync(IEstateApi api, string subjectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        var ids = new List<string>();
        foreach (string type in AboutTypes)
        {
            foreach (RecordResponse record in await api.RecordsAsync(type, cancellationToken).ConfigureAwait(false))
            {
                if (record.Envelope.TryGetProperty("subject", out JsonElement subject) && subject.ValueKind == JsonValueKind.Object && subject.TryGetProperty("id", out JsonElement id) && id.GetString() == subjectId)
                {
                    ids.Add(record.Id);
                }
            }
        }
        ids.Sort(StringComparer.Ordinal);
        return string.Join(",", ids);
    }

}
