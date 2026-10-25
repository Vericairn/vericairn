// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

[Collection("daemon")]
public sealed class PromptApiTests
{
    public PromptApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void ContractCoversEveryRequestFieldRouteAndDefaultInterfaceMember()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (Type type in new[] { typeof(PromptSaveRequest), typeof(PromptPreferenceRequest), typeof(PromptCopyRequest), typeof(PromptUseRequest), typeof(PromptSelection), typeof(AssignRequest), typeof(RunStartRequest) })
            Assert.Equal(type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(),
                schemas.GetProperty(type.Name).GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        foreach ((string method, Type argument) in new[] { ("Prompts", typeof(PromptQuery)), ("PromptSave", typeof(PromptSaveRequest)), ("PromptPreference", typeof(PromptPreferenceRequest)), ("PromptCopy", typeof(PromptCopyRequest)), ("PromptUse", typeof(PromptUseRequest)) })
        {
            Assert.False(typeof(IEstateApi).GetMethod(method, [argument])!.IsAbstract);
            Assert.False(typeof(IEstateApi).GetMethod(method + "Async", [argument, typeof(CancellationToken)])!.IsAbstract);
            Assert.NotNull(typeof(VericairnClient).GetMethod(method, [argument]));
            Assert.NotNull(typeof(CancellationBoundApi).GetMethod(method + "Async", [argument, typeof(CancellationToken)]));
        }
        foreach ((string verb, string path) in OpenApiDocument.Routes.Where(r => r.Path.StartsWith("/v1/prompts", StringComparison.Ordinal)))
            Assert.True(RequestScope.Covers(verb + " " + path));
        foreach (Type type in new[] { typeof(PromptQuery), typeof(PromptSaveRequest), typeof(PromptPreferenceRequest), typeof(PromptCopyRequest), typeof(PromptUseRequest) }) Assert.True(RequestScope.Covers(type));
    }

    [Fact]
    public async Task RealRoutesAndSdkRetainSourcePreferencesAndRecordedUses()
    {
        using var writer = new PromptWriter();
        using var http = new HttpClient(new WorkNewApiTests.WorkRouteHandler(writer.Api)) { BaseAddress = new Uri("http://fixture.invalid") };
        using var sdk = new VericairnClient(http);
        var bound = new CancellationBoundApi(sdk, TestContext.Current.CancellationToken);
        PromptLibraryTests.Ok(await sdk.PromptSaveAsync(new(writer.Operator, PolicyFixture.Element(PromptRecordsTests.Definition())), TestContext.Current.CancellationToken));
        PromptRow row = Assert.Single(sdk.Prompts(new(Category: "Work/Review")).Prompts, p => p.Source == "estate");
        PromptLibraryTests.Ok(bound.PromptPreference(new(writer.Operator, row.Identity, Favorite: true)));
        Assert.Equal(row.Identity, Assert.Single((await sdk.PromptsAsync(new(writer.Operator, Favorites: true), TestContext.Current.CancellationToken)).Prompts).Identity);
        PromptLibraryTests.Ok(await bound.PromptPreferenceAsync(new(writer.Operator, row.Identity, Hidden: true), TestContext.Current.CancellationToken));
        Assert.Empty(bound.Prompts(new(writer.Operator, Favorites: true)).Prompts);
        PromptLibraryTests.Ok(sdk.PromptPreference(new(writer.Operator, row.Identity, Hidden: false)));
        JsonObject definition = PromptRecordsTests.Definition("installed"); definition.Remove("state");
        PromptRow installed = PromptCatalog.BuiltIn("example", "1.2.3", PolicyFixture.Element(definition), draft: false);
        writer.Api.Providers.Prompts.Register([installed]);
        PromptLibraryTests.Ok(await bound.PromptCopyAsync(new(writer.Operator, installed.Identity, installed.Hash, "copied"), TestContext.Current.CancellationToken));
        var selection = new PromptSelection(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = "the assigned evidence" });
        PromptUseResponse used = await sdk.PromptUseAsync(new(writer.Operator, selection, Assignment: writer.Assignment()) { Favor = true }, TestContext.Current.CancellationToken);
        Assert.True(used.Ok, used.Problem?.Message);
        Assert.Equal(row.Hash, writer.Model.ById[used.Written!.Id].Body.GetProperty("promptUse").GetProperty("hash").GetString());
        PromptLibraryTests.Ok(sdk.PromptPreference(new(writer.Operator, row.Identity, Disabled: true)));
        Assert.False(bound.PromptUse(new(writer.Operator, selection, Assignment: writer.Assignment())).Ok);
        Assert.Equal(0, writer.Api.Validate().Errors);
    }

    [Fact]
    public async Task UnknownAuthorityFieldsAndForeignAuthorsAreRefusedWithoutAUse()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        using var http = new HttpClient(new WorkNewApiTests.WorkRouteHandler(writer.Api)) { BaseAddress = new Uri("http://fixture.invalid") };
        JsonObject body = JsonSerializer.SerializeToNode(new PromptUseRequest(writer.Operator,
            new(row.Identity, row.Hash, new Dictionary<string, string> { ["topic"] = "evidence" }), Assignment: writer.Assignment()), ProtocolJson.Options)!.AsObject();
        body["selection"]!["budget"] = 100;
        int before = writer.Model.Records.Count;
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri("/v1/prompts/use", UriKind.Relative), body, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, writer.Model.Records.Count);
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-prompt-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var scope = new RequestScope(writer.Api, new LocalNetworkLedger(scratch));
            var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = "/v1/prompts";
            context.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice("device", writer.Builder, "public-key", false);
            bool reached = false;
            object? result = await scope.InvokeAsync(new DefaultEndpointFilterInvocationContext(context, new PromptSaveRequest(writer.Operator, row.Definition)), _ =>
            { reached = true; return ValueTask.FromResult<object?>(null); });
            Assert.False(reached);
            Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }
}
