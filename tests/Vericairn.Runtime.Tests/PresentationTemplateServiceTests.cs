// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.TemplateFixture;

namespace Vericairn.Runtime.Tests;

public sealed class PresentationTemplateServiceTests
{
    public static IEnumerable<object[]> Variants => PresentationTemplateTests.Variants;

    [Fact]
    public void WorkAndSectionBriefsUseAdoptedContextWithoutRemovingTheirOriginalsOrCriteria()
    {
        using var estate = new TestEstate(3);
        (_, RecordEnvelope order) = Source(estate);
        TemplateDefinition contract = Install(estate, Contract(Section("answer")));
        RecordEnvelope declaration = Declare(estate, contract);
        Revision(estate, "docs/example.md", Fill(TemplateSections.Skeleton(contract), contract, "answer", "Keep every required fact.\n"));
        using EstateService api = EstateService.Open(estate.Root);
        SectionBriefResponse legacy = api.SectionBrief(declaration.Id, "answer");
        Assert.Null(legacy.Problem); Assert.Null(legacy.Presentation);
        TemplateDefinition context = PresentationTemplates.Definitions.Single(d => d.Id == PresentationTemplates.Context);
        PresentationTemplateTests.Adopt(estate, context);
        SectionBriefResponse adopted = api.SectionBrief(declaration.Id, "answer");
        Assert.Null(adopted.Problem); Assert.NotNull(adopted.Presentation);
        Assert.Equal(JsonSerializer.Serialize(legacy.Brief), JsonSerializer.Serialize(adopted.Brief));
        Assert.Equal(adopted.Presentation.Text, TemplatePresentation.Brief(adopted));
        Assert.All(adopted.Presentation.Checks, c => Assert.Equal("passed", c.Outcome));
        Assert.Equal("incomplete", api.SectionBrief(declaration.Id, "answer", adopted.Brief!.Bytes).Problem!.Code);
        foreach (string depth in new[] { "short", "standard", "comprehensive" })
        {
            BriefResponse brief = api.BriefFor(order.Id, estate.OperatorId, view: new(Handoff: depth))!;
            Assert.Null(brief.Problem);
            Assert.Contains("Depth: " + depth, brief.Markdown);
            Assert.Contains("Read every source.", brief.Markdown);
            Assert.Contains(order.ComputedHash, brief.Markdown);
            Assert.Equal(Encoding.UTF8.GetByteCount(brief.Markdown), brief.MarkdownMeasurement!.Bytes);
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void PrepareAndCheckEveryDepthUsesExactSourcesAndCreatesNoRecords(string id, string depth)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope result, RecordEnvelope order) = Source(estate);
        TemplateDefinition definition = PresentationTemplates.Definitions.Single(d => d.Id == id);
        RecordEnvelope adoption = PresentationTemplateTests.Adopt(estate, definition);
        using EstateService api = EstateService.Open(estate.Root);
        int before = api.Session.Load().Records.Count;
        var request = Request(estate, definition, id == PresentationTemplates.Context ? order : result, depth);
        TemplateRenderResponse prepared = api.TemplateRender(request);
        Assert.Null(prepared.Problem);
        Assert.All(prepared.Checks, c => Assert.Equal("passed", c.Outcome));
        Assert.Equal(Encoding.UTF8.GetByteCount(prepared.Text!), prepared.Bytes);
        Assert.Contains(request.Subject.Hash, prepared.Text!);
        Assert.Contains("not-performed", prepared.Text!);
        Assert.Equal(adoption.Id, prepared.Use!.Adoption.Id);
        Assert.Null(api.TemplateRender(request with { Candidate = prepared.Text, Adoption = prepared.Use.Adoption }).Problem);
        foreach (string key in new[] { "originals", "source-identities" })
        {
            TemplateSectionContent section = TemplateSections.Read(prepared.Text!, definition.Elements)[key];
            string broken = string.Concat(prepared.Text.AsSpan(0, section.Start), prepared.Text.AsSpan(section.Start + section.Length));
            TemplateRenderResponse checkedBroken = api.TemplateRender(request with { Candidate = broken });
            Assert.Equal("failed-check", checkedBroken.Problem!.Code);
            Assert.Contains(checkedBroken.Checks, c => c.Id == key + "/complete-source" && c.Outcome == "failed");
        }
        string changed = prepared.Text!.Replace("not-performed", "passed", StringComparison.Ordinal);
        Assert.Equal("failed-check", api.TemplateRender(request with { Candidate = changed }).Problem!.Code);
        TemplateRenderResponse overflow = api.TemplateRender(request with { MaxBytes = 1 });
        Assert.Equal("refused", overflow.Problem!.Code); Assert.Null(overflow.Text);
        Assert.Equal(before, api.Session.Load().Records.Count);
        Assert.Empty(api.Session.Load().OfType("review"));
    }

