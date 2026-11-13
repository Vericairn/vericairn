// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Cli.Tests;

/// <summary>
/// A clearance refused because a declared check failed names what failed: for links-resolve, the document's repository-relative path
/// and each link that does not resolve, with its line, column and reason, in the refusal's text and as its structured child findings.
/// The refusal and the clearance rules are unchanged.
/// </summary>
[Collection("cli")]
public sealed class DocumentCheckDetailTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";
    private readonly string root = CliHarness.TempDirectory("check-detail");
    private readonly string scratch = CliHarness.TempDirectory("check-detail-scratch");

    [Fact]
    public void ALinkOutsideTheRepositoryIsNamedWithItsLineColumnAndReason()
    {
        string reviewer = Initialize();
        Write("docs/guide.md", "# Guide\n\nSee [reference](../../other-repo/example.md).\n");

        CliHarness.Outcome refused = Review(reviewer, "current");

        Assert.Equal(1, refused.Code);
        JsonElement[] findings = Findings(refused);
        JsonElement failed = Assert.Single(findings, f => f.GetProperty("Code").GetString() == "document.check-failed");
        Assert.Contains("(links-resolve: Links in docs/guide.md that do not resolve within the repository (1): ../../other-repo/example.md at line 3, column 17, which leads outside the repository.)",
            failed.GetProperty("Message").GetString(), StringComparison.Ordinal);
        JsonElement detail = Assert.Single(findings, f => f.GetProperty("Code").GetString() == "document.check-detail");
        Assert.Equal("docs/guide.md:3:17", detail.GetProperty("Location").GetString());
        Assert.Equal("links-resolve: ../../other-repo/example.md does not resolve, which leads outside the repository.", detail.GetProperty("Message").GetString());
        Assert.Equal("info", detail.GetProperty("severity").GetString());
        Assert.DoesNotContain(root, refused.Out, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, Row().GetProperty("clearance").ValueKind);

        // The text refusal names the same document, link and reason.
        CliHarness.Outcome text = CliHarness.Run("--estate", root, "docs", "review", "docs/guide.md", "--by", reviewer, "--outcome", "current", "--summary", "Read it.");
        Assert.Equal(1, text.Code);
        Assert.Contains("document.check-detail docs/guide.md:3:17: links-resolve: ../../other-repo/example.md does not resolve, which leads outside the repository.", text.Out + text.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFailingLinkOfADocumentIsNamedAndAValidLinkIsNot()
    {
        string reviewer = Initialize();
        Write("docs/overview.md", "# Overview\n");
        Write("docs/guide.md", "# Guide\n\nSee [the overview](overview.md) and [the plan](plan.md).\n[Elsewhere](../../sibling/readme.md) and [a section](sub/missing.md#part).\n");

        CliHarness.Outcome refused = Review(reviewer, "current");

        Assert.Equal(1, refused.Code);
        JsonElement[] details = Findings(refused).Where(f => f.GetProperty("Code").GetString() == "document.check-detail").ToArray();
        Assert.Equal(["docs/guide.md:3:48", "docs/guide.md:4:13", "docs/guide.md:4:54"], details.Select(d => d.GetProperty("Location").GetString()));
        Assert.Equal("links-resolve: plan.md does not resolve, which names docs/plan.md, not in the repository.", details[0].GetProperty("Message").GetString());
        Assert.Equal("links-resolve: ../../sibling/readme.md does not resolve, which leads outside the repository.", details[1].GetProperty("Message").GetString());
        Assert.Equal("links-resolve: sub/missing.md#part does not resolve, which names docs/sub/missing.md, not in the repository.", details[2].GetProperty("Message").GetString());
        Assert.DoesNotContain("overview.md", string.Join("\n", details.Select(d => d.GetProperty("Message").GetString())), StringComparison.Ordinal);
        string message = Assert.Single(Findings(refused), f => f.GetProperty("Code").GetString() == "document.check-failed").GetProperty("Message").GetString()!;
        Assert.Contains("do not resolve within the repository (3): plan.md at line 3, column 48, which names docs/plan.md, not in the repository;", message, StringComparison.Ordinal);

        // The clearance rule is unchanged: needs-change is recorded with the failed check, and it clears nothing.
        string recorded = CliHarness.Ok("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", reviewer, "--outcome", "needs-change", "--summary", "Three links are broken.").Id;
        JsonElement check = Assert.Single(CliHarness.Ok("--estate", root, "--json", "records", "show", recorded).Json.GetProperty("body").GetProperty("checks").EnumerateArray());
        Assert.Equal("failed", check.GetProperty("outcome").GetString());
        Assert.StartsWith("Links in docs/guide.md that do not resolve within the repository (3): ", check.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, Row().GetProperty("clearance").ValueKind);

        // Once every link resolves, the same review clears it.
        Write("docs/plan.md", "# Plan\n");
        Write("docs/sub/missing.md", "# Part\n");
        Write("docs/guide.md", "# Guide\n\nSee [the overview](overview.md) and [the plan](plan.md).\n[A section](sub/missing.md#part).\n");
        string cleared = CliHarness.Ok("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", reviewer, "--outcome", "current", "--summary", "Every link resolves.").Id;
        Assert.Equal(cleared, Row().GetProperty("clearance").GetString());
    }

    [Fact]
    public void ManyFailingLinksAreCountedInTheSummaryAndEachIsAChildFinding()
    {
        string reviewer = Initialize();
        Write("docs/guide.md", "# Guide\n\n" + string.Concat(Enumerable.Range(1, 25).Select(i => $"[Part {i}](missing-{i}.md)\n")));

        CliHarness.Outcome refused = Review(reviewer, "current");

        Assert.Equal(1, refused.Code);
        Assert.Equal(25, Findings(refused).Count(f => f.GetProperty("Code").GetString() == "document.check-detail"));
        Assert.Equal("docs/guide.md:27:11", Findings(refused).Last().GetProperty("Location").GetString());
        string message = Assert.Single(Findings(refused), f => f.GetProperty("Code").GetString() == "document.check-failed").GetProperty("Message").GetString()!;
        Assert.Contains("(25): missing-1.md at line 3, column 10,", message, StringComparison.Ordinal);
        // The summary names the first links that fit a review's summary and counts the rest.
        int named = message.Split(" at line ").Length - 1;
        Assert.InRange(named, 1, 20);
        Assert.Contains($"; and {25 - named} more, each named by a refused clearance of the document.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-25.md", message, StringComparison.Ordinal);
    }

    public void Dispose() => CliHarness.Delete(root, scratch);

    private string Initialize()
    {
        string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Check details.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
        string reviewer = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, "reviewer.json", """{"kind":"agent","name":"Reviewer","roles":["reviewer"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
        string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;
        CliHarness.Ok("--estate", root, "docs", "declare", "--by", op, "--body", CliHarness.WriteJson(scratch, "declaration.json",
            $$"""{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"refuse","product":"{{product}}"}],"checks":["links-resolve"],"clearedBy":"any","summary":"Describes the widget."}"""));
        return reviewer;
    }

    private CliHarness.Outcome Review(string reviewer, string outcome) =>
        CliHarness.Run("--estate", root, "--json", "docs", "review", "docs/guide.md", "--by", reviewer, "--outcome", outcome, "--summary", "Read it.");

    private static JsonElement[] Findings(CliHarness.Outcome outcome) => outcome.Json.GetProperty("findings").EnumerateArray().ToArray();

    private JsonElement Row() =>
        CliHarness.Ok("--estate", root, "--json", "docs", "living").Json.EnumerateArray().Single(r => r.GetProperty("path").GetString() == "docs/guide.md");

    private void Write(string path, string text)
    {
        string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}
