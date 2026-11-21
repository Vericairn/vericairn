// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Signing;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (FIX-148; design/performance.md § 6): what one runtime, or one test, has spent judging estates, counted where it happens
/// (judgments and parses in the loader, verifications and key imports in <see cref="RecordSigning"/>) and attributed to this context,
/// which flows with the call from the runtime's session to the loader and the trust, never through a static: test hosts run collections
/// in parallel in one process, so a process-wide counter would not be exact. It also names the signature tables its judgments read (the
/// process's unless a test gives fresh ones) and scopes the seam that simulates a platform's refusal of an import or a verification.
/// </summary>
internal sealed class JudgmentContext
{
    private long judgments;
    private long parses;
    private long verifications;
    private long imports;
    private long evictedInUse;
    private long repositoryChecks;

    /// <summary>A context reading the given tables, or the process's.</summary>
    internal JudgmentContext(SignatureTables? tables = null) => Tables = tables ?? SignatureTables.Process;

    /// <summary>The signature tables this context's judgments read and keep verdicts and keys in.</summary>
    internal SignatureTables Tables { get; }

    /// <summary>Whole-estate judgments: loads that built trust and validated every record.</summary>
    internal long Judgments => Interlocked.Read(ref judgments);

    /// <summary>Record files parsed, whether by a parse cache's miss or by a load without one.</summary>
    internal long Parses => Interlocked.Read(ref parses);

    /// <summary>Signatures the platform was asked to verify.</summary>
    internal long Verifications => Interlocked.Read(ref verifications);

    /// <summary>Public keys the platform was asked to import.</summary>
    internal long KeyImports => Interlocked.Read(ref imports);

    /// <summary>Kept verdicts evicted while a judgment in progress had used them, as this context's judgments saw it.</summary>
    internal long EvictionsInUse => Interlocked.Read(ref evictedInUse);

    /// <summary>From 0.9.4 (FIX-152): passes of the repository checks (the walk and the writing scan) the runtime ran.</summary>
    internal long RepositoryCheckPasses => Interlocked.Read(ref repositoryChecks);

    /// <summary>
    /// A test's stand-in for a platform that refuses to import a key: true refuses the import of that key text, as a platform refusal
    /// would. Scoped to this context; null in every runtime.
    /// </summary>
    internal Func<string, bool>? RefuseImport { get; set; }

    /// <summary>
    /// A test's stand-in for a platform that refuses a verification: true answers false for that signature text, as a platform refusal
    /// would. Scoped to this context; null in every runtime.
    /// </summary>
    internal Func<string, bool>? RefuseVerification { get; set; }

    internal void Judged() => Interlocked.Increment(ref judgments);

    internal void Parsed() => Interlocked.Increment(ref parses);

    internal void Verified() => Interlocked.Increment(ref verifications);

    internal void Imported() => Interlocked.Increment(ref imports);

    internal void EvictedInUse() => Interlocked.Increment(ref evictedInUse);

    internal void CheckedRepository() => Interlocked.Increment(ref repositoryChecks);
}
