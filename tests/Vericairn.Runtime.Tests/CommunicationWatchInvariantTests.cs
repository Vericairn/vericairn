// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class CommunicationWatchInvariantTests
{
    public static TheoryData<string, string> Profiles
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string frequency in CommunicationResolution.Frequencies)
            {
                foreach (string depth in CommunicationResolution.Depths) { data.Add(frequency, depth); }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void WatchObservationsAndContainmentIgnoreEveryDepthAndFrequency(string frequency, string depth)
    {
        // RunEstate always supplies FakeHarness.Setting as the command; no real coding agent is launched.
        using var run = new RunEstate(keepOperatorKey: true);
        RunEstate.Ok(run.Service.CommunicationSet(new(run.Estate.OperatorId, "person", new(frequency, depth, depth, depth, depth), run.Estate.OperatorId)));
        // Match the normal RunEstate custody boundary after the person signs their preference.
        new FileKeyStore(VericairnHome.Path()).Remove(run.OperatorDevice);
        RunResponse started = run.Start("codex", "watch-codex-loop", run.Report("comms-watch"));
        Assert.True(started.Ok, started.Message);
        RunRow settled = run.Settled(started.Run);
        Assert.Equal("known-failure", settled.Status);
        Assert.Equal("worker", settled.Stop!.RequestedBy);
        Assert.NotNull(settled.Stop.ContainedAt);
        RunSignal[] signals = settled.Signals!.Where(s => s.Code == "loop").ToArray();
        Assert.Equal([5, 10], signals.Select(s => s.Count));
        Assert.Equal(["warning", "critical"], signals.Select(s => s.Severity));
        Assert.All(signals, s => Assert.NotNull(s.Attention));
        Assert.Equal(signals.Length, run.Service.Records("ask").Count);
        string identity = settled.Identity;
        RecordResponse operation = run.Service.Record(AgentRuns.Find(run.Estate.Load(), started.Run)!.Latest.Id)!;
        byte[] original = CanonicalJson.SerializePrettyUtf8(operation.Envelope);
        int count = run.Estate.Load().Records.Count;
        ProgressResponse progress = run.Service.Progress();
        CommunicationProfile profile = run.Service.Communication(run.Estate.OperatorId);
        IReadOnlyList<CommunicationAttention> attention = CommunicationPresentation.Attention(run.Service, run.Estate.OperatorId);
        Assert.True(CommunicationPresentation.ShouldDeliver(frequency, "progress", attention));
        foreach (RunSignal signal in signals)
        {
            Assert.Contains(signal.Summary, progress.Text, StringComparison.Ordinal);
            Assert.Contains(signal.Summary, CommunicationPresentation.RenderAttention(attention), StringComparison.Ordinal);
            Assert.Contains(progress.Json.GetProperty("runSignals").EnumerateArray(), r => r.GetProperty("run").GetString() == started.Run &&
                r.GetProperty("signals").EnumerateArray().Any(s => s.GetProperty("attention").GetString() == signal.Attention));
        }

        string human = CommunicationPresentation.Protected(profile, "result", identity, "Worker observations follow.", progress.Text);
        Assert.EndsWith(progress.Text, human, StringComparison.Ordinal);
        Assert.Equal(original, CanonicalJson.SerializePrettyUtf8(run.Service.Record(operation.Id)!.Envelope));
        Assert.Equal(count, run.Estate.Load().Records.Count);
        run.Reopen();
        Assert.Equal(identity, run.Row(started.Run).Identity);
        Assert.Equal(original, CanonicalJson.SerializePrettyUtf8(run.Service.Record(operation.Id)!.Envelope));
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
