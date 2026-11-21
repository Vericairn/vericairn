// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// OPS-21 through the worker: the drill starts a real scratch run under the worker's supervision, stops it as worker stop does, and records
/// the time from the stop to containment and to the run's last write. The scratch run ignores the stop, so containment is by escalation.
/// </summary>
public sealed class StopDrillTests
{
    [Fact]
    public void ADrillAgainstAScratchRunRecordsTheTimeToContainmentAndToItsLastWrite()
    {
        using var f = new DrillFixture();
        StopDrillResponse drill = f.Api.StopDrill(new(f.Operator, GraceSeconds: 0, LimitSeconds: 60));
        Assert.True(drill.Ok, drill.Message);
        Assert.True(drill.Passed, drill.Message);
        Assert.NotNull(drill.RequestedAt);
        Assert.NotNull(drill.ContainedAt);
        long contained = Assert.IsType<long>(drill.StopToContainedMilliseconds);
        long written = Assert.IsType<long>(drill.StopToLastWriteMilliseconds);
        Assert.InRange(contained, 0, 60_000);
        Assert.InRange(written, 0, 60_000);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", drill.LastWriteAt);

        RecordEnvelope verification = f.Api.Session.Load().ById[drill.Verification!.Id];
        Assert.Equal("verification", verification.Type);
        Assert.Equal(f.Api.Session.Load().CurrentEstate!.Reference, verification.Subject);
        Assert.Equal(("stop-drill", "passed", "automation", "vericairn worker drill"), (Body(verification, "method"), Body(verification, "outcome"), Body(verification, "role"), Body(verification, "command")));
        Assert.Matches("^[0-9a-f]{40}$", Body(verification, "commit"));
        Assert.Equal(f.Device, verification.Body.GetProperty("environment").GetProperty("device").GetString());
        string[] evidence = [.. verification.Body.GetProperty("evidence").EnumerateArray().Select(e => e.GetProperty("summary").GetString()!)];
        Assert.Contains(evidence, e => e == $"From the stop request to the observed containment of the scratch run's whole boundary: {contained} milliseconds.");
        Assert.Contains(evidence, e => e == $"From the stop request to the scratch run's last write, at {drill.LastWriteAt}: {written} milliseconds.");
        // The scratch run ignored the stop it was sent, so the worker escalated: the longest path a stop takes.
        Assert.Contains(evidence, e => e.Contains(" and escalated it at ", StringComparison.Ordinal));
        Assert.Contains("within the limit of 60 seconds", Body(verification, "summary"), StringComparison.Ordinal);
        Assert.DoesNotContain(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectId == verification.Id);
        Assert.False(Directory.Exists(f.RunsRoot) && Directory.EnumerateDirectories(f.RunsRoot).Any());
        Assert.Empty(f.Api.Runs().Runs);
    }

    [Fact]
    public void ARunThatDoesNotStopWithinTheLimitFailsTheDrillAndIsAttentionUntilALaterDrillPasses()
    {
        using var f = new DrillFixture();
        StopDrillResponse late = f.Api.StopDrill(new(f.Operator, GraceSeconds: 600, LimitSeconds: 1));
        Assert.True(late.Ok, late.Message);
        Assert.False(late.Passed);
        Assert.Contains("not contained within the limit of 1 seconds", late.Message, StringComparison.Ordinal);
        Assert.Contains("required attention until a later drill on this machine passes", late.Message, StringComparison.Ordinal);
        // Past its limit the drill contains the scratch run itself, so nothing is left running, and the times are still recorded.
        Assert.NotNull(late.StopToContainedMilliseconds);
        Assert.True(late.StopToContainedMilliseconds >= 1000);
        RecordEnvelope failed = f.Api.Session.Load().ById[late.Verification!.Id];
        Assert.Equal("failed", Body(failed, "outcome"));
        Assert.Contains(failed.Body.GetProperty("limitations").EnumerateArray(), l => l.GetString() == "The limit passed before containment was observed, so the drill escalated the stop itself.");
        AttentionNotice notice = Assert.Single(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectId == failed.Id);
        Assert.Equal("failed-check", EstateService.Category(notice.Kind));

        StopDrillResponse passed = f.Api.StopDrill(new(f.Operator, GraceSeconds: 0, LimitSeconds: 60));
        Assert.True(passed.Passed, passed.Message);
        Assert.DoesNotContain(f.Api.AttentionGet(f.Operator).Items, n => n.SubjectId == failed.Id);
    }

    [Fact]
    public void ADrillIsRefusedBeforeAnythingStartsWithoutADeviceToSignItOrWithinItsBounds()
    {
        using var f = new DrillFixture();
        StopDrillResponse unsigned = f.Api.StopDrill(new(f.Builder));
        Assert.False(unsigned.Ok);
        Assert.Equal("refused", unsigned.Problem!.Code);
        Assert.Contains("Nothing was started.", unsigned.Message, StringComparison.Ordinal);
        Assert.Equal("refused", f.Api.StopDrill(new(f.Operator, LimitSeconds: 0)).Problem!.Code);
        Assert.Equal("refused", f.Api.StopDrill(new(f.Operator, GraceSeconds: 3601)).Problem!.Code);
        Assert.Equal("refused", f.Api.StopDrill(new("not-a-principal")).Problem!.Code);
        Assert.False(Directory.Exists(f.RunsRoot) && Directory.EnumerateDirectories(f.RunsRoot).Any());
        Assert.Empty(f.Api.Records("verification"));
    }

    private static string? Body(RecordEnvelope record, string name) => record.Body.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;

    private sealed class DrillFixture : IDisposable
    {
        internal TestEstate Estate { get; } = new(2);
        internal EstateService Api { get; }
        internal string Device { get; }
        internal string Operator => Estate.OperatorId;
        internal string Builder => Estate.BuilderId;
        internal string RunsRoot => Path.Combine(Records.Signing.VericairnHome.Path(), "estates", Api.Session.Manifest.Estate, "runs");

        internal DrillFixture()
        {
            Api = EstateService.Open(Estate.Root);
            Api.WorkerOptions = new WorkerOptions
            {
                Heartbeat = TimeSpan.FromMilliseconds(50),
                Environment = WorkerEnvironment.Current(),
                Vericairn = FakeHarness.ScopeCommand(),
            };
            Device = Api.DeviceCreate(new DeviceCreateRequest(Operator, "Operator's laptop", "file")).Written!.Id;
            Assert.True(Api.DeviceAdmit(new AdmitRequest(Device, Operator, "The first authority.")).Ok);
        }

        public void Dispose()
        {
            Api.Dispose();
            Estate.Dispose();
        }
    }
}
