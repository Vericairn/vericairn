// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>Assign the exact prepared release review brief to the principal holding the reviewer role.</summary>
/// <param name="By">Acting principal.</param>
/// <param name="Expected">Hash of the complete prepared brief text, including its pinned sources.</param>
/// <param name="Product">Product id, required when the version is ambiguous.</param>
/// <param name="Decision">Current decision id selecting the approved review scope.</param>
public sealed record ReleaseReviewAssignRequest(string By, string Expected, string? Product = null, string? Decision = null);

/// <summary>A deterministic review brief and, when requested, its assignment receipt. No launcher is invoked.</summary>
/// <param name="Text">The complete UTF-8 brief, with LF line endings; retained unchanged in the assignment.</param>
/// <param name="Hash">SHA-256 of Text; supply it as Expected when assigning.</param>
/// <param name="Brief">The assignment's complete brief object, with exact source pins.</param>
/// <param name="Reviewer">The identity of the principal currently holding the reviewer role.</param>
/// <param name="Assignment">Receipt of the stored assignment, when requested.</param>
/// <param name="Problem">A refusal or conflict; no assignment is stored on a preparation refusal.</param>
/// <param name="RequestedProgress">WATCH-02 integration seam. Unavailable until requested progress is implemented.</param>
public sealed record ReleaseReviewBriefResponse(string Text, string? Hash = null, JsonElement? Brief = null,
    string? Reviewer = null, RecordWritten? Assignment = null, Problem? Problem = null, string RequestedProgress = "unavailable")
{
    /// <summary>Whether preparation or assignment succeeded.</summary>
    public bool Ok => Problem is null;
}
