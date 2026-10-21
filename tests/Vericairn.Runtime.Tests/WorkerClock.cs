// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Tests;

/// <summary>Semantic time advances only when the test has observed the preceding condition; real waits are hang guards.</summary>
internal sealed class WorkerClock(DateTimeOffset? start = null) : TimeProvider
{
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref timestamp);

    public override DateTimeOffset GetUtcNow() => start is DateTimeOffset at ? at.AddTicks(GetTimestamp()) : base.GetUtcNow();

    public void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
}
