// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Serialization;
using Vericairn.Protocol;

namespace Vericairn.Remote;

/// <summary>The action and the exact state reviewed before sending it.</summary>
public sealed record RemoteIntervention(string Operation, string Target, string Revision, string KnownState, string Effect)
{
    /// <summary>The earlier stop a reviewed containment will tighten, when there is one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedStopReason { get; init; }

    /// <summary>The exact goal or run operation the device will be asked to sign for.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AttentionReference? ReviewedSubject { get; init; }

    /// <summary>The target, review basis and expected effect.</summary>
    [JsonIgnore]
    public string Review => $"{Operation}\nTarget {Target}\n{KnownState}\n{Effect}";
}

/// <summary>The original submission, kept before sending, and the last observed result of that submission.</summary>
public sealed record RemoteInterventionReceipt(RemoteIntervention Request, string Id, string State, string Observation, string? Record = null)
{
    /// <summary>The exact record hash kept before a signed submission when known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordHash { get; init; }

    internal (string Target, string Operation, string Id) Key => (Request.Target, Request.Operation, Id);

    internal bool Pending => State is "pending" or "uncertain" or "requested" or "transmitted" or "acknowledged" or "escalated";

    internal bool Blocks(RemoteIntervention review) =>
        Pending && Request.Target == review.Target && Request.Operation == review.Operation;

    /// <summary>How this original request is named in the runtime's stop reason or pause record.</summary>
    [JsonIgnore]
    public string Marker => $"{Request.Operation} requested in Remote ({Id}).";
    /// <summary>A row in the list of original requests.</summary>
    [JsonIgnore]
    public string Label => $"{Request.Operation} — {Request.Target} — {State}";
}

/// <summary>A run that can be selected for an intervention from its assignment detail.</summary>
public sealed record RemoteInterventionTarget(string Run, string Status)
{
    /// <summary>The specific run and last observed operation state.</summary>
    public string Label => $"{Run} — {Status}";
}
