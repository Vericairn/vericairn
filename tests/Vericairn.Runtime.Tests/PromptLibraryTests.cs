// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class PromptLibraryTests
{
    [Fact]
    public void LibraryGroupsSourcesByCategoryAndPreservesPersonalAndEstateHistory()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        PromptsResponse library = writer.Api.Prompts(new(Category: "Work"));
        Assert.Null(library.Problem);
        Assert.Contains(library.Prompts, p => p.Source == "foundation");
        Assert.Contains(library.Prompts, p => p.Identity == row.Identity);
        Assert.Equal(library.Prompts.Select(p => p.Definition.GetProperty("category").GetString()).Order(StringComparer.Ordinal), library.Prompts.Select(p => p.Definition.GetProperty("category").GetString()));
        Assert.Equal(4, writer.Api.Prompts(new(Category: "Software")).Prompts.Count);
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Favorite: true)));
        Assert.Single(writer.Api.Prompts(new(writer.Operator, Favorites: true)).Prompts);
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Hidden: true)));
        Assert.Empty(writer.Api.Prompts(new(writer.Operator, Favorites: true)).Prompts);
        Assert.Contains(writer.Api.Prompts(new(writer.Builder)).Prompts, p => p.Identity == row.Identity && !p.Hidden && !p.Favorite);
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Hidden: false)));
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Disabled: true)));
        Assert.DoesNotContain(writer.Api.Prompts(new(writer.Builder)).Prompts, p => p.Identity == row.Identity);
        Assert.False(writer.Api.PromptPreference(new(writer.Builder, row.Identity, Disabled: false)).Ok);
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Disabled: false)));
        Assert.Single(writer.Api.Prompts(new(writer.Operator, Favorites: true)).Prompts);
        Assert.Equal(5, writer.Model.OfType("prompt-preference").Count());
        Assert.False(writer.Model.Findings.HasErrors, string.Join('\n', writer.Model.Findings));
    }

    [Fact]
    public void AnAssignmentAndItsExactPromptUseAreOneSignedWriteWithUnchangedTerms()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        AssignRequest assignment = writer.Assignment();
        int before = writer.Model.Records.Count;
        var selection = new PromptSelection(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = "literal {another} value" });
        PromptUseResponse used = writer.Api.PromptUse(new(writer.Operator, selection, Assignment: assignment));
        Assert.True(used.Ok, used.Problem?.Message);
        Assert.Equal(before + 1, writer.Model.Records.Count);
        RecordEnvelope recorded = writer.Model.ById[used.Written!.Id];
        Assert.True(SignatureChecks.IsAuthentic(writer.Model, recorded));
        Assert.Equal(CanonicalJson.Serialize(assignment.Brief), CanonicalJson.Serialize(recorded.Body.GetProperty("brief")));
        Assert.Equal(row.Version, recorded.Body.GetProperty("promptUse").GetProperty("version").GetString());
        Assert.Equal(row.Hash, recorded.Body.GetProperty("promptUse").GetProperty("hash").GetString());
        Assert.Contains("literal {another} value", used.Text, StringComparison.Ordinal);
        Assert.Contains("Saved prompt addendum", writer.Api.Brief(assignment.WorkOrder)!.Markdown, StringComparison.Ordinal);
        JsonObject revised = PromptRecordsTests.Definition(); revised["text"] = "Inspect {topic} again.";
        Ok(writer.Api.PromptSave(new(writer.Operator, PolicyFixture.Element(revised), row.Hash)));
        Assert.Equal(row.Hash, writer.Model.ById[used.Written.Id].Body.GetProperty("promptUse").GetProperty("hash").GetString());
        int after = writer.Model.Records.Count;
        Assert.False(writer.Api.PromptUse(new(writer.Operator, selection, Assignment: assignment)).Ok);
        Assert.Equal(after, writer.Model.Records.Count);
    }

    [Fact]
    public void AnAskAnswerIsRecordedByItsRecipientAndCannotBeAnsweredTwice()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        RecordEnvelope ask = writer.Fixture.Add("ask", new JsonObject { ["to"] = writer.Operator, ["question"] = "Which evidence should be examined?", ["recommendation"] = "Use the assigned evidence.", ["consequence"] = "Wait for an answer.", ["state"] = "open" }, builder: true);
        var selection = new PromptSelection(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = "the assigned evidence" });
        Assert.False(writer.Api.PromptUse(new(writer.Builder, selection, Ask: ask.Id)).Ok);
        PromptUseResponse result = writer.Api.PromptUse(new(writer.Operator, selection, Ask: ask.Id));
        Assert.True(result.Ok, result.Problem?.Message);
        RecordEnvelope answer = writer.Model.ById[result.Written!.Id];
        Assert.Equal("decision", answer.Type);
        Assert.Equal(ask.Id, answer.Body.GetProperty("answers")[0].GetString());
        Assert.Equal(answer.Body.GetProperty("promptUse").GetProperty("rendered").GetString(), EstateModel.BodyString(answer, "statement"));
        Assert.False(writer.Api.PromptUse(new(writer.Operator, selection, Ask: ask.Id)).Ok);
    }

    [Fact]
    public void ReviewedBuiltInsCanBeCopiedWithExactOriginWhileDraftsCannotBeUsed()
    {
        using var writer = new PromptWriter();
        JsonObject definition = PromptRecordsTests.Definition("pack-example"); definition.Remove("state");
        PromptRow installed = PromptCatalog.BuiltIn("example", "1.2.3", PolicyFixture.Element(definition), draft: false);
        writer.Api.Providers.Prompts.Register([installed]);
        RecordWritten copied = Ok(writer.Api.PromptCopy(new(writer.Operator, installed.Identity, installed.Hash, "copied")));
        JsonElement origin = writer.Model.ById[copied.Id].Body.GetProperty("origin");
        Assert.Equal(installed.Hash, origin.GetProperty("hash").GetString());
        Assert.Equal(installed.Version, origin.GetProperty("version").GetString());
        PromptRow draft = writer.Api.Prompts(new()).Prompts.First(p => p.Source == "foundation");
        Assert.True(draft.Draft);
        Assert.False(writer.Api.PromptCopy(new(writer.Operator, draft.Identity, draft.Hash, "unreviewed")).Ok);
        PromptUseResponse use = writer.Api.PromptUse(new(writer.Operator, new(installed.Identity, installed.Hash, new Dictionary<string, string> { ["topic"] = "the original evidence" }), Assignment: writer.Assignment()));
        Assert.True(use.Ok, use.Problem?.Message);
        Assert.Equal("example", writer.Model.ById[use.Written!.Id].Body.GetProperty("promptUse").GetProperty("source").GetString());
    }

    [Fact]
    public void InvalidValuesDisabledRetiredAndUnsuitablePromptsProduceNoUse()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        AssignRequest assignment = writer.Assignment();
        int before = writer.Model.Records.Count;
        Assert.False(writer.Api.PromptUse(new(writer.Operator, new(row.Identity, row.Hash, new Dictionary<string, string>()), Assignment: assignment)).Ok);
        Assert.Equal(before, writer.Model.Records.Count);
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Disabled: true)));
        var selection = new PromptSelection(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = "evidence" });
        Assert.False(writer.Api.PromptUse(new(writer.Operator, selection, Assignment: assignment)).Ok);
        JsonObject retired = PromptRecordsTests.Definition(); retired["state"] = "retired";
        Ok(writer.Api.PromptSave(new(writer.Operator, PolicyFixture.Element(retired), row.Hash)));
        Assert.DoesNotContain(writer.Api.Prompts(new()).Prompts, p => p.Identity == row.Identity);
        JsonObject restricted = PromptRecordsTests.Definition("operator-only"); restricted["roles"] = new JsonArray("operator");
        Ok(writer.Api.PromptSave(new(writer.Operator, PolicyFixture.Element(restricted))));
        Assert.DoesNotContain(writer.Api.Prompts(new(writer.Builder)).Prompts, p => p.Key == "operator-only");
        PromptRow op = writer.Api.Prompts(new(writer.Operator)).Prompts.Single(p => p.Key == "operator-only");
        Assert.False(writer.Api.Assign(assignment with { Prompt = selection with { Prompt = op.Identity, Expected = op.Hash } }).Ok);
    }

    [Fact]
    public void FavorAndUseAreOneRequestAndGenericAuthoringCannotBypassAvailabilityOrAuthority()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        AssignRequest assignment = writer.Assignment();
        var selection = new PromptSelection(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = "the assigned evidence" });
        PromptUseResponse used = writer.Api.PromptUse(new(writer.Operator, selection, Assignment: assignment) { Favor = true });
        Assert.True(used.Ok, used.Problem?.Message);
        Assert.Equal(row.Identity, Assert.Single(writer.Api.Prompts(new(writer.Operator, Favorites: true)).Prompts).Identity);
        RecordEnvelope recorded = writer.Model.ById[used.Written!.Id];
        JsonObject body = PolicyFixture.Body(recorded);
        string subject = "work-order:" + recorded.Subject!.Id + ":" + recorded.Subject.Hash;
        body["promptUse"]!["authority"] = "publication";
        WriteOutcome authority = writer.Api.NewRecord(new("assignment", writer.Operator, PolicyFixture.Element(body), subject));
        Assert.False(authority.Ok);
        Assert.Contains("amendment", authority.Problem!.Message, StringComparison.Ordinal);
        body = PolicyFixture.Body(recorded);
        Ok(writer.Api.PromptPreference(new(writer.Operator, row.Identity, Disabled: true)));
        int before = writer.Model.Records.Count;
        Assert.False(writer.Api.NewRecord(new("assignment", writer.Operator, PolicyFixture.Element(body), subject)).Ok);
        Assert.Equal(before, writer.Model.Records.Count);
        Assert.Equal(row.Hash, writer.Model.ById[recorded.Id].Body.GetProperty("promptUse").GetProperty("hash").GetString());
    }

    [Fact]
    public void PromptTextAndValuesCannotSelectAdditionalRecordsOrDocuments()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        AssignRequest assignment = writer.Assignment();
        JsonObject unrelated = PolicyFixture.Body(writer.Model.ById[assignment.WorkOrder]);
        unrelated["key"] = "UNRELATED";
        RecordEnvelope outside = writer.Fixture.Add("work-order", unrelated);
        const string path = "docs/outside.md";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("Outside this step's document selection.");
        ContextTests.AddDocument(writer.Fixture.Estate, path, bytes);
        PromptUseResponse used = writer.Api.PromptUse(new(writer.Operator,
            new(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = outside.Id + " " + path }), Assignment: assignment));
        Assert.True(used.Ok, used.Problem?.Message);
        RecordEnvelope ask = writer.Fixture.Add("ask", new JsonObject { ["to"] = writer.Operator, ["question"] = "Which evidence?", ["recommendation"] = "Read the assigned evidence.", ["consequence"] = "Await an answer.", ["state"] = "open" },
            builder: true, subject: writer.Model.ById[used.Written!.Id].Reference);
        PromptUseResponse answered = writer.Api.PromptUse(new(writer.Operator,
            new(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = outside.Id + " " + path }), Ask: ask.Id));
        Assert.True(answered.Ok, answered.Problem?.Message);
        ContextResponse context = writer.Api.Context(assignment.WorkOrder);
        Assert.Null(context.Problem);
        JsonElement sources = context.Package!.Payload.GetProperty("manifest").GetProperty("content").GetProperty("sources");
        Assert.DoesNotContain(sources.EnumerateArray(), s => s.TryGetProperty("id", out JsonElement id) && id.GetString() == outside.Id);
        Assert.DoesNotContain(sources.EnumerateArray(), s => s.TryGetProperty("path", out JsonElement value) && value.GetString() == path);
        Assert.Contains(outside.Id, writer.Api.Brief(assignment.WorkOrder)!.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RevocationInTheSameRecordedSecondKeepsTheEarlierDefinitionButRefusesAnotherWrite()
    {
        using var writer = new PromptWriter();
        var body = new JsonObject { ["grantee"] = writer.Builder, ["scope"] = new JsonArray("manage-prompts"), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = writer.Fixture.Decision.Id };
        RecordEnvelope grant = writer.Fixture.Add("authority", body);
        RecordWritten prompt = Ok(writer.Api.PromptSave(new(writer.Builder, PolicyFixture.Element(PromptRecordsTests.Definition()))));
        body["scope"] = new JsonArray("local-engineering");
        Ok(writer.Api.NewRecord(new("authority", writer.Operator, PolicyFixture.Element(body), Supersedes: grant.Id, At: writer.Model.ById[prompt.Id].At)));
        Assert.False(writer.Model.Findings.HasErrors, string.Join('\n', writer.Model.Findings));
        int before = writer.Model.Records.Count;
        Assert.False(writer.Api.PromptSave(new(writer.Builder, PolicyFixture.Element(PromptRecordsTests.Definition("after-revocation")))).Ok);
        Assert.Equal(before, writer.Model.Records.Count);
    }

    public static RecordWritten Ok(WriteOutcome outcome) { Assert.True(outcome.Ok, outcome.Problem?.Message + " " + string.Join('\n', outcome.Problem?.Findings ?? [])); return outcome.Written!; }

}
