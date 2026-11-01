// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class RevisionPinMergeJourneyTests
{
    private static readonly string[] GitOptions = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];
    [Fact]
    public void TwoBranchesRegenerateMergedHistoryWithoutChoosingAnyRevisionNumber()
    {
        using var estate = new TestEstate(3);
        estate.WriteFile(RevisionPinFixture.Document, "# Example\n\nAlpha original.\n\nOne.\n\nTwo.\n\nThree.\n\nOmega original.\n");
        RecordEnvelope first;
        using (EstateService service = EstateService.Open(estate.Root)) first = RevisionPinFixture.Pin(service, estate.OperatorId, "Initial history.", convert: true);
        Git(estate.Root, "init", "--initial-branch=main");
        Git(estate.Root, "add", "estate", "records", "design");
        Git(estate.Root, "commit", "-m", "Initial document history");
        string basis = Git(estate.Root, "rev-parse", "HEAD").Trim();
        RecordEnvelope Branch(string name, string old, string revised)
        {
            Git(estate.Root, "checkout", "-b", name, basis);
            estate.WriteFile(RevisionPinFixture.Document, Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate)).Replace(old, revised, StringComparison.Ordinal));
            using EstateService service = EstateService.Open(estate.Root);
            RecordEnvelope pin = RevisionPinFixture.Pin(service, estate.OperatorId, name + " change.");
            Git(estate.Root, "add", "records", "design");
            Git(estate.Root, "commit", "-m", "Revise authored document content");
            return pin;
        }
        RecordEnvelope left = Branch("left", "Alpha original.", "Alpha revised.");
        RecordEnvelope right = Branch("right", "Omega original.", "Omega revised.");
        Assert.Equal(first.Id, left.Supersedes);
        Assert.Equal(first.Id, right.Supersedes);
        // Git can leave a textual conflict in the generated payload. Regeneration resolves it from both records,
        // including the authored changes Git merged, without selecting a line, a branch's number or a kept pin.
        Git(estate.Root, ["merge", "--no-edit", "left"], allowConflict: true);
        using EstateService merged = EstateService.Open(estate.Root);
        Assert.DoesNotContain(merged.Session.Load().Findings, f => f.Code is "reconcile.successors" or "document.multiple");
        RevisionPinFixture.Pin(merged, estate.OperatorId, "Merged authored content.", render: true);
        string text = Encoding.UTF8.GetString(RevisionPinFixture.Bytes(estate));
        Assert.Contains("Alpha revised.", text, StringComparison.Ordinal);
        Assert.Contains("Omega revised.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<<<<<<<", text, StringComparison.Ordinal);
        Assert.Contains("left change.", text, StringComparison.Ordinal);
        Assert.Contains("right change.", text, StringComparison.Ordinal);
        Assert.Equal(4, text.Split('\n').Count(l => l.StartsWith("Revision ", StringComparison.Ordinal)));
        EstateModel history = merged.Session.Load();
        Assert.Equal(new long[] { 2, 3 }, new[] { RevisionLines.Ordinal(history, left), RevisionLines.Ordinal(history, right) }.Order());
        Assert.DoesNotContain(merged.Check().Findings, f => f.Code is "document.drift" or "document.revisions" or "document.multiple");
        Git(estate.Root, "add", "records", "design");
        Git(estate.Root, "commit", "-m", "Regenerate the merged revision history");
    }

    internal static string Git(string root, params string[] arguments) => Git(root, arguments, false);
    private static string Git(string root, string[] arguments, bool allowConflict)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in GitOptions.Concat(arguments)) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 || allowConflict && process.ExitCode == 1 && output.Contains("CONFLICT", StringComparison.Ordinal), output + error);
        return output;
    }
}
