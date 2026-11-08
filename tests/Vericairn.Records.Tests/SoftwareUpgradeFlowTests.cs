// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Tests;

/// <summary>
/// The software pack's estate upgrade flow (the estate's design/release-procedure.md §§ 7 and 10): check and upgrade, the runtime step
/// that leaves no runtime of an older release serving the estate (OPS-05), the estate verified with the pinned release, and the living
/// documents the upgrade made due cleared, each derived from the records. An upgrade with an older runtime serving the estate stops at
/// runtime until it is replaced.
/// </summary>
public class SoftwareUpgradeFlowTests
{
    private const string Version = "0.9.2";

    private static readonly string[] Checks = ["vericairn.software/serving-runtime", "vericairn.software/estate-check"];

    [Fact]
    public void AnUpgradeStopsAtRuntimeUntilTheOlderRuntimeIsReplacedThenVerifiesAndClearsItsDocuments()
    {
        using var records = new PolicyFixture();
        RecordEnvelope goal = Run(records);
        Assert.Equal(("next", "next"), (State(records, goal, "check"), State(records, goal, "upgrade")));
        Assert.Equal("blocked", State(records, goal, "runtime"));

        // An observation of an earlier release is no upgrade to this one.
        Observe(records, "0.9.1");
        Assert.Equal("next", State(records, goal, "upgrade"));

        // The upgrade's observation of the pinned release: a check records nothing, so it counts with the upgrade it checked.
        Observe(records, Version);
        Assert.Equal(("done", "done", "next"), (State(records, goal, "check"), State(records, goal, "upgrade"), State(records, goal, "runtime")));

        // estate verify found a daemon of the older release serving the estate here: the run stops at runtime, saying so.
        RecordEnvelope stale = Verify(records, "vericairn.software/serving-runtime", "failed", "daemon 4242 serves the estate on this machine with Vericairn 0.9.1, older than the estate's pin, release 0.9.2.");
        FlowPosition.StepInstance runtime = Step(records, goal, "runtime");
        Assert.Equal("blocked", runtime.State);
        string reason = Assert.Single(runtime.Reasons, r => r.StartsWith("trap older-runtime:", StringComparison.Ordinal));
        Assert.Contains("Vericairn 0.9.1, older than the estate's pin", reason, StringComparison.Ordinal);
        Assert.Equal("blocked", State(records, goal, "verify"));

        // Replaced and verified again: the runtime step is done, then the estate check, and nothing the upgrade made due is open.
        Verify(records, "vericairn.software/serving-runtime", "passed", "The pinned release's runtime serves the estate on this machine.", stale);
        Assert.Equal(("done", "next"), (State(records, goal, "runtime"), State(records, goal, "verify")));
        Verify(records, "vericairn.software/estate-check", "passed", "The estate check found no error, and status derives.");
        Assert.Equal(("done", "done"), (State(records, goal, "verify"), State(records, goal, "docs")));

        // Adoption is the root operator's, once: with no adoption checkpoint, the run waits on the person its role names.
        FlowPosition.StepInstance adopt = Step(records, goal, "adopt");
        Assert.Equal(("stopped", "person", records.Estate.OperatorId), (adopt.State, adopt.Stop, adopt.Principal));
        Assert.False(FlowPosition.Derive(records.Estate.Load(), goal, records.Estate.Clock).Complete);
        records.AssertValid();
    }

