// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Serialization;
using Vericairn.Protocol;

namespace Vericairn.Remote;

/// <summary>Complete material observed for one decision. The original remains with its draft when the current material changes.</summary>
public sealed record RemoteDecisionReview(string Root, RecordResponse Subject, string AsOf, IReadOnlyList<RecordResponse> Material,
    IReadOnlyList<RemoteDecisionEvidence> Evidence, IReadOnlyList<string> Basis, IReadOnlyList<string> Reasons)
{
    /// <summary>Whether all material can be inspected here. The runtime still judges every prepared write.</summary>
    [JsonIgnore]
    public bool Eligible => Reasons.Count == 0;

    /// <summary>
    /// For an ask, the estate's head commit the runtime supplied with no design document changed since it: the answer's change set
    /// uses it as base and head. Null when the runtime supplied none or documents changed, which leads to the desktop.
    /// </summary>
    public string? Head { get; init; }
}

/// <summary>Verified text evidence that the phone can display completely.</summary>
public sealed record RemoteDecisionEvidence(string Hash, string Text);

/// <summary>A local draft or retained original submission, scoped by its store to the connection, runtime, principal and device.</summary>
public sealed record RemoteDecisionDraft(string Key, RemoteDecisionReview Original, RemoteDecisionReview Review, string Statement,
    string? Stake = null, string State = "draft", string Message = "Draft; nothing signed or sent.",
    NewRecordRequest? Request = null, RecordPrepared? Prepared = null, RecordWritten? Receipt = null, Problem? Problem = null)
{
    /// <summary>An attempted original is never submitted again, including after a refusal or lost response.</summary>
    [JsonIgnore]
    public bool Attempted => Prepared is not null;

    /// <summary>The target and recorded local state for the retained-drafts list.</summary>
    [JsonIgnore]
    public string Label => Review.Subject.Type + " " + Review.Subject.Id + " · " + State;
}
