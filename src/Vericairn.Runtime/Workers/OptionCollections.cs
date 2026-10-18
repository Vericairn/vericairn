// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Vericairn.Runtime.Workers;

internal static class OptionCollections
{
    internal static IReadOnlyList<T> List<T>(IReadOnlyList<T> source) => Array.AsReadOnly(source.ToArray());

    internal static IReadOnlyList<T>? OptionalList<T>(IReadOnlyList<T>? source) => source is null ? null : List(source);

    internal static IReadOnlyDictionary<string, T>? OptionalDictionary<T>(IReadOnlyDictionary<string, T>? source) =>
        source is null ? null : Dictionary(source);

    internal static IReadOnlyDictionary<string, T> Dictionary<T>(IReadOnlyDictionary<string, T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Immutable inputs and our own snapshots already own their storage. For mutable dictionaries, retain
        // their key comparison as well as their enumeration order. Opaque views use the option keys' ordinal comparison.
        return source switch
        {
            SnapshotDictionary<T> or ImmutableDictionary<string, T> or ImmutableSortedDictionary<string, T> or FrozenDictionary<string, T> => source,
            Dictionary<string, T> values => new SnapshotDictionary<T>(new Dictionary<string, T>(values, values.Comparer)),
            SortedDictionary<string, T> values => new SnapshotDictionary<T>(new SortedDictionary<string, T>(values, values.Comparer)),
            SortedList<string, T> values => new SnapshotDictionary<T>(new SortedList<string, T>(values, values.Comparer)),
            _ => new SnapshotDictionary<T>(source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
        };
    }

    private sealed class SnapshotDictionary<T>(IDictionary<string, T> values) : ReadOnlyDictionary<string, T>(values);
}
