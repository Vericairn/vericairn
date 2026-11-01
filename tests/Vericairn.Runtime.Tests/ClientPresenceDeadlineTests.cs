// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// An operation that may write can sign with a device that waits up to five minutes for its owner's presence. Its client keeps
/// observing for longer than that, so a Touch ID prompt answered in time is not reported as an unknown outcome (FIX-78, issue 251);
/// a timeout after the window still reports an unknown outcome, and nothing retries.
/// </summary>
public sealed class ClientPresenceDeadlineTests
{
    [Fact]
    public void EveryWriteIsObservedForLongerThanThePresenceWait()
    {
        Assert.Equal(DeviceKeyStores.PresenceWait, ClientOperation.PresenceWait);
        Assert.True(ClientOperation.EffectDeadline > ClientOperation.PresenceWait);
        foreach (string operation in new[] { "NewRecord", "WorkNew", "Assign", "Attempt", "GoalActivate", "DeviceRevoke", "OperatorRequestWrite", "PackagesDeprecate" })
        {
            Assert.Equal(ClientOperation.EffectDeadline, ClientOperation.Deadline(operation, mayHaveEffect: true));
        }

        // A longer named deadline is kept, and a read keeps its short one.
        Assert.Equal(TimeSpan.FromMinutes(15), ClientOperation.Deadline("RunStart", mayHaveEffect: true));
        Assert.Equal(TimeSpan.FromSeconds(30), ClientOperation.Deadline("NewRecord", mayHaveEffect: false));
        Assert.Equal(TimeSpan.FromSeconds(30), ClientOperation.Deadline("Health"));
    }

    [Fact]
    public async Task AWriteWaitingOnPresenceIsStillObservedAtFiveMinutes()
    {
        var clock = new ClientClock();
        var signed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<string> observed = ClientOperation.RunAsync("NewRecord", _ => { calls++; return signed.Task; }, () => true, null, clock, TestContext.Current.CancellationToken);

        clock.Elapse(TimeSpan.FromSeconds(35));
        Assert.False(observed.IsCompleted);
        clock.Elapse(TimeSpan.FromSeconds(270));
        Assert.False(observed.IsCompleted);
        signed.SetResult("written");

        Assert.Equal("written", await observed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AWritePastTheWindowReportsAnUnknownOutcomeAndIsNotRetried()
    {
        var clock = new ClientClock();
        var signed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<string> observed = ClientOperation.RunAsync("NewRecord", _ => { calls++; return signed.Task; }, () => true, null, clock, TestContext.Current.CancellationToken);

        clock.Elapse(ClientOperation.EffectDeadline);
        ClientTimeoutException failure = await Assert.ThrowsAsync<ClientTimeoutException>(() => observed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(failure.OutcomeUnknown);
        Assert.Equal(1, calls);
        signed.SetResult("written later");
    }

    [Fact]
    public async Task AReadKeepsItsShortDeadline()
    {
        var clock = new ClientClock();
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> observed = ClientOperation.RunAsync("Health", _ => answer.Task, () => false, null, clock, TestContext.Current.CancellationToken);

        clock.Elapse(TimeSpan.FromSeconds(30));
        ClientTimeoutException failure = await Assert.ThrowsAsync<ClientTimeoutException>(() => observed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(failure.OutcomeUnknown);
        answer.SetResult("late");
    }
}
