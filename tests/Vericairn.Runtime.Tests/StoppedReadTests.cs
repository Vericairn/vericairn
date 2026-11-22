// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// From 0.9.4 (FIX-152): a read stopped at its request limit carries the read, the limit and what the embedded runtime last reported it
/// was judging, here the judgment of the estate with the number of record files it judges; each operation reads only its own stage. The
/// read is held inside its judgment by a signal and stopped by the runtime's operation clock, never by a real wait.
/// </summary>
[Collection("home")]
public sealed class StoppedReadTests
{
    [Fact]
    public async Task AReadStoppedWhileTheRuntimeJudgesTheEstateNamesTheJudgmentAndItsRecordFiles()
    {
        using var estate = new TestEstate();
        using EstateService service = EstateService.Open(estate.Root);
        var clock = new ClientClock();
        service.OperationClock = clock;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int armed = 1;
        // A runtime that is not one-shot judges at every load; the first judgment after arming waits, still inside its stage.
        service.Session.Judged = _ =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            held.Set();
            release.Wait(TestTiming.HangTimeout);
        };
        int files = service.Session.Live.Snapshot().Files.Count;
        try
        {
            Task<StatusResponse> status = ((IEstateApi)service).StatusAsync(TestContext.Current.CancellationToken);
            Assert.True(held.Wait(TestTiming.HangTimeout, TestContext.Current.CancellationToken), "The status read did not reach its judgment.");
            // Another read on this runtime meanwhile reports nothing into the held read's operation.
            Assert.Equal(service.Health().Estate, (await ((IEstateApi)service).HealthAsync(TestContext.Current.CancellationToken)).Estate);
            clock.Elapse(TimeSpan.FromSeconds(30));
            ClientTimeoutException stopped = await Assert.ThrowsAsync<ClientTimeoutException>(() => status);

            Assert.False(stopped.OutcomeUnknown);
            Assert.Equal(new StoppedRead("Status", TimeSpan.FromSeconds(30),
                string.Create(CultureInfo.InvariantCulture, $"judging the estate: verifying and validating its {files:N0} record files")), stopped.Read);
            // The shared message, which MCP and Workbench show, is unchanged.
            Assert.Equal("The request timed out.", ClientFailure.FromException(stopped)!.Message);
        }
        finally
        {
            release.Set();
        }
    }
}