    [Fact]
    public void AdoptionIsTheRootOperatorsOnceAndAnEstateThatAdoptedHasTheStepDone()
    {
        using var history = new HistoryFixture();
        RecordEnvelope goal = UpgradeRun(history);
        Assert.Equal("blocked", Step(history.Estate, goal, "adopt").State);

        // Upgraded and verified with the pinned release: adoption is next, the root operator's.
        RecordEnvelope charter = history.Load().CurrentEstate!;
        history.Estate.Add("observation", history.Estate.OperatorId, ObservationBody(history.Estate.Clock, Version).ToJsonString(), charter.Reference, signer: history.OperatorSigner);
        foreach (string method in Checks)
        {
            history.Estate.Add("verification", history.Estate.OperatorId, VerificationBody(method, "passed", "Verified with the pinned release.").ToJsonString(), charter.Reference, signer: history.OperatorSigner);
        }

        Assert.Equal(("done", "done"), (Step(history.Estate, goal, "verify").State, Step(history.Estate, goal, "docs").State));
        Assert.Equal(("stopped", "person"), (Step(history.Estate, goal, "adopt").State, Step(history.Estate, goal, "adopt").Stop));
        // The step points at the operator's list, whose one item carries the check and the signing command.
        string action = Step(history.Estate, goal, "adopt").Step.Action!;
        Assert.Contains("Adopt history checkpoints in vericairn operator list", action, StringComparison.Ordinal);
        Assert.DoesNotContain("--expect-plan", action, StringComparison.Ordinal);
        PinRelease(history, Version);
        JsonObject item = Assert.Single(OperatorActions.Derive(history.Load())["items"]!.AsArray().OfType<JsonObject>(), i => i["kind"]!.GetValue<string>() == "history-adoption");
        Assert.Equal("vericairn estate checkpoint --adopt --check", item["inspect"]!.GetValue<string>());
        Assert.Contains("--expect-plan <the plan hash the check prints>", item["waitsOn"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(OperatorActions.RenderText(OperatorActions.Derive(history.Load())), "Adopt history checkpoints"));

        // The root operator signs the adoption checkpoint: the step is done, and stays done after later checkpoints.
        history.Adopt();
        Assert.Equal("done", Step(history.Estate, goal, "adopt").State);
        history.Checkpoint();
        Assert.Equal("done", Step(history.Estate, goal, "adopt").State);
        Assert.Empty(HistoryFixture.Codes(history.Load()));
    }

    [Fact]
    public void AnAgentsAdoptionCheckpointLeavesTheAdoptionStepToTheRootOperator()
    {
        using var history = new HistoryFixture();
        RecordEnvelope goal = UpgradeRun(history);
        RecordEnvelope charter = history.Load().CurrentEstate!;
        history.Estate.Add("observation", history.Estate.OperatorId, ObservationBody(history.Estate.Clock, Version).ToJsonString(), charter.Reference, signer: history.OperatorSigner);
        foreach (string method in Checks)
        {
            history.Estate.Add("verification", history.Estate.OperatorId, VerificationBody(method, "passed", "Verified with the pinned release.").ToJsonString(), charter.Reference, signer: history.OperatorSigner);
        }

        // The builder signs a checkpoint that claims adoption: it adopts nothing, so the step still waits on the root operator.
        history.Checkpoint(by: history.Estate.BuilderId, signer: history.BuilderSigner, trigger: "adoption");
        Assert.Contains(history.Load().Findings, f => f.Code == "history.adoption-author");
        FlowPosition.StepInstance adopt = Step(history.Estate, goal, "adopt");
        Assert.Equal(("stopped", "person", history.Estate.OperatorId), (adopt.State, adopt.Stop, adopt.Principal));
    }

    // The manifest of an estate pinned to a published release, as an upgrade leaves it.
    private static void PinRelease(HistoryFixture f, string version)
    {
        var manifest = new JsonObject
        {
            ["estate"] = f.Estate.Estate.Id, ["format"] = EstateManifest.Format1, ["recordFormat"] = 3,
            ["vericairn"] = new JsonObject
            {
                ["mode"] = "release", ["repository"] = "Vericairn/vericairn", ["version"] = version,
                ["artifacts"] = new JsonObject { ["linux-x64"] = "sha256:" + new string('1', 64) },
            },
        };
        using JsonDocument document = JsonDocument.Parse(manifest.ToJsonString());
        File.WriteAllBytes(Path.Combine(f.Root, EstateLayout.ManifestPath), CanonicalJson.SerializePrettyUtf8(document.RootElement));
    }

    /// <summary>An upgrade run in a history fixture's estate, its root operator named in the root-operator role.</summary>
    private static RecordEnvelope UpgradeRun(HistoryFixture history)
    {
        TestEstate estate = history.Estate;
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HistoryFixture.Order("UPG-1", "Upgrade the estate"), signer: history.OperatorSigner);
        estate.Add("roster", estate.OperatorId, $$"""{"roles":[{"role":"upgrader","principals":["{{estate.BuilderId}}"]},{"role":"root-operator","principals":["{{estate.OperatorId}}"]}]}""", signer: history.OperatorSigner);
        var body = new JsonObject
        {
            ["title"] = "Upgrade to " + Version, ["objective"] = "Upgrade the estate.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = new JsonObject { ["type"] = "observation", ["fields"] = new JsonObject { ["kind"] = "vericairn-release", ["facts.version"] = Version } } }),
            ["constraints"] = new JsonArray(), ["kind"] = "finite", ["sponsor"] = estate.OperatorId, ["budget"] = new JsonObject { ["wallMinutes"] = 120, ["attemptsPerStep"] = 2 },
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order.Id),
                ["flow"] = new JsonObject { ["pin"] = FlowFixture.Pin(SoftwareFlows.UpgradeFlow), ["subject"] = new JsonObject { ["version"] = Version } },
            },
            ["inputs"] = new JsonArray(),
        };
        RecordEnvelope goal = estate.Add("goal", estate.OperatorId, body.ToJsonString(), signer: history.OperatorSigner);
        estate.Add("goal-status", estate.OperatorId, """{"status":"active","note":"Activated for the test."}""", goal.Reference, signer: history.OperatorSigner);
        return goal;
    }

    private static FlowPosition.StepInstance Step(TestEstate estate, RecordEnvelope goal, string key) =>
        Assert.Single(FlowPosition.Derive(estate.Load(), goal, estate.Clock).Steps, s => s.Name == key);

    [Fact]
    public void AFailedEstateCheckStopsVerify()
    {
        using var records = new PolicyFixture();
        RecordEnvelope goal = Run(records);
        Observe(records, Version);
        Verify(records, "vericairn.software/serving-runtime", "passed", "The pinned release's runtime serves the estate here.");
        Verify(records, "vericairn.software/estate-check", "failed", "estate check: 2 errors: record.signature in records/decision/x.json.");
        Assert.Contains(Step(records, goal, "verify").Reasons, r => r.StartsWith("trap estate-check:", StringComparison.Ordinal) && r.Contains("2 errors", StringComparison.Ordinal));
    }

    internal static RecordEnvelope Run(PolicyFixture records)
    {
        RecordEnvelope basis = records.Add("decision", records.DecisionBody());
        records.Add("authority", new JsonObject
        {
            ["grantee"] = records.Estate.BuilderId, ["scope"] = new JsonArray("local-engineering"), ["reserved"] = new JsonArray("publication"), ["conditions"] = new JsonArray(), ["basis"] = basis.Id,
        });
        RecordEnvelope order = records.Add("work-order", new JsonObject
        {
            ["key"] = "UPG-1", ["kind"] = "ordinary", ["title"] = "Upgrade the estate", ["intent"] = "Upgrade the estate to the release.", ["scope"] = new JsonArray("estate"),
            ["exitQuestion"] = "Is the estate upgraded?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("the upgraded estate"),
        });
        records.Add("roster", new JsonObject { ["roles"] = new JsonArray(FlowFixture.Role("upgrader", records.Estate.BuilderId), FlowFixture.Role("root-operator", records.Estate.OperatorId)) });
        RecordEnvelope goal = records.Add("goal", new JsonObject
        {
            ["title"] = "Upgrade to " + Version, ["objective"] = "Upgrade the estate.",
            ["completion"] = new JsonArray(new JsonObject { ["record"] = new JsonObject { ["type"] = "observation", ["fields"] = new JsonObject { ["kind"] = "vericairn-release", ["facts.version"] = Version } } }),
            ["constraints"] = new JsonArray(), ["kind"] = "finite", ["sponsor"] = records.Estate.OperatorId, ["budget"] = new JsonObject { ["wallMinutes"] = 120, ["attemptsPerStep"] = 2 },
            ["plan"] = new JsonObject
            {
                ["policy"] = "given", ["approval"] = "sponsor-approves", ["workOrders"] = new JsonArray(order.Id),
                ["flow"] = new JsonObject { ["pin"] = FlowFixture.Pin(SoftwareFlows.UpgradeFlow), ["subject"] = new JsonObject { ["version"] = Version } },
            },
            ["inputs"] = new JsonArray(),
        });
        records.Add("goal-status", new JsonObject { ["status"] = "active", ["note"] = "Activated for the test." }, subject: goal.Reference);
        return goal;
    }

    /// <summary>The observation estate upgrade --to writes of the release it pins.</summary>
    internal static RecordEnvelope Observe(PolicyFixture records, string version) =>
        records.Add("observation", ObservationBody(records.Estate.Clock, version), builder: true, subject: records.Estate.Load().CurrentEstate!.Reference);

    internal static JsonObject ObservationBody(DateTimeOffset at, string version)
    {
        string commit = new('c', 40);
        string tag = "v" + version;
        return new JsonObject
        {
            ["kind"] = "vericairn-release", ["asOf"] = RecordParser.FormatTimestamp(at), ["summary"] = "The published release the estate pins.",
            ["facts"] = new JsonObject
            {
                ["repository"] = VerifiedRelease.Repository, ["version"] = version, ["tag"] = tag, ["commit"] = commit, ["url"] = VerifiedRelease.PageUrl(tag),
                ["checksumsUrl"] = VerifiedRelease.AssetUrl(tag, "SHA256SUMS"), ["checksumsHash"] = FlowFixture.Hash('9'), ["observer"] = $"vericairn {version}+{commit}",
                ["verifiedPlatform"] = "osx-arm64", ["signing"] = "unsigned developer build",
                ["artifactNames"] = new JsonArray(ReleaseArchive.FileName(version, "osx-arm64")), ["artifactPlatforms"] = new JsonArray("osx-arm64"),
                ["artifactHashes"] = new JsonArray(FlowFixture.Hash('8')), ["reads"] = new JsonArray("1", "2", "3"), ["writes"] = 3,
            },
            ["limitations"] = new JsonArray(),
        };
    }

    /// <summary>estate verify's verification of one check, for the pinned release, superseding the earlier one.</summary>
    internal static RecordEnvelope Verify(PolicyFixture records, string method, string outcome, string summary, RecordEnvelope? supersedes = null) =>
        records.Add("verification", VerificationBody(method, outcome, summary), builder: true, subject: records.Estate.Load().CurrentEstate!.Reference, supersedes: supersedes?.Id);

    internal static JsonObject VerificationBody(string method, string outcome, string summary) => new()
    {
        ["method"] = method, ["commit"] = new string('e', 40), ["command"] = "vericairn estate verify",
        ["environment"] = new JsonObject { ["platform"] = "osx-arm64", ["release"] = Version }, ["role"] = "self", ["outcome"] = outcome, ["summary"] = summary,
        ["evidence"] = new JsonArray(), ["limitations"] = new JsonArray("What serves the estate on this machine only."),
    };

    private static FlowPosition.StepInstance Step(PolicyFixture records, RecordEnvelope goal, string key) =>
        Assert.Single(FlowPosition.Derive(records.Estate.Load(), goal, records.Estate.Clock).Steps, s => s.Name == key);

    private static string State(PolicyFixture records, RecordEnvelope goal, string key) => Step(records, goal, key).State;
}
