// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public sealed class RevisionHistoryOrderingTests
{
    private const string PathName = "design/example.md";
    internal static string Body(int revision, string summary, string? hashMode = RevisionLines.HashMode) => JsonSerializer.Serialize(new
    {
        path = PathName, title = "Example", hash = "sha256:" + new string('a', 64), bytes = 0,
        mediaType = "text/markdown", revision, summary, hashMode,
    });

    [Fact]
    public void ConcurrentPinsHaveOneDeterministicCurrentViewAndDistinctOrdinals()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope first = estate.Add("document-revision", estate.OperatorId, Body(1, "First."));
        DateTimeOffset at = estate.Clock;
        RecordEnvelope a = estate.Add("document-revision", estate.OperatorId, Body(50, "Branch A."), supersedes: first.Id);
        estate.SetClock(at);
        RecordEnvelope b = estate.Add("document-revision", estate.OperatorId, Body(50, "Branch B."), supersedes: first.Id);
        RecordEnvelope order = FirstPartyTemplateTests.Work(estate, "DOCS-1");
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        RecordEnvelope latest = new[] { a, b }.MaxBy(r => r.Id, StringComparer.Ordinal)!;
        Assert.Equal(latest.Id, Assert.Single(model.Current("document-revision")).Id);
        Assert.Equal(3, RevisionLines.Ordinal(model, latest));
        string rendered = RevisionLines.Render(model, PathName);
        Assert.StartsWith("Revision 3, October 13, 2026:", rendered, StringComparison.Ordinal);
        Assert.Contains("Revision 2, October 13, 2026:", rendered, StringComparison.Ordinal);
        Assert.Contains("Revision 1, October 13, 2026: First.", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Revision 50", rendered, StringComparison.Ordinal);
        Assert.Contains("design/example.md (revision 3,", WorkView.RenderBrief(model, order), StringComparison.Ordinal);
        EstateModel reversed = EstateLoader.Load(estate.Root, EstateLoader.EnumerateRecordFiles(estate.Root).Reverse().ToArray());
        Assert.Equal(rendered, RevisionLines.Render(reversed, PathName));
        Assert.Equal(latest.Id, Assert.Single(reversed.Current("document-revision")).Id);
        Assert.Equal(first.Id, a.Supersedes);
        Assert.Equal(first.Id, b.Supersedes);
    }

    [Fact]
    public void TheNewFieldsRequireFormatThreeWithoutChangingTheFormatNumber()
    {
        using var estate = new TestEstate(2);
        estate.Add("document-revision", estate.OperatorId, Body(1, "First."));
        Assert.Contains(estate.Load().Findings, f => f.Code == "body.format");
        Assert.Equal(3, RecordFormats.Current);
    }

    [Fact]
    public void EqualTimesUseIdentityEvenWhenTheRecordedPredecessorHasALaterIdentity()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope first = estate.Add("document-revision", estate.OperatorId, Body(1, "First."));
        estate.SetClock(first.AtTime.AddSeconds(-1));
        RecordEnvelope next = estate.Add("document-revision", estate.OperatorId, Body(2, "Next."), supersedes: first.Id, id: "00000000000000000000000001");
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal(first.Id, Assert.Single(model.Current("document-revision")).Id);
        Assert.Equal(first.Id, model.Latest(next).Id);
        Assert.Equal(first.Id, model.Latest(first).Id);
        Assert.Equal(first.Id, next.Supersedes);
    }
}
