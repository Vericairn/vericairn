// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.TestSupport;

// Elapsed time and timer delivery are separate: an overdue callback may still be queued on a busy runtime.
internal sealed class ClientClock(DateTimeOffset? start = null, bool allowPeriodic = false) : TimeProvider
{
    private readonly List<DeadlineTimer> timers = [];
    private readonly bool periodicTimers = allowPeriodic;
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    public override DateTimeOffset GetUtcNow() => start is DateTimeOffset at ? at.AddTicks(GetTimestamp()) : base.GetUtcNow();
    internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);

    internal void Elapse(TimeSpan elapsed)
    {
        Advance(elapsed);
        FireDueTimers();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new DeadlineTimer(this, callback, state);
        lock (timers)
        {
            timer.Change(dueTime, period);
            timers.Add(timer);
        }

        return timer;
    }

    internal void FireDueTimers()
    {
        DeadlineTimer[] pending;
        lock (timers) pending = [.. timers];
        foreach (DeadlineTimer timer in pending) timer.FireIfDue();
    }

    private sealed class DeadlineTimer(ClientClock clock, TimerCallback callback, object? state) : ITimer
    {
        private long? due;
        private long? interval;
        private bool disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (clock.periodicTimers)
                Assert.True(period == Timeout.InfiniteTimeSpan || period > TimeSpan.Zero, "A periodic test timer needs a positive interval.");
            else
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
            lock (clock.timers)
            {
                if (disposed) return false;
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetTimestamp() + dueTime.Ticks;
                interval = period == Timeout.InfiniteTimeSpan ? null : period.Ticks;
                return true;
            }
        }

        internal void FireIfDue()
        {
            lock (clock.timers)
            {
                if (disposed || due is null || due > clock.GetTimestamp()) return;
                due = interval is long ticks ? clock.GetTimestamp() + ticks : null;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (clock.timers)
            {
                disposed = true;
                clock.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
