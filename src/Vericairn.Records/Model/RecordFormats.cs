// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;

namespace Vericairn.Records.Model;

/// <summary>
/// The record formats this Vericairn writes and reads. A record states the format it was written in and is validated under that
/// format's rules; an estate's manifest states the format its new records are written in. A release reads every format from the
/// first up to the latest it knows and writes the estate's own format, never one beyond <see cref="Current"/>.
/// </summary>
public static class RecordFormats
{
    /// <summary>The first record format.</summary>
    public const long First = 1;

    /// <summary>The newest format this release writes; an estate at an older format gets records of its own format.</summary>
    public const long Current = 3;

    /// <summary>The prefix of a record's format identifier.</summary>
    public const string IdentifierPrefix = "vericairn.record/";

    /// <summary>The newest format this release reads; a manifest pinned beyond it is refused.</summary>
    public static long Latest { get; private set; } = Current;

    /// <summary>The formats this release reads, oldest first.</summary>
    public static IReadOnlyList<long> Reads => [.. Enumerable.Range((int)First, (int)(Latest - First + 1)).Select(f => (long)f)];

    /// <summary>The envelope's format identifier for a format number, for example <c>vericairn.record/2</c>.</summary>
    public static string Identifier(long format) => IdentifierPrefix + format.ToString(CultureInfo.InvariantCulture);

    /// <summary>The format number an envelope's identifier names, or null when it names none this release reads.</summary>
    public static long? Parse(string? identifier)
    {
        if (identifier is null || !identifier.StartsWith(IdentifierPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string digits = identifier[IdentifierPrefix.Length..];
        if (digits.Length is 0 or > 4 || digits[0] == '0' || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        long format = long.Parse(digits, CultureInfo.InvariantCulture);
        return format >= First && format <= Latest ? format : null;
    }

    /// <summary>Extends what this process reads, for tests that exercise a synthetic migration; restored on dispose.</summary>
    internal static IDisposable Extend(long latest)
    {
        long previous = Latest;
        Latest = Math.Max(latest, Current);
        return new Restore(() => Latest = previous);
    }

    /// <summary>Exercises the manifest refusal of a reader that knows only an earlier format.</summary>
    internal static IDisposable ReadThrough(long latest)
    {
        long previous = Latest;
        Latest = latest;
        return new Restore(() => Latest = previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action action;

        public Restore(Action action)
        {
            this.action = action;
        }

        public void Dispose() => action();
    }
}
