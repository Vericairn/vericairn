// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public sealed class PromptRecordsTests
{
    public static PolicyFixture RecordedFixture()
    {
        var fixture = new PolicyFixture();
        JsonObject definition = Definition();
        definition["origin"] = new JsonObject { ["source"] = "example", ["key"] = "review", ["version"] = "1.0.0", ["hash"] = "sha256:" + new string('a', 64) };
        RecordEnvelope prompt = fixture.Add("saved-prompt", definition);
        fixture.Add("prompt-preference", new JsonObject { ["prompt"] = "estate:review", ["scope"] = "person", ["person"] = fixture.Estate.OperatorId, ["favorite"] = true, ["hidden"] = false });
        fixture.Add("prompt-preference", new JsonObject { ["prompt"] = "estate:review", ["scope"] = "estate", ["disabled"] = false });
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope assigned = fixture.Estate.Load().ById[attempt.Subject!.Id];
        JsonObject body = PolicyFixture.Body(assigned); body["promptUse"] = Use(prompt);
        RecordEnvelope assignment = fixture.Add("assignment", body, subject: assigned.Subject);
        fixture.Add("operation", new JsonObject
        {
            ["provider"] = "example", ["action"] = "agent-run", ["identity"] = "prompt-fixture-run", ["intent"] = "Run within the existing assignment.",
            ["target"] = new JsonObject { ["assignment"] = assignment.Id, ["principal"] = fixture.Estate.BuilderId, ["workspace"] = "example repository" },
            ["status"] = "not-started", ["cap"] = new JsonObject { ["wallMinutes"] = 1 }, ["evidence"] = new JsonArray(), ["promptUse"] = Use(prompt),
        }, subject: assignment.Reference);
        RecordEnvelope ask = fixture.Add("ask", new JsonObject { ["to"] = fixture.Estate.OperatorId, ["question"] = "Which evidence?", ["recommendation"] = "Use the assigned evidence.", ["consequence"] = "Await an answer.", ["state"] = "open" }, builder: true);
        JsonObject answer = fixture.DecisionBody(); answer["promptUse"] = Use(prompt); answer["statement"] = answer["promptUse"]!["rendered"]!.DeepClone(); answer["answers"] = new JsonArray(ask.Id);
        fixture.Add("decision", answer);
        return fixture;
    }

    public static JsonObject Definition(string key = "review") => new()
    {
        ["key"] = key, ["title"] = "Review evidence", ["description"] = "Inspect the evidence within the assigned scope.",
        ["text"] = "Review {topic}; explain the observed evidence and unknowns.", ["category"] = "Work/Review",
        ["placeholders"] = new JsonArray(new JsonObject { ["name"] = "topic", ["description"] = "The evidence topic." }),
        ["tags"] = new JsonArray("evidence"), ["roles"] = new JsonArray(), ["state"] = "active",
    };

    public static JsonObject Use(RecordEnvelope prompt, string value = "the assigned change") => new()
    {
        ["source"] = "estate", ["key"] = EstateModel.BodyString(prompt, "key"), ["version"] = prompt.Id, ["hash"] = prompt.ComputedHash,
        ["definition"] = PolicyFixture.Body(prompt), ["values"] = new JsonObject { ["topic"] = value },
        ["rendered"] = SavedPrompts.Render(prompt.Body, new Dictionary<string, string> { ["topic"] = value }), ["authority"] = "unchanged",
    };

    [Fact]
    public void FormatThreeAddsSignedDefinitionsWithoutAdmittingEarlierFormats()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope first = fixture.Add("saved-prompt", Definition());
        Assert.False(fixture.Estate.Load().Findings.HasErrors);
        JsonObject revision = Definition(); revision["title"] = "Review the exact evidence";
        RecordEnvelope revised = fixture.Add("saved-prompt", revision, supersedes: first.Id);
        JsonObject retired = PolicyFixture.Body(revised); retired["state"] = "retired";
        fixture.Add("saved-prompt", retired, supersedes: revised.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors);
        Assert.Equal(3, fixture.Estate.Load().OfType("saved-prompt").Count());
        Assert.Equal(3, RecordFormats.Current);
        using var old = new PolicyFixture(2);
        old.Add("saved-prompt", Definition());
        Assert.True(old.Estate.Load().Findings.HasErrors);
        using var unsigned = new PolicyFixture();
        unsigned.Add("saved-prompt", Definition(), sign: false);
        Assert.Contains(unsigned.Estate.Load().Findings, f => f.Code == "prompt.signature");
    }

    [Fact]
    public void OnlyExplicitGrantsWriteSharedPromptsAndPreferencesBelongToTheirAuthor()
    {
        using var fixture = new PolicyFixture();
        fixture.Add("saved-prompt", Definition(), builder: true);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "prompt.authority");
        using var granted = new PolicyFixture();
        granted.Add("authority", new JsonObject { ["grantee"] = granted.Estate.BuilderId, ["scope"] = new JsonArray(Scopes.ManagePrompts), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = granted.Decision.Id });
        granted.Add("saved-prompt", Definition(), builder: true);
        Assert.False(granted.Estate.Load().Findings.HasErrors);
        granted.Add("prompt-preference", new JsonObject { ["prompt"] = "estate:review", ["scope"] = "person", ["person"] = granted.Estate.OperatorId, ["hidden"] = true, ["favorite"] = false }, builder: true);
        Assert.Contains(granted.Estate.Load().Findings, f => f.Code == "prompt.authority");
    }

    [Fact]
    public void GrantRevocationPreservesPastDefinitionsAndRefusesLaterOnes()
    {
        using var fixture = new PolicyFixture();
        var body = new JsonObject { ["grantee"] = fixture.Estate.BuilderId, ["scope"] = new JsonArray(Scopes.ManagePrompts), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = fixture.Decision.Id };
        RecordEnvelope grant = fixture.Add("authority", body);
        fixture.Add("saved-prompt", Definition(), builder: true);
        body["scope"] = new JsonArray("local-engineering");
        fixture.Add("authority", body, supersedes: grant.Id);
        Assert.False(fixture.Estate.Load().Findings.HasErrors);
        fixture.Add("saved-prompt", Definition("later"), builder: true);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "prompt.authority");
    }

    [Theory]
    [InlineData("conditions")]
    [InlineData("reserved")]
    [InlineData("unsigned")]
    public void UnprovenOrUnsignedGrantsCannotWriteSharedPrompts(string fault)
    {
        using var fixture = new PolicyFixture();
        var body = new JsonObject { ["grantee"] = fixture.Estate.BuilderId, ["scope"] = new JsonArray(Scopes.ManagePrompts), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = fixture.Decision.Id };
        if (fault == "conditions") body["conditions"] = new JsonArray("Only after a separate approval.");
        if (fault == "reserved") body["reserved"] = new JsonArray(Scopes.ManagePrompts);
        fixture.Add("authority", body, sign: fault != "unsigned");
        fixture.Add("saved-prompt", Definition(), builder: true);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "prompt.authority");
    }

    [Fact]
    public void PlaceholdersAreExactLiteralAndDescribedAndCategoriesAreNormalized()
    {
        JsonObject body = Definition();
        Assert.Equal("Review {other}; explain the observed evidence and unknowns.", SavedPrompts.Render(PolicyFixture.Element(body), new Dictionary<string, string> { ["topic"] = "{other}" }));
        Assert.Throws<ArgumentException>(() => SavedPrompts.Render(PolicyFixture.Element(body), new Dictionary<string, string>()));
        using var fixture = new PolicyFixture();
        body["category"] = "Work//Review";
        body["placeholders"]![0]!["name"] = "unused";
        fixture.Add("saved-prompt", body);
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "prompt.category");
        Assert.Contains(fixture.Estate.Load().Findings, f => f.Code == "prompt.placeholder");
    }

    [Fact]
    public void UsesPinTheirDefinitionAndValuesAndCannotDeclareChangedAuthority()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope prompt = fixture.Add("saved-prompt", Definition());
        RecordEnvelope attempt = fixture.BeginAttempt();
        RecordEnvelope assignment = fixture.Estate.Load().ById[attempt.Subject!.Id];
        JsonObject body = PolicyFixture.Body(assignment);
        body["promptUse"] = Use(prompt);
        RecordEnvelope used = fixture.Add("assignment", body, subject: assignment.Subject);
        Assert.False(fixture.Estate.Load().Findings.HasErrors);
        var findings = new FindingList();
        JsonObject bad = PolicyFixture.Body(used);
        bad["promptUse"]!["authority"] = "increase-budget";
        bad["promptUse"]!["rendered"] = "Unrecorded substitution";
        RecordEnvelope changed = fixture.Add("assignment", bad, subject: assignment.Subject);
        new AssignmentType().Validate(new ValidationContext(fixture.Estate.Load(), changed, findings));
        Assert.Contains(findings, f => f.Code == "prompt.authority" && f.Message.Contains("amendment", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "prompt.values");
    }

    [Fact]
    public void PromptReadersRetainTheSourceValuesAndNestedCollections()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope prompt = fixture.Add("saved-prompt", Definition());
        string text = System.Net.WebUtility.HtmlDecode(RecordCompanionTests.Text(RecordCompanionTests.Render([DocumentSourceSnapshot.Record(prompt)])));
        Assert.Contains("Review evidence", text, StringComparison.Ordinal);
        Assert.Contains("The evidence topic.", text, StringComparison.Ordinal);
        Assert.Contains(DocumentCoverage.All, c => c.Kind == "record/saved-prompt#placeholders");
        Assert.Contains(DocumentCoverage.All, c => c.Kind == "record/operation#promptUse.values");
    }
}
