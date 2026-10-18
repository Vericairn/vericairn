// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>An authoritative inventory entry, including separately browsed nested collections.</summary>
public sealed record DocumentManagedKind(string Id, string InputSchema, bool RequiresList = true);

/// <summary>An exact installed generator contract used by an item or collection reader.</summary>
public sealed record DocumentCoverageBinding(string Generator, int ContractVersion);

/// <summary>Shipped contribution metadata; mutable placement remains in living-document declarations.</summary>
public sealed record DocumentCoverageDescriptor(int Version, string Kind, string ReaderTask,
    DocumentCoverageBinding? Item, DocumentCoverageBinding? List, string InputSchema, string Audience,
    string Destination, string? NoReaderReason = null);

/// <summary>Bidirectional conformance for the kind inventory and its companion descriptors, without a persisted status ledger.</summary>
public static class DocumentCoverage
{
    /// <summary>The package coverage descriptor version, independent of output, response and record formats.</summary>
    public const int Version = 1;
    private static readonly Dictionary<string, DocumentCoverageDescriptor> Installed = new(StringComparer.Ordinal);

    static DocumentCoverage() => Register(RecordKinds, RecordCompanions.Coverage);

    /// <summary>The admitted record registry and its declared collection fields. Owners add deeper and non-record collections.</summary>
    public static ImmutableArray<DocumentManagedKind> RecordKinds => [.. RecordTypeRegistry.All.SelectMany(type =>
        new[] { new DocumentManagedKind("record/" + type.Name, "record/" + type.Name) }
        .Concat(type.Fields.Where(f => f.Kind.EndsWith("[]", StringComparison.Ordinal)).Select(f =>
            new DocumentManagedKind("record/" + type.Name + "#" + f.Name, "record/" + type.Name + "#" + f.Name)))), .. RecordCollections.Nested];

    /// <summary>All installed descriptors, ordered by kind for deterministic inspection and CI.</summary>
    public static ImmutableArray<DocumentCoverageDescriptor> All
    {
        get { lock (Installed) return [.. Installed.Values.OrderBy(d => d.Kind, StringComparer.Ordinal)]; }
    }

    /// <summary>Checks a contribution as a whole before activation and refuses duplicate ownership atomically.</summary>
    public static void Register(IEnumerable<DocumentManagedKind> inventory, IEnumerable<DocumentCoverageDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(descriptors);
        DocumentCoverageDescriptor[] proposed = descriptors.ToArray();
        if (!Check(inventory, proposed).IsEmpty) throw new ArgumentException("The contribution has incomplete or unsupported companion coverage.", nameof(descriptors));
        lock (Installed)
        {
            if (proposed.Any(d => Installed.ContainsKey(d.Kind))) throw new InvalidOperationException("A managed kind already has companion coverage.");
            foreach (DocumentCoverageDescriptor descriptor in proposed) Installed.Add(descriptor.Kind, descriptor);
        }
    }

    /// <summary>Compares both directions; a descriptor cannot hide a missing kind or invent an unowned one.</summary>
    public static ImmutableArray<string> Check(IEnumerable<DocumentManagedKind> inventory, IEnumerable<DocumentCoverageDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(descriptors);
        DocumentManagedKind[] kinds = inventory.ToArray();
        DocumentCoverageDescriptor[] coverage = descriptors.ToArray();
        var problems = ImmutableArray.CreateBuilder<string>();
        foreach (IGrouping<string, DocumentManagedKind> group in kinds.GroupBy(k => k.Id, StringComparer.Ordinal))
            if (group.Count() != 1) problems.Add("Duplicate managed kind: " + group.Key);
        foreach (IGrouping<string, DocumentCoverageDescriptor> group in coverage.GroupBy(d => d.Kind, StringComparer.Ordinal))
            if (group.Count() != 1) problems.Add("Duplicate companion ownership: " + group.Key);
        foreach (DocumentManagedKind kind in kinds.DistinctBy(k => k.Id, StringComparer.Ordinal))
        {
            DocumentCoverageDescriptor? descriptor = coverage.FirstOrDefault(d => d.Kind == kind.Id);
            if (descriptor is null) { problems.Add("Missing companion coverage: " + kind.Id); continue; }
            if (descriptor.Version != Version || descriptor.InputSchema != kind.InputSchema || string.IsNullOrWhiteSpace(kind.InputSchema)
                || string.IsNullOrWhiteSpace(kind.Id) || kind.Id.Contains('*', StringComparison.Ordinal))
                problems.Add("Unsupported coverage schema: " + kind.Id);
            if (descriptor.NoReaderReason is not null)
            {
                if (descriptor.NoReaderReason.Trim().Length < 20 || descriptor.Item is not null || descriptor.List is not null
                    || !string.IsNullOrEmpty(descriptor.ReaderTask) || !string.IsNullOrEmpty(descriptor.Destination))
                    problems.Add("Unjustified or ambiguous no-reader entry: " + kind.Id);
                continue;
            }
            if (string.IsNullOrWhiteSpace(descriptor.ReaderTask) || descriptor.Audience != "Internal"
                || descriptor.Destination is not ("repository" or "on-demand" or "artifact" or "local"))
                problems.Add("Incomplete reader contract: " + kind.Id);
            CheckBinding(descriptor.Item, "item", kind.Id);
            if (kind.RequiresList) CheckBinding(descriptor.List, "list", kind.Id);
        }
        foreach (DocumentCoverageDescriptor descriptor in coverage.Where(d => !kinds.Any(k => k.Id == d.Kind)))
            problems.Add("Coverage has no managed kind: " + descriptor.Kind);
        return [.. problems.Order(StringComparer.Ordinal)];

        void CheckBinding(DocumentCoverageBinding? binding, string selection, string kind)
        {
            if (binding is null || DocumentGenerators.Find(binding.Generator)?.Descriptor is not { } generator
                || generator.ContractVersion != binding.ContractVersion || !generator.Selections.Contains(selection))
                problems.Add("Unavailable " + selection + " generator contract: " + kind);
        }
    }
}