    [Fact]
    public void UndeclaredAndWithdrawnAdoptionPreserveLegacyOutputAndPriorInstancesRemainCheckable()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope result, RecordEnvelope order) = Source(estate);
        using EstateService api = EstateService.Open(estate.Root);
        TemplateDefinition communication = PresentationTemplates.Definitions.Single(d => d.Id == PresentationTemplates.Communication);
        TemplateDefinition context = PresentationTemplates.Definitions.Single(d => d.Id == PresentationTemplates.Context);
        CommunicationProfile profile = api.Communication(estate.OperatorId);
        Assert.Null(profile.Template);
        const string original = "Do not publish. Checks: not-performed. Read the original evidence.\n";
        string legacy = CommunicationPresentation.Protected(profile, "status", "subject", "Summary.", original);
        Assert.DoesNotContain("vericairn.estate/communication", legacy);
        Assert.EndsWith(original, legacy);
        Assert.DoesNotContain("template", JsonSerializer.Serialize(profile, ProtocolJson.Options), StringComparison.OrdinalIgnoreCase);
        ContextResponse before = api.Context(order.Id);
        Assert.Null(before.Problem);
        Assert.Equal("refused", api.TemplateRender(Request(estate, communication, result, "short")).Problem!.Code);

        RecordEnvelope adoption = PresentationTemplateTests.Adopt(estate, communication);
        RecordEnvelope contextAdoption = PresentationTemplateTests.Adopt(estate, context);
        CommunicationProfile adopted = api.Communication(estate.OperatorId);
        Assert.Equal(adoption.Id, adopted.Template!.Adoption.Id);
        foreach (string depth in new[] { "short", "standard", "comprehensive" })
        {
            CommunicationProfile view = api.Communication(estate.OperatorId, view: new(Status: depth));
            string text = CommunicationPresentation.Protected(view, "status", "subject", "Summary.", original);
            Assert.Contains("Depth: " + depth, text);
            Assert.Contains("Do not publish.", text);
            Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(original)), text);
            Assert.Equal("passed", CommunicationTemplatePresentation.Check(view.Template!, depth, "Summary.", original, "subject", view, text).Outcome);
            Assert.Equal("failed", CommunicationTemplatePresentation.Check(view.Template!, depth, "Summary.", original, "subject", view,
                text.Replace("Do not publish.", "Publish.", StringComparison.Ordinal)).Outcome);
            Assert.Equal("failed", CommunicationTemplatePresentation.Check(view.Template!, depth, "Summary.", original, "subject", view,
                text.Replace(view.RevisionHash!, "missing", StringComparison.Ordinal)).Outcome);
            string brief = CommunicationPresentation.Brief(original, view);
            Assert.Contains("\"bytes\": " + Encoding.UTF8.GetByteCount(brief).ToString(System.Globalization.CultureInfo.InvariantCulture), brief);
            Assert.Contains("Read communication_get again", brief);
        }
        ContextResponse changed = api.Context(order.Id);
        Assert.Null(changed.Problem); Assert.Contains(context.Hash, changed.Text);
        Assert.Equal(before.Package!.Payload.GetProperty("originals").GetRawText(), changed.Package!.Payload.GetProperty("originals").GetRawText());
        var request = Request(estate, communication, result, "short");
        TemplateRenderResponse prepared = api.TemplateRender(request);
        PresentationTemplateTests.Adopt(estate, communication, "disabled", adoption.Id);
        PresentationTemplateTests.Adopt(estate, context, "disabled", contextAdoption.Id);
        Assert.Null(api.Communication(estate.OperatorId).Template);
        Assert.Equal(legacy, CommunicationPresentation.Protected(profile with { Template = api.Communication(estate.OperatorId).Template }, "status", "subject", "Summary.", original));
        ContextResponse restored = api.Context(order.Id);
        Assert.Equal(ContextAssembly.Serialize(restored.Package!), restored.Text);
        Assert.Equal("refused", api.TemplateRender(request).Problem!.Code);
        Assert.Null(api.TemplateRender(request with { Candidate = prepared.Text, Adoption = prepared.Use!.Adoption }).Problem);
        Assert.Equal(4, api.Session.Load().OfType("template-defaults").Count());
    }

    [Fact]
    public void ReviewRequiresEveryCriterionAndFindingComponentAndDerivesIndependence()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope result, _) = Source(estate);
        TemplateDefinition definition = PresentationTemplates.Definitions.Single(d => d.Id == PresentationTemplates.Review);
        PresentationTemplateTests.Adopt(estate, definition);
        using EstateService api = EstateService.Open(estate.Root);
        var request = Request(estate, definition, result, "short");
        JsonObject authored = JsonNode.Parse(request.Authored!.Value.GetRawText())!.AsObject();
        authored["criteria"] = new JsonArray();
        Assert.Equal("refused", api.TemplateRender(request with { Authored = Element(authored) }).Problem!.Code);
        authored = JsonNode.Parse(request.Authored.Value.GetRawText())!.AsObject();
        authored["criteria"]![0]!["outcome"] = "passed";
        Assert.Equal("refused", api.TemplateRender(request with { Authored = Element(authored) }).Problem!.Code);
        authored["criteria"]![0]!["evidence"] = new JsonArray(Reference(result));
        Assert.Null(api.TemplateRender(request with { Authored = Element(authored) }).Problem);
        authored["reviewer"] = estate.BuilderId;
        Assert.Contains("self", api.TemplateRender(request with { Authored = Element(authored) }).Text!);
        authored["independence"] = "operator";
        Assert.Equal("refused", api.TemplateRender(request with { Authored = Element(authored) }).Problem!.Code);
    }

    [Fact]
    public void HistoricalSubjectsStayPinnedAndGeneratedFactsCannotBeOverridden()
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope result, _) = Source(estate);
        TemplateDefinition definition = PresentationTemplates.Definitions.Single(d => d.Id == PresentationTemplates.Communication);
        PresentationTemplateTests.Adopt(estate, definition);
        using EstateService api = EstateService.Open(estate.Root);
        TemplateRenderRequest request = Request(estate, definition, result, "short");
        JsonObject body = JsonNode.Parse(result.Body.GetRawText())!.AsObject(); body["summary"] = "New revision with a different conclusion.";
        estate.Add("result", estate.BuilderId, body.ToJsonString(), result.Subject, result.Id);
        TemplateRenderResponse historical = api.TemplateRender(request);
        Assert.Null(historical.Problem); Assert.Contains("The implementation is ready", historical.Text!);
        Assert.DoesNotContain("New revision with a different conclusion", historical.Text!);
        Assert.Equal("refused", api.TemplateRender(request with { Subject = request.Subject with { Hash = "sha256:" + new string('b', 64) } }).Problem!.Code);
        Assert.Equal("refused", api.TemplateRender(request with { Authored = Element(new JsonObject { ["facts"] = "Everything passed." }) }).Problem!.Code);
    }

    public static (RecordEnvelope Result, RecordEnvelope Order) Source(TestEstate estate)
    {
        RecordEnvelope attempt = Attempt(estate);
        RecordEnvelope result = estate.Add("result", estate.BuilderId, new JsonObject
        {
            ["outcome"] = "partial", ["summary"] = "The implementation is ready for the outstanding platform checks.",
            ["outputs"] = new JsonArray(), ["evidence"] = new JsonArray(),
            ["checks"] = new JsonArray(new JsonObject { ["name"] = "Platform suite", ["outcome"] = "not-performed", ["summary"] = "The platform suite has not run." }),
            ["limitations"] = new JsonArray("Platform coverage remains unverified."), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Run the platform suite before acceptance.",
        }.ToJsonString(), attempt.Reference);
        EstateModel model = estate.Load();
        return (result, model.ById[model.ById[attempt.Subject!.Id].Subject!.Id]);
    }

    public static TemplateRenderRequest Request(TestEstate estate, TemplateDefinition definition, RecordEnvelope subject, string depth)
    {
        JsonElement? authored = definition.Id switch
        {
            PresentationTemplates.Commit => Element(new JsonObject { ["outcome"] = "Preserve complete presentation sources", ["reason"] = "Readers need every required fact.", ["attribution"] = new JsonArray("Reviewed-by: Example Reviewer") }),
            PresentationTemplates.Review => Element(new JsonObject
            {
                ["reviewer"] = estate.OperatorId, ["scope"] = "The exact result and all assignment criteria.",
                ["criteria"] = new JsonArray(new JsonObject { ["criterion"] = "Read every source.", ["outcome"] = "not-performed", ["method"] = "Review remains outstanding.", ["evidence"] = new JsonArray() }),
                ["findings"] = new JsonArray(new JsonObject { ["condition"] = "The platform suite has not run.", ["effect"] = "Platform behavior remains unknown.", ["evidence"] = new JsonArray(Reference(subject)), ["disposition"] = "Run the suite before acceptance." }),
                ["limits"] = new JsonArray("No platform qualification."), ["next"] = "Run the outstanding checks.",
            }),
            _ => null,
        };
        return new(new(definition.Id, definition.Version, definition.Hash), new(subject.Type, subject.Id, subject.ComputedHash), depth, authored);
    }
}
