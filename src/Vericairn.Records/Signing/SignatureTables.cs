// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Signing;

/// <summary>
/// From 0.9.4 (FIX-148; design/performance.md § 3.1): the estate's signature verdicts and imported keys, kept for the process so that a
/// judgment verifies only what no earlier judgment in the process verified. A verdict is keyed by the exact public key text, content hash
/// and signature text, the three strings the arithmetic reads and nothing else, and only a verdict that verifies is kept: a false can be a
/// platform's refusal (<see cref="RecordSigning"/>), so it is asked again. A key is kept only when its import succeeded: a refused import
/// verifies nothing and is asked again. Only <see cref="EstateTrust"/>'s verdicts are kept here; signed requests and presence verify
/// outside these tables, always anew. Neither table is persisted or shared between processes. Proofs, admissions, closings and rotations
/// read these verdicts and are judged at every load.
/// </summary>
/// <remarks>
/// Both tables are concurrent. Each key verifies under its own lock, since a platform key is not safe for concurrent use, and is disposed
/// only once it is evicted and no verification holds it. The verdict bound grows with the estate: each judgment reserves at least twice its
/// signatures before it verifies any, so one estate's judgments never evict each other's verdicts; an eviction of a verdict that a judgment
/// in progress used is counted (<see cref="EvictedInUse"/>, and in the judging context), so thrashing is never silent.
/// </remarks>
internal sealed class SignatureTables
{
    /// <summary>The verdict bound a process starts with, before any judgment reserves more.</summary>
    internal const int StartingVerdicts = 4096;

    /// <summary>The key bound a process starts with, before any judgment reserves more.</summary>
    internal const int StartingKeys = 64;

    private readonly ConcurrentDictionary<(string Key, string Hash, string Signature), long> verdicts = new();
    private readonly ConcurrentDictionary<string, KeyEntry> keys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Judgment, long> active = new(ReferenceEqualityComparer.Instance);
    private readonly object evicting = new();
    private readonly object importing = new();
    private int verdictBound;
    private int keyBound;
    private long clock;
    private long evictedInUse;
    private long evictions;

