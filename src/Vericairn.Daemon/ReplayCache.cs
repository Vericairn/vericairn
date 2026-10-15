// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Daemon;

/// <summary>
/// The signatures the runtime accepted, so that each is accepted once: kept per device, each until its signing time plus the skew,
/// after which it is refused as stale anyway. Nothing is evicted early: when a device has as many signatures in its window as the
/// cache holds for one device, its new requests are refused until older ones expire, so no flood, by that device or another, can
/// make room for a replay.
/// </summary>
internal sealed class ReplayCache(int capacity)
{
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> devices = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>What a signature's acceptance came to.</summary>
    internal enum Outcome
    {
        /// <summary>First seen, and now remembered.</summary>
        Accepted,

        /// <summary>Accepted before, in this or an equivalent form.</summary>
        Repeated,

        /// <summary>The device has as many signatures in its window as the cache holds for one device.</summary>
        Full,
    }

    /// <summary>Accepts a signature of a device once, remembering it until it expires; never evicts a live one.</summary>
    internal Outcome Accept(string device, string key, DateTimeOffset expires, DateTimeOffset now)
    {
        lock (gate)
        {
            if (!devices.TryGetValue(device, out Dictionary<string, DateTimeOffset>? seen))
            {
                seen = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                devices[device] = seen;
            }

            if (seen.TryGetValue(key, out DateTimeOffset until) && until >= now)
            {
                return Outcome.Repeated;
            }

            if (seen.Count >= capacity)
            {
                foreach (string expired in seen.Where(e => e.Value < now).Select(e => e.Key).ToList())
                {
                    seen.Remove(expired);
                }

                if (seen.Count >= capacity)
                {
                    return Outcome.Full;
                }
            }

            seen[key] = expires;
            return Outcome.Accepted;
        }
    }
}
