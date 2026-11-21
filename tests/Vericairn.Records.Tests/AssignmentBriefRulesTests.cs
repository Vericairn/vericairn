// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// DOCS-46: the assignment validator reads each brief member by the rule records describe and the record reference show for it, so a
/// rule the reference states is the rule a proposed brief meets. Every limit and rule is written out here as a literal, never taken
/// from the rules it checks, so a changed limit, requiredness, empty rule or format fails a test: the reference's rows, and the
/// validator's verdict on each member at and beyond each limit.
/// </summary>
public sealed class AssignmentBriefRulesTests
{
    private const string OrderBody = """{"key":"W-1","kind":"ordinary","title":"Small task","intent":"Exercise the brief.","scope":["x"],"exitQuestion":"Done?","inputs":[],"deliverables":["y"]}""";

    // The reference's rows for the brief, member by member in the validator's order, each nested member after its parent: the member's
    // path, kind, requiredness, whether an array may be empty, its limits and the record format that introduced it.
    private static readonly (string Path, string Kind, bool Required, bool? EmptyAllowed, string? Limits, long Format)[] Reference =
    [
        ("outcome", "string", true, null, "1 to 4,000 characters", 1),
        ("sources", "object[]", true, true, null, 1),
        ("sources.type", "string", true, null, null, 1),
        ("sources.id", "ulid", true, null, null, 1),
        ("sources.hash", "hash", false, null, null, 1),
        ("permittedChanges", "string[]", true, false, "each item 1 to 2,000 characters", 1),
        ("prohibitedEffects", "string[]", true, true, "each item 1 to 2,000 characters", 1),
        ("workspace", "string[]", true, false, "each item 1 to 500 characters", 1),
        ("limits", "string[]", true, true, "each item 1 to 2,000 characters", 1),
        ("expectedOutputs", "string[]", true, false, "each item 1 to 2,000 characters", 1),
        ("reviewCriteria", "string[]", true, false, "each item 1 to 2,000 characters", 1),
        ("recovery", "string", true, null, "1 to 4,000 characters", 1),
        ("reviewBrief", "string", false, null, "1 to 1,000,000 characters", 3),
        ("repository", "ulid", false, null, null, 3),
        ("excludedChanges", "string[]", false, false, "each item 1 to 2,000 characters", 3),
        ("after", "ulid[]", false, false, "at most 20 items", 3),
        ("retrieval", "public", false, null, null, 3),
        ("progressRequest", "object", false, null, null, 3),
        ("progressRequest.types", "string[]", false, false, "each item 1 to 30 characters; distinct values of accurate, best-effort and not-measurable", 1),
        ("progressRequest.statusText", "boolean", false, null, null, 1),
        ("progressRequest.cadence", "object", true, null, "perStep true, minutes, or both", 1),
        ("progressRequest.cadence.perStep", "boolean", false, null, null, 1),
        ("progressRequest.cadence.minutes", "integer", false, null, "at least 1", 1),
    ];

    // Each string and string-array member's rule, written out: kind, requiredness, whether an array may be empty, the longest value or
    // item, and the record format that introduced it.
    public static TheoryData<string, string, bool, bool, int, long> Strings() => new()
    {
        { "outcome", "string", true, false, 4000, 1 },
        { "permittedChanges", "string[]", true, false, 2000, 1 },
        { "prohibitedEffects", "string[]", true, true, 2000, 1 },
        { "workspace", "string[]", true, false, 500, 1 },
        { "limits", "string[]", true, true, 2000, 1 },
        { "expectedOutputs", "string[]", true, false, 2000, 1 },
        { "reviewCriteria", "string[]", true, false, 2000, 1 },
        { "recovery", "string", true, false, 4000, 1 },
        { "reviewBrief", "string", false, false, 1_000_000, 3 },
        { "excludedChanges", "string[]", false, false, 2000, 3 },
    };

    [Fact]
    public void TheReferenceStatesEachMembersRuleAsWrittenHere()
    {
        FieldDescription brief = RecordTypeRegistry.Find("assignment")!.Fields.Single(f => f.Name == "brief");
        Assert.Equal(Reference, Rows(brief.Members, string.Empty));
        // The description is the rules', in the validator's order; the reference renders these rows (AssignmentBriefGuideTests).
        Assert.Equal(AssignmentBrief.Members.Select(m => m.Name), brief.Members.Select(m => m.Name));
    }

    [Fact]
    public void TheMinimalBriefIsValidInFormatsTwoAndThree()
    {
        using var estate = new TestEstate(3);
        Assert.Empty(Errors(estate, Minimal()));
        using var earlier = new TestEstate(2);
        Assert.Empty(Errors(earlier, Minimal()));
    }

