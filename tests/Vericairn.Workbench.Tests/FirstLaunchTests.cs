// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Workbench.Tests;

/// <summary>The real fixture runtime behind a metered connection; only daemon process/transport startup is replaced.</summary>
[Collection("workbench")]
public class FirstLaunchTests(HeadlessFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task AFailedSharedProgressReadStillProducesMandatoryUnknownAttention()
    {
        using var estate = new TestEstate();
        string preferences = Seed.Temp("first-launch-failed-progress");
        using var connection = new MeteredConnection(estate.Root) { FailProgress = true };
        var shell = new ShellViewModel(_ => connection, new StartingRuntime(connection), new Preferences(preferences));
        try
        {
            await fixture.UiAsync(() => shell.OpenEstateAsync(estate.Root));
            Assert.Equal(1, connection.Meter.Calls.Count(c => c.Name == "Progress"));
            Assert.Contains(shell.Attention, item => item.Kind == "unknown-check" && item.Why.Contains("fixture progress unavailable", StringComparison.Ordinal));
            connection.Meter.FailProgress = false;
            await fixture.Ui(shell.Refresh);
            Assert.DoesNotContain(shell.Attention, item => item.Why.Contains("fixture progress unavailable", StringComparison.Ordinal));
            Assert.NotEmpty(shell.Progress);
        }
        finally
        {
            Directory.Delete(preferences, recursive: true);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public async Task FirstLaunchReadsEachHomeProjectionOnceRegardlessOfUnshownHistory(int records)
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"PERF","kind":"ordinary","title":"Budget","intent":"Measure.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        for (int i = 4; i < records; i++)
        {
            estate.Add("work-status", estate.BuilderId, """{"status":"active","note":"Generated for the first launch measurement."}""", new RecordReference("work-order", order.Id, null));
        }

        string preferences = Seed.Temp("first-launch-prefs");
        using var connection = new MeteredConnection(estate.Root);
        var shell = new ShellViewModel(_ => connection, new StartingRuntime(connection), new Preferences(preferences)) { ActingOverride = estate.OperatorId };
        try
        {
            var watch = Stopwatch.StartNew();
            await fixture.UiAsync(() => shell.OpenEstateAsync(estate.Root));
            output.WriteLine($"{records} records: open {watch.Elapsed.TotalMilliseconds:F2} ms, service start {connection.StartMilliseconds:F2} ms");
            foreach (var group in connection.Meter.Calls.GroupBy(c => c.Name))
            {
                output.WriteLine($"  {group.Key}: {group.Count()} calls, {group.Sum(c => c.Milliseconds):F2} ms");
            }

            Assert.Equal("Test Estate", shell.EstateName);
            Assert.Equal("active", Assert.Single(shell.WorkOrders).Status);
            Assert.Equal("daemon", shell.RuntimeMode);
            Assert.Empty(shell.Attention);
            Assert.NotEmpty(shell.Progress);
            Assert.Contains("Test Operator", shell.ActingPrincipal, StringComparison.Ordinal);
            Assert.Contains("PERF", shell.HumanStatus, StringComparison.Ordinal);
            Assert.Equal(1, connection.Meter.Calls.Count(c => c.Name == "Status"));
            Assert.Equal(1, connection.Meter.Calls.Count(c => c.Name == "Progress"));
            Assert.Equal(1, connection.Meter.Calls.Count(c => c.Name == "Records:principal"));
            // Exactly one materialization and envelope parse per original for initial validation. None of the subsequent
            // first-screen projections rereads or reparses the growing history it does not display.
            Assert.Equal(records, connection.Service.Session.ParsedFiles);
            Assert.Equal(records, connection.Service.Session.Live.FilesRead);

            // A later refresh must still observe additions and principal revisions, not keep a session-wide UI cache.
            estate.Add("work-status", estate.BuilderId, """{"status":"submitted","note":"Ready for review."}""", new RecordReference("work-order", order.Id, null));
            estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Renamed Operator","roles":["operator"]}""", supersedes: estate.OperatorId);
            await fixture.Ui(shell.Refresh);
            Assert.Equal("submitted", Assert.Single(shell.WorkOrders).Status);
            Assert.Contains("Renamed Operator", shell.ActingPrincipal, StringComparison.Ordinal);
            Assert.Equal(records + 2, connection.Service.Session.ParsedFiles);
        }
        finally
        {
            Directory.Delete(preferences, recursive: true);
        }
    }

    private sealed class MeteredConnection(string root) : IEstateConnection
    {
        private EstateService? service;
        public string Root => root;
        public IEstateApi? Api { get; private set; }
        public string Mode => Api is null ? "unavailable" : "daemon";
        public string Where => "Fixture service in process";
        public ApiMeter Meter => (ApiMeter)Api!;
        public EstateService Service => service!;
        public bool FailProgress { get; init; }
        public double StartMilliseconds { get; private set; }
        public void Refresh() { }
        public void Start()
        {
            var watch = Stopwatch.StartNew();
            service = EstateService.Open(root);
            Api = DispatchProxy.Create<IEstateApi, ApiMeter>();
            Meter.Target = service;
            Meter.FailProgress = FailProgress;
            StartMilliseconds = watch.Elapsed.TotalMilliseconds;
        }
        public void Dispose() => service?.Dispose();
    }

    private sealed class StartingRuntime(MeteredConnection connection) : IRuntimeManager
    {
        public Task<RuntimeOutcome> StartAsync(string root)
        {
            connection.Start();
            return Task.FromResult(new RuntimeOutcome(true, "Fixture service started"));
        }
        public Task<RuntimeOutcome> StopAsync(string root) => throw new NotSupportedException();
        public Task<RuntimeOutcome> InstallPlanAsync(string root) => throw new NotSupportedException();
        public Task<RuntimeOutcome> InstallAsync(string root) => throw new NotSupportedException();
        public Task<RuntimeOutcome> UninstallAsync(string root) => throw new NotSupportedException();
    }

    public class ApiMeter : DispatchProxy
    {
        public IEstateApi Target { get; set; } = null!;
        public List<(string Name, double Milliseconds)> Calls { get; } = [];
        public bool FailProgress { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                if (FailProgress && targetMethod!.Name == "Progress") { throw new IOException("fixture progress unavailable"); }
                return targetMethod!.Invoke(Target, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            finally
            {
                string name = targetMethod!.Name + (args?.FirstOrDefault() is string first ? ":" + first : string.Empty);
                Calls.Add((name, watch.Elapsed.TotalMilliseconds));
            }
        }
    }
}
