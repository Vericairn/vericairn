// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class ChangesApiTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Merged = new('b', 40);
    private static readonly string Basis = new('c', 40);

    public ChangesApiTests() => DaemonTestHome.Ensure();

    [Fact(Timeout = TestTiming.HangTimeoutMilliseconds)]
    public async Task AllFourSdkOperationsRoundTripTheirRecordedOutcomesThroughTheLocalApi()
    {
        using var estate = new TestEstate(3);
        string repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""").Id;
        string product = estate.Add("product", estate.OperatorId, $$"""
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"memory-pull-requests","repository":"{{repository}}","target":"main","requiredChecks":["tests"]} }
            """).Id;
        RepositoryClones.Register(estate.Estate.Id, repository, estate.Root);
        var provider = new MemoryPullRequestProvider { Checks = [new("tests", Head, "completed", "success")] };
        var push = new LocalStandIn();
        TestContext.Current.TestOutputHelper!.WriteLine("Starting the local daemon.");
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine("The local daemon started.");
        host.Service.Providers = new() { PushAdapter = push, SecretScanner = push };
        host.Service.Providers.Register(provider);
        using var api = new VericairnClient(host.Endpoint);
        TestContext.Current.TestOutputHelper!.WriteLine("Checking the API's authority refusal.");
        Assert.False(api.ChangesPush(new(product, "work/change", Head, estate.BuilderId)).Ok);
        ChangeResponse pushed = api.ChangesPush(new(product, "work/change", Head, estate.OperatorId));
        TestContext.Current.TestOutputHelper!.WriteLine("The API returned the push outcome.");
        Assert.True(pushed.Ok, pushed.Message);
        Assert.NotNull(pushed.Operation);
        Assert.NotNull(pushed.Observation);
        ChangeResponse opened = api.ChangesOpen(new(product, "work/change", Head, "Change", "Review it.", estate.OperatorId));
        TestContext.Current.TestOutputHelper!.WriteLine("The API returned the opening outcome.");
        Assert.True(opened.Ok, opened.Message);
        ChangeResponse merged = api.ChangesMerge(new(product, opened.PullRequest!, Head, estate.OperatorId));
        TestContext.Current.TestOutputHelper!.WriteLine("The API returned the merge outcome.");
        Assert.Equal("observed-success", merged.Status);
        Assert.False(merged.Ok);
        Assert.Equal("pending", merged.CheckStatus);
        Assert.Equal(Merged, merged.MergeCommit);
        provider.SetTarget("Example/widget", new("main", Merged, [new("tests", Merged, "completed", "success")]));
        ChangeResponse observed = api.ChangesObserve(new(merged.Operation!.Id, estate.OperatorId));
        TestContext.Current.TestOutputHelper!.WriteLine("The API returned the target observation.");
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal(merged.Identity, observed.Identity);
        Assert.Equal(merged.Operation, observed.Operation);
        Assert.Equal("passed", observed.CheckStatus);
        Assert.Equal(2, provider.Effects);
        Assert.Equal(0, host.Service.Validate().Errors);
    }

    private sealed class LocalStandIn : IPushAdapter, ISecretScanner
    {
        private string? remoteHead;
        public PushPlan Prepare(string directory, string remote, string branch, string target, string head) => new(branch, target, head, Basis, null, Basis);
        public SecretScan Scan(string directory, PushPlan plan) => new("stand-in", "1.0.0", plan.Range, "passed", 0);
        public void Push(string directory, string remote, PushPlan plan, SecretScan scan)
        {
            Assert.True(scan.Passed(plan));
            remoteHead = plan.Head;
        }
        public string? Observe(string directory, string remote, string branch) => remoteHead;
    }
}