    [Theory]
    [MemberData(nameof(Strings))]
    public void EachStringMemberHoldsTheLimitsTheReferenceStates(string name, string kind, bool required, bool emptyAllowed, int longest, long format)
    {
        using var estate = new TestEstate(3);
        JsonObject brief = Minimal();
        brief.Remove(name);
        IReadOnlyList<Finding> missing = Errors(estate, brief);
        if (required) Assert.Contains(missing, f => f.Code == "body.missing" && f.Location.EndsWith("#body.brief." + name, StringComparison.Ordinal));
        else Assert.Empty(missing);

        if (kind == "string[]")
        {
            IReadOnlyList<Finding> empty = Errors(estate, With(name, new JsonArray()));
            if (emptyAllowed) Assert.Empty(empty);
            else Assert.Contains(empty, f => f.Code == "body.empty" && f.Location.EndsWith("#body.brief." + name, StringComparison.Ordinal));
            // An item is never an empty string, whatever the array's empty rule.
            Assert.Contains(Errors(estate, With(name, new JsonArray(""))), f => f.Code == "body.length");
        }
        else
        {
            Assert.Contains(Errors(estate, With(name, JsonValue.Create(""))), f => f.Code == "body.length");
        }

        // A pattern member's longest item still has to be a pattern, so it is read as one.
        string prefix = name == "excludedChanges" ? "path:" : string.Empty;
        string value = prefix + new string('a', longest - prefix.Length);
        JsonNode Value(string text) => kind == "string[]" ? new JsonArray(text) : JsonValue.Create(text);
        Assert.Empty(Errors(estate, With(name, Value(value))));
        Assert.Contains(Errors(estate, With(name, Value(value + "a"))), f => f.Code == "body.length" && f.Location.Contains("#body.brief." + name, StringComparison.Ordinal));

        // A member introduced in format 3 is refused in a format-2 record, and an earlier member is read there as in format 3.
        using var earlier = new TestEstate(2);
        IReadOnlyList<Finding> before = Errors(earlier, With(name, Value(value)));
        if (format == 3) Assert.Contains(before, f => f.Location.EndsWith("#body.brief." + name, StringComparison.Ordinal));
        else Assert.Empty(before);
    }

