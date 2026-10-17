// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class ReleaseDispatchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("known-failure")]
    [InlineData("canceled")]
    public async Task AnOverlappingSubmissionDispatchesOnceAndDoesNotHoldTheServiceGate(string? previous)
    {
        using var fixture = new ReleaseFixture(previous);
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        fixture.Provider.OnDispatch = count =>
        {
            if (count == 1)
            {
                entered.Set();
                Assert.True(finish.Wait(TimeSpan.FromSeconds(15)));
            }
        };
        Task<ReleaseResponse> first = Task.Run(() => fixture.Service.ReleaseSubmit(fixture.Request));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            await Task.Run(() => fixture.Service.Health()).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            ReleaseResponse second = await Task.Run(() => fixture.Service.ReleaseSubmit(fixture.Request)).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(1, fixture.Provider.Calls);
            Assert.False(second.Ok);
            Assert.Contains("observe and reconcile", second.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, fixture.Provider.Calls);
            ReleaseResponse cancel = fixture.Service.ReleaseObserve(new ReleaseObserveRequest("1.0.0", "Example/widget", fixture.Estate.OperatorId, Cancel: "No effect found."));
            Assert.False(cancel.Ok);
        }
        finally
        {
            finish.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.True((await first).Ok, (await first).Message);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(0, fixture.Service.Validate().Errors);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("known-failure", false)]
    [InlineData("canceled", false)]
    [InlineData(null, true)]
    public void ADispatchWithoutAStoredReceiptCannotDispatchAgainAfterReopening(string? previous, bool crash)
    {
        using var fixture = new ReleaseFixture(previous);
        if (crash)
        {
            fixture.Provider.OnDispatch = _ => throw new IOException("Simulated interruption after the external effect.");
            Assert.Throws<IOException>(() => fixture.Service.ReleaseSubmit(fixture.Request));
        }
        else
        {
            // The effect happened, but its invalid receipt cannot be recorded.
            fixture.Provider.InvalidReceipt = true;
            Assert.False(fixture.Service.ReleaseSubmit(fixture.Request).Ok);
        }

        Assert.Equal(1, fixture.Provider.Calls);
        fixture.Reopen();
        fixture.Provider.OnDispatch = null;
        fixture.Provider.InvalidReceipt = false;
        ReleaseResponse retried = fixture.Service.ReleaseSubmit(fixture.Request);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.False(retried.Ok);
        Assert.Equal("uncertain", retried.Status);
        Assert.Contains("observe and reconcile", retried.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Equal(0, fixture.Service.Validate().Errors);
    }

    [Fact]
    public void AnUnreceiptedIntentCanBeObservedAndReconciledButIsNeverAssumedNotDispatched()
    {
        using var fixture = new ReleaseFixture("not-started");
        ReleaseResponse observed = fixture.Service.ReleaseObserve(new ReleaseObserveRequest("1.0.0", "Example/widget", fixture.Estate.OperatorId));
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal("uncertain", observed.Status);
        Assert.Contains("no run is identified", observed.Pending, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Provider.Calls);
        ReleaseResponse canceled = fixture.Service.ReleaseObserve(new ReleaseObserveRequest("1.0.0", "Example/widget", fixture.Estate.OperatorId, Cancel: "The provider confirms no run or release exists."));
        Assert.True(canceled.Ok, canceled.Message);
        Assert.True(fixture.Service.ReleaseSubmit(fixture.Request).Ok);
        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("known-failure")]
    [InlineData("canceled")]
    public void AReconciledRetryRecordsItsActualSettingsBeforeTheProviderRuns(string previous)
    {
        using var fixture = new ReleaseFixture(previous);
        fixture.Provider.OnDispatch = _ =>
        {
            RecordEnvelope claim = Assert.Single(fixture.Service.Session.Load().Current("operation"));
            Assert.Equal("uncertain", claim.Body.GetProperty("status").GetString());
            Assert.Equal("release-fix", claim.Body.GetProperty("target").GetProperty("ref").GetString());
            Assert.Equal("fixed-release.yml", claim.Body.GetProperty("target").GetProperty("workflow").GetString());
            Assert.Contains("release-fix", claim.Body.GetProperty("intent").GetString(), StringComparison.Ordinal);
        };

        ReleaseResponse result = fixture.Service.ReleaseSubmit(fixture.Request with { Ref = "release-fix", Workflow = "fixed-release.yml" });
        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, fixture.Provider.Calls);
    }

    private sealed class ReleaseFixture : IDisposable
    {
        public TestEstate Estate { get; } = new(recordFormat: 2);
        public TestProvider Provider { get; } = new();
        public EstateService Service { get; private set; }
        public ReleaseSubmitRequest Request { get; }

        public ReleaseFixture(string? previous)
        {
            RecordEnvelope product = Estate.Add("product", Estate.OperatorId, """
                {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}],
                 "release":{"destination":{"provider":"github-releases","settings":{"repository":"Example/widget"}}}}
                """);
            Request = new ReleaseSubmitRequest(product.Id, "1.0.0", "Example/widget", Estate.OperatorId, Reconciliation: "The previous attempt was confirmed to have no effect.");
            if (previous is not null)
            {
                JsonObject intent = ReleaseFlow.IntentBody(Provider.Provider, "Example/widget", new Dictionary<string, string> { ["repository"] = "Example/widget" }, "1.0.0", product.Id);
                RecordEnvelope original = Estate.Add("operation", Estate.OperatorId, intent.ToJsonString(), new RecordReference("product", product.Id, null));
                if (previous != "not-started")
                {
                    JsonObject body = previous == "canceled" ? ReleaseFlow.Canceled(original.Body, "No effect.") : ReleaseFlow.AfterRefusal(original.Body, "No effect.");
                    Estate.Add("operation", Estate.OperatorId, body.ToJsonString(), original.Subject, original.Id);
                }
            }

            Service = Open();
        }

        public void Reopen()
        {
            Service.Dispose();
            Service = Open();
        }

        private EstateService Open()
        {
            var service = EstateService.Open(Estate.Root);
            var registry = new ProviderRegistry();
            registry.Register(Provider);
            service.Providers = registry;
            return service;
        }

        public void Dispose()
        {
            Service.Dispose();
            Estate.Dispose();
        }
    }

    private sealed class TestProvider : IReleaseProvider
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public Action<int>? OnDispatch { get; set; }
        public bool InvalidReceipt { get; set; }
        public string Provider => "github-releases";
        public ProviderDescriptor Descriptor { get; } = new FakeReleaseProvider().Descriptor;

        public DispatchReceipt Dispatch(DispatchRequest request)
        {
            int count = Interlocked.Increment(ref calls);
            OnDispatch?.Invoke(count);
            return new DispatchReceipt("1000", "https://example.invalid/runs/1000", "The workflow was dispatched.", InvalidReceipt ? "invalid" : RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
        }

        public RunObservation? ObserveRun(string destination, string runId) => null;
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) => null;
        public string ManualPublicationCommand(string destination, string tag) => "Publish manually.";
    }
}
