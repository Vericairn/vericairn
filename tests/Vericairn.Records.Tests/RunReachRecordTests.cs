// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>An agent run's intent records what the run could reach outside its workspace (CODE-82), within format 3.</summary>
public sealed class RunReachRecordTests
{
    private static readonly string[] LocalOnly = ["local"];

    /// <summary>A minimal admitted execution: one stand-in harness, launcher-scope choices, no adapter and an unknown package.</summary>
    internal static JsonObject Execution(params (string Id, string Kind, string[] Destinations)[] components)
    {
        var choices = new JsonObject { ["model"] = "model-a", ["effort"] = "high", ["provider"] = "provider", ["destination"] = "inference", ["dataBoundary"] = "worker-user" };
        var inventory = new JsonArray([.. (components.Length == 0 ? [("stand-in", "harness", LocalOnly)] : components).OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => (JsonNode)new JsonObject
        {
            ["configurationHash"] = "sha256:" + new string('b', 64), ["dependencies"] = new JsonArray(), ["destinations"] = new JsonArray([.. c.Destinations.Order(StringComparer.Ordinal).Select(d => (JsonNode)d)]),
            ["environment"] = new JsonArray(), ["hash"] = "sha256:" + new string('a', 64), ["id"] = c.Id, ["kind"] = c.Kind, ["version"] = "1",
        })]);
        return new JsonObject
        {
            ["configuration"] = "stand-in", ["configurationHash"] = "sha256:" + new string('c', 64), ["resolvedRevision"] = "unknown", ["harnessVersion"] = "1.0", ["role"] = "builder",
            ["adapter"] = "disabled", ["contextPackage"] = "unknown", ["requested"] = choices.DeepClone(),
            ["provenance"] = new JsonArray(new JsonObject { ["scope"] = "launcher", ["source"] = "launcher", ["policy"] = new JsonObject { ["choices"] = choices } }),
            ["allocation"] = new JsonObject { ["unknown"] = new JsonArray("inputTokens", "outputTokens") },
            ["environment"] = new JsonObject
            {
                ["policy"] = "stand-in-policy", ["inventoryHash"] = ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(inventory))),
                ["variables"] = new JsonArray(), ["inventory"] = inventory,
            },
        };
    }

    internal static JsonObject Intent(RecordEnvelope assignment, JsonObject execution, string identity = "reach-fixture-run") => new()
    {
        ["provider"] = "example", ["action"] = "agent-run", ["identity"] = identity, ["intent"] = "Run within the existing assignment.",
        ["target"] = new JsonObject { ["assignment"] = assignment.Id, ["principal"] = EstateModel.BodyString(assignment, "contributor"), ["workspace"] = "example repository" },
        ["status"] = "not-started", ["cap"] = new JsonObject { ["wallMinutes"] = 1 }, ["evidence"] = new JsonArray(), ["execution"] = execution,
    };

    private static JsonObject Reach(string file = "readable", string host = "reachable", int count = 2, params string[] kinds) => new()
    {
        ["outsideFile"] = file, ["testHost"] = host, ["secretPaths"] = count,
        ["secretKinds"] = new JsonArray([.. (kinds.Length == 0 && count > 0 ? ["ssh-key", "harness-login"] : kinds).Select(k => (JsonNode)k)]), ["secretPathsComplete"] = true,
    };

    private static List<Finding> Errors(JsonObject? reach, long format = 3)
    {
        using var fixture = new PolicyFixture(format);
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope assignment = fixture.Estate.Load().ById[attempt.Subject!.Id];
        JsonObject execution = Execution();
        if (reach is not null) execution["reach"] = reach;
        RecordEnvelope intent = fixture.Add("operation", Intent(assignment, execution), subject: assignment.Reference);
        return [.. fixture.Estate.Load().Findings.Where(f => f.Severity == Severity.Error && f.Location.Contains(intent.Id, StringComparison.Ordinal))];
    }

    [Fact]
    public void AnIntentWithoutReachAndOneWithAMeasuredReachAreValid()
    {
        Assert.Empty(Errors(null));
        Assert.Empty(Errors(Reach()));
        Assert.Empty(Errors(Reach("blocked", "blocked", 0)));
        Assert.Empty(Errors(Reach("unknown", "unknown", 1, "device-key")));
    }

    [Theory]
    [InlineData("reachable", "reachable", "body.enum")]
    [InlineData("readable", "readable", "body.enum")]
    public void AReachNamesOnlyItsOwnOutcomes(string file, string host, string code) =>
        Assert.Contains(Errors(Reach(file, host)), f => f.Code == code);

    [Fact]
    public void AReachCountsEachKindItNamesAndNamesOnlyKnownKinds()
    {
        Assert.Contains(Errors(Reach(count: 0, kinds: ["ssh-key"])), f => f.Code == "operation.execution");
        Assert.Contains(Errors(Reach(count: 1, kinds: ["ssh-key", "harness-login"])), f => f.Code == "operation.execution");
        Assert.Contains(Errors(Reach(count: 1, kinds: ["browser-cookies"])), f => f.Code == "operation.execution");
        Assert.Contains(Errors(Reach(count: 2, kinds: ["ssh-key", "ssh-key"])), f => f.Code == "operation.execution");
        Assert.Contains(Errors(Reach(count: 513)), f => f.Code == "body.range");
        JsonObject withPath = Reach();
        withPath["paths"] = new JsonArray("~/.ssh/id_ed25519");
        Assert.Contains(Errors(withPath), f => f.Code == "body.unknown-key");
    }

    [Fact]
    public void AFormatTwoIntentRefusesReach() => Assert.Contains(Errors(Reach(), format: 2), f => f.Code == "body.format");
}
