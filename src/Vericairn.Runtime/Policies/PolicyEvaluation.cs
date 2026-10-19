// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Policies;

internal sealed record PolicyTarget(string Kind, string Identity, RecordReference? Record = null, string? Repository = null, string? Path = null, string? Hash = null);

internal sealed record PolicyFinding(
    string PolicyIdentity, RecordReference Policy, string PolicyKey, string Rule, string Kind, int Version,
    PolicyTarget Target, string Item, string Verdict, string Disposition, string Level, string Owner,
    string Basis, string? AsOf, string Actual, string Required, string NextAction,
    RecordReference? Anchor, string? AnchorAt, ImmutableArray<RecordReference> Evidence, ImmutableArray<RecordReference> Exceptions,
    int SubjectCount = 1, DutyFinding? Duty = null, bool Enforced = true)
{
    public bool ReleaseHold => Enforced && Level == "refuse" && Verdict is "unmet" or "unavailable" && Disposition is not ("excepted" or "ended-unmet");
    public Severity Severity => !Enforced || Verdict == "satisfied" || Disposition is "excepted" or "ended-unmet" || Level == "remind" ? Severity.Info : Severity.Warning;
}

internal sealed record PolicyRuleSummary(RecordReference Policy, string Rule, int Subjects, int Satisfied, int Unmet, int Unavailable, int Excepted);

internal sealed record PolicyRequirement(RecordReference Decision, string Key, string Action, string Policy, string Change,
    string? PriorHash, string? DefinitionHash, string State, RecordReference? Recorded, string Reason);

internal sealed record PolicyEvaluation(string Basis, string? AsOf, PolicyTimeOrigin Origin, bool Complete,
    ImmutableArray<Finding> Diagnostics, ImmutableArray<PolicyFinding> Findings, ImmutableArray<PolicyRuleSummary> Rules)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PolicyRequirement>? Requirements { get; init; }
    public int Errors => Diagnostics.Count(f => f.Severity == Severity.Error);
    public int Warnings => Diagnostics.Count(f => f.Severity == Severity.Warning) + Findings.Count(f => f.Severity == Severity.Warning);
    public int Holds => Findings.Count(f => f.ReleaseHold);
    internal string Canonical() => PolicyBasis.Canonical(this);
}
