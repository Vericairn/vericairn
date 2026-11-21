// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (CODE-73): work assign names a brief's repository and reports its overlaps on standard error, work overlap lists the active
/// assignments and their overlaps and previews a draft brief, status shows the open overlaps, and the MCP read tool reads the same view.
/// </summary>
[Collection("cli")]
public class WorkOverlapJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void AssignmentsOverlapOnTheCommandLineAndTheMcpReadToolReadsTheSameView()
    {
        string root = CliHarness.TempDirectory("work-overlap");
        string bodies = CliHarness.TempDirectory("work-overlap-bodies");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Overlaps.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            foreach (string key in new[] { "W-1", "W-2" })
            {
                CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(bodies, key + ".json", $$"""{"key":"{{key}}","kind":"ordinary","title":"Guide {{key}}","intent":"Write the guide.","scope":["the guide"],"exitQuestion":"Written?","inputs":[],"deliverables":["guide"]}"""));
            }

            string guides = CliHarness.WriteJson(bodies, "guides.json", Brief("path:docs/**"));
            CliHarness.Outcome first = CliHarness.Ok("--estate", root, "work", "assign", "W-1", "--contributor", op, "--by", op, "--brief", guides);
            Assert.DoesNotContain("Overlap:", first.Err, StringComparison.Ordinal);
            string firstId = first.Out.Split("  ")[1];
            CliHarness.Outcome second = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-2", "--contributor", op, "--by", op, "--brief", CliHarness.WriteJson(bodies, "guide.json", Brief("path:docs/guide.md")));
            Assert.Equal(["hash", "id", "path"], second.Json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Contains($"Overlap: W-1's assignment {firstId} and W-2's assignment {second.Id} in the estate's own repository share paths either could create, such as docs/guide.md.", second.Err, StringComparison.Ordinal);

            CliHarness.Outcome view = CliHarness.Ok("--estate", root, "work", "overlap");
            Assert.Contains("Active assignments with permitted paths: 2", view.Out, StringComparison.Ordinal);
            Assert.Contains("Overlaps: 1 (1 open, 0 held at refuse, 0 sequenced)", view.Out, StringComparison.Ordinal);
            JsonElement json = CliHarness.Ok("--estate", root, "--json", "work", "overlap").Json;
            Assert.Equal("vericairn.overlaps/1", json.GetProperty("format").GetString());
            Assert.Equal(2, json.GetProperty("assignments").GetArrayLength());
            Assert.Equal("docs/guide.md", Assert.Single(json.GetProperty("overlaps").EnumerateArray()).GetProperty("witnesses")[0].GetString());

            CliHarness.Outcome preview = CliHarness.Ok("--estate", root, "work", "overlap", "--brief", CliHarness.WriteJson(bodies, "draft.json", Brief("path:docs/cli.md")), "--repository", "estate");
            Assert.Contains("Draft brief in the estate's own repository: docs/cli.md", preview.Out, StringComparison.Ordinal);
            Assert.Contains("and the draft brief in the estate's own repository share paths either could create, such as docs/cli.md.", preview.Out, StringComparison.Ordinal);
            CliHarness.Outcome unknown = CliHarness.Run("--estate", root, "work", "overlap", "--repository", "01M4TRNE1GYEK4P8MXQAB3C8Q4");
            Assert.NotEqual(0, unknown.Code);
            Assert.Contains("No repository has id 01M4TRNE1GYEK4P8MXQAB3C8Q4", unknown.Err, StringComparison.Ordinal);

            Assert.Contains($"Overlapping assignments: 1 open (vericairn work overlap)\n  W-1 {firstId} and W-2 {second.Id}", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);

            using EstateService api = EstateService.Open(root);
            var registry = new ToolRegistry(api, null);
            ToolCallResult text = registry.Call("work_overlap", new JsonObject());
            Assert.False(text.IsError);
            Assert.Contains("Overlaps: 1 (1 open", text.Text, StringComparison.Ordinal);
            ToolCallResult drafted = registry.Call("work_overlap", new JsonObject { ["brief"] = JsonNode.Parse(Brief("path:docs/guide.md")), ["repository"] = "estate", ["form"] = "json" });
            Assert.False(drafted.IsError);
            using JsonDocument draftJson = JsonDocument.Parse(drafted.Text);
            Assert.Equal(2, draftJson.RootElement.GetProperty("overlaps").GetArrayLength());
            Assert.True(registry.Call("work_overlap", new JsonObject { ["repository"] = "01M4TRNE1GYEK4P8MXQAB3C8Q4" }).IsError);
        }
        finally
        {
            CliHarness.Delete(root, bodies);
        }
    }

    private static string Brief(string pattern) =>
        $$"""{"outcome":"The guide.","sources":[],"permittedChanges":["{{pattern}}"],"prohibitedEffects":["no publication"],"workspace":["the estate, a work branch"],"limits":[],"expectedOutputs":["guide"],"reviewCriteria":["it reads"],"recovery":"Record a result."}""";
}
