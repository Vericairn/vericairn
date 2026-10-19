// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;

namespace Vericairn.Protocol;

/// <summary>Reader text from the runtime response; it performs no independent selection or freshness judgment.</summary>
public static class CompanionPresentation
{
    /// <summary>Coverage, contracts and actual destinations remain distinct.</summary>
    public static string Coverage(CompanionsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Status != "success") return response.Reason ?? "Reader coverage is unavailable.";
        var text = new StringBuilder("Readable companions\nSnapshot: ").Append(response.Snapshot).Append('\n');
        text.Append("Manifest snapshot: ").Append(response.ManifestSnapshot).Append('\n');
        foreach (CompanionCoverageRow row in response.Coverage)
            text.Append(row.Kind).Append(": ").Append(row.NoReaderReason ?? row.ReaderTask).Append("; ")
                .Append(row.Destination).Append("; item ").Append(row.Item).Append("; list ").Append(row.List)
                .Append(row.ReaderOperation is null ? "" : "; owning reader " + row.ReaderOperation).Append('\n');
        text.Append("\nInstalled generators (a pattern is a capability, not an adoption):\n");
        foreach (CompanionGeneratorRow row in response.Generators)
            text.Append(row.Id).Append('/').Append(row.Contract).Append("; ").AppendJoin(", ", row.Selections)
                .Append(row.LocalOnly ? "; local inspector only" : "").Append(row.Pattern is null ? "" : "; pattern " + row.Pattern).Append('\n');
        text.Append("\nDeclared destinations:\n");
        if (response.Destinations.Count == 0) text.Append("None.\n");
        foreach (CompanionDestination row in response.Destinations) text.Append(row.Path).Append("; ").Append(row.Generator)
            .Append("; declaration ").Append(row.Declaration).Append(" at ").Append(row.Hash).Append('\n');
        return text.ToString();
    }

    /// <summary>Source basis and currentness accompany every displayed page.</summary>
    public static string Render(CompanionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = new StringBuilder().Append(response.Status).Append("; ").Append(response.Currentness).Append("; ")
            .Append(response.Completeness).Append('\n');
        if (response.Snapshot is not null) text.Append("Snapshot: ").Append(response.Snapshot).Append('\n');
        if (response.Commit is not null) text.Append("Source commit: ").Append(response.Commit).Append('\n');
        foreach (CompanionSource source in response.Sources) text.Append(source.Kind).Append(' ').Append(source.Identity)
            .Append(" at ").Append(source.Hash).Append("; ").Append(source.Path).Append('\n');
        if (response.Reason is not null) text.Append(response.Reason).Append('\n');
        if (response.Text is not null) text.Append('\n').Append(response.Text);
        text.Append("\nGenerated view. Correct its authoritative source, then refresh. Generation clears no authored review and grants no authority.\n");
        return text.ToString();
    }

    /// <summary>Fresh differences without implying that a repair or review took place.</summary>
    public static string Compare(CompanionComparison response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = new StringBuilder("Compare only: ").Append(response.Status).Append("; ").Append(response.Currentness).Append('\n');
        if (response.Commit is not null) text.Append("Source commit: ").Append(response.Commit).Append('\n');
        foreach (CompanionDifference difference in response.Differences) text.Append(difference.Change).Append(": ").Append(difference.Path)
            .Append("; actual ").Append(difference.ActualHash ?? "absent").Append("; expected ").Append(difference.ExpectedHash ?? "absent").Append('\n');
        foreach (string problem in response.Problems) text.Append(problem).Append('\n');
        return text.Append("No repair, pin or authored clearance was written.\n").ToString();
    }
}
