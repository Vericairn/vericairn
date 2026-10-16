// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
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

    [Fact]
    public async Task D08B_TheSdkKeepsMandatoryAttentionAndEveryAskOptionAtNoneAndShort()
    {
        using var estate = new TestEstate(2);
        var order = estate.Add("work-order", estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Interrupted work","intent":"Inspect before retrying.","scope":["tests"],"exitQuestion":"Reconciled?","inputs":[],"deliverables":["results"]}""");
        var assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"Check the result.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no retry"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["checks pass"],"recovery":"Inspect the original effect."}}""", order.Reference);
        var attempt = estate.Add("attempt", estate.BuilderId, """{"startedAt":"2026-10-13T12:00:00Z","workspace":"local clone","note":"Inspect."}""", assignment.Reference);
        var result = estate.Add("result", estate.BuilderId, """{"outcome":"partial","summary":"Needs attention.","outputs":[],"evidence":[],"checks":[{"name":"journey","outcome":"failed","summary":"The response was incomplete."}],"limitations":[],"unresolvedEffects":["The original widget effect remains uncertain."],"nextStep":"Inspect before retrying."}""", attempt.Reference);
        var ask = estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Retry?","recommendation":"Inspect first.","options":["Reconcile the original effect.","Abandon the retry."],"consequence":"The work waits without a retry.","state":"open"}""");
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        Assert.True(client.CommunicationSet(new(estate.OperatorId, "person", new("none", "short", "short", "short", "short"), estate.OperatorId)).Ok);
        int count = estate.Load().Records.Count;
        IReadOnlyList<CommunicationAttention> attention = CommunicationPresentation.Attention(client, estate.OperatorId);
        Assert.True(CommunicationPresentation.ShouldDeliver(client.Communication(estate.OperatorId).Fields["progress"].Value, "progress", attention));
        string rendered = CommunicationPresentation.RenderAttention(attention);
        foreach (string required in new[] { result.Id, result.ComputedHash, "The response was incomplete.", "The original widget effect remains uncertain.", ask.Id, "Inspect first.", "Reconcile the original effect.", "Abandon the retry.", "The work waits without a retry." })
        {
            Assert.Contains(required, rendered, StringComparison.Ordinal);
        }

        Assert.Equal(CanonicalJson.SerializePrettyUtf8(host.Service.Asks(estate.OperatorId).Json), CanonicalJson.SerializePrettyUtf8(client.Asks(estate.OperatorId).Json));
        Assert.Equal(CanonicalJson.SerializePrettyUtf8(host.Service.Record(result.Id)!.Envelope), CanonicalJson.SerializePrettyUtf8(client.Record(result.Id)!.Envelope));
        Assert.Equal(count, estate.Load().Records.Count);
        Assert.Empty(client.Records("acceptance"));
    }

    [Fact]
    public async Task D08C_TheAuthenticatedSdkRetrievesTheMeasuredOriginalAndRefusesStaleBytesAndOverflow()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        byte[] original = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("café 漢字\n", 20_000)));
        harness.Estate.WriteFile("docs/large.md", Encoding.UTF8.GetString(original));
        harness.Estate.Add("document-revision", harness.Estate.OperatorId, $$"""{"path":"docs/large.md","title":"Optional source","hash":"{{ContentHash.Sha256(original)}}","bytes":{{original.Length}},"mediaType":"text/markdown","revision":1,"summary":"Measured original."}""");
        harness.Estate.Add("work-order", harness.Estate.OperatorId, """{"key":"COMMS-J","kind":"ordinary","title":"Measured omission","intent":"Read docs/large.md if useful.","scope":["tests"],"exitQuestion":"Measured?","inputs":[],"deliverables":["results"]}""");
        RuntimeConnection paired = harness.Pair(harness.Estate.BuilderId, "Context reader");
        using VericairnClient client = VericairnClient.LocalNetwork(paired, harness.Signer(paired));
        ContextResponse served = client.Context("COMMS-J", 100_000);
        Assert.Null(served.Problem);
        ContextPackage package = served.Package!;
        Assert.Equal(harness.Host.Service.Context("COMMS-J", 100_000).Package!.Hash, package.Hash);
        Assert.Equal(package.Bytes, CanonicalJson.SerializePrettyUtf8(package.Payload).LongLength);
        Assert.Equal(package.Bytes, package.Payload.GetProperty("accounting").GetProperty("bytes").GetInt64());
        JsonElement source = Assert.Single(package.Payload.GetProperty("withheld").EnumerateArray());
        Assert.Equal(original.Length, source.GetProperty("withheldBytes").GetInt64());
        string pin = source.GetProperty("pin").GetString()!;
        ContextReadResponse fetched = client.ContextRead(pin);
        Assert.Null(fetched.Problem);
        Assert.Equal(original, fetched.Content);
        Assert.Equal(source.GetProperty("byteHash").GetString(), ContentHash.Sha256(fetched.Content!));
        BriefResponse brief = client.BriefContext("COMMS-J", 100_000)!;
        Assert.Contains(pin, brief.Markdown, StringComparison.Ordinal);
        Assert.Equal(package.Hash, brief.Json.GetProperty("context").GetProperty("hash").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount(brief.Markdown), brief.MarkdownMeasurement!.Bytes);
        Assert.Equal("context-over-budget", client.Context("COMMS-J", 1).Problem!.Code);
        Assert.Null(client.Context("COMMS-J", 1).Package);
        harness.Estate.WriteFile("docs/large.md", "Changed while the successor was away.\n");
        Assert.Equal("original-unavailable", client.ContextRead(pin).Problem!.Code);
        Assert.Null(client.ContextRead(pin).Content);
        Assert.NotEqual(package.Hash, client.Context("COMMS-J", 100_000).Package!.Hash);
        Assert.Empty(client.Records("operation"));
    }
}
