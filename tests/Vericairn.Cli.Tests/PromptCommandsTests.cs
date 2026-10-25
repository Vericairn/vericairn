// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PromptCommandsTests
{
    // The harness moves per-user state to its own home when first used. The prompt writer's devices must be created there, or the
    // command line holds none of their keys and cannot sign; without this the outcome depended on which test ran first.
    static PromptCommandsTests() => RuntimeHelpers.RunClassConstructor(typeof(CliHarness).TypeHandle);

    [Fact]
    public void CategoriesSourcesAndReversibleChoicesRemainVisibleThroughTheCommandLine()
    {
        using var writer = new PromptWriter();
        string root = writer.Fixture.Estate.Root;
        string scratch = CliHarness.TempDirectory("prompt-command");
        try
        {
            string definition = CliHarness.WriteJson(scratch, "prompt.json", PromptRecordsTests.Definition().ToJsonString());
            string first = CliHarness.Ok("--estate", root, "--json", "prompt", "add", "--by", writer.Operator, "--body", definition).Id;
            CliHarness.Outcome listed = CliHarness.Ok("--estate", root, "--json", "prompt", "list", "--category", "Work", "--person", writer.Operator);
            Assert.Contains(listed.Json.GetProperty("prompts").EnumerateArray(), p => p.GetProperty("source").GetString() == "foundation");
            Assert.Contains(listed.Json.GetProperty("prompts").EnumerateArray(), p => p.GetProperty("identity").GetString() == "estate:review");
            Assert.Equal(4, CliHarness.Ok("--estate", root, "--json", "prompt", "list", "--category", "Software").Json.GetProperty("prompts").GetArrayLength());
            Assert.Equal(first, CliHarness.Ok("--estate", root, "--json", "prompt", "show", "estate:review").Json.GetProperty("version").GetString());
            CliHarness.Ok("--estate", root, "prompt", "favor", "estate:review", "--by", writer.Operator);
            Assert.Single(CliHarness.Ok("--estate", root, "--json", "prompt", "list", "--person", writer.Operator, "--favorites").Json.GetProperty("prompts").EnumerateArray());
            CliHarness.Ok("--estate", root, "prompt", "hide", "estate:review", "--by", writer.Operator);
            Assert.Empty(CliHarness.Ok("--estate", root, "--json", "prompt", "list", "--person", writer.Operator, "--favorites").Json.GetProperty("prompts").EnumerateArray());
            CliHarness.Ok("--estate", root, "prompt", "hide", "estate:review", "--by", writer.Operator, "--undo");
            CliHarness.Ok("--estate", root, "prompt", "disable", "estate:review", "--by", writer.Operator);
            Assert.True(CliHarness.Ok("--estate", root, "--json", "prompt", "show", "estate:review").Json.GetProperty("disabled").GetBoolean());
            CliHarness.Ok("--estate", root, "prompt", "disable", "estate:review", "--by", writer.Operator, "--undo");
            CliHarness.Ok("--estate", root, "prompt", "retire", "estate:review", "--by", writer.Operator);
            Assert.True(CliHarness.Ok("--estate", root, "--json", "prompt", "show", "estate:review").Json.GetProperty("retired").GetBoolean());
            Assert.Equal(2, writer.Model.OfType("saved-prompt").Count());
            CliHarness.Ok("--estate", root, "estate", "check");
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void AssignmentAndAskUsesReturnSignedReceiptsAndFavorInOneCommand()
    {
        using var writer = new PromptWriter();
        PromptRow row = writer.Add();
        string root = writer.Fixture.Estate.Root;
        string scratch = CliHarness.TempDirectory("prompt-use");
        try
        {
            AssignRequest assignment = writer.Assignment();
            string target = CliHarness.WriteJson(scratch, "assignment.json", JsonSerializer.Serialize(assignment, ProtocolJson.Options));
            CliHarness.Outcome used = CliHarness.Ok("--estate", root, "--json", "prompt", "use", row.Identity, "--by", writer.Operator,
                "--assignment", target, "--value", "topic=the assigned evidence", "--favor");
            string id = used.Json.GetProperty("written").GetProperty("id").GetString()!;
            RecordEnvelope recorded = writer.Model.ById[id];
            Assert.True(SignatureChecks.IsAuthentic(writer.Model, recorded));
            Assert.Equal(row.Hash, recorded.Body.GetProperty("promptUse").GetProperty("hash").GetString());
            Assert.Single(writer.Api.Prompts(new(writer.Operator, Favorites: true)).Prompts);
            RecordEnvelope ask = writer.Fixture.Add("ask", new JsonObject { ["to"] = writer.Operator, ["question"] = "Which evidence?", ["recommendation"] = "Review the evidence.", ["consequence"] = "Await an answer.", ["state"] = "open" }, builder: true);
            CliHarness.Outcome answer = CliHarness.Ok("--estate", root, "--json", "prompt", "use", row.Identity, "--by", writer.Operator,
                "--ask", ask.Id, "--value", "topic=the original evidence");
            Assert.Equal("decision", writer.Model.ById[answer.Json.GetProperty("written").GetProperty("id").GetString()!].Type);
            int before = writer.Model.Records.Count;
            Assert.NotEqual(0, CliHarness.Run("--estate", root, "prompt", "use", row.Identity, "--by", writer.Operator, "--assignment", target,
                "--value", "topic=evidence", "--value", "topic=duplicate").Code);
            Assert.Equal(before, writer.Model.Records.Count);
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void DescriptionsAndDraftRefusalsExposeTheAdditiveContract()
    {
        using var writer = new PromptWriter();
        string root = writer.Fixture.Estate.Root;
        Assert.Contains("placeholders", CliHarness.Ok("records", "describe", "saved-prompt").Out, StringComparison.Ordinal);
        Assert.Contains("disabled", CliHarness.Ok("records", "describe", "prompt-preference").Out, StringComparison.Ordinal);
        Assert.Contains("promptUse", CliHarness.Ok("records", "describe", "operation").Out, StringComparison.Ordinal);
        PromptRow draft = writer.Api.Prompts(new()).Prompts.First(p => p.Source == "foundation");
        CliHarness.Outcome copy = CliHarness.Run("--estate", root, "prompt", "copy", draft.Identity, "--by", writer.Operator, "--key", "unreviewed");
        Assert.NotEqual(0, copy.Code);
        Assert.Contains("draft", copy.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(writer.Model.OfType("saved-prompt"));
        Assert.NotEqual(0, CliHarness.Run("--estate", root, "prompt", "retire", draft.Identity, "--by", writer.Operator).Code);
    }
}
