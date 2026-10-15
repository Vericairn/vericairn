// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// <c>vericairn progress</c> over an estate built through the command line: every section from the records alone, the same bytes on a
/// fresh copy, the states status derives, and what each release contained as <c>vericairn docs notes</c> derives it.
/// </summary>
[Collection("cli")]
public class ProgressJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ProgressRendersEverySectionFromTheRecordsTheSameOnACopyAgreeingWithStatusAndTheNotes()
    {
        string root = CliHarness.TempDirectory("progress");
        string scratch = CliHarness.TempDirectory("progress-scratch");
        string? copy = null;
        try
        {
            string op = Init(root);
            string builder = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""")).Id;

            // W-1 is accepted before 0.1.0 is published; W-2 before 0.2.0, and W-3's result is not accepted.
            Worked(root, scratch, op, builder, "W-1", "2026-09-01T00:00:00Z", accepted: true);
            Publish(root, scratch, op, product, "0.1.0", "2026-09-01T01:00:00Z");
            Worked(root, scratch, op, builder, "W-2", "2026-09-01T02:00:00Z", accepted: true);
            Worked(root, scratch, op, builder, "W-3", "2026-09-01T02:10:00Z", accepted: false);
            Publish(root, scratch, op, product, "0.2.0", "2026-09-01T03:00:00Z");

            // A release work order carrying two, a work order that starts after one of them, a milestone submitted and an open ask.
            CliHarness.Outcome w4 = Order(root, scratch, op, "W-4", "ordinary", "[]");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"work-order:{w4.Id}:{w4.Hash}", "--body", CliHarness.WriteJson(scratch, "accept-w4.json", """{"statement":"Done.","basis":[],"self":true}"""));
            CliHarness.Outcome w5 = Order(root, scratch, op, "W-5", "ordinary", "[]");
            Order(root, scratch, op, "REL-030", "ordinary", $$"""[{"type":"work-order","id":"{{w4.Id}}"},{"type":"work-order","id":"{{w5.Id}}"}]""");
            Order(root, scratch, op, "W-6", "ordinary", "[]", $$""","after":["{{w5.Id}}"]""");
            CliHarness.Outcome milestone = Order(root, scratch, op, "M-1", "milestone", "[]");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-status", "--by", op, "--subject", $"work-order:{milestone.Id}", "--body", CliHarness.WriteJson(scratch, "submitted.json", """{"status":"submitted","note":"For the operator's acceptance."}"""));
            string ask = CliHarness.Ok("--estate", root, "--json", "records", "new", "ask", "--by", builder, "--body", CliHarness.WriteJson(scratch, "ask.json", $$"""{"to":"{{op}}","question":"Ship the widget in blue?","recommendation":"Yes, blue reads best.","consequence":"The widget stays grey.","state":"open"}""")).Id;

            CliHarness.Outcome text = CliHarness.Ok("--estate", root, "progress");
            Assert.StartsWith("Progress of Example (example), rendered from its records [estate ", text.Out, StringComparison.Ordinal);
            Assert.Contains("  Check: 0 errors, 0 warnings [every record and the repository, as vericairn estate check runs them]\n", text.Out, StringComparison.Ordinal);
            Assert.Contains("Work orders by state: 8 current\n  planned: 6\n", text.Out, StringComparison.Ordinal);
            Assert.Contains("Published releases and what each contained: 2 published of 2 releases recorded\n", text.Out, StringComparison.Ordinal);
            Assert.Contains("      accepted: W-2 Order W-2 [result ", text.Out, StringComparison.Ordinal);
            Assert.Contains("      recorded, not yet accepted: W-3 Order W-3 [result ", text.Out, StringComparison.Ordinal);
            Assert.Contains("  REL-030  planned  Order REL-030 [work-order ", text.Out, StringComparison.Ordinal);
            Assert.Contains("    2 work orders, 1 accepted\n", text.Out, StringComparison.Ordinal);
            Assert.Contains("The next eligible work: 5 of 6 planned work orders\n", text.Out, StringComparison.Ordinal);
            Assert.Contains($"      after W-5: planned, holds it [work-order {w5.Id}]\n", text.Out, StringComparison.Ordinal);
            Assert.Contains($"    open: Ship the widget in blue? [ask {ask}]\n      recommendation: Yes, blue reads best.\n", text.Out, StringComparison.Ordinal);
            Assert.Contains($"    milestone work orders submitted: 1\n      M-1  Order M-1 [work-order {milestone.Id}, work-status ", text.Out, StringComparison.Ordinal);
            Assert.Contains("    results submitted toward milestone work orders: none\n", text.Out, StringComparison.Ordinal);

            // The machine form is the same report; the API gives both, and a fresh copy of the estate gives the same bytes.
            CliHarness.Outcome json = CliHarness.Ok("--estate", root, "--json", "progress");
            Assert.Equal("vericairn.progress/1", json.Json.GetProperty("format").GetString());
            using (EstateService service = EstateService.Open(root))
            {
                Assert.Equal(text.Out, service.Progress().Text);
            }

            copy = CliHarness.Clone(root, "progress-copy");
            Assert.Equal(text.Out, CliHarness.Ok("--estate", copy, "progress").Out);
            Assert.Equal(json.Out, CliHarness.Ok("--estate", copy, "--json", "progress").Out);

            // Every work order's state is the one status derives.
            Dictionary<string, string> status = CliHarness.Ok("--estate", root, "--json", "status").Json.GetProperty("workOrders").EnumerateArray()
                .ToDictionary(w => w.GetProperty("key").GetString()!, w => w.GetProperty("status").GetString()!, StringComparer.Ordinal);
            Dictionary<string, string> reported = json.Json.GetProperty("workOrders").GetProperty("states").EnumerateArray()
                .SelectMany(s => s.GetProperty("workOrders").EnumerateArray()).ToDictionary(w => w.GetProperty("key").GetString()!, w => w.GetProperty("status").GetString()!, StringComparer.Ordinal);
            Assert.Equal(status.OrderBy(p => p.Key, StringComparer.Ordinal), reported.OrderBy(p => p.Key, StringComparer.Ordinal));

            // What 0.2.0 contained is what its notes list as accepted, with no documentation set of 0.1.0 beside them.
            string set = Path.Combine(scratch, "sets", "0.2.0");
            CliHarness.Ok("--estate", root, "docs", "notes", "--from", "0.1.0", "--to", "0.2.0", "--out", set);
            string notes = File.ReadAllText(Path.Combine(set, "release-notes.md"));
            string accepted = notes[notes.IndexOf("## Work accepted", StringComparison.Ordinal)..notes.IndexOf("## Work recorded, not yet accepted", StringComparison.Ordinal)];
            List<string> fromNotes = accepted.Split('\n').Where(l => l.StartsWith("### ", StringComparison.Ordinal)).Select(l => l[4..].Split(':')[0]).ToList();
            JsonElement release = json.Json.GetProperty("releases").GetProperty("published").EnumerateArray().Single(r => r.GetProperty("version").GetString() == "0.2.0");
            Assert.Equal(["W-2"], fromNotes);
            Assert.Equal(fromNotes, release.GetProperty("accepted").EnumerateArray().Select(a => a.GetProperty("key").GetString()));
            Assert.Contains("### W-3:", notes[notes.IndexOf("## Work recorded, not yet accepted", StringComparison.Ordinal)..], StringComparison.Ordinal);
            Assert.Equal(["W-3"], release.GetProperty("notAccepted").EnumerateArray().Select(a => a.GetProperty("key").GetString()));
        }
        finally
        {
            CliHarness.Delete(root, scratch, copy ?? string.Empty);
        }
    }

    [Fact]
    public void ProgressExitsWithErrorsWhenTheCheckItShowsFindsThem()
    {
        string root = CliHarness.TempDirectory("progress-errors");
        try
        {
            Init(root);
            File.WriteAllText(Path.Combine(root, "scratch.txt"), "not estate content\n");
            CliHarness.Outcome outcome = CliHarness.Run("--estate", root, "progress");
            Assert.Equal(1, outcome.Code);
            Assert.Contains("  Check: 1 error, 0 warnings [every record and the repository, as vericairn estate check runs them]\n    ERROR layout.unexpected scratch.txt: ", outcome.Out, StringComparison.Ordinal);
            Assert.Equal(1, CliHarness.Run("--estate", root, "status").Code);
            Assert.Equal(2, CliHarness.Run("--estate", Path.Combine(root, "records"), "progress").Code);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    private static string Init(string root) =>
        CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Progress.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;

    private static CliHarness.Outcome Order(string root, string scratch, string op, string key, string kind, string inputs, string extra = "", string? at = null)
    {
        string[] time = at is null ? [] : ["--at", at];
        return CliHarness.Ok(["--estate", root, "--json", "records", "new", "work-order", "--by", op, .. time, "--body", CliHarness.WriteJson(scratch, key + ".json", $$"""{"key":"{{key}}","kind":"{{kind}}","title":"Order {{key}}","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":{{inputs}},"deliverables":["work"]{{extra}}}""")]);
    }

    /// <summary>A work order assigned to the builder, its attempt and result, and the operator's acceptance of the result when asked, a minute apart from <paramref name="at"/>.</summary>
    private static void Worked(string root, string scratch, string op, string builder, string key, string at, bool accepted)
    {
        DateTimeOffset start = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
        string Minute(int minutes) => Vericairn.Records.Model.RecordParser.FormatTimestamp(start.AddMinutes(minutes));
        CliHarness.Outcome order = Order(root, scratch, op, key, "ordinary", "[]", at: Minute(0));
        CliHarness.Outcome assignment = CliHarness.Ok("--estate", root, "--json", "records", "new", "assignment", "--by", op, "--subject", $"work-order:{order.Id}:{order.Hash}", "--at", Minute(1), "--body", CliHarness.WriteJson(scratch, key + "-assignment.json", $$$"""{"contributor":"{{{builder}}}","brief":{"outcome":"The work.","sources":[],"permittedChanges":["the work"],"prohibitedEffects":["no publication"],"workspace":["example repository"],"limits":[],"expectedOutputs":["work"],"reviewCriteria":["it is done"],"recovery":"Record a result."}}"""));
        CliHarness.Outcome attempt = CliHarness.Ok("--estate", root, "--json", "records", "new", "attempt", "--by", builder, "--subject", $"assignment:{assignment.Id}:{assignment.Hash}", "--at", Minute(2), "--body", CliHarness.WriteJson(scratch, key + "-attempt.json", $$"""{"startedAt":"{{Minute(2)}}","workspace":"example repository, work branch","note":"Do it."}"""));
        CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "records", "new", "result", "--by", builder, "--subject", $"attempt:{attempt.Id}:{attempt.Hash}", "--at", Minute(3), "--body", CliHarness.WriteJson(scratch, key + "-result.json", """{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}"""));
        if (accepted)
        {
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"result:{result.Id}:{result.Hash}", "--at", Minute(4), "--body", CliHarness.WriteJson(scratch, key + "-acceptance.json", """{"statement":"Accepted.","basis":[],"self":false}"""));
        }
    }

    /// <summary>A candidate, the operator's acceptance of its exact hash and its publication, a minute apart from <paramref name="at"/>.</summary>
    private static void Publish(string root, string scratch, string op, string product, string version, string at)
    {
        DateTimeOffset start = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
        string Minute(int minutes) => Vericairn.Records.Model.RecordParser.FormatTimestamp(start.AddMinutes(minutes));
        string body = $$"""{"version":"{{version}}","repository":"Example/widget","commit":"{{Commit}}","status":"candidate","artifacts":[{"name":"widget-{{version}}-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
        CliHarness.Outcome candidate = CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--at", Minute(0), "--body", CliHarness.WriteJson(scratch, version + "-candidate.json", body));
        CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate.Id}:{candidate.Hash}", "--at", Minute(1), "--body", CliHarness.WriteJson(scratch, version + "-accept.json", """{"statement":"Accepted.","basis":[],"self":true}"""));
        CliHarness.Ok("--estate", root, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--supersedes", candidate.Id, "--at", Minute(2), "--body", CliHarness.WriteJson(scratch, version + "-published.json", body.Replace("\"status\":\"candidate\"", $"\"status\":\"published\",\"publishedAt\":\"{Minute(2)}\",\"url\":\"https://example.invalid/releases/{version}\"", StringComparison.Ordinal)));
    }
}
