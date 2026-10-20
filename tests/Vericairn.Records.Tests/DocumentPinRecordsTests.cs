// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public sealed class DocumentPinRecordsTests
{
    [Fact]
    public void ForkFindingsIdentifyBothPinsAndTheirPredecessorUntilReconciliation()
    {
        using var estate = new TestEstate();
        estate.WriteFile("docs/guide.md", "# Guide\n");
        string hash = ContentHash.Sha256File(Path.Combine(estate.Root, "docs", "guide.md"));
        string Body(int revision, string summary) => JsonSerializer.Serialize(new
        {
            path = "docs/guide.md", title = "Guide", hash, bytes = 8, mediaType = "text/markdown", revision, summary,
        });
        RecordEnvelope first = estate.Add("document-revision", estate.OperatorId, Body(1, "First pin."));
        RecordEnvelope a = estate.Add("document-revision", estate.OperatorId, Body(2, "Pin from A."), supersedes: first.Id);
        RecordEnvelope b = estate.Add("document-revision", estate.OperatorId, Body(2, "Pin from B."), supersedes: first.Id);
        EstateModel fork = estate.Load();
        Assert.Contains(fork.Findings, f => f.Code == "reconcile.successors");
        Assert.Equal(2, fork.Current("document-revision").Count());
        string message = DocumentRevisionType.MultipleMessage("docs/guide.md", [b, a]);
        Assert.Equal(message, DocumentRevisionType.MultipleMessage("docs/guide.md", [a, b]));
        foreach (string id in new[] { first.Id, a.Id, b.Id }) Assert.Contains(id, message, StringComparison.Ordinal);
        Assert.All(fork.Findings.Where(f => f.Code == "document.multiple"), f => Assert.Equal(message, f.Message));
        Assert.Equal(2, fork.Findings.Count(f => f.Code == "document.multiple"));
        estate.Add("reconciliation", estate.OperatorId,
            JsonSerializer.Serialize(new { kept = a.Id, withdrawn = new[] { b.Id }, note = "Keep A's pin." }), first.Reference);
        EstateModel reconciled = estate.Load();
        Assert.False(reconciled.Findings.HasErrors, string.Join("\n", reconciled.Findings));
        Assert.Equal(a.Id, Assert.Single(reconciled.Current("document-revision")).Id);
        Assert.Equal(3, reconciled.OfType("document-revision").Count());
    }
}
