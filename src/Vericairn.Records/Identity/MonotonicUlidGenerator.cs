// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;

namespace Vericairn.Records.Identity;

// One instance serves the public generator. Separate instances let tests control time without resetting process-wide state.
internal sealed class MonotonicUlidGenerator
{
    private readonly object gate = new();
    private readonly byte[] bytes = new byte[16];
    private long lastMilliseconds = -1;

    internal Ulid Next(DateTimeOffset at)
    {
        long milliseconds = at.ToUnixTimeMilliseconds();
        if (milliseconds < 0 || milliseconds > 0xFFFF_FFFF_FFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(at), "The timestamp does not fit in 48 bits.");
        }

        lock (gate)
        {
            if (milliseconds > lastMilliseconds)
            {
                RandomNumberGenerator.Fill(bytes.AsSpan(6));
                lastMilliseconds = milliseconds;
                for (int i = 5; i >= 0; i--)
                {
                    bytes[i] = (byte)(milliseconds & 0xFF);
                    milliseconds >>= 8;
                }
            }
            else
            {
                // Find the carry before changing state: exhaustion must never wrap or allow a later call to reuse an id.
                int index = bytes.Length - 1;
                while (index >= 6 && bytes[index] == byte.MaxValue)
                {
                    index--;
                }

                if (index < 6)
                {
                    throw new OverflowException("The ULID suffix is exhausted; the clock must advance before another identifier can be generated.");
                }

                bytes[index]++;
                bytes.AsSpan(index + 1).Clear();
            }

            return Ulid.FromBytes(bytes);
        }
    }
}
