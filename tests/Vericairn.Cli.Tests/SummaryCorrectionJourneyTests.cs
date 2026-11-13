// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Cli.Tests;

/// <summary>
/// A document revision's summary is corrected without changing its bytes: a whole-file document by a successor record that pins the
/// same path, hash and size, a converted history by docs pin --render-template. The original signed record stays, and a finding on it
/// is reported as historical, naming its successor, while a finding on the current record is reported as it always was.
/// </summary>
[Collection("cli")]
public sealed class SummaryCorrectionJourneyTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private readonly string root = CliHarness.TempDirectory("summary-correction");
    private readonly string scratch = CliHarness.TempDirectory("summary-correction-scratch");

    [Fact]
    public void AWholeFileSummaryIsCorrectedByASuccessorAndTheOldFindingIsHistorical()
    {
        string op = Init();
        Write("docs/guide.md", "# Guide\n\nHow the widget works.\n");
        string first = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "docs/guide.md", "--by", op, "--summary", "Describe the widget in Revision2 form.").Id;
        string firstPath = Path.Combine(root, Record(first).ExpectedPath);
        byte[] original = File.ReadAllBytes(firstPath);
        Assert.NotEmpty(Record(first).Signatures);
        JsonElement finding = Assert.Single(Spacing());
        Assert.StartsWith(Record(first).ExpectedPath + "#body.summary", finding.GetProperty("Location").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Historical", finding.GetProperty("Message").GetString(), StringComparison.Ordinal);

        // docs pin names the route: --render-template needs a template instance or a converted history, and unchanged bytes refuse.
        Assert.Contains("--render-template needs an estate template instance or a converted revision history", CliHarness.Ok("docs", "pin", "--help").Out, StringComparison.Ordinal);
        CliHarness.Outcome rendered = CliHarness.Run("--estate", root, "docs", "pin", "docs/guide.md", "--by", op, "--summary", "Corrected.", "--render-template");
        Assert.Equal(2, rendered.Code);
        Assert.Contains("Template rendering requires an existing estate template instance or a converted revision history, and docs/guide.md is neither.", rendered.Err, StringComparison.Ordinal);
        Assert.Contains("records new document-revision", rendered.Err, StringComparison.Ordinal);
        CliHarness.Outcome unchanged = CliHarness.Run("--estate", root, "docs", "pin", "docs/guide.md", "--by", op, "--summary", "Corrected.");
        Assert.Equal(2, unchanged.Code);
        Assert.Contains($"has unchanged bytes; nothing to pin (current pin {first}).", unchanged.Err, StringComparison.Ordinal);
        Assert.Contains("--supersedes naming the current pin", unchanged.Err, StringComparison.Ordinal);

        // The recipe: read the current pin, check the file's hash, size and path against it, and write the successor.
        string second = Correct(op, first, "Describe the widget in revision 2 form.");
        JsonElement corrected = CliHarness.Ok("--estate", root, "--json", "records", "show", second).Json.GetProperty("body");
        Assert.Equal(2, corrected.GetProperty("revision").GetInt64());
        Assert.Equal("Describe the widget in revision 2 form.", corrected.GetProperty("summary").GetString());
        Assert.Equal(original, File.ReadAllBytes(firstPath));
        Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);

        // The original finding stays, every occurrence of it, now marked historical and naming the successor; it is still inspectable.
        finding = Assert.Single(Spacing());
        Assert.StartsWith(Record(first).ExpectedPath + "#body.summary", finding.GetProperty("Location").GetString(), StringComparison.Ordinal);
        Assert.Contains($"Historical: document-revision {first} is superseded by document-revision {second}", finding.GetProperty("Message").GetString(), StringComparison.Ordinal);
        Assert.Contains("'Revision2'", finding.GetProperty("Message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Revision2", CliHarness.Ok("--estate", root, "records", "show", first).Out, StringComparison.Ordinal);

        // A new current occurrence is reported as current, never hidden behind the historical one.
        string third = Correct(op, second, "Describe the widget again3.");
        JsonElement[] findings = Spacing();
        Assert.Equal(2, findings.Length);
        JsonElement current = Assert.Single(findings, f => f.GetProperty("Location").GetString()!.StartsWith(Record(third).ExpectedPath, StringComparison.Ordinal));
        Assert.DoesNotContain("Historical", current.GetProperty("Message").GetString(), StringComparison.Ordinal);
        Assert.Contains($"superseded by document-revision {second}", Assert.Single(findings, f => f.GetProperty("Location").GetString()!.StartsWith(Record(first).ExpectedPath, StringComparison.Ordinal)).GetProperty("Message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("none")]
    [InlineData("hash")]
    [InlineData("bytes")]
    public void AStaleOrForkedPredecessorOrOtherBytesAreRefusedAndNothingIsWritten(string fault)
    {
        string op = Init();
        Write("docs/guide.md", "# Guide\n\nFirst text.\n");
        string first = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "docs/guide.md", "--by", op, "--summary", "First text.").Id;
        Write("docs/guide.md", "# Guide\n\nSecond text.\n");
        string second = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "docs/guide.md", "--by", op, "--summary", "Second text.").Id;
        JsonElement pin = CliHarness.Ok("--estate", root, "--json", "records", "show", second).Json.GetProperty("body");
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "docs", "guide.md"));
        string hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(hash, pin.GetProperty("hash").GetString());

        // A successor of the stale first pin forks the document; one of none claims a first revision it is not; other bytes are not the file's.
        long revision = fault == "stale" ? 2 : fault == "none" ? 1 : 3;
        string claimed = fault == "hash" ? "sha256:" + Convert.ToHexStringLower(SHA256.HashData("# Guide\n\nFirst text.\n"u8)) : hash;
        long size = fault == "bytes" ? bytes.Length + 1 : bytes.Length;
        string body = CliHarness.WriteJson(scratch, "successor.json",
            $$"""{"path":"docs/guide.md","title":"guide","hash":"{{claimed}}","bytes":{{size}},"mediaType":"text/markdown","revision":{{revision}},"summary":"Corrected summary."}""");
        List<string> args = ["--estate", root, "--json", "records", "new", "document-revision", "--by", op, "--body", body];
        if (fault != "none") args.AddRange(["--supersedes", fault == "stale" ? first : second]);
        int before = CommandContext.LoadCopy(root).Records.Count;

        CliHarness.Outcome refused = CliHarness.Run([.. args]);

        Assert.NotEqual(0, refused.Code);
        string code = fault is "hash" or "bytes" ? "document.pin-content" : "document.multiple";
        Assert.Contains(refused.Json.GetProperty("findings").EnumerateArray(), f => f.GetProperty("Code").GetString() == code);
        Assert.Equal(before, CommandContext.LoadCopy(root).Records.Count);
        Assert.Equal(second, CommandContext.LoadCopy(root).Current("document-revision").Single().Id);
    }

    [Fact]
    public void AConvertedHistoryCorrectsItsSummaryWithRenderTemplateAndUnchangedBytes()
    {
        string op = Init();
        Write("design/example.md", "# Example\n\nBody.\n");
        string converted = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "design/example.md", "--by", op, "--summary", "Start the history in Revision1 form.", "--convert-revisions").Id;
        string file = Path.Combine(root, "design", "example.md");
        string authored = File.ReadAllText(file);
        byte[] record = File.ReadAllBytes(Path.Combine(root, Record(converted).ExpectedPath));

        string corrected = CliHarness.Ok("--estate", root, "--json", "docs", "pin", "design/example.md", "--by", op, "--summary", "Start the history.", "--render-template").Id;

        Assert.Equal("Start the history.", CliHarness.Ok("--estate", root, "--json", "records", "show", corrected).Json.GetProperty("body").GetProperty("summary").GetString());
        Assert.Equal(record, File.ReadAllBytes(Path.Combine(root, Record(converted).ExpectedPath)));
        Assert.Contains("Start the history.", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(authored.Split("<!-- vericairn:end revisions -->")[1], File.ReadAllText(file).Split("<!-- vericairn:end revisions -->")[1]);
        Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        JsonElement finding = Assert.Single(Spacing(), f => f.GetProperty("Location").GetString()!.StartsWith(Record(converted).ExpectedPath, StringComparison.Ordinal));
        Assert.Contains($"superseded by document-revision {corrected}", finding.GetProperty("Message").GetString(), StringComparison.Ordinal);
    }

    public void Dispose() => CliHarness.Delete(root, scratch);

    // The recipe of the living-documents guide: the current pin's own path, title, hash, bytes and media type, checked against the file.
    private string Correct(string by, string current, string summary)
    {
        JsonElement pin = CliHarness.Ok("--estate", root, "--json", "records", "show", current).Json.GetProperty("body");
        string path = pin.GetProperty("path").GetString()!;
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(pin.GetProperty("hash").GetString(), "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(pin.GetProperty("bytes").GetInt64(), bytes.LongLength);
        string body = CliHarness.WriteJson(scratch, "successor-" + current + ".json", JsonSerializer.Serialize(new
        {
            path,
            title = pin.GetProperty("title").GetString(),
            hash = pin.GetProperty("hash").GetString(),
            bytes = pin.GetProperty("bytes").GetInt64(),
            mediaType = pin.GetProperty("mediaType").GetString(),
            revision = pin.GetProperty("revision").GetInt64() + 1,
            summary,
        }));
        return CliHarness.Ok("--estate", root, "--json", "records", "new", "document-revision", "--by", by, "--body", body, "--supersedes", current).Id;
    }

    private JsonElement[] Spacing() => CliHarness.Run("--estate", root, "--json", "estate", "check").Json.GetProperty("findings").EnumerateArray()
        .Where(f => f.GetProperty("Code").GetString() == "writing.spacing").ToArray();

    private RecordEnvelope Record(string id) => CommandContext.LoadCopy(root).ById[id];

    // The operator's admitted device signs every record it authors here.
    private string Init()
    {
        string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Summary corrections.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
        string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator laptop", "--custody", "file").Id;
        CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "Root device.");
        return op;
    }

    private void Write(string path, string text)
    {
        string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}
