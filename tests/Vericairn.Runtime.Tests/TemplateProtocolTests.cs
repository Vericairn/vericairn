// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class TemplateProtocolTests
{
    private static readonly string[] BriefParameters = ["commit", "id", "maxBytes", "section"];

    [Fact]
    public async Task PresentationMcpToolOnlyReadsAndCatchesLostRequiredContent()
    {
        using var estate = new TestEstate(3);
        var (result, _) = PresentationTemplateServiceTests.Source(estate);
        var definition = Vericairn.Records.Templates.PresentationTemplates.Definitions.Single(d => d.Id == Vericairn.Records.Templates.PresentationTemplates.Communication);
        PresentationTemplateTests.Adopt(estate, definition);
        using EstateService service = EstateService.Open(estate.Root);
        var registry = new ToolRegistry(service, estate.BuilderId);
        Assert.False(registry.Visible().Single(t => t.Name == "template_render").Writes);
        Assert.False(registry.All.Single(t => t.Name == "template_render").Writes);
        int count = service.Session.Load().Records.Count;
        var request = PresentationTemplateServiceTests.Request(estate, definition, result, "short");
        ToolCallResult prepared = await registry.CallAsync("template_render", new JsonObject { ["request"] = JsonSerializer.SerializeToNode(request, ProtocolJson.Options) }, TestContext.Current.CancellationToken);
        Assert.False(prepared.IsError, prepared.Text);
        TemplateRenderResponse rendered = JsonSerializer.Deserialize<TemplateRenderResponse>(prepared.Text, ProtocolJson.Options)!;
        Assert.Equal(definition.Hash, rendered.Use!.Template.Hash);
        Assert.All(rendered.Checks, c => Assert.Equal("passed", c.Outcome));
        ToolCallResult failed = await registry.CallAsync("template_render", new JsonObject
        {
            ["request"] = JsonSerializer.SerializeToNode(request with { Candidate = rendered.Text!.Replace(result.ComputedHash, "missing", StringComparison.Ordinal) }, ProtocolJson.Options),
        }, TestContext.Current.CancellationToken);
        Assert.True(failed.IsError);
        Assert.Equal("failed-check", JsonSerializer.Deserialize<TemplateRenderResponse>(failed.Text, ProtocolJson.Options)!.Problem!.Code);
        Assert.Equal(count, service.Session.Load().Records.Count);
    }
    [Fact]
    public void EveryAddedInterfaceMemberHasADefaultAndOpenApiDescribesEveryContractProperty()
    {
        foreach (string name in new[] { "Templates", "DocumentSections", "SectionBrief", "DocumentNew", "TemplateRender", "DocumentAdopt" })
        {
            Assert.False(typeof(IEstateApi).GetMethod(name)!.IsAbstract);
            Assert.False(typeof(IEstateApi).GetMethod(name + "Async")!.IsAbstract);
        }
        Assert.Single(typeof(DocumentReviewRequest).GetConstructors(), c => c.GetParameters().Length == 6);
        Assert.Single(typeof(DocumentPinRequest).GetConstructors(), c => c.GetParameters().Length == 5);
        Assert.False(typeof(IDocumentGenerator).GetMethod("GenerateRegion")!.IsAbstract);
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        foreach ((string method, string path, string response) in new[]
        {
            ("GET", "/v1/templates", "TemplatesResponse"), ("GET", "/v1/documents/{id}/sections", "DocumentSectionsResponse"),
            ("GET", "/v1/documents/{id}/sections/{section}/brief", "SectionBriefResponse"), ("POST", "/v1/documents/new", "DocumentNewResponse"),
            ("POST", "/v1/templates/render", "TemplateRenderResponse"),
            ("POST", "/v1/documents/adopt", "DocumentAdoptResponse"),
        })
        {
            Assert.Contains((method, path), OpenApiDocument.Routes);
            JsonElement operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method.ToLowerInvariant());
            Assert.Equal("#/components/schemas/" + response, operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        }
        foreach (Type contract in new[] { typeof(DocumentNewRequest), typeof(DocumentNewResponse), typeof(DocumentAdoptRequest), typeof(DocumentAdoptResponse), typeof(TemplatesResponse), typeof(TemplateDto), typeof(TemplatePinDto),
            typeof(TemplateRecordReference), typeof(TemplateSourceDto), typeof(TemplateCheckDto), typeof(TemplateSectionDto), typeof(TemplateBriefDto), typeof(DocumentSectionsResponse),
            typeof(SectionBriefResponse), typeof(DocumentSectionReview), typeof(DocumentSectionJudgment), typeof(DocumentReviewRequest), typeof(DocumentPinRequest),
            typeof(TemplateRenderRequest), typeof(TemplateRenderResponse), typeof(PresentationTemplateUse) })
            Assert.Equal(contract.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
                document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(contract.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(BriefParameters, document.RootElement.GetProperty("paths").GetProperty("/v1/documents/{id}/sections/{section}/brief")
            .GetProperty("get").GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).Order());
    }

    [Fact]
    public async Task SdkAndBoundClientsPreserveExactPinsSectionJudgmentsReceiptsAndCancellation()
    {
        using var handler = new Handler();
        using var client = new VericairnClient(new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") });
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);
        var request = new DocumentNewRequest("author", new("example/design", "1.0.0", Handler.Hash), "docs/example.md", "Example");
        var review = new DocumentReviewRequest("reviewer", "current", "Read exact evidence.")
        { Sections = [new("answer", "current", "final", [new("meaning", "passed", "Coherent with the sources.", [Handler.Hash])])] };
        foreach (IEstateApi api in new IEstateApi[] { client, bound })
        {
            Assert.Empty(api.Templates().Templates);
            Assert.Empty((await api.TemplatesAsync(TestContext.Current.CancellationToken)).Templates);
            Assert.Empty(api.DocumentSections("docs/a b.md", "commit/one").Sections);
            Assert.Empty((await api.DocumentSectionsAsync("docs/a b.md", "commit/one", TestContext.Current.CancellationToken)).Sections);
            Assert.Null(api.SectionBrief("docs/a b.md", "answer one", 12000, "commit/one").Brief);
            Assert.Null((await api.SectionBriefAsync("docs/a b.md", "answer one", 12000, "commit/one", TestContext.Current.CancellationToken)).Brief);
            Assert.Equal(Handler.Created, api.DocumentNew(request));
            Assert.Equal(Handler.Created, await api.DocumentNewAsync(request, TestContext.Current.CancellationToken));
            Assert.True(api.DocumentReview("instance", review).Ok);
            Assert.True((await api.DocumentReviewAsync("instance", review, TestContext.Current.CancellationToken)).Ok);
        }
        Assert.Equal(20, handler.Paths.Count);
        for (int i = 0; i < handler.Paths.Count; i += 2) Assert.Equal(handler.Paths[i], handler.Paths[i + 1]);
        Assert.Equal("/v1/documents/docs%2Fa%20b.md/sections?commit=commit%2Fone", handler.Paths[2]);
        Assert.Equal("/v1/documents/docs%2Fa%20b.md/sections/answer%20one/brief?maxBytes=12000&commit=commit%2Fone", handler.Paths[4]);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        ClientCancelledException read = await Assert.ThrowsAsync<ClientCancelledException>(() => client.SectionBriefAsync("instance", "answer", cancellationToken: canceled.Token));
        ClientCancelledException write = await Assert.ThrowsAsync<ClientCancelledException>(() => client.DocumentNewAsync(request, canceled.Token));
        Assert.False(read.OutcomeUnknown); Assert.False(write.OutcomeUnknown);
        Assert.Equal(20, handler.Paths.Count);
    }

    [Fact]
    public async Task McpReadsCarryCompleteEvidenceAndReviewKeepsItsSectionSelection()
    {
        using TestEstate estate = TemplateFixture.RecordedFixture();
        using EstateService service = EstateService.Open(estate.Root);
        var declaration = service.Session.Load().Current("living-document").Single();
        var registry = new ToolRegistry(service, null);
        foreach (string name in new[] { "document_sections", "section_brief" }) Assert.False(registry.All.Single(t => t.Name == name).Writes);
        Assert.Equal("array", registry.All.Single(t => t.Name == "document_review").InputSchema["properties"]!["sections"]!["type"]!.GetValue<string>());
        int count = service.Session.Load().Records.Count;
        ToolCallResult sections = await registry.CallAsync("document_sections", new() { ["document"] = declaration.Id }, TestContext.Current.CancellationToken);
        Assert.False(sections.IsError, sections.Text);
        Assert.Equal(CanonicalJson.Serialize(JsonSerializer.SerializeToElement(service.DocumentSections(declaration.Id), ProtocolJson.Options)), CanonicalJson.Serialize(JsonDocument.Parse(sections.Text).RootElement));
        ToolCallResult brief = await registry.CallAsync("section_brief", new() { ["document"] = declaration.Id, ["section"] = "answer" }, TestContext.Current.CancellationToken);
        Assert.False(brief.IsError, brief.Text);
        TemplateBriefDto package = JsonSerializer.Deserialize<SectionBriefResponse>(brief.Text, ProtocolJson.Options)!.Brief!;
        Assert.Equal(package.Bytes, CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(package, ProtocolJson.Options)).LongLength);
        Assert.Contains(package.Sources, s => s.Kind == "original-record" && s.Content is not null);
        Assert.True((await registry.CallAsync("section_brief", new() { ["document"] = declaration.Id, ["section"] = "answer", ["maxBytes"] = "1" }, TestContext.Current.CancellationToken)).IsError);
        Assert.True((await registry.CallAsync("section_brief", new() { ["document"] = declaration.Id, ["section"] = "answer", ["maxBytes"] = "0" }, TestContext.Current.CancellationToken)).IsError);
        Assert.Equal(count, service.Session.Load().Records.Count);

        // Invoke the registered write adapter against the service; admission visibility has separate coverage.
        ToolDefinition reviewTool = new ToolRegistry(service, estate.OperatorId).All.Single(t => t.Name == "document_review");
        ToolCallResult reviewed = await reviewTool.InvokeAsync(service, new JsonObject
        {
            ["path"] = "docs/example.md", ["outcome"] = "current", ["summary"] = "Read one exact section.",
            ["sections"] = new JsonArray(new JsonObject { ["id"] = "answer", ["outcome"] = "current", ["fidelity"] = "draft" }),
        }, TestContext.Current.CancellationToken);
        Assert.False(reviewed.IsError, reviewed.Text);
        Assert.Equal("reviewed", Assert.Single(service.DocumentSections(declaration.Id).Sections).State);
        Assert.Empty(service.Session.Load().OfType("acceptance"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal const string Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
        internal static readonly DocumentNewResponse Created = new(new("records/living-document/instance.json", "instance", Hash), "docs/example.md", Hash);
        internal List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            object response;
            if (request.Method == HttpMethod.Post)
            {
                if (request.RequestUri.AbsolutePath == "/v1/documents/new")
                {
                    DocumentNewRequest body = (await request.Content!.ReadFromJsonAsync<DocumentNewRequest>(ProtocolJson.Options, cancellationToken))!;
                    Assert.Equal(Hash, body.Template.Hash); Assert.Equal("docs/example.md", body.Path); response = Created;
                }
                else
                {
                    Assert.Equal("/v1/documents/instance/reviews", request.RequestUri.AbsolutePath);
                    DocumentReviewRequest body = (await request.Content!.ReadFromJsonAsync<DocumentReviewRequest>(ProtocolJson.Options, cancellationToken))!;
                    DocumentSectionReview section = Assert.Single(body.Sections!); Assert.Equal("answer", section.Id); Assert.Equal(Hash, Assert.Single(Assert.Single(section.Judgments!).Evidence));
                    response = WriteOutcome.Success(new("records/document-review/review.json", "review", Hash));
                }
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Content);
                response = request.RequestUri.AbsolutePath switch
                {
                    "/v1/templates" => new TemplatesResponse([]),
                    var path when path.EndsWith("/brief", StringComparison.Ordinal) => new SectionBriefResponse(null, new("unavailable", "No fixture section.", [])),
                    _ => new DocumentSectionsResponse("instance", "docs/example.md", []),
                };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(response, response.GetType(), options: ProtocolJson.Options) };
        }
    }
}
