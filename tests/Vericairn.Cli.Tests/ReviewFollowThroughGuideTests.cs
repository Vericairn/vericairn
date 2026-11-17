// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Cli.Tests;

/// <summary>
/// DOCS-41 (decision 01M7F2C6GB9GX7KYTK5Q30S6P7, issue 385): the living documents guide's recipe for following up every stale review
/// after several pins, run word for word against a synthetic estate. Every command is matched against the recipe's text before it
/// runs, and the review bodies are the recipe's named JSON blocks, so the guide and this check cannot drift apart. The test adds only
/// the estate's directory, and --json to the pins and the successor reviews, whose receipts the recipe says are read with it.
/// </summary>
[Collection("cli")]
public sealed partial class ReviewFollowThroughGuideTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Heading = "## Following up reviews after several pins (0.9.4)";
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";
    private static readonly string[] Blocks = ["guide-review.json", "setup-review.json"];

    private const string PinGuide = "vericairn docs pin docs/guide.md --by <builder-id> --expected <guide-pin-id> --summary \"Describe the new setup step.\"";
    private const string PinSetup = "vericairn docs pin docs/setup.md --by <builder-id> --expected <setup-pin-id> --summary \"Add the new setup step.\"";
    private const string ShowPin = "vericairn --json records show <new-guide-pin-id>";
    private const string Validate = "vericairn --json records validate";
    private const string Living = "vericairn docs living";
    private const string ReviewGuide = "vericairn records new review --by <reviewer-id> --subject document-revision:<new-guide-pin-id>:<new-guide-pin-hash> --supersedes <guide-review-id> --body guide-review.json";
    private const string ReviewSetup = "vericairn records new review --by <reviewer-id> --subject document-revision:<new-setup-pin-id>:<new-setup-pin-hash> --supersedes <setup-review-id> --body setup-review.json";
    private const string ShowReview = "vericairn --json records show <new-guide-review-id>";

    [Fact]
    public void TheGuideFollowsUpEveryStaleReviewAfterSeveralPinsWithoutAManufacturedPass()
    {
        string section = Section();
        Dictionary<string, string> blocks = JsonBlocks(section);
        Assert.Equal(Blocks.Order(StringComparer.Ordinal), blocks.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("With `--json`, each pin prints the new pin's `id` and `hash`", section, StringComparison.Ordinal);
        Assert.Contains("With `--json`, each review prints its receipt too, the new review's `id` and `hash`", section, StringComparison.Ordinal);
        string root = CliHarness.TempDirectory("docs41-estate");
        string scratch = CliHarness.TempDirectory("docs41-scratch");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            // The starting state the recipe describes: two documents, each pinned by the builder and judged by a separate reviewer's
            // generic review of that pin, and the guide declared living, cleared by anyone the declaration admits.
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Review follow-through.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            values["<builder-id>"] = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            values["<reviewer-id>"] = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "reviewer.json", """{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;
            Write(root, "docs/guide.md", "# Guide\n\nHow to set up the widget.\n");
            Write(root, "docs/setup.md", "# Setup\n\n1. Install the widget.\n");
            CliHarness.Ok("--estate", root, "docs", "declare", "--by", op, "--body", CliHarness.WriteJson(scratch, "declaration.json",
                $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"refuse","product":"{{product}}"}],"checks":["links-resolve"],"clearedBy":"any","summary":"Describes the widget."}"""));
            CliHarness.Outcome guidePin = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "docs/guide.md", "--by", values["<builder-id>"], "--summary", "First revision.");
            CliHarness.Outcome setupPin = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "docs/setup.md", "--by", values["<builder-id>"], "--summary", "First revision.");
            values["<guide-pin-id>"] = guidePin.Id;
            values["<setup-pin-id>"] = setupPin.Id;
            string read = CliHarness.WriteJson(scratch, "first-review.json", """{"independence":"independent-agent","outcome":"pass","summary":"Read revision 1.","findings":[]}""");
            values["<guide-review-id>"] = CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", values["<reviewer-id>"], "--subject", $"document-revision:{guidePin.Id}:{guidePin.Hash}", "--body", read).Id;
            values["<setup-review-id>"] = CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", values["<reviewer-id>"], "--subject", $"document-revision:{setupPin.Id}:{setupPin.Hash}", "--body", read).Id;
            Assert.Empty(Stale(CliHarness.Ok("--estate", root, "--json", "records", "validate")));

            // The builder revises both files and pins each against its current pin; each pin's receipt names the new pin.
            File.AppendAllText(Path.Combine(root, "docs", "guide.md"), "\nThe new setup step comes first.\n");
            File.AppendAllText(Path.Combine(root, "docs", "setup.md"), "2. Run the new setup step.\n");
            CliHarness.Outcome newGuide = Step(section, values, blocks, scratch, root, PinGuide, json: true);
            values["<new-guide-pin-id>"] = newGuide.Id;
            values["<new-guide-pin-hash>"] = newGuide.Hash;
            CliHarness.Outcome newSetup = Step(section, values, blocks, scratch, root, PinSetup, json: true);
            values["<new-setup-pin-id>"] = newSetup.Id;
            values["<new-setup-pin-hash>"] = newSetup.Hash;

            // Before anything else writes: the new pin, one stale warning at each earlier review naming the pin that superseded its
            // subject, and the living document still current, cleared by its new pin.
            JsonElement pin = Step(section, values, blocks, scratch, root, ShowPin).Json;
            Assert.Equal("docs/guide.md", pin.GetProperty("body").GetProperty("path").GetString());
            Assert.Equal(2, pin.GetProperty("body").GetProperty("revision").GetInt32());
            Assert.Equal(values["<new-guide-pin-hash>"], pin.GetProperty("hash").GetString());
            var bothStale = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [values["<guide-review-id>"]] = values["<new-guide-pin-id>"],
                [values["<setup-review-id>"]] = values["<new-setup-pin-id>"],
            };
            Assert.Equal(bothStale, Stale(Step(section, values, blocks, scratch, root, Validate)));
            string living = Step(section, values, blocks, scratch, root, Living).Out;
            Assert.Contains("docs/guide.md  current", living, StringComparison.Ordinal);
            Assert.Contains("cleared by " + values["<new-guide-pin-id>"], living, StringComparison.Ordinal);

            // The run stops after the first successor review, which answers its review alone.
            CliHarness.Outcome guideReview = Step(section, values, blocks, scratch, root, ReviewGuide, json: true);
            values["<new-guide-review-id>"] = guideReview.Id;
            var remaining = new Dictionary<string, string>(StringComparer.Ordinal) { [values["<setup-review-id>"]] = values["<new-setup-pin-id>"] };
            Assert.Equal(remaining, Stale(Step(section, values, blocks, scratch, root, Validate)));

            // Running it again from the start writes nothing: the pin is refused, and so is a second successor of the answered review.
            int records = RecordCount(root);
            Step(section, values, blocks, scratch, root, PinGuide, json: true, refused: true);
            CliHarness.Outcome again = Step(section, values, blocks, scratch, root, ReviewGuide, json: true, refused: true);
            Assert.Contains("reconcile.successors", again.Out + again.Err, StringComparison.Ordinal);
            Assert.Equal(records, RecordCount(root));
            Assert.Equal(remaining, Stale(Step(section, values, blocks, scratch, root, Validate)));

            // Only the remaining review is written, and it records what the reviewer found rather than a pass.
            CliHarness.Outcome setupReview = Step(section, values, blocks, scratch, root, ReviewSetup, json: true);
            JsonElement setupBody = CliHarness.Ok("--estate", root, "--json", "records", "show", setupReview.Id).Json.GetProperty("body");
            Assert.Equal("findings", setupBody.GetProperty("outcome").GetString());
            Assert.Equal("warning", Assert.Single(setupBody.GetProperty("findings").EnumerateArray()).GetProperty("severity").GetString());

            // Each successor's subject is the exact new revision, and no stale review remains; the earlier reviews are kept.
            JsonElement review = Step(section, values, blocks, scratch, root, ShowReview).Json;
            JsonElement subject = review.GetProperty("subject");
            Assert.Equal("document-revision", subject.GetProperty("type").GetString());
            Assert.Equal(values["<new-guide-pin-id>"], subject.GetProperty("id").GetString());
            Assert.Equal(values["<new-guide-pin-hash>"], subject.GetProperty("hash").GetString());
            Assert.Equal(values["<reviewer-id>"], review.GetProperty("by").GetString());
            Assert.Equal(values["<guide-review-id>"], review.GetProperty("supersedes").GetString());
            CliHarness.Outcome final = Step(section, values, blocks, scratch, root, Validate);
            Assert.Empty(Stale(final));
            Assert.DoesNotContain(Findings(final), f => f.GetProperty("severity").GetString() == "error");
            foreach (string earlier in new[] { values["<guide-review-id>"], values["<setup-review-id>"] })
                Assert.Equal(earlier, CliHarness.Ok("--estate", root, "--json", "records", "show", earlier).Id);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    /// <summary>
    /// Runs one of the recipe's commands as written: it must appear verbatim in the recipe; its placeholders are filled, its body file
    /// names resolve to the recipe's JSON blocks, filled and saved, and it runs in the estate given, with --json where asked.
    /// </summary>
    private static CliHarness.Outcome Step(string section, Dictionary<string, string> values, Dictionary<string, string> blocks, string scratch, string estate,
        string command, bool json = false, bool refused = false)
    {
        Assert.True(section.Contains("\n" + command + "\n", StringComparison.Ordinal), $"The recipe no longer shows the command: {command}");
        List<string> words = Words(command);
        Assert.Equal("vericairn", words[0]);
        var args = new List<string> { "--estate", estate };
        if (json && !words.Contains("--json")) args.Add("--json");
        foreach (string word in words.Skip(1))
        {
            if (blocks.TryGetValue(word, out string? body))
            {
                string filled = Fill(body, values);
                JsonDocument.Parse(filled).Dispose();
                args.Add(CliHarness.WriteJson(scratch, word, filled));
            }
            else args.Add(Fill(word, values));
        }

        CliHarness.Outcome outcome = CliHarness.Run([.. args]);
        if (refused) Assert.True(outcome.Code != 0, $"{command} was not refused\n{outcome.Out}");
        else Assert.True(outcome.Code == 0, $"{command} exited {outcome.Code}\n{outcome.Out}\n{outcome.Err}");
        return outcome;
    }

    // A command's words as a shell reads them: separated by spaces, a double-quoted run one word without its quotes.
    private static List<string> Words(string command)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false;
        foreach (char c in command)
        {
            if (c == '"') quoted = !quoted;
            else if (c == ' ' && !quoted)
            {
                if (word.Length > 0) words.Add(word.ToString());
                word.Clear();
            }
            else word.Append(c);
        }

        Assert.False(quoted, $"Unbalanced quotes in {command}");
        if (word.Length > 0) words.Add(word.ToString());
        return words;
    }

    /// <summary>Fills every placeholder; one the check does not know fails it, so a new placeholder in the recipe needs a value here.</summary>
    private static string Fill(string text, Dictionary<string, string> values)
    {
        string filled = Placeholder().Replace(text, m => values.TryGetValue(m.Value, out string? value) ? value : m.Value);
        Match left = Placeholder().Match(filled);
        Assert.False(left.Success, $"No value for the recipe's placeholder {left.Value}.");
        return filled;
    }

    // The stale reviews a validation reports, each by the id of the review it is located at, with the pin its message names.
    private static Dictionary<string, string> Stale(CliHarness.Outcome validation) =>
        Findings(validation).Where(f => f.GetProperty("Code").GetString() == "record.subject-stale").ToDictionary(
            f => Path.GetFileNameWithoutExtension(f.GetProperty("Location").GetString()!),
            f => SupersededBy().Match(f.GetProperty("Message").GetString()!).Groups["pin"].Value,
            StringComparer.Ordinal);

    private static JsonElement[] Findings(CliHarness.Outcome outcome) => [.. outcome.Json.GetProperty("findings").EnumerateArray()];

    private static int RecordCount(string root) => Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories).Count();

    private static Dictionary<string, string> JsonBlocks(string section)
    {
        var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match block in Fence().Matches(section).Where(m => m.Groups["language"].Value == "json"))
        {
            string name = block.Groups["name"].Value;
            Assert.False(string.IsNullOrEmpty(name), "Every JSON block in the recipe names the file it is saved as.");
            Assert.True(blocks.TryAdd(name, block.Groups["body"].Value), $"The recipe names {name} twice.");
        }

        return blocks;
    }

    /// <summary>The recipe's section of the living documents guide, from its heading to the next section of the same level.</summary>
    private static string Section()
    {
        string guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "living-documents.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = guide.IndexOf("\n" + Heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "The living documents guide no longer holds the recipe.");
        int end = guide.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = guide[start..(end < 0 ? guide.Length : end)];

        // The record.subject-stale explanation and the pinning section link to it.
        const string Anchor = "following-up-reviews-after-several-pins-094";
        Assert.Contains("(#" + Anchor + ")", guide[..start], StringComparison.Ordinal);
        Assert.Contains("(living-documents.md#" + Anchor + ")", File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "record-format-v1.md")), StringComparison.Ordinal);
        return section;
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return root;
    }

    private static void Write(string root, string path, string text)
    {
        string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    [GeneratedRegex(@"^```(?<language>[a-z]+)(?: (?<name>[a-z0-9.-]+))?\n(?<body>.*?)\n```$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"<[a-z0-9-]+>")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"superseded by (?<pin>[0-9A-HJKMNP-TV-Z]{26});")]
    private static partial Regex SupersededBy();
}
