// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// DOCS-46: the assignment validator reads each brief member by the rule records describe and the record reference show for it, so a
/// rule the reference states is the rule a proposed brief meets: requiredness, an array's empty rule, and each length limit.
/// </summary>
public sealed class AssignmentBriefRulesTests
{
    private const string OrderBody = """{"key":"W-1","kind":"ordinary","title":"Small task","intent":"Exercise the brief.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";

    // The members whose shape alone the rule states: strings and arrays of strings.
    public static TheoryData<string> Members() => [.. AssignmentBrief.Members.Where(m => m.Kind is "string" or "string[]").Select(m => m.Name)];

    [Fact]
    public void TheBriefsDescriptionIsItsRulesInTheValidatorsOrder()
    {
        FieldDescription brief = RecordTypeRegistry.Find("assignment")!.Fields.Single(f => f.Name == "brief");
        Assert.Equal(AssignmentBrief.Members.Select(m => m.Name), brief.Members.Select(m => m.Name));
        Assert.Equal(["outcome", "sources", "permittedChanges", "prohibitedEffects", "workspace", "limits", "expectedOutputs", "reviewCriteria", "recovery"],
            brief.Members.Where(m => m.Required).Select(m => m.Name));
        Assert.All(brief.Members.Where(m => m.Kind.EndsWith("[]", StringComparison.Ordinal)), m => Assert.NotNull(m.EmptyAllowed));
        Assert.All(brief.Members.Where(m => !m.Kind.EndsWith("[]", StringComparison.Ordinal)), m => Assert.Null(m.EmptyAllowed));
        FieldDescription reviewCriteria = brief.Members.Single(m => m.Name == "reviewCriteria");
        Assert.Equal(("string[]", true, false, "each item 1 to 2,000 characters", 1L),
            (reviewCriteria.Kind, reviewCriteria.Required, reviewCriteria.EmptyAllowed!.Value, reviewCriteria.Limits, reviewCriteria.SinceFormat));
        FieldDescription recovery = brief.Members.Single(m => m.Name == "recovery");
        Assert.Equal(("string", true, "1 to 4,000 characters"), (recovery.Kind, recovery.Required, recovery.Limits));
        Assert.Equal(["type", "id", "hash"], brief.Members.Single(m => m.Name == "sources").Members.Select(m => m.Name));
        FieldDescription progress = brief.Members.Single(m => m.Name == "progressRequest");
        Assert.Equal(3, progress.SinceFormat);
        Assert.Equal(["perStep", "minutes"], progress.Members.Single(m => m.Name == "cadence").Members.Select(m => m.Name));
    }

    [Fact]
    public void TheMinimalBriefIsValid()
    {
        using var estate = new TestEstate(3);
        Assert.Empty(Errors(estate, Minimal()));
    }

    [Theory]
    [MemberData(nameof(Members))]
    public void ARequiredMemberLeftOutIsRefusedAndAnOptionalOneIsNot(string name)
    {
        MemberRule rule = AssignmentBrief.Members.Single(m => m.Name == name);
        using var estate = new TestEstate(3);
        JsonObject brief = Minimal();
        brief.Remove(name);
        IReadOnlyList<Finding> errors = Errors(estate, brief);
        if (rule.Required) Assert.Contains(errors, f => f.Code == "body.missing" && f.Location.EndsWith("#body.brief." + name, StringComparison.Ordinal));
        else Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(Members))]
    public void AnEmptyArrayIsRefusedExactlyWhereTheRuleSaysAndEachLengthLimitHolds(string name)
    {
        MemberRule rule = AssignmentBrief.Members.Single(m => m.Name == name);
        using var estate = new TestEstate(3);
        if (rule.Kind == "string[]")
        {
            IReadOnlyList<Finding> empty = Errors(estate, With(name, new JsonArray()));
            if (rule.AllowEmpty) Assert.Empty(empty);
            else Assert.Contains(empty, f => f.Code == "body.empty" && f.Location.EndsWith("#body.brief." + name, StringComparison.Ordinal));
            // An item is never an empty string, whatever the array's empty rule.
            Assert.Contains(Errors(estate, With(name, new JsonArray(""))), f => f.Code == "body.length");
        }

        // A pattern member's longest item still has to be a pattern, so it is read as one.
        string prefix = name == "excludedChanges" ? "path:" : string.Empty;
        string longest = prefix + new string('a', rule.MaxLength - prefix.Length);
        JsonNode Value(string text) => rule.Kind == "string[]" ? new JsonArray(text) : JsonValue.Create(text);
        Assert.Empty(Errors(estate, With(name, Value(longest))));
        Assert.Contains(Errors(estate, With(name, Value(longest + "a"))), f => f.Code == "body.length");
        if (rule.Kind == "string") Assert.Contains(Errors(estate, With(name, Value(""))), f => f.Code == "body.length");
    }

    private static JsonObject Minimal() => (JsonObject)JsonNode.Parse(
        """{"outcome":"NOTES.md holds a greeting.","sources":[],"permittedChanges":["NOTES.md, one line"],"prohibitedEffects":[],"workspace":["the estate"],"limits":[],"expectedOutputs":["NOTES.md"],"reviewCriteria":["Does NOTES.md hold a greeting?"],"recovery":"Read this brief."}""")!;

    private static JsonObject With(string name, JsonNode value)
    {
        JsonObject brief = Minimal();
        brief[name] = value;
        return brief;
    }

    private static List<Finding> Errors(TestEstate estate, JsonObject brief)
    {
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, OrderBody);
        var body = new JsonObject { ["contributor"] = estate.BuilderId, ["brief"] = brief };
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, body.ToJsonString(), order.Reference);
        return [.. estate.Load().Findings.Where(f => f.Severity == Severity.Error && f.Location.Contains(assignment.Id, StringComparison.Ordinal))];
    }
}
