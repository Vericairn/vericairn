// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>An isolated format-3 estate with admitted test signers; no runtime storage is inherited.</summary>
public sealed class PolicyFixture : IDisposable
{
    private readonly string keyHome = Path.Combine(Path.GetTempPath(), "vericairn-policy-test-keys-" + Ulid.NewUlid().Value);
    private readonly FileKeyStore store;

    public PolicyFixture(long format = 3)
    {
        Estate = new TestEstate(format);
        store = new FileKeyStore(keyHome);
        OperatorDevice = Device(Estate.OperatorId);
        OperatorSigner = new StoreSigner(store, OperatorDevice.Id);
        Add("admission", new JsonObject { ["principal"] = Estate.OperatorId, ["status"] = "admitted", ["note"] = "Test root admission." }, subject: OperatorDevice.Reference);
        BuilderDevice = Device(Estate.BuilderId);
        BuilderSigner = new StoreSigner(store, BuilderDevice.Id);
        Add("admission", new JsonObject { ["principal"] = Estate.BuilderId, ["status"] = "admitted", ["note"] = "Test contributor admission." }, subject: BuilderDevice.Reference);
        Decision = Add("decision", DecisionBody());
    }

    public TestEstate Estate { get; }
    public StoreSigner OperatorSigner { get; }
    public StoreSigner BuilderSigner { get; }
    public RecordEnvelope OperatorDevice { get; }
    public RecordEnvelope BuilderDevice { get; }
    public RecordEnvelope Decision { get; }

    public static JsonObject Body(RecordEnvelope record) => JsonNode.Parse(record.Body.GetRawText())!.AsObject();
    public static JsonObject Ref(RecordEnvelope record, bool exact = true) => exact ? new JsonObject { ["type"] = record.Type, ["id"] = record.Id, ["hash"] = record.ComputedHash } : new JsonObject { ["type"] = record.Type, ["id"] = record.Id };
    public static JsonElement Element(JsonNode body) => JsonSerializer.SerializeToElement(body);

    public RecordEnvelope Add(string type, JsonObject body, bool builder = false, RecordReference? subject = null, string? supersedes = null, bool sign = true, string? id = null) =>
        Estate.Add(type, builder ? Estate.BuilderId : Estate.OperatorId, body.ToJsonString(), subject, supersedes, id, sign ? builder ? BuilderSigner : OperatorSigner : null);

    public JsonObject DecisionBody() => new()
    {
        ["title"] = "Test decision", ["statement"] = "Approve the stated definition only.", ["summary"] = "Approve a bounded test change.", ["synthesis"] = "Test evidence, without external effects.",
        ["effects"] = new JsonArray("Apply the exact approved definition."), ["affects"] = new JsonArray(), ["decidedBy"] = Estate.OperatorId, ["decidedAt"] = RecordParser.FormatTimestamp(Estate.Clock),
        ["stake"] = "medium", ["notAuthorized"] = new JsonArray("publication"),
    };

    public JsonObject PolicyBody(string key = "maintenance", bool duty = true) => new()
    {
        ["key"] = key, ["title"] = "Test maintenance", ["owner"] = Estate.OperatorId, ["reason"] = "Keep an attributable maintenance history.", ["state"] = "active",
        ["appliesTo"] = new JsonObject { ["kind"] = "estate" }, ["level"] = "refuse", ["startsAt"] = "2026-10-13T13:00:00Z", ["maxExceptionDays"] = 30,
        ["rules"] = new JsonArray(duty ? new JsonObject
        {
            ["key"] = "monthly", ["kind"] = DutyRecords.Kind, ["version"] = 1,
            ["parameters"] = new JsonObject
            {
                ["subject"] = Ref(Estate.Estate, false), ["schedule"] = new JsonObject { ["kind"] = "interval", ["unit"] = "months", ["every"] = 1, ["firstDueAt"] = "2026-10-13T13:00:00Z" }, ["graceDays"] = 3, ["renewal"] = "none",
                ["discharge"] = new JsonObject { ["checks"] = new JsonArray("inventory"), ["review"] = "independent", ["records"] = new JsonArray() },
            },
        } : new JsonObject { ["key"] = "age", ["kind"] = "example.policy/age", ["version"] = 1, ["parameters"] = new JsonObject { ["plannedDays"] = 30 } }),
    };

