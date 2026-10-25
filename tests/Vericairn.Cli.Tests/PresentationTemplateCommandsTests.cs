// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Templates;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PresentationTemplateCommandsTests
{
    [Fact]
    public void CommandsRequireRecordedAdoptionAndCheckCompleteCandidateBytesWithoutWritingRecords()
    {
        using var estate = new TestEstate(3);
        var (result, _) = PresentationTemplateServiceTests.Source(estate);
        TemplateDefinition definition = PresentationTemplates.Definitions.Single(d => d.Id == PresentationTemplates.Commit);
        TemplateRenderRequest request = PresentationTemplateServiceTests.Request(estate, definition, result, "short");
        estate.WriteFile("scratch/request.json", JsonSerializer.Serialize(request, ProtocolJson.Options));
        string input = Path.Combine(estate.Root, "scratch", "request.json");
        using EstateService api = EstateService.Open(estate.Root);
        CliHarness.Outcome Run(params string[] args) => CliHarness.RunWithApi(api, null, ["--estate", estate.Root, "--json", .. args]);
        Assert.Equal(2, Run("templates", "render", input).Code);
        var adoption = new JsonObject
        {
            ["template"] = TemplateFixture.Pin(definition), ["scope"] = new JsonObject { ["kind"] = "estate" },
            ["options"] = new JsonObject { ["adoption"] = "enabled" }, ["reason"] = "Use the complete commit contract.",
        };
        estate.WriteFile("scratch/adoption.json", adoption.ToJsonString());
        string adoptionFile = Path.Combine(estate.Root, "scratch", "adoption.json");
        Assert.Equal(2, Run("records", "new", "template-defaults", "--by", estate.BuilderId, "--body", adoptionFile).Code);
        Assert.Empty(api.Session.Load().OfType("template-defaults"));
        Assert.Equal(0, Run("records", "new", "template-defaults", "--by", estate.OperatorId, "--body", adoptionFile).Code);
        string adoptionId = Assert.Single(api.Session.Load().OfType("template-defaults")).Id;
        int count = api.Session.Load().Records.Count;
        CliHarness.Outcome rendered = Run("templates", "render", input);
        Assert.Equal(0, rendered.Code);
        string text = rendered.Json.GetProperty("text").GetString()!;
        estate.WriteFile("scratch/candidate.md", text);
        string candidate = Path.Combine(estate.Root, "scratch", "candidate.md");
        Assert.Equal(0, Run("templates", "check", input, candidate).Code);
        estate.WriteFile("scratch/candidate.md", text.Replace("not-performed", "passed", StringComparison.Ordinal));
        CliHarness.Outcome failed = Run("templates", "check", input, candidate);
        Assert.Equal(1, failed.Code);
        Assert.Equal("failed-check", failed.Json.GetProperty("problem").GetProperty("code").GetString());
        Assert.Equal(count, api.Session.Load().Records.Count);
        adoption["options"]!["adoption"] = "disabled";
        estate.WriteFile("scratch/adoption.json", adoption.ToJsonString());
        Assert.Equal(0, Run("records", "new", "template-defaults", "--by", estate.OperatorId, "--body", adoptionFile, "--supersedes", adoptionId).Code);
        Assert.Equal(2, Run("templates", "render", input).Code);
        Assert.Empty(api.Session.Load().OfType("review"));
    }
}
