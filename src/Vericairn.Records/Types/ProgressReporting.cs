// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>The format-3 request and claim vocabulary, shared by every record that carries it.</summary>
internal static class ProgressReporting
{
    private static readonly string[] Types = ["accurate", "best-effort", "not-measurable"];

    private static readonly MemberRule RequestTypes = new("types", "string[]", false, "The progress types the requester reads; omitted means all three.")
    { MaxLength = 30, Limit = "distinct values of accurate, best-effort and not-measurable" };

    private static readonly MemberRule RequestStatusText = new("statusText", "boolean", false, "Whether the requester asks for a one-line status text with each report.");

    private static readonly MemberRule PerStep = new("perStep", "boolean", false, "Report at each step.");

    private static readonly MemberRule Minutes = new("minutes", "integer", false, "Report at least every this many minutes.") { Min = 1 };

    private static readonly MemberRule Cadence = new("cadence", "object", true, "When to report.")
    { Limit = "perStep true, minutes, or both", Members = [PerStep, Minutes] };

    /// <summary>From 0.9.4 (DOCS-46): the format-3 progress request and the rules <see cref="Request"/> reads it by.</summary>
    internal static readonly MemberRule RequestRule = new("progressRequest", "object", false,
        "Asks for progress reports in place of broader progress requests: types, statusText and cadence.", 3)
    { Members = [RequestTypes, RequestStatusText, Cadence] };

    internal static void Request(BodyReader parent)
    {
        if (!parent.Since(3, RequestRule.Name) || RequestRule.Object(parent) is not BodyReader request)
            return;

        IReadOnlyList<string>? types = RequestTypes.Strings(request);
        if (types is not null && (types.Any(t => !Types.Contains(t, StringComparer.Ordinal)) || types.Distinct(StringComparer.Ordinal).Count() != types.Count))
            request.Error("progress.types", "types", "Progress types are distinct members of accurate, best-effort and not-measurable; omitted means all three.");
        RequestStatusText.Flag(request);
        if (Cadence.Object(request) is BodyReader cadence)
        {
            bool perStep = PerStep.Flag(cadence) == true;
            long? minutes = Minutes.Number(cadence);
            if (!perStep && minutes is null)
                cadence.Error("progress.cadence", "perStep", "A cadence requires perStep true, a positive integer number of minutes, or both.");
            cadence.NoOtherKeys();
        }
        request.NoOtherKeys();
    }

    internal static void Claim(BodyReader body)
    {
        if (body.Since(3, "statusText") && body.Has("statusText"))
        {
            string? status = body.Text("statusText", false, 500);
            if (status is not null && (string.IsNullOrWhiteSpace(status) || status.Contains('\n') || status.Contains('\r')))
                body.Error("progress.status-text", "statusText", "Status text must be a nonblank sentence on one line, at most 500 characters.");
        }
        if (!body.Since(3, "progress") || body.Nested("progress", false) is not BodyReader progress)
            return;

        string? type = progress.Enum("type", true, Types);
        if (type == "accurate")
        {
            long? done = progress.Number("done", true, 0);
            long? total = progress.Number("total", false, 0);
            string? unit = progress.Text("unit", true, 100);
            if (unit is not null && (string.IsNullOrWhiteSpace(unit) || unit.Contains('\n') || unit.Contains('\r')))
                progress.Error("progress.unit", "unit", "An accurate count needs a nonblank unit on one line.");
            if (done is not null && total is not null && done > total)
                progress.Error("progress.count", "done", "The done count cannot exceed the known total.");
            progress.Number("estimatePercent", false, 0, 100);
        }
        else if (type == "best-effort")
        {
            progress.Number("percent", true, 0, 100);
        }
        // not-measurable has no value. Fields from another type are unknown, including explicit nulls.
        progress.NoOtherKeys();
    }
}
