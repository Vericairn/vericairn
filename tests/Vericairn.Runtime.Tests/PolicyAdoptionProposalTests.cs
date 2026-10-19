// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Packs.Software;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

/// <summary>Checks the actual proposed document bodies with the shipped readers, in an isolated estate.</summary>
public sealed partial class PolicyAdoptionProposalTests
{
    private static readonly string[] ObligationKeys = ["reviewer-separation-revisit", "security-disclosure", "writing-grant"];

    [Fact]
    public void ProposedDefinitionsUseTheExistingRecordAndRuleSchemas()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope product = fixture.Add("product", new JsonObject
        {
            ["name"] = "Proposal fixture", ["slug"] = "proposal-fixture", ["summary"] = "Synthetic adoption subjects.",
            ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
            ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture surface." }),
        });
        RecordEnvelope repository = fixture.Add("resource", new JsonObject
        {
            ["kind"] = "repository", ["name"] = "Proposal source", ["role"] = "Retain the fixture inventory.",
            ["location"] = new JsonObject { ["github"] = "Example/proposal-fixture" }, ["visibility"] = "public",
        }, subject: product.Reference);
        RecordEnvelope work = fixture.Add("work-order", new JsonObject
        {
            ["key"] = "CRED-01", ["kind"] = "ordinary", ["title"] = "Fixture custody work", ["intent"] = "Exercise an exact acceptance gate.",
            ["scope"] = new JsonArray("Synthetic records"), ["exitQuestion"] = "Is the gate evidenced?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("Result"),
        });
        JsonObject decisionBody = fixture.DecisionBody();
        decisionBody["obligations"] = new JsonArray(ObligationKeys
            .Select(key => (JsonNode)new JsonObject
            {
                ["key"] = key, ["owner"] = fixture.Estate.BuilderId, ["summary"] = "Retain the required evidence.",
                ["resolution"] = "evidence", ["gates"] = "The exact proposed gate.", ["discharge"] = "Record the inspected evidence.",
            }).ToArray());
        RecordEnvelope decision = fixture.Add("decision", decisionBody);
        RecordEnvelope historicalReview = fixture.Review(fixture.Result());
        var replacements = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal)
        {
            ["01M4TRNF0RZE2VEZQYP9WK3GA4"] = fixture.Estate.Estate,
            ["01M4TRNE1GWEAQ17M4QCDHXEZ5"] = product,
            ["01M4TRNE1GB7CYB7DND8GBTTM1"] = repository,
            ["01M504XBMAJ0TFWXCQZZ8P3BE7"] = work,
            ["01M51CDST3GS2CDFXD22Z3NKJP"] = decision,
            ["01M54ZFB99VAHZ9E5HDJPEV0TF"] = decision,
            ["01M54ZFDD1MG1GS49CQA56J6QW"] = decision,
            ["01M539T84D1MY51VP5JJPJC2JM"] = historicalReview,
        };
        JsonObject[] bodies = Bodies();
        JsonObject[] policies = bodies.Where(b => b.ContainsKey("rules")).ToArray();
        Assert.Equal(19, policies.Length);
        Assert.Single(policies, b => b["level"]!.GetValue<string>() == "refuse");
        var written = new List<RecordEnvelope>();
        foreach (JsonObject body in policies)
        {
            body.Remove("basis"); // The fixture supplies its own signed exact-definition approval.
            Rebase(body);
            written.Add(fixture.Policy(body));
        }
        fixture.AssertValid();
        PolicyKindRegistry foundation = PolicyKindRegistry.Foundation(PolicyBasis.Hash("fixture foundation"));
        var registry = new PolicyKindRegistry(foundation.Registrations.Append(ReferenceCoverage.Registration(PolicyBasis.Hash("fixture software"))));
        var basis = new PolicyBasis(fixture.Estate.Load(), "2100-01-01T00:00:00Z", PolicyTimeOrigin.Preview, implementations: registry.Implementations);
        foreach (RecordEnvelope policy in written)
        foreach (JsonElement rule in policy.Body.GetProperty("rules").EnumerateArray())
        {
            PolicyKindRegistration registration = Assert.Single(registry.Registrations, r => r.Kind == rule.GetProperty("kind").GetString() && r.Version == rule.GetProperty("version").GetInt32());
            Assert.Contains(policy.Body.GetProperty("appliesTo").GetProperty("kind").GetString(), registration.Selections);
            registration.Validate(new PolicyRuleContext(basis, policy, rule));
        }

        // The real grant is intentionally unresolved in the proposal. Validate the scoped contents
        // only after supplying a fixture grant, without pretending that this creates estate authority.
        JsonObject[] entries = bodies.Where(b => b.ContainsKey("token")).ToArray();
        Assert.Equal(4, entries.Length);
        JsonObject bounds = PolicyFixture.WritingBounds();
        bounds["tokens"] = new JsonArray(entries.Select(e => e["token"]!.DeepClone()).ToArray());
        RecordEnvelope grant = fixture.Grant("manage-writing-exceptions", bounds);
        foreach (JsonObject entry in entries)
        {
            Assert.False(entry.ContainsKey("basis"));
            Rebase(entry);
            entry["basis"] = new JsonObject { ["kind"] = "authority", ["grant"] = PolicyFixture.Ref(grant) };
            fixture.Add("writing-exception", entry, builder: true);
        }
        fixture.AssertValid();

        void Rebase(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                if (obj["id"] is JsonValue id && replacements.TryGetValue(id.GetValue<string>(), out RecordEnvelope? target))
                {
                    obj["id"] = target.Id;
                    if (obj.ContainsKey("hash")) obj["hash"] = target.ComputedHash;
                }
                foreach ((string key, JsonNode? child) in obj.ToArray())
                {
                    if (child is JsonValue value && value.TryGetValue(out string? text))
                        obj[key] = text switch
                        {
                            "<adoption UTC instant>" => "2100-01-01T00:00:00Z",
                            "01M4TRNE1GYEK4P8MXQAB3C8Q4" => fixture.Estate.BuilderId,
                            "01M4TRNEE10EJ1WXNDK3H2FKKS" => fixture.Estate.OperatorId,
                            _ => replacements.TryGetValue(text, out RecordEnvelope? record) ? record.Id : text,
                        };
                    else if (child is not null) Rebase(child);
                }
            }
            else if (node is JsonArray array)
                foreach (JsonNode child in array.OfType<JsonNode>()) Rebase(child);
        }
    }

    private static JsonObject[] Bodies()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        string source = File.ReadAllText(Path.Combine(root, "docs", "policy-adoption-proposals.md"));
        return JsonBlocks().Matches(source).Select(m => JsonNode.Parse(m.Groups[1].Value)!.AsObject()).ToArray();
    }

    [GeneratedRegex(@"```json\r?\n(.*?)\r?\n```", RegexOptions.Singleline)]
    private static partial Regex JsonBlocks();
}
