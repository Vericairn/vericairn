// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class EstateChecksTests
{
    [Fact]
    public void CleanEstatePassesChecks()
    {
        using var estate = new TestEstate();
        FindingList checks = EstateChecks.Run(estate.Load());
        Assert.False(checks.HasErrors, string.Join("\n", checks));
    }

    [Fact]
    public void AbsolutePathsSecretsAndStrayFilesAreReported()
    {
        using var estate = new TestEstate();
        estate.WriteFile("docs/notes.md", "See /Users/someone/project for details.\n");
        estate.WriteFile("design/plan.md", "token: ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij123456\n");
        estate.WriteFile("scratch/output.log", "noise\n");
        FindingList checks = EstateChecks.Run(estate.Load());
        Assert.Contains(checks, f => f.Code == "portability.absolute-path" && f.Location == "docs/notes.md:1");
        Assert.Contains(checks, f => f.Code == "secrets.pattern" && f.Location == "design/plan.md:1");
        Assert.Contains(checks, f => f.Code == "layout.unexpected" && f.Location == "scratch");
    }

    [Fact]
    public void DocumentRevisionsDetectDrift()
    {
        using var estate = new TestEstate();
        estate.WriteFile("design/master.md", "# Master\n");
        string hash = ContentHash.Sha256File(Path.Combine(estate.Root, "design", "master.md"));
        estate.Add("document-revision", estate.BuilderId, $$"""{"path":"design/master.md","title":"Master","hash":"{{hash}}","bytes":9,"mediaType":"text/markdown","revision":1,"summary":"First."}""");
        Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code == "document.drift");
        estate.WriteFile("design/master.md", "# Master (edited)\n");
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.drift");
    }

    [Fact]
    public void ARetiredDocumentMayBeRemovedWithoutBeingMissing()
    {
        using var estate = new TestEstate();
        estate.WriteFile("docs/temp.md", "temporary\n");
        string hash = ContentHash.Sha256File(Path.Combine(estate.Root, "docs", "temp.md"));
        estate.Add("document-revision", estate.BuilderId, $$"""{"path":"docs/temp.md","title":"Temp","hash":"{{hash}}","bytes":10,"mediaType":"text/markdown","revision":1,"summary":"Temporary."}""");
        RecordEnvelope pending = estate.Add("retirement", estate.BuilderId, """{"path":"docs/temp.md","reason":"Working document.","condition":"When accepted."}""");
        File.Delete(Path.Combine(estate.Root, "docs", "temp.md"));
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "document.missing");
        estate.Add("retirement", estate.BuilderId, """{"path":"docs/temp.md","reason":"Working document.","condition":"When accepted.","retiredAt":"2026-10-13T16:00:00Z"}""", supersedes: pending.Id);
        FindingList checks = EstateChecks.Run(estate.Load());
        Assert.DoesNotContain(checks, f => f.Code == "document.missing");
        Assert.Contains(checks, f => f.Code == "document.retired");
    }

    [Fact]
    public void RetirementsAreEnforced()
    {
        using var estate = new TestEstate();
        estate.WriteFile("docs/bootstrap-notes.md", "temporary\n");
        RecordEnvelope pending = estate.Add("retirement", estate.BuilderId, """{"path":"docs/bootstrap-notes.md","reason":"Bootstrap notes.","condition":"When the CLI exists."}""");
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "retirement.pending");
        estate.Add("retirement", estate.BuilderId, """{"path":"docs/bootstrap-notes.md","reason":"Bootstrap notes.","condition":"When the CLI exists.","retiredAt":"2026-10-13T13:00:00Z"}""", supersedes: pending.Id);
        Assert.Contains(EstateChecks.Run(estate.Load()), f => f.Code == "retirement.present");
        File.Delete(Path.Combine(estate.Root, "docs", "bootstrap-notes.md"));
        Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code.StartsWith("retirement.", StringComparison.Ordinal));
    }

    [Fact]
    public void WordsRunIntoNumbersAreFlaggedButIdentifiersAreNot()
    {
        using var estate = new TestEstate();
        estate.WriteFile("docs/notes.md", "On October13 we saved checkpoint2693.\nThe hash sha256:abc and `code42` and version 0.1.0 and net10.0 and base64 are fine; M0 and 01M4TV are keys.\n");
        FindingList checks = EstateChecks.Run(estate.Load());
        var spacing = checks.Where(f => f.Code == "writing.spacing").ToList();
        Assert.Equal(2, spacing.Count);
        Assert.All(spacing, f => Assert.Equal("docs/notes.md:1", f.Location));
    }

    [Fact]
    public void StatusJsonIsCanonicalAndStable()
    {
        using var estate = new TestEstate();
        EstateModel model = estate.Load();
        string first = EstateStatus.RenderJson(model, EstateChecks.Run(model));
        string second = EstateStatus.RenderJson(estate.Load(), EstateChecks.Run(model));
        Assert.Equal(first, second);
        Assert.EndsWith("}\n", first, StringComparison.Ordinal);
    }
}
