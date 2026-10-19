// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Tests;

// Elapsed time and timer delivery are separate: an overdue callback may still be queued on a busy runtime.
internal sealed class ClientClock : TimeProvider
{
    private readonly List<DeadlineTimer> timers = [];
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);

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
        private bool disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            lock (clock.timers)
            {
                if (disposed) return false;
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetTimestamp() + dueTime.Ticks;
                return true;
            }
        }

        internal void FireIfDue()
        {
            lock (clock.timers)
            {
                if (disposed || due is null || due > clock.GetTimestamp()) return;
                due = null;
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
