// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Presentation;
using Vericairn.Records.Canonical;

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
        foreach (string required in new[] { result.Written!.Id, result.Written.Hash, "1 failed checks", "1 unresolved effects", ask.Written!.Id, "vericairn records show " + ask.Written.Id })
        {
            Assert.Contains(required, attention, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(api.Record(result.Written.Id)!.Envelope.GetRawText(), attention, StringComparison.Ordinal);
        Presentation.AttentionItem askItem = Assert.Single(shell.Attention, a => a.SubjectId == ask.Written.Id);
        await fixture.Ui(() => shell.OpenAttention(askItem));
        foreach (string required in new[] { "Inspect first.", "Reconcile the original effect.", "Abandon the retry.", "The work waits without a retry." })
        {
            Assert.Contains(required, api.Asks(person).Text, StringComparison.Ordinal);
        }
        Assert.Empty(api.Records("acceptance"));
        Assert.Equal("open", api.Record(ask.Written.Id)!.Envelope.GetProperty("body").GetProperty("state").GetString());
        WriteOutcome review = api.NewRecord(new("review", person, Seed.Json("""{"independence":"self","outcome":"findings","summary":"Inspect the remaining effect.","findings":[{"severity":"error","summary":"Follow up."}]}"""), $"result:{result.Written.Id}:{result.Written.Hash}"));
        Assert.True(review.Ok, review.Problem?.Message);
        await fixture.Ui(shell.RefreshProgress);
        Presentation.AttentionItem reviewItem = Assert.Single(shell.Attention, a => a.Kind == "required-decision" && a.SubjectId == result.Written.Id);
        Assert.Contains(review.Written!.Hash, reviewItem.Why, StringComparison.Ordinal);
        await fixture.Ui(() => shell.OpenAttention(reviewItem));
        Assert.Equal(result.Written.Id, Assert.IsType<ReviewWorkspace>(shell.SelectedDocument).SubjectId);
        RecordResponse order = Assert.Single(api.Records("work-order"));
        WriteOutcome accepted = api.NewRecord(new("acceptance", person, Seed.Json("""{"statement":"Work accepted.","basis":[],"self":true}"""), $"work-order:{order.Id}:{order.Hash}"));
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        await fixture.Ui(shell.RefreshProgress);
        Assert.DoesNotContain(shell.Attention, a => a.SubjectId == result.Written.Id);
        Assert.Contains(shell.Attention, a => a.SubjectId == ask.Written.Id);

    }

    [Fact]
    public async Task D08E_ASavedHandoffKeepsOriginalDecisionsFailuresAndEffectsWhenTheSourceChanges()
    {
        string root = Seed.Temp("wb-comms-continuity");
        string person = Seed.Init(root);
        using var connection = new ServiceConnection(root);
        IEstateApi api = connection.Api!;
        string orderBody = """{"key":"COMMS-J","kind":"ordinary","title":"Interrupted work","intent":"Reconcile before retrying.","scope":["tests"],"exitQuestion":"Reconciled?","inputs":[],"deliverables":["result"]}""";
        WriteOutcome order = api.NewRecord(new("work-order", person, Seed.Json(orderBody)));
        Assert.True(order.Ok, order.Problem?.Message);
        WriteOutcome decision = api.NewRecord(new("decision", person, Seed.Json($$"""{"title":"Keep the original identity","statement":"Do not retry the uncertain effect.","summary":"Inspect first.","synthesis":"The interruption did not settle the effect.","effects":["Reconcile before retrying."],"affects":["{{order.Written!.Id}}"],"decidedBy":"{{person}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["No second dispatch."]}""")));
        Assert.True(decision.Ok, decision.Problem?.Message);
        string assignment = api.Assign(new("COMMS-J", person, person, Seed.Json("""{"outcome":"Reconcile the operation.","sources":[],"permittedChanges":["tests"],"prohibitedEffects":["no retry"],"workspace":["local clone"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["effect reconciled"],"recovery":"Read the original operation."}"""))).Written!.Id;
        string attempt = api.Attempt(new(assignment, person, "local clone", "Inspect.")).Written!.Id;
        JsonObject effect = JsonNode.Parse("""{"provider":"fixture","action":"write-widget","identity":"wb-original-effect","intent":"Write the widget.","target":{"repository":"Example/widget"},"status":"not-started","evidence":[]}""")!.AsObject();
        WriteOutcome operation = api.NewRecord(new("operation", person, Seed.Json(effect.ToJsonString())));
        Assert.True(operation.Ok, operation.Problem?.Message);
        effect["dispatchedAt"] = api.Record(operation.Written!.Id)!.Envelope.GetProperty("at").GetString();
        effect["status"] = "dispatched";
        operation = api.NewRecord(new("operation", person, Seed.Json(effect.ToJsonString()), Supersedes: operation.Written.Id));
        Assert.True(operation.Ok, operation.Problem?.Message);
        effect["status"] = "uncertain";
        operation = api.NewRecord(new("operation", person, Seed.Json(effect.ToJsonString()), Supersedes: operation.Written!.Id));
        Assert.True(operation.Ok, operation.Problem?.Message);
        WriteOutcome result = api.Result(new(attempt, person, Seed.Json($$"""{"outcome":"partial","summary":"Interrupted.","outputs":[],"evidence":[],"checks":[{"name":"reply","outcome":"failed","summary":"The reply was lost."},{"name":"external state","outcome":"not-performed","summary":"No external observation."}],"limitations":[],"unresolvedEffects":["Operation {{operation.Written!.Id}} remains uncertain under wb-original-effect."],"nextStep":"Inspect before retrying."}""")));
        Assert.True(result.Ok, result.Problem?.Message);
        Assert.True(api.CommunicationSet(new(person, "person", new(Progress: "none", Handoff: "short"), person)).Ok);
        var previous = new HandoffDocument(() => api, () => api.Communication(person));
        await fixture.Ui(previous.Prepare);
        string saved = previous.PackageJson;
        var successor = new HandoffDocument(() => api, () => api.Communication(person));
        await fixture.Ui(() => Assert.True(successor.Load(saved)));
        Assert.Equal(saved, successor.PackageJson);
        Assert.Contains("handoff (short)", successor.Text, StringComparison.Ordinal);
        foreach (string required in new[] { decision.Written!.Hash, result.Written!.Hash, operation.Written.Hash, "Do not retry the uncertain effect.", "The reply was lost.", "No external observation.", "wb-original-effect" })
        {
            Assert.Contains(required, successor.Text, StringComparison.Ordinal);
        }

        WriteOutcome revised = api.NewRecord(new("work-order", person, Seed.Json(orderBody.Replace("Reconcile before retrying.", "Inspect the changed source.", StringComparison.Ordinal)), Supersedes: order.Written.Id));
        Assert.True(revised.Ok, revised.Problem?.Message);
        await fixture.Ui(successor.Compare);
        Assert.Contains(revised.Written!.Id, successor.ResumeText, StringComparison.Ordinal);
        Assert.Contains("Inspect the changed source.", successor.ResumeText, StringComparison.Ordinal);
        Assert.Contains("Nothing is restarted or accepted", successor.ResumeText, StringComparison.Ordinal);
        Assert.Equal(saved, successor.PackageJson);
        Assert.Equal(CanonicalJson.SerializePrettyUtf8(JsonSerializer.Deserialize<HandoffPackage>(saved, ProtocolJson.Options)!.Payload), CanonicalJson.SerializePrettyUtf8(successor.Package!.Payload));
        Assert.Equal(operation.Written.Id, Assert.Single(api.Records("operation")).Id);
        Assert.Empty(api.Records("acceptance"));
    }

    [Fact]
    public void WatchSummaryKeepsEveryWorkerObservationAtEveryDepthAndFrequency()
    {
        JsonElement report = Seed.Json("""{"format":"vericairn.progress/1","agentClaims":[{"attempt":"fixture-attempt","phase":"build","note":"Optional live claim."}],"runSignals":[{"run":"fixture-run","signals":[{"severity":"warning","code":"loop","summary":"The worker observed five repeated actions.","attention":"fixture-ask"},{"severity":"critical","code":"loop","summary":"The worker observed ten repeated actions.","attention":null},{"severity":"warning","code":"audit-unverified","summary":"Invocation scope could not be verified.","attention":null}]}]}""");
        byte[] original = CanonicalJson.SerializePrettyUtf8(report);
        ProgressRow[] expected = ProgressView.Summary(report, "verbose", onDemand: false).Where(row => row.Heading.StartsWith("Worker ·", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, expected.Length);
        foreach (string frequency in CommunicationResolution.Frequencies)
        {
            foreach (string depth in CommunicationResolution.Depths)
            {
                IReadOnlyList<ProgressRow> summary = ProgressView.Summary(report, frequency, onDemand: false);
                Assert.Equal(expected, summary.Where(row => row.Heading.StartsWith("Worker ·", StringComparison.Ordinal)));
                CommunicationProfile profile = CommunicationResolution.Resolve(null, null, [], new(frequency, depth, depth, depth, depth));
                string human = CommunicationPresentation.Protected(profile, "status", "worker observations", "Inspect required attention.", string.Join('\n', summary.Select(row => row.Detail)));
                Assert.All(expected, row => Assert.Contains(row.Detail, human, StringComparison.Ordinal));
                Assert.Contains("attention recording pending", human, StringComparison.Ordinal);
                Assert.Contains("Invocation scope could not be verified.", human, StringComparison.Ordinal);
                if (frequency == "none") { Assert.DoesNotContain("Optional live claim.", human, StringComparison.Ordinal); }
                Assert.Equal(original, CanonicalJson.SerializePrettyUtf8(report));
            }
        }
    }
}
