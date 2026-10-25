// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public sealed class OperatorRequestAttentionTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task AttentionDeliversThreeRequestsAndAdvancesAfterASignedClose()
    {
        using var estate = new TestEstate(3);
        var work = estate.Add("work-order", estate.OperatorId,
            """{"key":"REQUESTS","kind":"ordinary","title":"Operator requests","intent":"Finish the work.","scope":["requests"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("request-attention-prefs"))) { ActingOverride = estate.OperatorId };
        await fixture.UiAsync(() => shell.OpenEstateAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken));
        IEstateApi api = shell.Api!;
        var ids = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            WriteOutcome written = api.OperatorRequestWrite(new(estate.BuilderId, JsonSerializer.SerializeToElement(new
            {
                to = estate.OperatorId, askedBy = estate.BuilderId, action = "Action " + i, why = "Finish the work.",
                recommendation = "Recommendation " + i, consequence = "The work waits.", kind = "action", destination = "The work's destination",
                content = new[] { new { kind = "text", text = "Exact draft.", hash = ContentHash.Sha256(Encoding.UTF8.GetBytes("Exact draft.")) } },
            }), "work-order:" + work.Id + ":" + work.ComputedHash));
            Assert.True(written.Ok, written.Problem?.Message);
            ids.Add(written.Written!.Id);
        }
        await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
        var delivered = shell.Attention.Where(a => a.Kind == "operator-request" && a.State == "open").ToList();
        Assert.Equal(ids.Take(3), delivered.Select(a => a.SubjectId));
        Assert.All(delivered, a => Assert.StartsWith("Recommendation:", a.Why, StringComparison.Ordinal));
        Assert.Contains(ids[4], Assert.Single(shell.Attention, a => a.Kind == "operator-request" && a.State == "queued").Why, StringComparison.Ordinal);
        await fixture.UiAsync(() => shell.OpenAttentionAsync(delivered[0], cancellationToken: TestContext.Current.CancellationToken));
        CompanionDocument page = Assert.IsType<CompanionDocument>(shell.SelectedDocument);
        Assert.Equal(ids[0], page.Request.Revision);
        Assert.Contains(ids[0], page.Text, StringComparison.Ordinal);
        Assert.Equal(ids[0], delivered[0].SubjectId);
        WriteOutcome device = api.DeviceCreate(new(estate.OperatorId, "Operator test device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        Assert.True(api.DeviceAdmit(new(device.Written!.Id, estate.OperatorId, "Admit the test device.")).Ok);
        Assert.True(api.OperatorRequestDone(ids[0], new(estate.OperatorId)).Ok);
        await fixture.UiAsync(() => shell.RefreshAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ids.Skip(1).Take(3), shell.Attention.Where(a => a.Kind == "operator-request" && a.State == "open").Select(a => a.SubjectId));
        Assert.Single(api.Records("operator-request-close"));
    }
}
