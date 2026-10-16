// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public class CommunicationQualificationTests
{
    public CommunicationQualificationTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task D08A_TwoRecipientsKeepTheirProfilesAndCanonicalBriefsAcrossTheApiAndSdk()
    {
        using var estate = new TestEstate(2);
        string other = estate.Add("principal", estate.OperatorId, """{"kind":"human","name":"Reviewer","roles":["contributor"]}""").Id;
        estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Communication journey","intent":"Keep exact originals.","scope":["tests"],"exitQuestion":"Consistent?","inputs":[],"deliverables":["results"]}""");
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        foreach ((string recipient, string depth, string decision) in new[] { (estate.OperatorId, "short", "comprehensive"), (other, "comprehensive", "standard") })
        {
            WriteOutcome saved = client.CommunicationSet(new(recipient, "person", new(Status: depth, Decision: decision, Handoff: depth), recipient));
            Assert.True(saved.Ok, saved.Problem?.Message);
        }

        string canonical = client.Brief("COMMS-J")!.Json.GetRawText();
        string status = client.Status().Json.GetRawText();
        int records = estate.Load().Records.Count;
        foreach ((string recipient, string depth, string decision) in new[] { (estate.OperatorId, "short", "comprehensive"), (other, "comprehensive", "standard") })
        {
            CommunicationProfile local = host.Service.Communication(recipient);
            CommunicationProfile served = client.Communication(recipient);
            Assert.Equal(local.RevisionHash, served.RevisionHash);
            Assert.Equal(depth, served.Fields["status"].Value);
            Assert.Equal(decision, served.Fields["decision"].Value);
            Assert.Equal("person", served.Fields["status"].Source);
            Assert.Contains(local.Revisions.Single().Hash!, client.BriefFor("COMMS-J", recipient)!.Markdown, StringComparison.Ordinal);
            foreach (string view in CommunicationResolution.Depths)
            {
                CommunicationProfile selected = client.Communication(recipient, view: new(Status: view));
                Assert.Equal("view", selected.Fields["status"].Source);
                Assert.Equal(view, selected.Fields["status"].Value);
                BriefResponse brief = client.BriefFor("COMMS-J", recipient, view: new(Handoff: view))!;
                Assert.Equal(canonical, brief.Json.GetRawText());
                Assert.Equal(canonical, host.Service.BriefFor("COMMS-J", recipient, view: new(Handoff: view))!.Json.GetRawText());
                Assert.Contains($"handoff ({view})", brief.Markdown, StringComparison.Ordinal);
                Assert.Equal(status, client.Status().Json.GetRawText());
            }
        }

        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Equal("short", client.Communication(estate.OperatorId).Fields["status"].Value);
        Assert.Equal("comprehensive", client.Communication(other).Fields["status"].Value);
    }
}
