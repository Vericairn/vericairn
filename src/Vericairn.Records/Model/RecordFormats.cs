// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Model;

/// <summary>The record formats this Vericairn writes and reads. A release reads every format up to the latest it knows and writes the current one.</summary>
public static class RecordFormats
{
    /// <summary>The format this release writes.</summary>
    public const long Current = 1;

    /// <summary>The newest format this release reads; a manifest pinned beyond it is refused.</summary>
    public static long Latest { get; private set; } = Current;

    /// <summary>Extends what this process reads, for tests that exercise a synthetic migration; restored on dispose.</summary>
    internal static IDisposable Extend(long latest)
    {
        long previous = Latest;
        Latest = Math.Max(latest, Current);
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
