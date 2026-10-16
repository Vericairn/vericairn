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
}
