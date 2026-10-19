// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>An exact record revision used as policy evidence.</summary>
public sealed record PolicyRecordReference(string Type, string Id, string Hash);

/// <summary>The stable subject of a finding, with exact record or content evidence.</summary>
public sealed record PolicyTargetDto(string Kind, string Identity, PolicyRecordReference? Record, string? Repository, string? Path, string? Hash);

/// <summary>A period during which an exact result and its evidence discharged an occurrence.</summary>
public sealed record DutyCompletionDto(PolicyRecordReference Result, string From, string? Until, IReadOnlyList<PolicyRecordReference> Evidence);

/// <summary>Occurrence debt and exact completion history; scheduled and grace items are not completed work.</summary>
public sealed record DutyFindingDto(PolicyRecordReference Origin, string SubjectIdentity, PolicyRecordReference Subject, JsonElement Occurrence,
    string DueAt, string Deadline, bool Completed, string? CompletedAt, bool Missed, decimal LateSeconds,
    bool AlreadyDueAtAdoption, string? EndedReason, IReadOnlyList<DutyCompletionDto> Completions);

/// <summary>A finding from the one policy evaluator, including exceptions and release enforcement separately from its verdict.</summary>
public sealed record PolicyFindingDto(string PolicyIdentity, PolicyRecordReference Policy, string PolicyKey, string Rule, string Kind, int Version,
    PolicyTargetDto Target, string Item, string Verdict, string Disposition, string Level, string Owner, string Basis, string? AsOf,
    string Actual, string Required, string NextAction, PolicyRecordReference? Anchor, string? AnchorAt,
    IReadOnlyList<PolicyRecordReference> Evidence, IReadOnlyList<PolicyRecordReference> Exceptions, int SubjectCount,
    DutyFindingDto? Duty, bool Enforced, bool ReleaseHold);

/// <summary>Complete rule counts; excepted findings retain their underlying verdict.</summary>
public sealed record PolicyRuleSummaryDto(PolicyRecordReference Policy, string Rule, int Subjects, int Satisfied, int Unmet, int Unavailable, int Excepted);

/// <summary>The named candidate used by an evaluation; a preview grants no submission authority.</summary>
public sealed record PolicySubmissionDto(PolicyRecordReference? Release, string Product, string Version, string Commit,
    IReadOnlyList<string>? Repositories = null, IReadOnlyList<string>? WorkOrders = null);

/// <summary>A repository inventory identity without machine paths or source bytes.</summary>
public sealed record PolicyRepositoryDto(string Identity, string Revision, string Contract, bool Complete, IReadOnlyList<PolicyFileDto> Files);

/// <summary>Exact source content identity and availability.</summary>
public sealed record PolicyFileDto(string Path, string Hash, bool Available, bool IsLink);

/// <summary>An installed rule contract and its implementation digest.</summary>
public sealed record PolicyImplementationDto(string Kind, int Version, string Digest);

/// <summary>Explicit evaluation inputs. Recorded time is reproducible; preview time cannot authorize submission.</summary>
public sealed record PolicyBasisDto(string Digest, string RecordDigest, string? AsOf, string Origin, PolicySubmissionDto? Submission,
    IReadOnlyList<PolicyRepositoryDto> Repositories, IReadOnlyList<string> Objects, IReadOnlyList<PolicyImplementationDto> Implementations);

/// <summary>Policy declarations, exception history and complete derived findings; reading this authors nothing.</summary>
public sealed record PolicyResponse(string Text, PolicyBasisDto Basis, IReadOnlyList<RecordResponse> Policies,
    IReadOnlyList<RecordResponse> Exceptions, IReadOnlyList<PolicyFindingDto> Findings, IReadOnlyList<PolicyRuleSummaryDto> Rules,
    IReadOnlyList<FindingDto> Diagnostics, bool Complete, int Holds, Problem? Problem = null);

/// <summary>An installed kind's supported selections, source contracts and evaluation bounds.</summary>
public sealed record PolicyKindDto(string Kind, int Version, string ImplementationDigest, IReadOnlyList<string> Selections,
    IReadOnlyList<string> SourceContracts, int MaximumRecords, int MaximumFindings);

/// <summary>A scanned record field or repository-relative path.</summary>
public sealed record WritingLocationDto(string Kind, string Identity, string Member);

/// <summary>One exact whole-token match in the original UTF-8 source.</summary>
public sealed record SpacingOccurrenceDto(string Token, int Line, int ByteOffset, int ByteLength, FindingDto Diagnostic);

/// <summary>A visible warning or excepted occurrence with every matching entry reference.</summary>
public sealed record WritingOccurrenceDto(WritingLocationDto Location, SpacingOccurrenceDto Occurrence, string Basis, IReadOnlyList<PolicyRecordReference> Entries);

/// <summary>An active writing entry and its match count; null means the scan is incomplete.</summary>
public sealed record WritingEntryDto(PolicyRecordReference Entry, string Token, string Author, string Reason, JsonElement? Scope, int? Matches);

/// <summary>The writing registry, including removal history, counts and exact occurrences.</summary>
public sealed record WritingExceptionsResponse(string Text, PolicyBasisDto Basis, string Scanner, bool Complete,
    IReadOnlyList<RecordResponse> Records, IReadOnlyList<WritingEntryDto> Entries, IReadOnlyList<WritingOccurrenceDto> Warnings,
    IReadOnlyList<WritingOccurrenceDto> ExceptedOccurrences, IReadOnlyList<FindingDto> Diagnostics);

/// <summary>A prepared body and actual author. Expected pins the current target revision, or the canonical body hash for a new declaration or writing entry.</summary>
public sealed record PolicyChangeRequest(string By, JsonElement Body, string Expected);

/// <summary>Remove one writing identity. A delegate can supply its own exact authority basis; omission retains the original basis.</summary>
public sealed record WritingExceptionRemoveRequest(string By, string Expected, string Reason, JsonElement? Basis = null);

/// <summary>Place a work revision in one release plan or backlog. ExpectedSelection pins the current carrier or placement policy.</summary>
public sealed record WorkPlaceRequest(string By, string Expected, string ExpectedSelection, string? ReleasePlan = null, string? Backlog = null);

/// <summary>An unfinished duty waiting on an operator, with its unchanged owner and actual occurrence-linked asks.</summary>
public sealed record OperatorDutyDto(string Operator, string Owner, PolicyFindingDto Finding, IReadOnlyList<PolicyRecordReference> Asks, string Preparation);