    [Fact]
    public void SourcesMayBeEmptyAndEachIsATypeAnIdAndAnOptionalHash()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, OrderBody);
        Assert.Contains(Errors(estate, Without("sources")), f => f.Code == "body.missing" && f.Location.EndsWith("#body.brief.sources", StringComparison.Ordinal));
        Assert.Empty(Errors(estate, With("sources", new JsonArray())));
        Assert.Empty(Errors(estate, With("sources", new JsonArray(new JsonObject { ["type"] = "work-order", ["id"] = order.Id }))));
        Assert.Empty(Errors(estate, With("sources", new JsonArray(new JsonObject { ["type"] = "work-order", ["id"] = order.Id, ["hash"] = order.ComputedHash }))));
        Assert.Contains(Errors(estate, With("sources", new JsonArray(new JsonObject { ["id"] = order.Id }))), f => f.Code == "record.reference");
        Assert.Contains(Errors(estate, With("sources", new JsonArray(new JsonObject { ["type"] = "work-order" }))), f => f.Code == "record.reference");
        Assert.Contains(Errors(estate, With("sources", new JsonArray(new JsonObject { ["type"] = "work-order", ["id"] = order.Id, ["hash"] = "sha256:00" }))),
            f => f.Code == "record.reference");
    }

    [Fact]
    public void RepositoryIsTheEstatesOrARepositorysIdentity()
    {
        using var estate = new TestEstate(3);
        Assert.Empty(Errors(estate, With("repository", JsonValue.Create(estate.Estate.Id))));
        Assert.Contains(Errors(estate, With("repository", JsonValue.Create("not a ulid"))), f => f.Code == "body.ulid");
        Assert.Contains(Errors(estate, With("repository", JsonValue.Create(estate.OperatorId))), f => f.Code == "assignment.repository");
        using var earlier = new TestEstate(2);
        Assert.Contains(Errors(earlier, With("repository", JsonValue.Create(earlier.Estate.Id))), f => f.Code == "body.format");
    }

    [Fact]
    public void AfterHoldsOneToTwentyAssignmentIds()
    {
        using var estate = new TestEstate(3);
        static JsonArray Ids(int count) => [.. Enumerable.Range(0, count).Select(i => (JsonNode)JsonValue.Create("01M7PV6AXW41AVCKTFJP3J9Y" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))!)];
        Assert.Contains(Errors(estate, With("after", new JsonArray())), f => f.Code == "body.empty" && f.Location.EndsWith("#body.brief.after", StringComparison.Ordinal));
        // Twenty is the limit: twenty unknown ids are refused as unknown, never as too many; twenty-one are too many.
        Assert.DoesNotContain(Errors(estate, With("after", Ids(20))), f => f.Code == "assignment.after" && f.Message.Contains("at most", StringComparison.Ordinal));
        Assert.Contains(Errors(estate, With("after", Ids(21))), f => f.Code == "assignment.after" && f.Message.Contains("at most 20", StringComparison.Ordinal));
        Assert.Contains(Errors(estate, With("after", new JsonArray(new string('0', 27)))), f => f.Code == "body.length");
        Assert.Contains(Errors(estate, With("after", new JsonArray(new string('U', 26)))), f => f.Code == "assignment.after");
        using var earlier = new TestEstate(2);
        Assert.Contains(Errors(earlier, With("after", Ids(1))), f => f.Code == "body.format");
    }

    [Fact]
    public void RetrievalIsPublicOrAbsent()
    {
        using var estate = new TestEstate(3);
        Assert.Empty(Errors(estate, With("retrieval", JsonValue.Create("public"))));
        Assert.Contains(Errors(estate, With("retrieval", JsonValue.Create("private"))), f => f.Code == "body.enum");
        using var earlier = new TestEstate(2);
        Assert.Contains(Errors(earlier, With("retrieval", JsonValue.Create("public"))), f => f.Code == "body.format");
    }

    [Fact]
    public void AProgressRequestHoldsTheRulesTheReferenceStates()
    {
        using var estate = new TestEstate(3);
        JsonObject Request(string json) => With("progressRequest", JsonNode.Parse(json)!);
        Assert.Empty(Errors(estate, Request("""{"cadence":{"perStep":true}}""")));
        Assert.Empty(Errors(estate, Request("""{"cadence":{"minutes":1}}""")));
        Assert.Empty(Errors(estate, Request("""{"types":["accurate","best-effort","not-measurable"],"statusText":true,"cadence":{"perStep":true,"minutes":5}}""")));
        // cadence is required, and needs perStep true, minutes, or both; minutes is at least 1; perStep and statusText are booleans.
        Assert.Contains(Errors(estate, Request("""{"types":["accurate"]}""")), f => f.Code == "body.missing" && f.Location.EndsWith("progressRequest.cadence", StringComparison.Ordinal));
        Assert.Contains(Errors(estate, Request("""{"cadence":{}}""")), f => f.Code == "progress.cadence");
        Assert.Contains(Errors(estate, Request("""{"cadence":{"perStep":false}}""")), f => f.Code == "progress.cadence");
        Assert.Contains(Errors(estate, Request("""{"cadence":{"minutes":0}}""")), f => f.Code == "body.range");
        Assert.Contains(Errors(estate, Request("""{"cadence":{"perStep":"yes"}}""")), f => f.Code == "body.type");
        Assert.Contains(Errors(estate, Request("""{"statusText":"yes","cadence":{"perStep":true}}""")), f => f.Code == "body.type");
        // types may be left out but not empty, and holds distinct values of the three, each item 1 to 30 characters.
        Assert.Contains(Errors(estate, Request("""{"types":[],"cadence":{"perStep":true}}""")), f => f.Code == "body.empty");
        Assert.Contains(Errors(estate, Request("""{"types":["accurate","accurate"],"cadence":{"perStep":true}}""")), f => f.Code == "progress.types");
        Assert.Contains(Errors(estate, Request("""{"types":["precise"],"cadence":{"perStep":true}}""")), f => f.Code == "progress.types");
        Assert.Contains(Errors(estate, Request("{\"types\":[\"" + new string('a', 31) + "\"],\"cadence\":{\"perStep\":true}}")), f => f.Code == "body.length");
        Assert.Contains(Errors(estate, Request("""{"cadence":{"perStep":true},"other":1}""")), f => f.Code == "body.unknown-key");
        using var earlier = new TestEstate(2);
        Assert.Contains(Errors(earlier, With("progressRequest", JsonNode.Parse("""{"cadence":{"perStep":true}}""")!)), f => f.Code == "body.format");
    }

    [Fact]
    public void TheBriefAdmitsNoOtherMember()
    {
        using var estate = new TestEstate(3);
        Assert.Contains(Errors(estate, With("notes", JsonValue.Create("extra"))), f => f.Code == "body.unknown-key" && f.Location.EndsWith("#body.brief.notes", StringComparison.Ordinal));
    }

    private static IEnumerable<(string, string, bool, bool?, string?, long)> Rows(IReadOnlyList<FieldDescription> members, string parent)
    {
        foreach (FieldDescription member in members)
        {
            yield return (parent + member.Name, member.Kind, member.Required, member.EmptyAllowed, member.Limits, member.SinceFormat);
            foreach (var row in Rows(member.Members, parent + member.Name + ".")) yield return row;
        }
    }

    private static JsonObject Minimal() => (JsonObject)JsonNode.Parse(
        """{"outcome":"NOTES.md holds a greeting.","sources":[],"permittedChanges":["NOTES.md, one line"],"prohibitedEffects":[],"workspace":["the estate"],"limits":[],"expectedOutputs":["NOTES.md"],"reviewCriteria":["Does NOTES.md hold a greeting?"],"recovery":"Read this brief."}""")!;

    private static JsonObject With(string name, JsonNode value)
    {
        JsonObject brief = Minimal();
        brief[name] = value;
        return brief;
    }

    private static JsonObject Without(string name)
    {
        JsonObject brief = Minimal();
        brief.Remove(name);
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
