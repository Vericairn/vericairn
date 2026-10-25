// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

public sealed class DocumentPassTests
{
    private static readonly string[] DuePaths = ["README.md", "ROADMAP.md", "docs/guide.md", "docs/releases/README.md"];
    private static readonly string[] ReadmeCoverage = ["README.md", "src"];
    [Fact]
    public void PassListsOnlyThePublicationsRemainingDueDocumentsWithCoveredChangesAndRecordContext()
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        Assert.True(pass.Ok, pass.Problem?.Message);
        Assert.Equal(f.Second.Id, pass.Release!.Id);
        Assert.Equal(f.First.Id, pass.Previous!.Id);
        Assert.Equal(f.SecondCommit, pass.Commit);
        Assert.Equal(DuePaths, pass.Documents.Select(d => d.Path));
        Assert.Equal(f.Service.Documents().Where(d => d.Reasons.Any(r => r.On == "release" && r.Record == f.Second.Id)).Select(d => d.Declaration), pass.Documents.Select(d => d.Declaration.Id));
        DocumentPassItem readme = Assert.Single(pass.Documents, d => d.Path == "README.md");
        Assert.Equal(ReadmeCoverage, readme.CoveredPaths);
        Assert.Equal(new[] { new DocumentPassChange("src/added.txt", "added"), new DocumentPassChange("src/changed.txt", "modified"), new DocumentPassChange("src/deleted.txt", "deleted") }, readme.Changes);
        Assert.Contains(pass.Records, r => r.Record.Id == f.Decision.Id && r.Summary == "The guide explains the added command.");
        Assert.Equal(f.Decision.Id, Assert.Single(Assert.Single(pass.Documents, d => d.Declaration.Id == f.Guide.Id).Records).Record.Id);
        Assert.DoesNotContain(pass.Documents, d => d.Declaration.Id == f.Cleared.Id || d.Declaration.Id == f.Unrelated.Id);
        Assert.Equal("due", Assert.Single(f.Service.Documents(), d => d.Declaration == f.Unrelated.Id).State);
        Assert.Empty(f.Service.DocumentPass("0.1.0").Documents);
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    public void FrontPatchesApplyToExactBytesWithoutChangingSourcesRecordsCommitsOrFrozenSets(string newline, bool finalNewline)
    {
        using var f = new DocumentPassFixture(newline, finalNewline);
        int records = f.Estate.Load().Records.Count;
        string frozen = ContentHash.Sha256File(Path.Combine(f.Repository.Root, "docs/releases/0.1.0/README.md"));
        string index = ContentHash.Sha256File(Path.Combine(f.Repository.Root, "docs/releases/frozen-sha256.json"));
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        Assert.Equal(3, pass.Updates.Count);
        Assert.Equal(records, f.Estate.Load().Records.Count);
        Assert.Equal(f.SecondCommit, f.Git("rev-parse", "HEAD").Trim());
        Assert.Empty(f.Git("status", "--porcelain"));
        foreach (DocumentPassUpdate update in pass.Updates)
        {
            Assert.Equal(f.SecondCommit, update.BaseCommit);
            Assert.Equal(ContentHash.Sha256File(Path.Combine(f.Repository.Root, update.Path)), update.BeforeHash);
            f.GitInput(update.Patch, "apply", "--check", "-");
            f.GitInput(update.Patch, "apply", "-");
            Assert.Equal(update.AfterHash, ContentHash.Sha256File(Path.Combine(f.Repository.Root, update.Path)));
        }
        string readme = File.ReadAllText(Path.Combine(f.Repository.Root, "README.md"));
        Assert.Contains("The current published release is 0.2.0, from" + newline + "October 13, 2026.", readme, StringComparison.Ordinal);
        Assert.Equal(finalNewline, readme.EndsWith(newline, StringComparison.Ordinal));
        string roadmap = File.ReadAllText(Path.Combine(f.Repository.Root, "ROADMAP.md"));
        Assert.Contains("## 0.1.0 — previous published release", roadmap, StringComparison.Ordinal);
        Assert.Contains("## 0.2.0 — current published release", roadmap, StringComparison.Ordinal);
        Assert.Contains("New work.", roadmap, StringComparison.Ordinal);
        string releases = File.ReadAllText(Path.Combine(f.Repository.Root, "docs/releases/README.md"));
        Assert.Contains("The current published release is **0.2.0**", releases, StringComparison.Ordinal);
        Assert.Contains("[Documentation set](0.2.0/README.md)", releases, StringComparison.Ordinal);
        Assert.Contains("Historical details stay here.", releases, StringComparison.Ordinal);
        Assert.Equal(f.SecondCommit, f.Git("rev-parse", "HEAD").Trim());
        Assert.Equal(records, f.Estate.Load().Records.Count);
        Assert.Equal(frozen, ContentHash.Sha256File(Path.Combine(f.Repository.Root, "docs/releases/0.1.0/README.md")));
        Assert.Equal(index, ContentHash.Sha256File(Path.Combine(f.Repository.Root, "docs/releases/frozen-sha256.json")));
        // Committing here is an explicit fixture action, never an action of the pass.
        f.Git("add", "--all");
        f.Git("commit", "-m", "Apply reviewed fixture patches");
        Assert.Empty(f.Service.DocumentPass("0.2.0").Updates);
    }

