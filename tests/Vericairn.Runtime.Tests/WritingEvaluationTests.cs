// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

public class WritingEvaluationTests
{
    [Fact]
    public void WholeExactTokensAreExceptedOnceWithEveryMatchingEntryRetained()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope first = fixture.Add("writing-exception", PolicyFixture.WritingBody());
        RecordEnvelope second = fixture.Add("writing-exception", PolicyFixture.WritingBody());
        WritingEvaluation result = Evaluate(fixture, "Open311 open311 Open3110 SomeOpen311 `Open311`\n~~~\nOpen311\n~~~");
        Assert.Equal(1, result.ExceptedCount);
        Assert.Equal(new[] { first.Reference, second.Reference }.OrderBy(r => r.Id, StringComparer.Ordinal), result.ExceptedOccurrences[0].Entries);
        Assert.Equal(["open311", "Open3110"], result.Warnings.Select(o => o.Occurrence.Token));
        Assert.All(result.Entries, e => Assert.Equal(1, e.Matches));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "writing.exception-unused");
        Assert.Equal(2, result.WarningCount);
    }

    [Fact]
    public void UnionScopesFollowPathsAndExactRecordFieldsWithoutFollowingRevisions()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope note = fixture.Add("work-order", new JsonObject { ["key"] = "TEST-01", ["kind"] = "ordinary", ["title"] = "Open311", ["intent"] = "Open311", ["scope"] = new JsonArray("testing"), ["exitQuestion"] = "Complete?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("result") });
        JsonObject entry = PolicyFixture.WritingBody();
        entry["scope"] = new JsonObject
        {
            ["paths"] = new JsonArray(new JsonObject { ["repository"] = fixture.Estate.Estate.Id, ["selectors"] = new JsonArray("docs/selected/**") }),
            ["records"] = new JsonArray(new JsonObject { ["record"] = note.Id, ["field"] = "title" }),
        };
        fixture.Add("writing-exception", entry);
        RecordEnvelope edited = fixture.Add("work-order", PolicyFixture.Body(note), supersedes: note.Id);
        WritingEvaluation original = Evaluate(fixture, "Open311", "docs/selected/first.md");
        WritingEvaluation moved = Evaluate(fixture, "\n\nOpen311", "docs/selected/next.md");
        Assert.Equal(2, original.ExceptedCount);
        Assert.Equal(2, moved.ExceptedCount);
        Assert.Equal(1, Evaluate(fixture, "Open311", "docs/outside.md").ExceptedCount);
        Assert.Contains(moved.Warnings, o => o.Location.Identity == note.Id && o.Location.Member == "intent");
        Assert.Contains(moved.Warnings, o => o.Location.Identity == edited.Id && o.Location.Member == "title");
    }

    [Fact]
    public void UnusedRequiresTheCompleteScanAndRemovedEntriesNeverWarn()
    {
        using var fixture = new PolicyFixture();
        RecordEnvelope entry = fixture.Add("writing-exception", PolicyFixture.WritingBody());
        Assert.Contains(Evaluate(fixture, "`Open311`" ).Diagnostics, d => d.Code == "writing.exception-unused");
        Assert.Contains(Evaluate(fixture, "").Diagnostics, d => d.Code == "writing.exception-unused");
        WritingEvaluation missing = Evaluate(fixture, "", complete: false);
        Assert.False(missing.Complete);
        Assert.Null(Assert.Single(missing.Entries).Matches);
        Assert.DoesNotContain(missing.Diagnostics, d => d.Code == "writing.exception-unused");
        JsonObject removal = PolicyFixture.Body(entry);
        removal["state"] = "removed"; removal["removalReason"] = "No longer needed.";
        fixture.Add("writing-exception", removal, supersedes: entry.Id);
        Assert.Empty(Evaluate(fixture, "Open311").Entries);
        Assert.Equal(1, Evaluate(fixture, "Open311").WarningCount);
    }

    [Fact]
    public void RegistryCannotQuietAnErrorAnotherWarningOrUnusedEntryDiagnostic()
    {
        using var fixture = new PolicyFixture();
        fixture.Add("writing-exception", PolicyFixture.WritingBody());
        PolicyBasis basis = Basis(fixture, "Open311");
        var location = new WritingLocation("file", fixture.Estate.Estate.Id, "docs/test.md");
        SpacingOccurrence candidate = Assert.Single(WritingScanner.Scan("Open311", "docs/test.md"));
        var error = candidate with { Diagnostic = candidate.Diagnostic with { Severity = Severity.Error } };
        var other = candidate with { Diagnostic = candidate.Diagnostic with { Code = "other.warning" } };
        Finding unused = new(Severity.Warning, "writing.exception-unused", "fixture", "Open311");
        WritingEvaluation result = WritingEvaluation.Match(basis, [(location, error), (location, other), (location, candidate)], [unused], true);
        Assert.Equal(1, result.ExceptedCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(2, result.WarningCount);
        Assert.Contains(unused, result.Diagnostics);
    }

    [Fact]
    public void FileOrderAndElapsedTimeDoNotChangeMatchesOrExpireEntries()
    {
        using var fixture = new PolicyFixture();
        fixture.Add("writing-exception", PolicyFixture.WritingBody());
        PolicyBasis basis = Basis(fixture, "Open311");
        PolicyRepository repository = basis.Repositories[0] with { Files = [PolicyBasisTests.File("docs/z.md", "Open311"), PolicyBasisTests.File("docs/a.md", "Open311")] };
        var ordered = new PolicyBasis(basis.Model, basis.AsOf, basis.Origin, [repository]);
        var shuffled = new PolicyBasis(basis.Model, basis.AsOf, basis.Origin, [repository with { Files = repository.Files.Reverse().ToImmutableArray() }]);
        Assert.Equal(PolicyBasis.Canonical(WritingEvaluation.Evaluate(ordered)), PolicyBasis.Canonical(WritingEvaluation.Evaluate(shuffled)));
        Assert.Equal(2, WritingEvaluation.Evaluate(new(basis.Model, "2036-01-01T00:00:00Z", PolicyTimeOrigin.Preview, [repository])).ExceptedCount);
    }

    [Fact]
    public void ASharedScannerDiagnosticIsCountedOnceAndUnreadSourcesKeepTheirWarnings()
    {
        using var fixture = new PolicyFixture();
        fixture.Add("writing-exception", PolicyFixture.WritingBody());
        PolicyBasis original = Basis(fixture, "Open311 Open311");
        EstateModel model = fixture.Estate.Load();
        foreach (SpacingOccurrence occurrence in WritingScanner.Scan("Open311 Open311", "docs/test.md")) model.Findings.Add(occurrence.Diagnostic);
        Finding unavailable = Assert.Single(WritingScanner.Scan("Open311", "docs/unavailable.md")).Diagnostic;
        model.Findings.Add(unavailable);
        WritingEvaluation result = WritingEvaluation.Evaluate(new(model, original.AsOf, original.Origin, original.Repositories));
        Assert.Equal(2, result.ExceptedCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(unavailable, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void ATimeBeforeTheExceptionWasRecordedCannotUseThatException()
    {
        using var fixture = new PolicyFixture();
        fixture.Add("writing-exception", PolicyFixture.WritingBody());
        PolicyBasis original = Basis(fixture, "Open311");
        WritingEvaluation result = WritingEvaluation.Evaluate(new(original.Model, "2026-10-13T12:00:00Z", PolicyTimeOrigin.Preview, original.Repositories));
        Assert.False(result.Complete);
        Assert.Equal(0, result.ExceptedCount);
        Assert.Single(result.Warnings);
        Assert.Contains(result.Diagnostics, d => d.Code == "writing.basis-unavailable");
    }

    private static WritingEvaluation Evaluate(PolicyFixture fixture, string text, string path = "docs/test.md", bool complete = true) => WritingEvaluation.Evaluate(Basis(fixture, text, path, complete));
    private static PolicyBasis Basis(PolicyFixture fixture, string text, string path = "docs/test.md", bool complete = true) => PolicyBasis.Recorded(fixture.Estate.Load(),
        [new(fixture.Estate.Estate.Id, "working-tree", PolicyBasis.RepositoryContract, complete, [PolicyBasisTests.File(path, text)])]);
}
