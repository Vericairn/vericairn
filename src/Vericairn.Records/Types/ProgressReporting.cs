// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>The format-3 request and claim vocabulary, shared by every record that carries it.</summary>
internal static class ProgressReporting
{
    private static readonly string[] Types = ["accurate", "best-effort", "not-measurable"];

    internal static void Request(BodyReader parent)
    {
        if (!parent.Since(3, "progressRequest") || parent.Nested("progressRequest", false) is not BodyReader request)
            return;

        IReadOnlyList<string>? types = request.StringArray("types", false, allowEmpty: false, maxItemLength: 30);
        if (types is not null && (types.Any(t => !Types.Contains(t, StringComparer.Ordinal)) || types.Distinct(StringComparer.Ordinal).Count() != types.Count))
            request.Error("progress.types", "types", "Progress types are distinct members of accurate, best-effort and not-measurable; omitted means all three.");
        request.Flag("statusText", false);
        if (request.Nested("cadence", true) is BodyReader cadence)
        {
            bool perStep = cadence.Flag("perStep", false) == true;
            long? minutes = cadence.Number("minutes", false, 1, int.MaxValue);
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