    [Fact]
    public void ComparisonUsesPublicationCommitsEvenWhenCloneHeadHasLaterWork()
    {
        using var f = new DocumentPassFixture();
        f.Repository.WriteFile("src/unpublished.txt", "later work\n");
        f.Git("add", "--all");
        f.Git("commit", "-m", "Unpublished fixture work");
        string head = f.Git("rev-parse", "HEAD").Trim();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        Assert.DoesNotContain(pass.Documents.SelectMany(d => d.Changes), c => c.Path == "src/unpublished.txt");
        Assert.All(pass.Updates, u => Assert.Equal(head, u.BaseCommit));
        Assert.Equal(head, f.Git("rev-parse", "HEAD").Trim());
        Assert.Empty(f.Git("status", "--porcelain"));
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("builder")]
    [InlineData("person")]
    public void AChosenGroupBecomesAnOrdinaryReviewAssignmentWithExactSourcesAndChanges(string kind)
    {
        using var f = new DocumentPassFixture();
        string reviewer = kind switch
        {
            "operator" => f.Estate.OperatorId, "builder" => f.Estate.BuilderId,
            _ => f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Test Reviewer","roles":["reviewer"]}""").Id,
        };
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        DocumentPassPin[] selected = [.. pass.Documents.Where(d => d.Declaration.Id == f.Readme.Id || d.Declaration.Id == f.Guide.Id).Select(d => d.Declaration)];
        DocumentPassAssignResponse assigned = f.Service.DocumentPassAssign("0.2.0", new(f.Estate.OperatorId, reviewer, "DOCS-REVIEW-1", pass.Release!, selected));
        Assert.True(assigned.Ok, JsonSerializer.Serialize(assigned.Problem));
        Assert.Equal(2, assigned.Written.Count);
        EstateModel model = f.Estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        RecordEnvelope assignment = model.ById[assigned.Written[1].Id];
        Assert.Equal("assignment", assignment.Type);
        Assert.Equal(reviewer, EstateModel.BodyString(assignment, "contributor"));
        Assert.Equal(assigned.Written[0].Id, assignment.Subject!.Id);
        JsonElement brief = assignment.Body.GetProperty("brief");
        foreach (DocumentPassPin pin in selected.Append(pass.Release!))
            Assert.Contains(brief.GetProperty("sources").EnumerateArray(), s => s.GetProperty("id").GetString() == pin.Id && s.GetProperty("hash").GetString() == pin.Hash);
        Assert.Contains("src/deleted.txt", brief.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(f.Decision.Id, brief.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(f.Estate.Root, brief.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(f.Repository.Root, brief.GetRawText(), StringComparison.Ordinal);
        DocumentPassResponse after = f.Service.DocumentPass("0.2.0");
        Assert.Equal(pass.Documents.Select(d => d.Declaration), after.Documents.Select(d => d.Declaration));
        foreach (DocumentPassItem item in after.Documents)
            Assert.Equal(selected.Contains(item.Declaration) ? 1 : 0, item.Assignments.Count);
        Assert.True(f.Service.Attempt(new(assignment.Id, reviewer, "Fixture repository, review branch", "Read the assigned documents.")).Ok);
    }

    [Fact]
    public void AssignmentDoesNotClearDocumentsOrBypassIndependenceAndLaterClearanceRemovesOnlyThatDocument()
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        Assert.True(f.Service.DocumentPassAssign("0.2.0", Request(f, pass)).Ok);
        Assert.False(f.Service.DocumentReview(f.Readme.Id, new(f.Estate.BuilderId, "revised", "Changed it.", Commit: f.SecondCommit)).Ok);
        Assert.Equal(4, f.Service.DocumentPass("0.2.0").Documents.Count);
        Assert.True(f.Service.DocumentReview(f.Readme.Id, new(f.Estate.OperatorId, "current", "Checked it.", Commit: f.SecondCommit)).Ok);
        Assert.DoesNotContain(f.Service.DocumentPass("0.2.0").Documents, d => d.Declaration.Id == f.Readme.Id);
        Assert.Equal(3, f.Service.DocumentPass("0.2.0").Documents.Count);
        Assert.Contains(LivingDocuments.Gate(f.Estate.Load(), f.Product.Id), d => d.Declaration.Id == f.Guide.Id);
    }

    [Fact]
    public void InvalidStaleAndUnauthorizedSelectionsWriteNothing()
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        DocumentPassAssignRequest valid = Request(f, pass);
        int before = f.Estate.Load().Records.Count;
        foreach (DocumentPassAssignRequest bad in new[]
        {
            valid with { Documents = [] }, valid with { Documents = [valid.Documents[0], valid.Documents[0]] },
            valid with { Release = valid.Release with { Hash = "sha256:" + new string('0', 64) } },
            valid with { Documents = [new(f.Cleared.Id, f.Cleared.ComputedHash)] },
            valid with { By = f.Estate.BuilderId }, valid with { Reviewer = Ulid.NewUlid().Value }, valid with { Key = "invalid" },
        })
        {
            DocumentPassAssignResponse refused = f.Service.DocumentPassAssign("0.2.0", bad);
            Assert.False(refused.Ok);
            Assert.Empty(refused.Written);
            Assert.Equal(before, f.Estate.Load().Records.Count);
        }
        JsonObject revised = JsonNode.Parse(f.Readme.Body.GetRawText())!.AsObject();
        revised["summary"] = "Revised declaration.";
        f.Estate.Add("living-document", f.Estate.OperatorId, revised.ToJsonString(), supersedes: f.Readme.Id);
        Assert.False(f.Service.DocumentPassAssign("0.2.0", valid).Ok);
        Assert.Empty(f.Service.Records("assignment"));
        Assert.Empty(f.Service.Records("work-order"));
    }

