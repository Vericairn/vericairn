// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>A due cadence and its recorded or proposed release target. Context grants no submission or acceptance authority.</summary>
public sealed record ReleaseCadenceDto(PolicyRecordReference Product, string ProductName, PolicyRecordReference Policy,
    string Rule, PolicyRecordReference Origin, JsonElement Occurrence, string DueAt, string Line, string? Version,
    IReadOnlyList<PolicyRecordReference> Targets);

/// <summary>The same cadence context in the operator's waits and the release gate.</summary>
public static class ReleaseCadencePresentation
{
    /// <summary>Render only due occurrences; an empty collection contributes no text.</summary>
    public static string Render(IReadOnlyList<ReleaseCadenceDto> cadences)
    {
        ArgumentNullException.ThrowIfNull(cadences);
        if (cadences.Count == 0) return string.Empty;
        var text = new StringBuilder("  Release cadence context:\n");
        foreach (ReleaseCadenceDto cadence in cadences)
        {
            text.AppendLine($"    {cadence.ProductName}: release due on the cadence; line {cadence.Line}; target version {cadence.Version ?? "not declared"}; due {cadence.DueAt}.");
            text.AppendLine($"      Product {cadence.Product.Id} {cadence.Product.Hash}; policy {cadence.Policy.Id} {cadence.Policy.Hash}; rule {cadence.Rule}; origin {cadence.Origin.Id} {cadence.Origin.Hash}; occurrence {cadence.Occurrence.GetRawText()}.");
            foreach (PolicyRecordReference target in cadence.Targets) text.AppendLine($"      target {target.Type} {target.Id} {target.Hash}");
        }
        text.AppendLine("    A person must submit the release and accept its candidate. Cadence context never holds, starts or publishes a release.");
        return text.ToString().ReplaceLineEndings("\n");
    }
}
