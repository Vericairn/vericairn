// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class PromptRunTests
{
    [Fact]
    public void ANewRunPinsItsPromptInTheIntentAndAccountedContextAndRetainsItsOriginalAuthority()
    {
        using var run = new RunEstate(recordFormat: 3);
        string by = run.Estate.BuilderId;
        string decision = RunEstate.Ok(run.Service.NewRecord(new("decision", run.Estate.OperatorId, RunEstate.Json($$"""{"title":"Local work","statement":"Permit the assigned local work.","summary":"Local engineering only.","synthesis":"The test work is bounded.","effects":["local engineering"],"affects":[],"decidedBy":"{{run.Estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"low","notAuthorized":["publication"]}""")))).Id;
        RunEstate.Ok(run.Service.NewRecord(new("authority", run.Estate.OperatorId, RunEstate.Json($$"""{"grantee":"{{by}}","scope":["local-engineering"],"reserved":[],"conditions":[],"basis":"{{decision}}"}"""))));
        JsonObject definition = PromptRecordsTests.Definition("run-review"); definition.Remove("state");
        PromptRow prompt = PromptCatalog.BuiltIn("example", "1.2.3", PolicyFixture.Element(definition), draft: false);
        run.Service.Providers.Prompts.Register([prompt]);
        var request = new RunStartRequest(run.Assignment, "generic", by, by, run.Resource, WallMinutes: 1,
            Settings: new Dictionary<string, string> { ["command"] = FakeHarness.Setting("generic", run.Report("prompt-run")) });
        var selection = new PromptSelection(prompt.Identity, prompt.Hash, new Dictionary<string, string> { ["topic"] = "the assigned widget evidence" });
        RecordEnvelope assignment = run.Estate.Load().ById[run.Assignment];
        byte[] terms = CanonicalJson.SerializeUtf8(assignment.Body);
        PromptUseResponse started = run.Service.PromptUse(new(by, selection, Run: request) { Favor = true });
        Assert.True(started.Ok, started.Problem?.Message ?? started.Run?.Message);
        Assert.NotNull(started.Written);
        Assert.Equal("observed-success", run.Settled(started.Run!.Run).Status);
        AgentRuns.Run recorded = AgentRuns.Find(run.Estate.Load(), started.Run.Run)!;
        JsonElement use = recorded.Intent.Body.GetProperty("promptUse");
        Assert.True(SignatureChecks.IsAuthentic(run.Estate.Load(), recorded.Intent));
        Assert.Equal(prompt.Version, use.GetProperty("version").GetString());
        Assert.Equal(prompt.Hash, use.GetProperty("hash").GetString());
        Assert.Equal("unchanged", use.GetProperty("authority").GetString());
        Assert.Equal(1, recorded.Cap.GetProperty("wallMinutes").GetInt64());
        Assert.Equal(run.Assignment, recorded.Intent.Subject!.Id);
        Assert.Equal(terms, CanonicalJson.SerializeUtf8(run.Estate.Load().ById[run.Assignment].Body));
        Assert.All(recorded.Revisions, revision => Assert.Equal(CanonicalJson.Serialize(use), CanonicalJson.Serialize(revision.Body.GetProperty("promptUse"))));
        ContextPackage package = CommunicationRunJourneyTests.RetainedContext(run, started.Run.Run);
        Assert.True(ContextAccounting.Verify(package));
        Assert.Equal(CanonicalJson.Serialize(use), CanonicalJson.Serialize(package.Payload.GetProperty("promptUse")));
        Assert.Equal(package.Hash, recorded.Intent.Body.GetProperty("execution").GetProperty("contextPackage").GetString());
        Assert.Equal(prompt.Identity, Assert.Single(run.Service.Prompts(new(by, Favorites: true)).Prompts).Identity);
        Assert.Equal(0, run.Service.Validate().Errors);
    }

    [Fact]
    public void SelectingAPromptDoesNotGrantPermissionToLaunch()
    {
        using var run = new RunEstate(recordFormat: 3);
        JsonObject definition = PromptRecordsTests.Definition("run-review"); definition.Remove("state");
        PromptRow prompt = PromptCatalog.BuiltIn("example", "1.2.3", PolicyFixture.Element(definition), draft: false);
        run.Service.Providers.Prompts.Register([prompt]);
        string by = run.Estate.BuilderId;
        int before = run.Estate.Load().Records.Count;
        PromptUseResponse refused = run.Service.PromptUse(new(by,
            new(prompt.Identity, prompt.Hash, new Dictionary<string, string> { ["topic"] = "the assigned evidence" }),
            Run: new(run.Assignment, "generic", by, by, run.Resource)));
        Assert.False(refused.Ok);
        Assert.Null(refused.Text);
        Assert.Null(refused.Written);
        Assert.Equal(before, run.Estate.Load().Records.Count);
        Assert.Empty(run.Service.Runs().Runs);
    }
}
