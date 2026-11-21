// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (FIX-152; decision 01M7XFRC7KTTKD96NNBRWERHDZ): when status, human or --json, stops at the runtime's request limit, its
/// diagnostic names the limit, what the runtime was judging when it stopped and the lighter reads to use meanwhile, and writes nothing to
/// standard output. The read is held inside its repository checks by a signal and stopped by the runtime's operation clock, never by a
/// real wait; the limit is the one every read has, not raised, and the next status on the same runtime answers.
/// </summary>
[Collection("cli")]
public sealed class StatusRequestLimitCommandTests : IDisposable
{
    private readonly PolicyFixture fixture = new();

    public StatusRequestLimitCommandTests()
    {
        AddWorkOrder(0);
        AddWorkOrder(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusStoppedAtTheRequestLimitNamesTheLimitWhatWasJudgedAndTheLighterReads(bool json)
    {
        string root = fixture.Estate.Root;
        var clock = new ClientClock();
        var source = new HeldRepositoryChecks();
        using EstateService service = EstateService.Open(root, null, null, oneShot: true, judgments: new JudgmentContext(new SignatureTables()));
        service.HostsEmbedded = true;
        service.OperationClock = clock;
        var providers = new ProviderRegistry();
        providers.Register(source);
        service.Providers = providers;
        string[] status = json ? ["--estate", root, "status", "--json"] : ["--estate", root, "status"];
        source.Armed = true;
        try
        {
            Task<CliHarness.Outcome> stopped = Task.Run(() => CliHarness.RunWithApi(service, null, status), TestContext.Current.CancellationToken);
            await source.Entered.Task.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            Assert.False(stopped.IsCompleted);
            clock.Elapse(TimeSpan.FromSeconds(30));
            CliHarness.Outcome outcome = await stopped.WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);

            Assert.Equal(124, outcome.Code);
            Assert.Empty(outcome.Out);
            // Standard error also carries the release-check notice every estate command writes; the diagnostic is one line of its own.
            Assert.Equal(["error: The request timed out. The status read stopped at its 30-second request limit while the runtime was running the "
                + "repository checks (the working tree walk and the writing scan). Lighter reads to use meanwhile: vericairn operator list, "
                + "vericairn goal and vericairn estate check."], Diagnostics(outcome.Err));
        }
        finally
        {
            source.Armed = false;
            source.Release.TrySetResult();
        }

        // The stopped read finishes on its own; the next status on the same runtime waits for it and answers within the same limit.
        CliHarness.Outcome next = await Task.Run(() => CliHarness.RunWithApi(service, null, status), TestContext.Current.CancellationToken)
            .WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        Assert.True(next.Code == 0, next.Err);
        Assert.Empty(Diagnostics(next.Err));
        Assert.Contains(json ? "\"workOrders\"" : "estate status", next.Out, StringComparison.Ordinal);
    }

    public void Dispose() => fixture.Dispose();

    private static string[] Diagnostics(string error) =>
        error.Split('\n').Where(line => line.StartsWith("error:", StringComparison.Ordinal) || line.StartsWith("warning:", StringComparison.Ordinal)).ToArray();

    private void AddWorkOrder(int index)
    {
        RecordEnvelope order = fixture.Add("work-order", new()
        {
            ["key"] = "TASK-" + index.ToString("D3", CultureInfo.InvariantCulture), ["kind"] = "ordinary", ["title"] = "Limited work",
            ["intent"] = "Exercise the request limit.", ["scope"] = new JsonArray("test fixtures"), ["exitQuestion"] = "Is the result ready?",
            ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("checked result"),
        });
        fixture.BeginAttempt(order: order);
    }

    /// <summary>A source adapter whose ignored-entries read, part of the repository checks, holds once armed until the test releases it.</summary>
    private sealed class HeldRepositoryChecks : ISourceAdapter
    {
        internal TaskCompletionSource Entered { get; } = TestTiming.Signal();

        internal TaskCompletionSource Release { get; } = TestTiming.Signal();

        private volatile bool armed;

        internal bool Armed { get => armed; set => armed = value; }

        public ProviderDescriptor Descriptor { get; } = new("held-source", ISourceAdapter.Contract, "implemented", ["inspect"], ["Test stand-in; observes no real repository."]);

        public bool IsRepository(string path) => false;

        public RepositoryObservation Inspect(string path) => throw new ProviderRefusedException("Not a repository.");

        public IReadOnlySet<string>? IgnoredEstateEntries(string root)
        {
            if (!Armed) return null;
            Entered.TrySetResult();
            // A time limit only against a hang: the test releases the hold once it has observed the stopped read.
            Assert.True(Release.Task.Wait(TestTiming.HangTimeout), "The held repository checks were never released.");
            return null;
        }
    }
}
