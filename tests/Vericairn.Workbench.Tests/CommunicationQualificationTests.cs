// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;

namespace Vericairn.Workbench.Tests;

[Collection("workbench")]
public class CommunicationQualificationTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task D08A_TwoPeopleSeeTheirOwnChosenDepthAndAnEphemeralViewDoesNotPersist()
    {
        string root = Seed.Temp("wb-comms-qualification");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-comms-prefs"))) { ActingOverride = person };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root));
        IEstateApi api = shell.Api!;
        WriteOutcome added = api.NewRecord(new("principal", person, Seed.Json("""{"kind":"human","name":"Reviewer","roles":["contributor"]}""")));
        Assert.True(added.Ok, added.Problem?.Message);
        string other = added.Written!.Id;
        foreach ((string recipient, string depth, string decision) in new[] { (person, "short", "comprehensive"), (other, "comprehensive", "standard") })
        {
            await fixture.Ui(() => shell.ActingOverride = recipient);
            CommunicationDocument page = await fixture.Ui(shell.OpenCommunication);
            await fixture.Ui(() => { page.StatusDepth = depth; page.DecisionDepth = decision; page.HandoffDepth = depth; page.Save(); });
            CommunicationProfile profile = api.Communication(recipient);
            Assert.Contains($"status ({depth})", shell.HumanStatus, StringComparison.Ordinal);
            Assert.Contains($"decision: {decision} ← person", page.Effective, StringComparison.Ordinal);
            string savedHash = profile.Revisions.Single().Hash!;
            var mcp = new ToolRegistry(api, null);
            ToolCallResult read = mcp.Call("communication_get", new JsonObject { ["recipient"] = recipient });
            Assert.False(read.IsError);
            CommunicationProfile served = JsonSerializer.Deserialize<CommunicationProfile>(read.Text, ProtocolJson.Options)!;
            Assert.Equal(profile.RevisionHash, served.RevisionHash);
            Assert.Equal(savedHash, served.Revisions.Single().Hash);
        }

        await fixture.Ui(() => { shell.ActingOverride = person; shell.Refresh(); });
        Assert.Contains("status (short)", shell.HumanStatus, StringComparison.Ordinal);
        string canonical = api.Status().Json.GetRawText();
        int settings = api.Records("communication-setting").Count;
        foreach (string depth in CommunicationResolution.Depths)
        {
            await fixture.Ui(() => shell.StatusViewDepth = depth);
            Assert.Contains($"status ({depth})", shell.HumanStatus, StringComparison.Ordinal);
            CommunicationDocument page = await fixture.Ui(shell.OpenCommunication);
            Assert.Contains("status: short ← person", page.Effective, StringComparison.Ordinal);
            Assert.Equal(canonical, api.Status().Json.GetRawText());
        }

        Assert.Equal(settings, api.Records("communication-setting").Count);
        Assert.Equal("short", api.Communication(person).Fields["status"].Value);
        Assert.Equal("comprehensive", api.Communication(other).Fields["status"].Value);
        await fixture.Ui(() => shell.StatusViewDepth = "inherit");
        Assert.Contains("status (short)", shell.HumanStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D08B_QuietShortWorkbenchKeepsFailedChecksUncertainEffectsAndTheWholeAsk()
    {
        string root = Seed.Temp("wb-comms-attention");
        string person = Seed.Init(root);
        var shell = new ShellViewModel(r => new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-comms-attention-prefs"))) { ActingOverride = person };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root));
        IEstateApi api = shell.Api!;
        Assert.True(api.NewRecord(new("work-order", person, Seed.Json("""{"key":"COMMS-J","kind":"ordinary","title":"Interrupted work","intent":"Inspect before retrying.","scope":["tests"],"exitQuestion":"Reconciled?","inputs":[],"deliverables":["results"]}"""))).Ok);
        WriteOutcome assigned = api.Assign(new("COMMS-J", person, person, Seed.Json("""{"outcome":"Check the result.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no retry"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["checks pass"],"recovery":"Inspect the original effect."}""")));
        Assert.True(assigned.Ok, assigned.Problem?.Message);
        string attempt = api.Attempt(new(assigned.Written!.Id, person, "local clone", "Inspect.")).Written!.Id;
        WriteOutcome result = api.Result(new(attempt, person, Seed.Json("""{"outcome":"partial","summary":"Needs attention.","outputs":[],"evidence":[],"checks":[{"name":"journey","outcome":"failed","summary":"The response was incomplete."}],"limitations":[],"unresolvedEffects":["The original widget effect remains uncertain."],"nextStep":"Inspect before retrying."}""")));
        Assert.True(result.Ok, result.Problem?.Message);
        WriteOutcome ask = api.NewRecord(new("ask", person, Seed.Json($$"""{"to":"{{person}}","question":"Retry?","recommendation":"Inspect first.","options":["Reconcile the original effect.","Abandon the retry."],"consequence":"The work waits without a retry.","state":"open"}""")));
        Assert.True(ask.Ok, ask.Problem?.Message);
        Assert.True(api.CommunicationSet(new(person, "person", new("none", "short", "short", "short", "short"), person)).Ok);
        await fixture.Ui(shell.Refresh);
        Assert.Contains("status (short)", shell.HumanStatus, StringComparison.Ordinal);
        string attention = string.Join('\n', shell.Attention.Select(item => item.Why));
        foreach (string required in new[] { result.Written!.Id, "The response was incomplete.", "The original widget effect remains uncertain.", ask.Written!.Id, "Inspect first.", "Reconcile the original effect.", "Abandon the retry.", "The work waits without a retry." })
        {
            Assert.Contains(required, attention, StringComparison.Ordinal);
        }

        Assert.Empty(api.Records("acceptance"));
        Assert.Equal("open", api.Record(ask.Written.Id)!.Envelope.GetProperty("body").GetProperty("state").GetString());
    }
}