    /// <summary>Fresh tables with the given starting bounds: what a cost or safety test starts from.</summary>
    internal SignatureTables(int startingVerdicts = StartingVerdicts, int startingKeys = StartingKeys)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startingVerdicts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(startingKeys, 1);
        verdictBound = startingVerdicts;
        keyBound = startingKeys;
    }

    /// <summary>The process's tables, which every judgment without others given reads.</summary>
    internal static SignatureTables Process { get; } = new();

    /// <summary>How many verdicts may be kept before the least recently used are evicted.</summary>
    internal int VerdictBound => Volatile.Read(ref verdictBound);

    /// <summary>How many imported keys may be kept before the least recently used are evicted.</summary>
    internal int KeyBound => Volatile.Read(ref keyBound);

    /// <summary>The verdicts kept now.</summary>
    internal int Verdicts => verdicts.Count;

    /// <summary>The imported keys kept now.</summary>
    internal int Keys => keys.Count;

    /// <summary>Every verdict evicted so far.</summary>
    internal long Evictions => Interlocked.Read(ref evictions);

    /// <summary>The evicted verdicts that a judgment in progress had used: none while the bound follows the estate.</summary>
    internal long EvictedInUse => Interlocked.Read(ref evictedInUse);

    /// <summary>
    /// Begins a judgment of <paramref name="signatures"/> signatures by <paramref name="distinctKeys"/> keys: each bound grows to at least
    /// twice what it needs, before anything is verified, and the judgment counts as in progress until disposed.
    /// </summary>
    internal Judgment Begin(int signatures, int distinctKeys, JudgmentContext? context)
    {
        Grow(ref verdictBound, 2L * signatures);
        Grow(ref keyBound, 2L * distinctKeys);
        var judgment = new Judgment(this, context, Interlocked.Increment(ref clock));
        active[judgment] = judgment.Started;
        return judgment;
    }

    private static void Grow(ref int bound, long wanted)
    {
        int target = (int)Math.Min(int.MaxValue, wanted);
        int current;
        while ((current = Volatile.Read(ref bound)) < target)
        {
            if (Interlocked.CompareExchange(ref bound, target, current) == current) return;
        }
    }

    /// <summary>
    /// Whether a signature over a content hash verifies with a key, as <see cref="RecordSigning.Verify"/> answers it: a kept verdict when
    /// one verified before, otherwise verified now with the key's kept import, and kept when it verifies.
    /// </summary>
    internal bool Verifies(string key, string hash, ReadOnlySpan<byte> digest, string signature, Judgment? judgment)
    {
        if (Kept(key, hash, signature)) return true;
        JudgmentContext? context = judgment?.Context;
        KeyEntry? entry = Acquire(key, context);
        if (entry is null) return false;
        bool verified;
        try
        {
            lock (entry) verified = entry.Key.Verify(digest, signature, context);
        }
        finally
        {
            Release(entry);
        }

        if (verified) Keep(key, hash, signature, context);
        return verified;
    }

    /// <summary>Whether a verdict that verified is kept for exactly this key, content hash and signature; reading it marks it used.</summary>
    internal bool Kept(string key, string hash, string signature)
    {
        var verdict = (key, hash, signature);
        if (!verdicts.ContainsKey(verdict)) return false;
        verdicts[verdict] = Interlocked.Increment(ref clock);
        return true;
    }

    /// <summary>Keeps a verdict that verified, evicting the least recently used beyond the bound. A false verdict is never kept.</summary>
    internal void Keep(string key, string hash, string signature, JudgmentContext? context)
    {
        verdicts[(key, hash, signature)] = Interlocked.Increment(ref clock);
        if (verdicts.Count > VerdictBound) EvictVerdicts(context);
    }

    // A key whose import the platform refused is not kept, so the next verification asks again.
    private KeyEntry? Acquire(string key, JudgmentContext? context)
    {
        while (true)
        {
            if (!keys.TryGetValue(key, out KeyEntry? entry))
            {
                lock (importing)
                {
                    if (!keys.TryGetValue(key, out entry))
                    {
                        RecordSigning.ImportedKey imported = RecordSigning.ImportedKey.Import(key, context);
                        if (!imported.Usable)
                        {
                            imported.Dispose();
                            return null;
                        }

                        entry = new KeyEntry(imported);
                        keys[key] = entry;
                        if (keys.Count > KeyBound) EvictKeys(entry);
                    }
                }
            }

            lock (entry)
            {
                if (entry.Evicted) continue;
                entry.Holders++;
                entry.Used = Interlocked.Increment(ref clock);
                return entry;
            }
        }
    }

    private static void Release(KeyEntry entry)
    {
        lock (entry)
        {
            entry.Holders--;
            if (entry.Evicted && entry.Holders == 0) entry.Key.Dispose();
        }
    }

    // Called while importing is held: the least recently used keys beyond the bound leave, each disposed once no verification holds it.
    private void EvictKeys(KeyEntry keep)
    {
        int excess = keys.Count - KeyBound;
        foreach ((string key, KeyEntry entry) in keys.ToArray().Where(k => !ReferenceEquals(k.Value, keep)).OrderBy(k => k.Value.Used).Take(excess).ToArray())
        {
            if (!keys.TryRemove(new KeyValuePair<string, KeyEntry>(key, entry))) continue;
            lock (entry)
            {
                entry.Evicted = true;
                if (entry.Holders == 0) entry.Key.Dispose();
            }
        }
    }

    // The least recently used verdicts leave, down to three quarters of the bound; one that a judgment in progress used is counted.
    private void EvictVerdicts(JudgmentContext? context)
    {
        lock (evicting)
        {
            int bound = VerdictBound;
            if (verdicts.Count <= bound) return;
            long oldestActive = active.IsEmpty ? long.MaxValue : active.Values.DefaultIfEmpty(long.MaxValue).Min();
            int target = bound - bound / 4;
            // A consistent snapshot first: other judgments keep adding and reading verdicts while these are chosen.
            KeyValuePair<(string Key, string Hash, string Signature), long>[] held = verdicts.ToArray();
            foreach (KeyValuePair<(string Key, string Hash, string Signature), long> verdict in held.OrderBy(v => v.Value).Take(Math.Max(0, held.Length - target)).ToArray())
            {
                if (!verdicts.TryRemove(verdict)) continue;
                Interlocked.Increment(ref evictions);
                if (verdict.Value < oldestActive) continue;
                Interlocked.Increment(ref evictedInUse);
                context?.EvictedInUse();
            }
        }
    }

    private void End(Judgment judgment) => active.TryRemove(judgment, out _);

    /// <summary>One judgment's use of the tables: what an eviction is checked against, and the context its verifications are counted in.</summary>
    internal sealed class Judgment : IDisposable
    {
        private readonly SignatureTables tables;

        internal Judgment(SignatureTables tables, JudgmentContext? context, long started)
        {
            this.tables = tables;
            Context = context;
            Started = started;
        }

        /// <summary>The context this judgment's verifications, imports and evictions are counted in; none for an uncounted judgment.</summary>
        internal JudgmentContext? Context { get; }

        /// <summary>The tables' clock when the judgment began: a verdict used since was used by it or a judgment still in progress.</summary>
        internal long Started { get; }

        /// <summary>The tables the judgment reads.</summary>
        internal SignatureTables Tables => tables;

        /// <summary>Ends the judgment: its verdicts are no longer in use by it.</summary>
        public void Dispose() => tables.End(this);
    }

    private sealed class KeyEntry(RecordSigning.ImportedKey key)
    {
        internal RecordSigning.ImportedKey Key { get; } = key;

        internal int Holders { get; set; }

        internal bool Evicted { get; set; }

        internal long Used { get; set; }
    }
}