    [Fact]
    public void MissingCloneAndUnavailableContributionsKeepDueDocumentsWithExplicitGaps()
    {
        using var f = new DocumentPassFixture();
        f.Estate.WriteFile("docs/unavailable.md", "Check is unavailable.\n");
        RecordEnvelope missingCheck = f.Declare("docs/unavailable.md", estateFile: true, check: "example.docs/missing");
        File.Delete(EstateLocations.ClonesPath(f.Estate.Estate.Id));
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        Assert.True(pass.Ok);
        Assert.Equal(5, pass.Documents.Count);
        Assert.Contains(Assert.Single(pass.Documents, d => d.Declaration.Id == missingCheck.Id).Notes, n => n.Contains("Unavailable contribution", StringComparison.Ordinal));
        Assert.Contains(Assert.Single(pass.Documents, d => d.Declaration.Id == f.Readme.Id).Notes, n => n.Contains("both publication commits", StringComparison.Ordinal));
        Assert.Empty(pass.Updates);
    }

    [Fact]
    public void AVersionSharedByProductsRequiresSelectionAndTheFirstPublicationListsCoveredFilesAsAdded()
    {
        using var f = new DocumentPassFixture();
        RecordEnvelope other = f.Estate.Add("product", f.Estate.OperatorId, f.Product.Body.GetRawText().Replace("widget", "other", StringComparison.Ordinal).Replace("Widget", "Other", StringComparison.Ordinal));
        f.Repository.WriteFile("docs/other.md", "Another product.\n");
        f.Git("add", "--all");
        f.Git("commit", "-m", "Other fixture product");
        RecordEnvelope declaration = f.Declare("docs/other.md", product: other.Id, covers: true);
        RecordEnvelope publication = f.Publish("0.2.0", f.Git("rev-parse", "HEAD").Trim(), other);
        Assert.False(f.Service.DocumentPass("0.2.0").Ok);
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0", "other");
        Assert.Equal(publication.Id, pass.Release!.Id);
        Assert.Null(pass.Previous);
        DocumentPassItem item = Assert.Single(pass.Documents);
        Assert.Equal(declaration.Id, item.Declaration.Id);
        Assert.NotEmpty(item.Changes);
        Assert.All(item.Changes, c => Assert.Equal("added", c.Kind));
        Assert.False(f.Service.DocumentPass("9.9.9").Ok);
    }

