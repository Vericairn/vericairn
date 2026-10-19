// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>An ordinary review of the expected current result, authored with the runtime's fresh time and admitted signing device.</summary>
public sealed record ResultReviewRequest(string Result, string By, JsonElement Body, string Expected);

/// <summary>A tagged occurrence: interval number, one date, or exact publication. Only the selected tag's field is serialized.</summary>
public sealed record DutyOccurrenceDto(string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Number = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? At = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PolicyRecordReference? Publication = null);

/// <summary>An exact repository resource revision and commit examined for a duty.</summary>
public sealed record DutyRepositoryDto(PolicyRecordReference Record, string Commit);

/// <summary>The complete declared scope, with retained bytes and exact record revisions.</summary>
public sealed record DutyScopeDto(IReadOnlyList<DutyRepositoryDto> Repositories, IReadOnlyList<string> Objects, IReadOnlyList<PolicyRecordReference> Records);

/// <summary>A supporting clause's ordered exact records, or a result-verification binding whose record is authored after the result.</summary>
public sealed record DutyProofDto(string Clause,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PolicyRecordReference>? Records = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind = null);

/// <summary>One result.duties binding. It claims completion of this occurrence; the evaluator determines whether all evidence qualifies.</summary>
public sealed record ResultDutyDto(PolicyRecordReference Policy, string Rule, DutyOccurrenceDto Occurrence,
    PolicyRecordReference Subject, DutyScopeDto Scope, string Coverage, IReadOnlyList<DutyProofDto> Proofs);

/// <summary>An ordinary result review with exact evidence read. It is neither an acceptance nor a release authorization.</summary>
public sealed record ResultReviewBody(string Independence, string Outcome, string Summary, IReadOnlyList<ResultReviewFindingDto> Findings,
    IReadOnlyList<PolicyRecordReference> Read);

/// <summary>One finding in an ordinary result review.</summary>
public sealed record ResultReviewFindingDto(string Severity, string Summary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Location = null);

/// <summary>A retained release-admission snapshot. Reading historical evidence grants no authority for a later submission.</summary>
public sealed record ReleaseBasisResponse(string Hash, ReleaseBasisEvidence Evidence);

/// <summary>The complete immutable inputs, findings and document comparisons retained under ordinary operation evidence.</summary>
public sealed record ReleaseBasisEvidence(string Format, PolicyBasisDto Basis, IReadOnlyList<JsonElement> Records,
    IReadOnlyList<ReleaseBasisRepository> Repositories, IReadOnlyList<ReleaseBasisObject> Objects, PolicyResponse Policies,
    IReadOnlyList<string> Documents, IReadOnlyList<PolicyRecordReference> ExcludedOperationRevisions);

/// <summary>Exact repository bytes used at admission, including separately identified historical completion sources.</summary>
public sealed record ReleaseBasisRepository(string Identity, string Revision, string Contract, bool Complete, IReadOnlyList<ReleaseBasisFile> Files, bool Evidence = false);

/// <summary>One retained source file, with its hash, original bytes and availability.</summary>
public sealed record ReleaseBasisFile(string Path, string Hash, IReadOnlyList<byte> Bytes, bool Available, bool IsLink);

/// <summary>One retained proof object, with its original bytes and availability.</summary>
public sealed record ReleaseBasisObject(string Hash, IReadOnlyList<byte> Bytes, bool Available);
