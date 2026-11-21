// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.DocsTool;
using Vericairn.Records.Types;

namespace Vericairn.Cli.Tests;

/// <summary>
/// DOCS-46: the record format guide's minimal assignment brief, run word for word with <c>work assign</c> and with <c>records new
/// assignment</c>, so the example cannot drift from the runtime; the validation route it states; and the record reference's tables of
/// the brief's members, generated from the rules the validator reads the brief by.
/// </summary>
[Collection("cli")]
public sealed partial class AssignmentBriefGuideTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Heading = "## Writing an assignment brief (0.9.4)";
    private const string Assign = "vericairn work assign W-1 --contributor <contributor-id> --by <operator-id> --brief brief.json";
    private const string RecordsNew = "vericairn records new assignment --by <operator-id> --subject work-order:<work-order-id>:<work-order-hash> --body assignment.json";

    [Fact]
    public void TheMinimalBriefIsAssignedWordForWord()
    {
        string section = Section();
        using var e = new Estate("brief-assign");
        File.WriteAllText(Path.Combine(e.Bodies, "brief.json"), Block(section, "with `brief.json`:"));
        CliHarness.Outcome assigned = e.Step(section, Assign);
        Assert.True(assigned.Code == 0, assigned.Out + assigned.Err);
        JsonElement record = CliHarness.Ok("--estate", e.Root, "--json", "records", "show", assigned.Out.Split("  ")[1]).Json;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Block(section, "with `brief.json`:")), JsonNode.Parse(record.GetProperty("body").GetProperty("brief").GetRawText())));
        Assert.Equal(e.Values["<work-order-id>"], record.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal(e.Values["<work-order-hash>"], record.GetProperty("subject").GetProperty("hash").GetString());
    }

    [Fact]
    public void TheSameBriefIsWrittenWordForWordWithRecordsNewAssignment()
    {
        string section = Section();
        using var e = new Estate("brief-records-new");
        string body = e.Fill(Block(section, "with `assignment.json`:"));
        File.WriteAllText(Path.Combine(e.Bodies, "assignment.json"), body);
        // The two examples carry the same brief.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Block(section, "with `brief.json`:")), JsonNode.Parse(body)!["brief"]));
        Assert.Contains("which `vericairn work brief W-1` shows as `work order <id> at <hash>`", Flat(section), StringComparison.Ordinal);
        Assert.Contains($"work order {e.Values["<work-order-id>"]} at {e.Values["<work-order-hash>"]}", CliHarness.Ok("--estate", e.Root, "work", "brief", "W-1").Out, StringComparison.Ordinal);
        CliHarness.Outcome written = e.Step(section, RecordsNew);
        Assert.True(written.Code == 0, written.Out + written.Err);
        Assert.Single(e.Assignments());
    }

    // There is no dry run: a proposed record that would leave the estate invalid is refused with every finding and nothing is written.
    [Fact]
    public void ABriefMissingARequiredMemberIsRefusedAndNothingIsWritten()
    {
        string section = Section();
        using var e = new Estate("brief-refused");
        JsonObject brief = JsonNode.Parse(Block(section, "with `brief.json`:"))!.AsObject();
        brief.Remove("reviewCriteria");
        brief.Remove("recovery");
        File.WriteAllText(Path.Combine(e.Bodies, "brief.json"), brief.ToJsonString());
        CliHarness.Outcome refused = e.Step(section, Assign);
        Assert.Equal(1, refused.Code);
        string said = refused.Out + refused.Err;
        Assert.Contains("Not written: the estate would be invalid with this record (2 errors).", said, StringComparison.Ordinal);
        Assert.Contains("body.missing", said, StringComparison.Ordinal);
        Assert.Contains("#body.brief.reviewCriteria: 'reviewCriteria' is required.", said, StringComparison.Ordinal);
        Assert.Contains("#body.brief.recovery: 'recovery' is required.", said, StringComparison.Ordinal);
        Assert.Empty(e.Assignments());

        string flat = Flat(section);
        Assert.Contains("There is no dry-run command.", flat, StringComparison.Ordinal);
        Assert.Contains("they refuse with `Not written: the estate would be invalid with this record`, the number of errors and every finding at once, such as `body.missing` for a required member left out, and exit with code 1.", flat, StringComparison.Ordinal);
        Assert.Contains("`records validate` checks the estate as it stands, not a proposed body.", flat, StringComparison.Ordinal);
    }

    // The reference's brief tables are the validator's rules, row by row, and records describe gives the same members.
    [Fact]
    public void TheRecordReferenceTablesTheBriefsMembersFromTheValidatorsRules()
    {
        string output = CliHarness.TempDirectory("brief-reference");
        try
        {
            string declaration = Path.Combine(output, "..", "documents-" + Path.GetFileName(output) + ".json");
            File.WriteAllText(declaration, "[\"record-reference.md\"]");
            var console = new StringWriter();
            TextWriter previous = Console.Out;
            Console.SetOut(console);
            int code;
            try { code = ReferenceSet.Generate(output, declaration, json: true); }
            finally { Console.SetOut(previous); }
            Assert.True(code == 0, console.ToString());
            string records = WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(output, "record-reference.md")));
            string assignment = records[records.IndexOf("\n## `assignment`\n", StringComparison.Ordinal)..records.IndexOf("\n## `attempt`\n", StringComparison.Ordinal)];
            const string Columns = "| Member | Kind | Required | Empty allowed | Limits | Introduced in format | Meaning |";
            Tables(assignment, "assignment.brief", AssignmentBrief.Members.Select(m => m.Describe()).ToList(), Columns);
            Assert.True(assignment.IndexOf("### `assignment.brief`", StringComparison.Ordinal) < assignment.IndexOf("### `assignment.brief.sources`", StringComparison.Ordinal));
            Assert.Contains("| reviewCriteria | string[] | true | false | each item 1 to 2,000 characters | 1 | The questions a review of the result answers. |", assignment, StringComparison.Ordinal);
            Assert.Contains("| recovery | string | true |  | 1 to 4,000 characters | 1 |", assignment, StringComparison.Ordinal);

            JsonElement described = CliHarness.Ok("--json", "records", "describe", "assignment").Json;
            JsonElement brief = described.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "brief");
            Assert.Equal(AssignmentBrief.Members.Select(m => m.Name), brief.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("name").GetString()));
            Assert.Contains("brief.reviewCriteria", CliHarness.Ok("records", "describe", "assignment").Out, StringComparison.Ordinal);
        }
        finally
        {
            CliHarness.Delete(output);
            File.Delete(Path.Combine(output, "..", "documents-" + Path.GetFileName(output) + ".json"));
        }
    }

    // Each member's row, and a table of its own for each member of fixed members, after its parent's.
    private static void Tables(string reference, string path, IReadOnlyList<FieldDescription> members, string columns)
    {
        Assert.Contains("\n### `" + path + "`\n\n" + columns + "\n", reference, StringComparison.Ordinal);
        foreach (FieldDescription member in members)
        {
            string row = "| " + string.Join(" | ", member.Name, member.Kind, member.Required ? "true" : "false",
                member.EmptyAllowed is bool empty ? (empty ? "true" : "false") : string.Empty, member.Limits ?? string.Empty,
                member.SinceFormat.ToString(System.Globalization.CultureInfo.InvariantCulture), member.Description) + " |";
            Assert.Contains(row, reference, StringComparison.Ordinal);
            if (member.Members.Count > 0) Tables(reference, path + "." + member.Name, member.Members, columns);
        }
    }

    private static string Section()
    {
        string guide = Doc("record-format.md");
        int start = guide.IndexOf("\n" + Heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = guide.IndexOf("\n## ", start + Heading.Length + 1, StringComparison.Ordinal);
        return guide[start..(end + 1)];
    }

    // The fenced JSON block that follows a line of the guide.
    private static string Block(string section, string after)
    {
        int at = section.IndexOf("\n" + after + "\n", StringComparison.Ordinal);
        Assert.True(at >= 0, after);
        int open = section.IndexOf("```json\n", at, StringComparison.Ordinal) + "```json\n".Length;
        return section[open..section.IndexOf("\n```", open, StringComparison.Ordinal)];
    }

    private static string Flat(string text) => FlatSpace().Replace(text, " ");

    private static string Doc(string name)
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root, "docs", name)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\n[ ]*")]
    private static partial Regex FlatSpace();

    [GeneratedRegex("<[a-z-]+>")]
    private static partial Regex Placeholder();

    /// <summary>A new estate with work order W-1, its operator the guide's operator and contributor.</summary>
    private sealed class Estate : IDisposable
    {
        public Estate(string name)
        {
            Root = CliHarness.TempDirectory(name);
            Bodies = CliHarness.TempDirectory(name + "-bodies");
            string op = CliHarness.Ok("--estate", Root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Assign work.",
                "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            Values["<operator-id>"] = op;
            Values["<contributor-id>"] = op;
            CliHarness.Outcome order = CliHarness.Ok("--estate", Root, "--json", "records", "new", "work-order", "--by", op, "--body", CliHarness.WriteJson(Bodies, "order.json",
                """{"key":"W-1","kind":"ordinary","title":"Write a greeting","intent":"Try the brief.","scope":["NOTES.md"],"exitQuestion":"Does NOTES.md hold a greeting?","inputs":[],"deliverables":["NOTES.md"]}"""));
            Values["<work-order-id>"] = order.Json.GetProperty("id").GetString()!;
            Values["<work-order-hash>"] = order.Json.GetProperty("hash").GetString()!;
        }

        public string Root { get; }

        public string Bodies { get; }

        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string Fill(string text) => Placeholder().Replace(text, m => Values.TryGetValue(m.Value, out string? value) ? value : m.Value);

        /// <summary>Runs a command the guide shows, word for word, its placeholders filled and its body files read from the bodies folder.</summary>
        public CliHarness.Outcome Step(string section, string command)
        {
            Assert.True(section.Contains("\n" + command + "\n", StringComparison.Ordinal), $"The guide no longer shows the command: {command}");
            string[] words = [.. command.Split(' ')[1..].Select(Fill).Select(w => w.EndsWith(".json", StringComparison.Ordinal) ? Path.Combine(Bodies, w) : w)];
            Assert.DoesNotContain(words, w => Placeholder().IsMatch(w));
            return CliHarness.Run(["--estate", Root, .. words]);
        }

        public string[] Assignments()
        {
            string folder = Path.Combine(Root, "records", "assignment");
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories) : [];
        }

        public void Dispose() => CliHarness.Delete(Root, Bodies);
    }
}