    public RecordEnvelope Approve(JsonObject definition, RecordEnvelope? previous = null, bool sign = true, bool builder = false)
    {
        JsonObject body = DecisionBody();
        var requirement = new JsonObject
        {
            ["key"] = "definition", ["action"] = previous is null ? "declare" : definition["state"]!.GetValue<string>() == "retired" ? "retire" : "revise",
            ["policy"] = previous is null ? definition["key"]!.GetValue<string>() : Estate.Load().Chain(previous)[0], ["change"] = "Approve this exact body.", ["definitionHash"] = PolicyRequirements.DefinitionHash(Element(definition)),
        };
        if (previous is not null) requirement["priorHash"] = previous.ComputedHash;
        body["policies"] = new JsonArray(requirement);
        return Add("decision", body, builder: builder, sign: sign);
    }

    public RecordEnvelope Policy(JsonObject? body = null, RecordEnvelope? previous = null, bool builder = false, RecordEnvelope? grant = null, bool sign = true)
    {
        body ??= PolicyBody();
        RecordEnvelope approval = Approve(body, previous);
        var basis = new JsonObject { ["decision"] = Ref(approval), ["requirement"] = "definition" };
        if (grant is not null) basis["authority"] = Ref(grant);
        body["basis"] = new JsonArray(basis);
        return Add("estate-policy", body, builder: builder, supersedes: previous?.Id, sign: sign);
    }

    public RecordEnvelope Grant(string scope, JsonObject bounds, string expiry = "2027-01-01T00:00:00Z", bool sign = true, RecordEnvelope? previous = null) => Add("authority", new JsonObject
    {
        ["grantee"] = Estate.BuilderId, ["scope"] = new JsonArray(scope), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = Decision.Id,
        ["expires"] = expiry, ["bounds"] = new JsonArray(bounds),
    }, sign: sign, supersedes: previous?.Id);

    public static JsonObject WritingBounds() => new() { ["scope"] = Scopes.ManageWritingExceptions, ["actions"] = new JsonArray("add", "remove", "reconcile"), ["tokens"] = new JsonArray("Open311") };
    public static JsonObject PolicyBounds(string key = "maintenance") => new() { ["scope"] = Scopes.ManageEstatePolicies, ["actions"] = new JsonArray("declare", "revise", "transfer", "retire", "reconcile"), ["policies"] = new JsonArray(key), ["levels"] = new JsonArray("remind", "warn", "refuse"), ["appliesTo"] = new JsonArray(new JsonObject { ["kind"] = "estate" }) };

    public static JsonObject WritingBody(RecordEnvelope? grant = null) => new()
    {
        ["token"] = "Open311", ["code"] = "writing.spacing", ["reason"] = "An intentional service name.", ["state"] = "active",
        ["basis"] = grant is null ? new JsonObject { ["kind"] = "operator" } : new JsonObject { ["kind"] = "authority", ["grant"] = Ref(grant) },
    };

    public JsonObject DutyLink(RecordEnvelope policy, int number = 0) => new()
    {
        ["policy"] = Ref(policy), ["rule"] = "monthly", ["subject"] = Ref(Estate.Estate), ["occurrence"] = new JsonObject { ["kind"] = "interval", ["number"] = number },
        ["scope"] = new JsonObject { ["repositories"] = new JsonArray(), ["objects"] = new JsonArray(), ["records"] = new JsonArray() }, ["coverage"] = "Inspected the declared inventory.", ["proofs"] = new JsonArray(),
    };

    public JsonObject OccurrenceTarget(RecordEnvelope policy, int number = 0)
    {
        JsonObject link = DutyLink(policy, number);
        foreach (string field in new[] { "scope", "coverage", "proofs" }) link.Remove(field);
        link["kind"] = "occurrence";
        return link;
    }

