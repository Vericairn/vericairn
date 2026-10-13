// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Tests;

/// <summary>Opening an estate stays within a budget as it grows; the live store makes a second open cheaper than the first.</summary>
[Collection("home")]
public class PerformanceBudgetTests : IDisposable
{
    private const int Records = 20_000;
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-perf-home-" + Ulid.NewUlid().Value);
    private readonly string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);

    public PerformanceBudgetTests()
    {
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, home);
    }

    [Fact]
    public void TwentyThousandRecordsOpenWithinBudgetAndTheLiveStoreAvoidsRereading()
    {
        using var estate = new TestEstate();
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"PERF","kind":"ordinary","title":"Budget","intent":"Measure.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        DateTimeOffset at = estate.Clock;
        using (JsonDocument body = JsonDocument.Parse("""{"status":"active","note":"Generated for the performance budget."}"""))
        {
            for (int i = 0; i < Records; i++)
            {
                at = at.AddSeconds(1);
                AuthoredRecord record = RecordAuthor.Create(new RecordDraft("work-status", estate.BuilderId, body.RootElement, new RecordReference("work-order", order.Id, null), null, at));
                RecordAuthor.Write(estate.Root, record);
            }
        }

        using EstateSession session = EstateSession.Open(estate.Root);
        var first = Stopwatch.StartNew();
        EstateModel model = session.Load();
        first.Stop();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings.Take(5)));
        Assert.Equal(Records + 4, model.Records.Count);
        Assert.True(first.Elapsed < TimeSpan.FromSeconds(90), $"First open of {Records:N0} records took {first.Elapsed}.");

        int reread = session.Live.SyncFromDirectory(estate.Root);
        Assert.Equal(0, reread);
        var second = Stopwatch.StartNew();
        model = session.Load();
        second.Stop();
        Assert.Equal(Records + 4, model.Records.Count);
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(60), $"Second open took {second.Elapsed}.");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
        try
        {
            Directory.Delete(home, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
