// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (FIX-152; design/performance.md § 3.5): what status and attention derive within one command is never carried to the next
/// request, whatever the clock says between them. A standing agent's daily occurrence coming due between two requests to one runtime
/// shows in the second exactly as a runtime opened afresh at that time shows it, and health answers while another call holds the gate.
/// </summary>
[Collection("home")]
public sealed class StatusDerivationTests
{
    [Fact]
    public void AnOccurrenceComingDueBetweenTwoRequestsToOneRuntimeShowsInTheSecond()
    {
        using var run = new RunEstate(recordFormat: 3);
        StandingAgentRoutingTests.Standing(run);
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        run.Service.FlowClock = new Clock(now);
        string first = Read(run.Service);

        // A day later another daily occurrence is due; the second request reads the clock again.
        var later = new Clock(now.AddDays(1));
        run.Service.FlowClock = later;
        string second = Read(run.Service);
        Assert.NotEqual(Standing(first), Standing(second));

        using Vericairn.Runtime.Service.EstateService fresh = Vericairn.Runtime.Service.EstateService.Open(run.Estate.Root);
        fresh.FlowClock = later;
        Assert.Equal(Standing(Read(fresh)), Standing(second));
    }

    [Fact]
    public void HealthAnswersWhileAnotherCallHoldsTheGate()
    {
        using var run = new RunEstate(recordFormat: 3);
        HealthResponse expected = run.Service.Health();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // The session's judgments signal that the gate is held: a status call loads under it, and waits there for the release.
        run.Service.Session.Loaded = _ => { held.Set(); release.Wait(TimeSpan.FromMinutes(2)); };
        var status = new Thread(() => run.Service.Status());
        status.Start();
        try
        {
            Assert.True(held.Wait(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken), "The status call did not reach its load.");
            // Asked on a thread of its own, so a health that waited for the gate would show as a timeout here rather than a hang.
            HealthResponse? answered = null;
            var health = new Thread(() => answered = run.Service.Health());
            health.Start();
            Assert.True(health.Join(TimeSpan.FromMinutes(1)), "Health waited for the gate.");
            Assert.Equal(expected, answered);
        }
        finally
        {
            release.Set();
            Assert.True(status.Join(TimeSpan.FromMinutes(2)));
            run.Service.Session.Loaded = null;
        }
    }

    // One human status command's calls in one command scope, as the command line makes them.
    private static string Read(Vericairn.Runtime.Service.EstateService service)
    {
        using IDisposable command = service.Command();
        return service.Status().Text + service.AttentionGet().Items.Count;
    }

    private static string Standing(string status)
    {
        int start = status.IndexOf("Standing agents", StringComparison.Ordinal);
        Assert.True(start >= 0, status);
        int end = status.IndexOf("\n\n", start, StringComparison.Ordinal);
        return end < 0 ? status[start..] : status[start..end];
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