    public JsonObject ExceptionBody(RecordEnvelope policy, RecordEnvelope? grant = null) => new()
    {
        ["rule"] = "monthly", ["targets"] = new JsonArray(OccurrenceTarget(policy)), ["reason"] = "A bounded delay, with the debt retained.", ["expiresAt"] = RecordParser.FormatTimestamp(Estate.Clock.AddDays(1)), ["state"] = "granted",
        ["basis"] = grant is null ? new JsonObject { ["decision"] = Ref(Decision) } : new JsonObject { ["decision"] = Ref(Decision), ["authority"] = Ref(grant) },
    };

    public RecordEnvelope BeginAttempt(bool builder = true, RecordEnvelope? order = null)
    {
        order ??= Add("work-order", new JsonObject { ["key"] = "TASK-" + Estate.Load().OfType("work-order").Count(), ["kind"] = "ordinary", ["title"] = "Maintenance", ["intent"] = "Perform the declared checks.", ["scope"] = new JsonArray("test inventory"), ["exitQuestion"] = "Was the inventory checked?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("report") });
        RecordEnvelope assignment = Add("assignment", new JsonObject { ["contributor"] = builder ? Estate.BuilderId : Estate.OperatorId, ["brief"] = new JsonObject { ["outcome"] = "A checked inventory.", ["sources"] = new JsonArray(), ["permittedChanges"] = new JsonArray("test fixtures"), ["prohibitedEffects"] = new JsonArray("publication"), ["workspace"] = new JsonArray("example repository"), ["limits"] = new JsonArray(), ["expectedOutputs"] = new JsonArray("report"), ["reviewCriteria"] = new JsonArray("inventory"), ["recovery"] = "Read the prior result." } }, subject: order.Reference);
        RecordEnvelope attempt = Add("attempt", new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(Estate.Clock), ["workspace"] = "example repository", ["note"] = "Check the inventory." }, builder: builder, subject: assignment.Reference);
        return attempt;
    }

    public RecordEnvelope Result(JsonObject? link = null, bool sign = true, bool builder = true)
    {
        RecordEnvelope attempt = BeginAttempt(builder);
        return Add("result", ResultBody(link), builder: builder, subject: attempt.Reference, sign: sign);
    }

    public static JsonObject ResultBody(JsonObject? link = null)
    {
        JsonObject body = new() { ["outcome"] = "completed", ["summary"] = "Checked the inventory.", ["outputs"] = new JsonArray(), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(new JsonObject { ["name"] = "inventory", ["outcome"] = "passed", ["summary"] = "Inventory checked." }), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Review the exact result." };
        if (link is not null) body["duties"] = new JsonArray(link);
        return body;
    }

    public RecordEnvelope Review(RecordEnvelope result, JsonArray? read = null, bool sign = true, string outcome = "pass")
    {
        JsonObject body = new() { ["independence"] = "operator", ["outcome"] = outcome, ["summary"] = "Reviewed the exact result and its coverage.", ["findings"] = new JsonArray() };
        if (read is not null) body["read"] = read;
        return Add("review", body, subject: result.Reference, sign: sign);
    }

    public JsonObject Disposition(RecordEnvelope policy, int number = 0)
    {
        JsonObject disposition = OccurrenceTarget(policy, number);
        disposition.Remove("kind");
        disposition["subject"] = Ref(Estate.Estate, false);
        disposition["disposition"] = "ended-unmet";
        disposition["reason"] = "The operator deliberately ends this debt without claiming performance.";
        return disposition;
    }

    public void AssertValid()
    {
        EstateModel model = Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
    }

    private RecordEnvelope Device(string principal)
    {
        string id = Ulid.NewUlid().Value;
        string publicKey = RecordSigning.EncodePublicKey(store.Create(id));
        return Estate.Add("device", principal, $$"""{"principal":"{{principal}}","name":"Test signing device","platform":"linux","custody":"file-owner-only","algorithm":"ecdsa-p256-sha256","publicKey":"{{publicKey}}","profiles":["embedded"],"presence":"none","purpose":"signing"}""", id: id, signer: new StoreSigner(store, id));
    }

    public void Dispose()
    {
        Estate.Dispose();
        if (Directory.Exists(keyHome)) Directory.Delete(keyHome, true);
    }
}
