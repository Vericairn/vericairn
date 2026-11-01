// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class BlockedTermsTests
{
    private static readonly string[] TernOnly = ["Tern"];
    private static readonly string[] HeronOnly = ["Heron"];
    private static readonly ImmutableArray<string> Terms = BlockedTerms.TermsOf(JsonSerializer.SerializeToElement(new { terms = new[] { "Tern", "orbit-7-lens" } }));

    [Theory]
    [InlineData("A Tern session wrote this.", "tern")]
    [InlineData("TERN", "tern")]
    [InlineData("Ｔｅｒｎ in compatibility form", "tern")]
    [InlineData("(tern)", "tern")]
    [InlineData("ran on orbit-7-lens.", "orbit-7-lens")]
    [InlineData("Terns and a tern-like bird", "tern")]
    [InlineData("Lanterns, terne plate, intern", null)]
    [InlineData("orbit-7-lens2 is a different name", null)]
    public void TermsMatchWholeIgnoringCaseAndCompatibilityForms(string text, string? expected) =>
        Assert.Equal(expected, BlockedTerms.Introduced(Terms, "", text));

    [Fact]
    public void OnlyAnIntroducedOccurrenceIsRefused()
    {
        Assert.Null(BlockedTerms.Introduced(Terms, "kept Tern", "still kept Tern, reworded"));
        Assert.Equal("tern", BlockedTerms.Introduced(Terms, "kept Tern", "kept Tern and another tern"));
        Assert.Null(BlockedTerms.Introduced([], "", "Tern"));
    }

    [Theory]
    [InlineData("{\"terms\":[]}")]
    [InlineData("{\"terms\":[\"x\"]}")]
    [InlineData("{\"terms\":[\"Tern\",\"TERN\"]}")]
    [InlineData("{\"terms\":[\"bad\\u0007term\"]}")]
    [InlineData("{\"terms\":[\"--\"]}")]
    [InlineData("{\"terms\":[\"Tern\"],\"mode\":\"warn\"}")]
    [InlineData("{\"terms\":[7]}")]
    [InlineData("[]")]
    public void MalformedListsAreRefused(string parameters)
    {
        using JsonDocument json = JsonDocument.Parse(parameters);
        Assert.NotNull(BlockedTerms.Validate(json.RootElement));
        Assert.Empty(BlockedTerms.TermsOf(json.RootElement));
    }

    [Fact]
    public void RecordsAreReadOutsideModelSelectorsAndTheDeclaringPolicyIsExempt()
    {
        JsonElement selector = JsonSerializer.SerializeToElement(new { execution = new { requested = new { model = "orbit-7-lens" } }, summary = "Ran." });
        Assert.Null(BlockedTerms.IntroducedInRecord(Terms, "operation", selector, null));
        JsonElement prose = JsonSerializer.SerializeToElement(new { findings = new[] { new { summary = "Reviewed by a Tern session." } } });
        Assert.Equal("tern", BlockedTerms.IntroducedInRecord(Terms, "review", prose, null));
        Assert.Null(BlockedTerms.IntroducedInRecord(Terms, "review", prose, prose));
        JsonElement named = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Tern"] = "a property name is prose too" });
        Assert.Equal("tern", BlockedTerms.IntroducedInRecord(Terms, "observation", named, null));
        JsonElement policy = JsonSerializer.SerializeToElement(new { rules = new[] { new { kind = BlockedTerms.Kind, parameters = new { terms = TernOnly } } } });
        Assert.True(BlockedTerms.Declares("estate-policy", policy));
        Assert.Null(BlockedTerms.IntroducedInRecord(Terms, "estate-policy", policy, null));
        Assert.Equal("tern", BlockedTerms.IntroducedInRecord(Terms, "decision", policy, null));
    }

    [Fact]
    public void GeneratedFilesAreComparedPathByPath()
    {
        ImmutableArray<DocumentFile> before = [File("a.md", "Kept Tern."), File("b.md", "Nothing.")];
        Assert.Null(BlockedTerms.IntroducedInFiles(Terms, before, [File("a.md", "Kept Tern, regenerated."), File("b.md", "Still nothing.")]));
        Assert.Equal(("b.md", "tern"), BlockedTerms.IntroducedInFiles(Terms, before, [File("a.md", "Kept Tern."), File("b.md", "Now a Tern.")]));
        Assert.Equal(("c.md", "orbit-7-lens"), BlockedTerms.IntroducedInFiles(Terms, before, [File("c.md", "New file naming orbit-7-lens.")]));
    }

    [Fact]
    public void TheListComesFromCurrentPoliciesThatHaveStarted()
    {
        RecordEnvelope started = FoundationPolicyTests.P(BlockedTerms.Kind, new { terms = TernOnly }, key: "started");
        RecordEnvelope future = FoundationPolicyTests.R("estate-policy", "future", new
        {
            key = "future", owner = "operator", state = "active", level = "refuse", startsAt = "2027-01-01T00:00:00Z", maxExceptionDays = 30,
            appliesTo = new { kind = "estate" }, rules = new[] { new { key = "rule", kind = BlockedTerms.Kind, version = 1, parameters = new { terms = HeronOnly } } },
        });
        EstateModel model = PolicyBasisTests.Model([started, future]);
        Assert.Equal(["tern"], BlockedTerms.In(model, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(["heron", "tern"], BlockedTerms.In(model, new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ThePolicyReportsRecordsWrittenAfterItBeganWithoutRepeatingTheTerm()
    {
        RecordEnvelope earlier = FoundationPolicyTests.R("observation", "earlier", new { summary = "A Tern session, before the policy." }, at: "2025-12-01T00:00:00Z");
        RecordEnvelope later = FoundationPolicyTests.R("observation", "later", new { summary = "A Tern session, after the policy." }, at: "2026-01-01T12:00:00Z");
        RecordEnvelope selector = FoundationPolicyTests.R("operation", "run", new { execution = new { requested = new { model = "Tern" } } }, at: "2026-01-01T12:00:00Z");
        PolicyEvaluation evaluation = FoundationPolicyTests.Run(BlockedTerms.Kind, new { terms = TernOnly }, [earlier, later, selector]);
        PolicyFinding finding = Assert.Single(evaluation.Findings, f => f.Verdict == "unmet");
        Assert.Equal("later", finding.Target.Record!.Id);
        Assert.DoesNotContain("tern", (finding.Actual + finding.Required + finding.NextAction).ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Equal("satisfied", Assert.Single(FoundationPolicyTests.Run(BlockedTerms.Kind, new { terms = HeronOnly }, [earlier, later]).Findings).Verdict);
    }

    [Fact]
    public void AuthoringRefusesAnIntroducedTermAndAllowsTheDeclaringPolicy()
    {
        using var fixture = new PolicyFixture();
        JsonObject policy = fixture.PolicyBody("blocked-terms", duty: false);
        policy["rules"] = new JsonArray(new JsonObject
        {
            ["key"] = "terms", ["kind"] = BlockedTerms.Kind, ["version"] = 1, ["parameters"] = new JsonObject { ["terms"] = new JsonArray("Tern") },
        });
        fixture.Policy(policy);
        using EstateService api = EstateService.Open(fixture.Estate.Root);
        RecordWritten device = Ok(api.DeviceCreate(new(fixture.Estate.OperatorId, "Blocked-terms writer", "file")));
        fixture.Estate.SetClock(api.Session.Load().ById[device.Id].AtTime);
        fixture.Add("admission", new JsonObject { ["principal"] = fixture.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Writer." },
            subject: api.Session.Load().ById[device.Id].Reference);

        int before = api.Session.Load().Records.Count;
        WriteOutcome refused = api.NewRecord(new("observation", fixture.Estate.OperatorId, Ask("Reviewed by a TERN session.")));
        Assert.False(refused.Ok);
        Assert.Contains("blocked term", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, api.Session.Load().Records.Count);
        // Without the term, or in a revision of the declaring policy, the same write meets no blocked-term refusal; any other
        // validation it meets is the record type's own.
        WriteOutcome clean = api.NewRecord(new("observation", fixture.Estate.OperatorId, Ask("Reviewed by an independent session.")));
        Assert.DoesNotContain("blocked term", clean.Problem?.Message ?? "", StringComparison.Ordinal);
        RecordEnvelope declared = Assert.Single(api.Session.Load().Current("estate-policy"), r => BlockedTerms.Declares(r.Type, r.Body));
        JsonObject revision = PolicyFixture.Body(declared);
        revision["rules"]![0]!["parameters"]!["terms"] = new JsonArray("Tern", "Heron");
        WriteOutcome policyRevision = api.NewRecord(new("estate-policy", fixture.Estate.OperatorId, PolicyFixture.Element(revision), Supersedes: declared.Id));
        Assert.DoesNotContain("blocked term", policyRevision.Problem?.Message ?? "", StringComparison.Ordinal);
    }

    private static JsonElement Ask(string summary) => JsonSerializer.SerializeToElement(new { summary, observedAt = "2026-01-01T00:00:00Z" });

    private static DocumentFile File(string path, string text) => new(path, [.. Encoding.UTF8.GetBytes(text)]);

    private static RecordWritten Ok(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!;
    }
}
