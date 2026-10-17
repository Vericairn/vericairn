// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class ProgressProjectionWorkTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void HomeProgressDoesNotWalkRecordsItDoesNotDisplayAfterTheModelIsLoaded(int records)
    {
        using var estate = new TestEstate();
        for (int i = 3; i < records; i++)
        {
            estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Fixture source","role":"Unshown resource history.","location":{"github":"Example/source"},"visibility":"public"}""");
        }
        EstateModel loaded = estate.Load();
        Assert.False(loaded.Findings.HasErrors, string.Join('\n', loaded.Findings));
        var counted = new CountedRecords(loaded.Records);
        var model = new EstateModel(loaded.Root, counted, loaded.ById, loaded.SupersededBy, loaded.Findings) { Manifest = loaded.Manifest };
        counted.Reads = 0;
        string text = ProgressReport.Render(model, null);
        string json = ProgressReport.RenderJson(model, null);
        Assert.Equal(0, counted.Reads);
        Assert.Equal(ProgressReport.Render(loaded, null), text);
        Assert.Equal(ProgressReport.RenderJson(loaded, null), json);
    }

    private sealed class CountedRecords(IReadOnlyList<RecordEnvelope> records) : IReadOnlyList<RecordEnvelope>
    {
        internal int Reads { get; set; }
        public int Count => records.Count;
        public RecordEnvelope this[int index] { get { Reads++; return records[index]; } }
        public IEnumerator<RecordEnvelope> GetEnumerator()
        {
            foreach (RecordEnvelope record in records)
            {
                Reads++;
                yield return record;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
