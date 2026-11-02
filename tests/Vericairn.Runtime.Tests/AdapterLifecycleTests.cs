// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public class AdapterLifecycleTests
{
    [Fact]
    public async Task StartupFailureKeepsTheDirectRouteWithOneDispatchAndNoHookResidue()
    {
        using var upstream = new CountingAdapterUpstream();
        var launcher = new FixtureLauncher(upstream.Address);
        LaunchContext context = new("workspace", "condition", "brief", "mcp", ["vericairn"], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());
        using var run = new EfficiencyRun(launcher, context, RunEstate.Json("{}"), () => throw new IOException("startup unavailable"));
        Assert.Same(context, run.Context);
        Assert.Null(run.Context.AdapterAddress);
        Assert.Equal("direct-start-fallback", run.Measurement()["state"]!.GetValue<string>());
        using HttpClient client = AdapterTransportTests.Client();
        using HttpResponseMessage response = await client.PostAsync(new Uri(run.Context.AdapterAddress ?? upstream.Address, "responses"), AdapterTransportTests.Json("{}"u8.ToArray()), TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Single(upstream.Requests);
        Assert.Equal(0, launcher.Hooks);
    }

    [Fact]
    public void RunDisposalRemovesTheHookEvenWithoutAnyDispatch()
    {
        using var upstream = new CountingAdapterUpstream();
        var launcher = new FixtureLauncher(upstream.Address);
        LaunchContext context = new("workspace", "condition", "brief", "mcp", ["vericairn"], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());
        using (var run = new EfficiencyRun(launcher, context, RunEstate.Json("{}"))) Assert.NotNull(run.Context.AdapterAddress);
        Assert.Equal(2, launcher.Hooks);
        Assert.Null(launcher.Address);
        Assert.Empty(upstream.Requests);
    }

    private sealed class FixtureLauncher(Uri upstream) : IAgentLauncher
    {
        public string Launcher => "fixture";
        public ProviderDescriptor Descriptor => new("fixture", IAgentLauncher.Contract, "implemented", [], [], []);
        public IReadOnlyList<string> Command(LaunchContext context) => [];
        public Uri EfficiencyUpstream(LaunchContext context) => upstream;
        public int Hooks { get; private set; }
        public Uri? Address { get; private set; }
        public LaunchContext ConfigureEfficiencyHook(LaunchContext context, Uri? address)
        {
            Hooks++;
            Address = address;
            return context with { AdapterAddress = address };
        }
    }
}
