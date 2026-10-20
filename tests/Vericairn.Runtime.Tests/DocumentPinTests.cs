// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class DocumentPinTests
{
    private const string Document = "docs/guide.md";
    private static readonly string[] GitOptions = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void FirstAndNextPinsPreserveExactBytesAndCarryTheTitle()
    {
        using var estate = new TestEstate(recordFormat: 3);
        using EstateService service = EstateService.Open(estate.Root);
        WriteOutcome device = service.DeviceCreate(new(estate.OperatorId, "Test author", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        Assert.True(service.DeviceAdmit(new(device.Written!.Id, estate.OperatorId, "Test signing.")).Ok);
        byte[] firstBytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# Café\r\n\r\nFirst.\r\n")];
        estate.WriteFile(Document, "");
        File.WriteAllBytes(Path.Combine(estate.Root, Document), firstBytes);
        DocumentPinResponse first = service.DocumentPin(new(Document, estate.OperatorId, "First edition.", "The guide"));
        RecordEnvelope initial = AssertPin(estate, first, firstBytes, 1, "The guide");
        Assert.Null(initial.Supersedes);
        Assert.NotEmpty(initial.Signatures);
        byte[] initialRecord = File.ReadAllBytes(Path.Combine(estate.Root, first.Written!.Path));

        byte[] nextBytes = Encoding.UTF8.GetBytes("# Café\n\nSecond.\n");
        File.WriteAllBytes(Path.Combine(estate.Root, Document), nextBytes);
        DocumentPinResponse next = service.DocumentPin(new(Document, estate.OperatorId, "Second edition.", Expected: first.Written.Id));
        RecordEnvelope revised = AssertPin(estate, next, nextBytes, 2, "The guide");
        Assert.Equal(initial.Id, revised.Supersedes);
        Assert.Equal(initialRecord, File.ReadAllBytes(Path.Combine(estate.Root, first.Written.Path)));
        Assert.False(service.Record(initial.Id)!.Current);
        Assert.Equal(revised.Id, Assert.Single(service.Records("document-revision")).Id);
        Assert.Equal(0, service.Check().Errors);

        estate.WriteFile(Document, "# Renamed\n");
        DocumentPinResponse renamed = service.DocumentPin(new(Document, estate.OperatorId, "Rename the guide.", "Renamed guide"));
        AssertPin(estate, renamed, File.ReadAllBytes(Path.Combine(estate.Root, Document)), 3, "Renamed guide");
    }

    [Theory]
    [InlineData("docs/readme.md", "text/markdown")]
    [InlineData("design/layout.JSON", "application/json")]
    [InlineData("docs/manual.pdf", "application/pdf")]
    [InlineData("docs/source.txt", "text/plain")]
    [InlineData("docs/data.bin", "application/octet-stream")]
    public void FirstTitleAndMediaTypeComeFromTheFileName(string path, string mediaType)
    {
        using var estate = new TestEstate();
        estate.WriteFile(path, "document bytes\n");
        using EstateService service = EstateService.Open(estate.Root);
        DocumentPinResponse result = service.DocumentPin(new(path, estate.OperatorId, "First pin."));
        Assert.True(result.Ok, result.Problem?.Message);
        JsonElement body = service.Record(result.Written!.Id)!.Envelope.GetProperty("body");
        Assert.Equal(Path.GetFileNameWithoutExtension(path), body.GetProperty("title").GetString());
        Assert.Equal(mediaType, body.GetProperty("mediaType").GetString());
    }

    [Fact]
    public void UnchangedBytesAndStaleOrUnrelatedExpectedPinsWriteNothing()
    {
        using var estate = new TestEstate();
        estate.WriteFile(Document, "# First\n");
        using EstateService service = EstateService.Open(estate.Root);
        DocumentPinResponse first = service.DocumentPin(new(Document, estate.OperatorId, "First pin."));
        Assert.True(first.Ok, first.Problem?.Message);
        AssertRefusedUnchanged(estate, service, new(Document, estate.OperatorId, "Only metadata changes.", "Different title"), "unchanged bytes");
        estate.WriteFile(Document, "# Second\n");
        DocumentPinResponse second = service.DocumentPin(new(Document, estate.OperatorId, "Second pin.", Expected: first.Written!.Id));
        Assert.True(second.Ok, second.Problem?.Message);
        estate.WriteFile(Document, "# Third\n");
        AssertRefusedUnchanged(estate, service, new(Document, estate.OperatorId, "Stale pin.", Expected: first.Written.Id), second.Written!.Id);
        AssertRefusedUnchanged(estate, service, new(Document, estate.OperatorId, "Unrelated pin.", Expected: estate.Estate.Id), "not current");
        AssertRefusedUnchanged(estate, service, new(Document, estate.OperatorId, "Unknown pin.", Expected: Ulid.NewUlid().Value), "not current");
        estate.WriteFile("docs/new.md", "# New\n");
        AssertRefusedUnchanged(estate, service, new("docs/new.md", estate.OperatorId, "No pin yet.", Expected: second.Written.Id), "current pin: none");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("records/probe.json")]
    [InlineData("docs/../outside.md")]
    [InlineData("docs/./guide.md")]
    [InlineData("docs//guide.md")]
    [InlineData("docs/guide.md/")]
    [InlineData("docs\\guide.md")]
    [InlineData("docs/GUIDE.md")]
    [InlineData("docs/missing.md")]
    [InlineData("docs/directory.md")]
    public void InvalidOrUnavailablePathsWriteNothing(string path)
    {
        using var estate = new TestEstate();
        estate.WriteFile(Document, "# Guide\n");
        Directory.CreateDirectory(Path.Combine(estate.Root, "docs", "directory.md"));
        using EstateService service = EstateService.Open(estate.Root);
        AssertRefusedUnchanged(estate, service, new(path, estate.OperatorId, "Refuse this path."));
        AssertRefusedUnchanged(estate, service, new(Path.Combine(estate.Root, Document), estate.OperatorId, "Refuse an absolute path."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedDocumentsAndDirectoriesAreNotReadAsEstateDocuments(bool directory)
    {
        using var estate = new TestEstate();
        using var outside = new TestEstate();
        outside.WriteFile("docs/source.md", "# Outside document\n");
        estate.WriteFile(Document, "# Guide\n");
        string link = Path.Combine(estate.Root, "docs", directory ? "linked" : "linked.md");
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, Path.Combine(outside.Root, "docs"));
            else File.CreateSymbolicLink(link, Path.Combine(outside.Root, "docs", "source.md"));
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Assert.Skip("Creating Windows symbolic links requires Developer Mode or the symlink privilege.");
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            Assert.Skip("Creating Windows symbolic links requires Developer Mode or the symlink privilege.");
        }
        using EstateService service = EstateService.Open(estate.Root);
        AssertRefusedUnchanged(estate, service, new(directory ? "docs/linked/source.md" : "docs/linked.md", estate.OperatorId, "Refuse a linked document."), "without symbolic links");
    }

    [Fact]
    public void InvalidAuthorSummaryTitleAndExpectedIdWriteNothing()
    {
        using var estate = new TestEstate();
        estate.WriteFile(Document, "# Guide\n");
        using EstateService service = EstateService.Open(estate.Root);
        var request = new DocumentPinRequest(Document, estate.OperatorId, "Valid summary.");
        foreach (DocumentPinRequest invalid in new[]
        {
            request with { By = "not-an-id" }, request with { Summary = " " }, request with { Title = " " },
            request with { Expected = "not-an-id" }, request with { Summary = new string('a', 4001) },
        }) AssertRefusedUnchanged(estate, service, invalid);
    }

    [Fact]
    public void GeneratedDocumentsAndRegisterMembersWriteNothing()
    {
        using var fixture = new GeneratedWritingFixture();
        using EstateService service = EstateService.Open(fixture.Estate.Root);
        foreach (string path in fixture.GeneratedPaths)
            AssertRefusedUnchanged(fixture.Estate, service, new(path, fixture.Estate.OperatorId, "No generated pin."), "generated document");
        DocumentPinResponse authored = service.DocumentPin(new(Document, fixture.Estate.OperatorId, "Pin authored prose with generated regions."));
        Assert.True(authored.Ok, JsonSerializer.Serialize(authored.Problem));
    }

    [Fact]
    public void LegacyGeneratedDeclarationsAlsoRefuseAnAuthoredPin()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("living-document", estate.OperatorId,
            """{"path":"docs/guide.md","title":"Generated guide","kind":"generated","source":"estate-file","generator":{"id":"vericairn.records/releases","inputs":[]},"triggers":[{"on":"decision","level":"warn"}],"summary":"A generated file."}""");
        estate.WriteFile(Document, "# Generated\n");
        Assert.False(estate.Load().Findings.HasErrors);
        using EstateService service = EstateService.Open(estate.Root);
        AssertRefusedUnchanged(estate, service, new(Document, estate.OperatorId, "No legacy generated pin."), "generated document");
    }

    [Fact]
    public void APinThatMovesDuringValidationCannotBeSilentlySuperseded()
    {
        using var estate = new TestEstate();
        estate.WriteFile(Document, "# First\n");
        using EstateService service = EstateService.Open(estate.Root);
        DocumentPinResponse first = service.DocumentPin(new(Document, estate.OperatorId, "First pin."));
        Assert.True(first.Ok, first.Problem?.Message);
        estate.WriteFile(Document, "# Second\n");
        DocumentPinResponse? competing = null;
        service.BeforeValidatingRecords = _ =>
        {
            using EstateService other = EstateService.Open(estate.Root);
            competing = other.DocumentPin(new(Document, estate.OperatorId, "Competing pin.", Expected: first.Written!.Id));
            Assert.True(competing.Ok, competing.Problem?.Message);
        };
        DocumentPinResponse refused = service.DocumentPin(new(Document, estate.OperatorId, "Losing pin.", Expected: first.Written!.Id));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Null(refused.Revision);
        Assert.Equal("rejected", refused.Problem!.Code);
        Assert.Equal(competing!.Written!.Id, Assert.Single(service.Records("document-revision")).Id);
        Assert.Equal(2, estate.Load().OfType("document-revision").Count());
    }

    [Fact]
    public void TwoClonesPinningOneRevisionLeaveAForkUntilReconciled()
    {
        using var estate = new TestEstate();
        estate.WriteFile(Document, "# Initial\n");
        RecordWritten first;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            DocumentPinResponse response = service.DocumentPin(new(Document, estate.OperatorId, "Initial pin.", "Guide"));
            Assert.True(response.Ok, response.Problem?.Message);
            first = response.Written!;
        }
        Git(estate.Root, "init", "--initial-branch=main");
        Git(estate.Root, "add", "estate", "records", "docs");
        Git(estate.Root, "commit", "-m", "Initial document pin");
        using var cloneA = new TestEstate();
        using var cloneB = new TestEstate();
        string a = Path.Combine(cloneA.Root, "clone");
        string b = Path.Combine(cloneB.Root, "clone");
        Git(cloneA.Root, "clone", "--no-hardlinks", estate.Root, a);
        Git(cloneB.Root, "clone", "--no-hardlinks", estate.Root, b);
        Git(a, "checkout", "-b", "pin-a");
        Git(b, "checkout", "-b", "pin-b");
        RecordWritten fromA = PinBranch(a, estate.OperatorId, first.Id, "Branch A.");
        RecordWritten fromB = PinBranch(b, estate.OperatorId, first.Id, "Branch B.");
        Git(a, "fetch", b, "pin-b");
        Git(a, "merge", "--no-edit", "FETCH_HEAD");

        using EstateService merged = EstateService.Open(a);
        FindingsResponse check = merged.Check();
        Assert.Contains(check.Findings, f => f.Code == "reconcile.successors" && f.Message.Contains(first.Id, StringComparison.Ordinal)
            && f.Message.Contains(fromA.Id, StringComparison.Ordinal) && f.Message.Contains(fromB.Id, StringComparison.Ordinal));
        Assert.Contains(check.Findings, f => f.Code == "document.multiple" && f.Message.Contains(fromA.Id, StringComparison.Ordinal)
            && f.Message.Contains(fromB.Id, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(a, Document), "# Reconciled\n");
        var before = Snapshot(a);
        DocumentPinResponse refused = merged.DocumentPin(new(Document, estate.OperatorId, "Fork must be reconciled.", Expected: fromA.Id));
        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        Assert.Contains("forked pins", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains(fromA.Id, refused.Problem.Message, StringComparison.Ordinal);
        Assert.Contains(fromB.Id, refused.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(a));
        WriteOutcome reconciliation = merged.NewRecord(new("reconciliation", estate.OperatorId,
            JsonSerializer.SerializeToElement(new { kept = fromA.Id, withdrawn = new[] { fromB.Id }, note = "Keep A and carry the combined text into the next pin." }),
            "document-revision:" + first.Id));
        Assert.True(reconciliation.Ok, reconciliation.Problem?.Message);
        DocumentPinResponse next = merged.DocumentPin(new(Document, estate.OperatorId, "Combined revision.", Expected: fromA.Id));
        Assert.True(next.Ok, next.Problem?.Message);
        Assert.Equal(3, next.Revision);
        Assert.Equal(fromA.Id, merged.Record(next.Written!.Id)!.Envelope.GetProperty("supersedes").GetString());
        Assert.Equal(0, merged.Check().Errors);
        Assert.Equal(4, merged.Session.Load().OfType("document-revision").Count());
    }

    private static RecordWritten PinBranch(string root, string by, string expected, string summary)
    {
        File.WriteAllText(Path.Combine(root, Document), "# Shared next bytes\n");
        using EstateService service = EstateService.Open(root);
        DocumentPinResponse response = service.DocumentPin(new(Document, by, summary, Expected: expected));
        Assert.True(response.Ok, response.Problem?.Message);
        Git(root, "add", "docs", "records");
        Git(root, "commit", "-m", "Pin the next document revision");
        return response.Written!;
    }

    private static RecordEnvelope AssertPin(TestEstate estate, DocumentPinResponse response, byte[] bytes, long revision, string title)
    {
        Assert.True(response.Ok, JsonSerializer.Serialize(response.Problem));
        Assert.Equal(revision, response.Revision);
        RecordEnvelope pin = estate.Load().ById[response.Written!.Id];
        Assert.Equal(response.Written.Hash, pin.ComputedHash);
        Assert.Equal(ContentHash.Sha256(bytes), pin.Body.GetProperty("hash").GetString());
        Assert.Equal(bytes.LongLength, pin.Body.GetProperty("bytes").GetInt64());
        Assert.Equal(revision, pin.Body.GetProperty("revision").GetInt64());
        Assert.Equal(title, pin.Body.GetProperty("title").GetString());
        Assert.Equal("text/markdown", pin.Body.GetProperty("mediaType").GetString());
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(estate.Root, Document)));
        return pin;
    }

    private static void AssertRefusedUnchanged(TestEstate estate, EstateService service, DocumentPinRequest request, string? message = null)
    {
        var before = Snapshot(estate.Root);
        DocumentPinResponse response = service.DocumentPin(request);
        Assert.False(response.Ok);
        Assert.Null(response.Written);
        Assert.Null(response.Revision);
        Assert.NotNull(response.Problem);
        if (message is not null) Assert.Contains(message, response.Problem.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(estate.Root));
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*", SearchOption.AllDirectories))
        .Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), ContentHash.Sha256File, StringComparer.Ordinal);

    private static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string value in GitOptions.Concat(args)) start.ArgumentList.Add(value);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output + error);
    }
}