    private static DocumentPassAssignRequest Request(DocumentPassFixture f, DocumentPassResponse pass) =>
        new(f.Estate.OperatorId, f.Estate.BuilderId, "DOCS-REVIEW-1", pass.Release!, pass.Documents.Select(d => d.Declaration).ToArray());

    [Fact]
    public void RoutineUpdatePreservesProseAfterTheReadmeDate()
    {
        using var f = new DocumentPassFixture();
        f.Repository.WriteFile("README.md", "# Widget\n\nThe current published release is 0.1.0, from\nOctober 13, 2026. Keep this qualification.\n");
        f.Git("add", "--all");
        f.Git("commit", "-m", "Add fixture qualification");
        DocumentPassUpdate update = Assert.Single(f.Service.DocumentPass("0.2.0").Updates, u => u.Path == "README.md");
        f.GitInput(update.Patch, "apply", "-");
        Assert.Contains("October 13, 2026. Keep this qualification.", File.ReadAllText(Path.Combine(f.Repository.Root, "README.md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md", "# Ambiguous\n\nThe current published release is 0.1.0, from\nOctober 13, 2026.\n\nCurrent release: 0.1.0\n")]
    [InlineData("README.md", "# Custom version prose\n")]
    [InlineData("ROADMAP.md", "# Roadmap\n\n## 0.1.0 — current published release\n\n## 0.2.0 — custom status\n")]
    [InlineData("ROADMAP.md", "# Roadmap\n\n## 0.1.0 — current published release\n\n## 0.2.0 — planned\n\n## 0.2.0 — planned\n")]
    public void UnsupportedOrAmbiguousFrontMarkersStayForManualReview(string path, string contents)
    {
        using var f = new DocumentPassFixture();
        f.Repository.WriteFile(path, contents);
        f.Git("add", "--all");
        f.Git("commit", "-m", "Custom fixture front document");
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        Assert.DoesNotContain(pass.Updates, u => u.Path == path);
        Assert.Contains(Assert.Single(pass.Documents, d => d.Path == path).Notes, n => n.Contains("prepare its update by hand", StringComparison.Ordinal));
        Assert.Equal(contents, File.ReadAllText(Path.Combine(f.Repository.Root, path)));
        Assert.Empty(f.Git("status", "--porcelain"));
    }

    [Fact]
    public void AReviewArrivingDuringValidationRefusesTheWholeAssignmentSelection()
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        bool changed = false;
        f.Service.BeforeValidatingRecords = _ =>
        {
            if (changed) return;
            changed = true;
            f.Clear(f.Readme, f.SecondCommit);
        };
        DocumentPassAssignResponse assigned = f.Service.DocumentPassAssign("0.2.0", Request(f, pass));
        Assert.True(changed);
        Assert.False(assigned.Ok);
        Assert.Empty(assigned.Written);
        Assert.Empty(f.Service.Records("work-order"));
        Assert.Empty(f.Service.Records("assignment"));
        Assert.DoesNotContain(f.Service.DocumentPass("0.2.0").Documents, d => d.Declaration.Id == f.Readme.Id);
    }

    [Fact]
    public void InterruptedBatchReturnsTheStoredReceiptAndDuplicateKeyCannotCreateAnotherTask()
    {
        using var f = new DocumentPassFixture();
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        int stored = 0;
        f.Service.BeforeStoringTogether = _ =>
        {
            if (++stored == 2) throw new IOException("Fixture storage interruption.");
        };
        DocumentPassAssignResponse assigned = f.Service.DocumentPassAssign("0.2.0", Request(f, pass));
        Assert.False(assigned.Ok);
        Assert.Equal("reconcile-required", assigned.Problem!.Code);
        Assert.Equal("work-order", f.Estate.Load().ById[Assert.Single(assigned.Written).Id].Type);
        Assert.Empty(f.Service.Records("assignment"));
        Assert.Equal(4, f.Service.DocumentPass("0.2.0").Documents.Count);
        f.Service.BeforeStoringTogether = null;
        Assert.False(f.Service.DocumentPassAssign("0.2.0", Request(f, pass)).Ok);
        Assert.Single(f.Service.Records("work-order"));
    }

    [Fact]
    public void RevisedProductIdentityRetainsThePreviousPublicationAndReleaseTriggers()
    {
        using var f = new DocumentPassFixture();
        JsonObject body = JsonNode.Parse(f.Product.Body.GetRawText())!.AsObject();
        body["summary"] = "Revised product description.";
        RecordEnvelope revised = f.Estate.Add("product", f.Estate.OperatorId, body.ToJsonString(), supersedes: f.Product.Id);
        RecordEnvelope published = f.Publish("0.3.0", f.SecondCommit, revised);
        DocumentPassResponse pass = f.Service.DocumentPass("0.3.0", f.Product.Id);
        Assert.True(pass.Ok);
        Assert.Equal(published.Id, pass.Release!.Id);
        Assert.Equal(f.Second.Id, pass.Previous!.Id);
        Assert.Equal(f.Product.Id, pass.Product);
        Assert.Contains(pass.Documents, d => d.Declaration.Id == f.Readme.Id);
        Assert.Equal(pass.Release, f.Service.DocumentPass("0.3.0", revised.Id).Release);
    }

    [Fact]
    public void ADocumentInAnotherRepositoryStaysDueWithoutUsingTheProductsCommitsOrFrontPatches()
    {
        using var f = new DocumentPassFixture();
        RecordEnvelope repository = f.Estate.Add("resource", f.Estate.OperatorId,
            """{"kind":"repository","name":"Other repository","role":"Separate sources.","location":{"github":"Example/other"},"visibility":"private"}""");
        RepositoryClones.Register(f.Estate.Estate.Id, repository.Id, f.Repository.Root);
        JsonObject body = JsonNode.Parse(f.Readme.Body.GetRawText())!.AsObject();
        body["repository"] = repository.Id;
        RecordEnvelope declaration = f.Estate.Add("living-document", f.Estate.OperatorId, body.ToJsonString());
        DocumentPassResponse pass = f.Service.DocumentPass("0.2.0");
        DocumentPassItem document = Assert.Single(pass.Documents, d => d.Declaration.Id == declaration.Id);
        Assert.Empty(document.Changes);
        Assert.Contains(document.Notes, n => n.Contains("another repository", StringComparison.Ordinal));
        Assert.DoesNotContain(pass.Updates, u => u.Declaration == declaration.Id);
        Assert.True(f.Service.DocumentPassAssign("0.2.0", new(f.Estate.OperatorId, f.Estate.BuilderId, "DOCS-OTHER-1", pass.Release!, [document.Declaration])).Ok);
    }
}
