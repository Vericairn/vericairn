// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>
/// From 0.9.4 (DOCS-46): the members of an assignment's brief and the rules <see cref="AssignmentType"/> reads them by, in the order it
/// reads them. <c>records describe assignment</c> and the record reference describe the brief from these rules.
/// </summary>
internal static class AssignmentBrief
{
    internal static readonly MemberRule Outcome = new("outcome", "string", true, "What the work is to achieve.") { MaxLength = 4000 };

    internal static readonly MemberRule Sources = new("sources", "object[]", true,
        "The records the work relies on, each a reference {type, id, hash?} to a record of this estate; with hash, the record must have that content hash.")
    {
        AllowEmpty = true,
        Members =
        [
            new("type", "string", true, "The record's type, in kebab case, such as decision."),
            new("id", "ulid", true, "The record's id."),
            new("hash", "hash", false, "The content hash the brief relies on; a record with another hash is refused."),
        ],
    };

    internal static readonly MemberRule PermittedChanges = new("permittedChanges", "string[]", true,
        "The changes the work may make; an entry such as path:src/** is a repository-relative pattern.")
    { MaxLength = 2000 };

    internal static readonly MemberRule ProhibitedEffects = new("prohibitedEffects", "string[]", true, "Effects the work must not have, such as no publication.")
    { MaxLength = 2000, AllowEmpty = true };

    internal static readonly MemberRule Workspace = new("workspace", "string[]", true,
        "Where the work happens: the allowed trees, such as a repository and branch, named without machine paths.")
    { MaxLength = 500 };

    internal static readonly MemberRule Limits = new("limits", "string[]", true, "The bounds the work keeps within, such as a time or a budget.")
    { MaxLength = 2000, AllowEmpty = true };

    internal static readonly MemberRule ExpectedOutputs = new("expectedOutputs", "string[]", true, "What the work hands back.") { MaxLength = 2000 };

    internal static readonly MemberRule ReviewCriteria = new("reviewCriteria", "string[]", true, "The questions a review of the result answers.") { MaxLength = 2000 };

    internal static readonly MemberRule Recovery = new("recovery", "string", true, "How a successor resumes the work: what to read and where it stands.")
    { MaxLength = 4000 };

    internal static readonly MemberRule ReviewBrief = new("reviewBrief", "string", false, "The complete release review text a review brief's assignment retains unchanged.", 3)
    { MaxLength = 1_000_000 };

    internal static readonly MemberRule Repository = new("repository", "ulid", false,
        "From 0.9.4, the identity of the estate or of a repository resource the work changes: the first record of its lineage.", 3);

    internal static readonly MemberRule ExcludedChanges = new("excludedChanges", "string[]", false,
        "From 0.9.4, patterns in permittedChanges' grammar the work leaves out, such as path:docs/operator-guide.md; prose excludes nothing.", 3)
    { MaxLength = 2000 };

    internal static readonly MemberRule After = new("after", "ulid[]", false,
        "From 0.9.4, the assignments whose work this one waits for, each followed to its current revision: each named once, never this assignment and never a cycle.", 3)
    { MaxLength = Identity.Ulid.Length, MaxItems = AssignmentType.MaxAfter };

    internal static readonly MemberRule Retrieval = new("retrieval", AssignmentType.PublicRetrieval, false,
        "From 0.9.4 (RUN-04), public permits the work's runs to retrieve public sources with their harness's web tools, never with credentials, each source retained as an untrusted original; nothing else permits it.", 3);

    /// <summary>Every member, in the order the validator reads them; the brief admits no other.</summary>
    internal static readonly IReadOnlyList<MemberRule> Members =
    [
        Outcome, Sources, PermittedChanges, ProhibitedEffects, Workspace, Limits, ExpectedOutputs, ReviewCriteria, Recovery, ReviewBrief, Repository,
        ExcludedChanges, After, Retrieval, ProgressReporting.RequestRule,
    ];
}
