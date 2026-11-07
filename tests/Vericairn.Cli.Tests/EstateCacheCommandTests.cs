// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>FIX-93 (estate report #298): a malformed derived cache is diagnosed and rebuilt through the command line.</summary>
[Collection("cli")]
public sealed class EstateCacheCommandTests
{
    [Fact]
    public void AMalformedCacheIsDiagnosedThenRebuiltAndTheOriginalCommandSucceeds()
    {
        Assert.Contains("local-only", CliHarness.Ok("estate", "cache", "rebuild", "--help").Out, StringComparison.Ordinal);
        using var estate = new TestEstate();
        string database = LiveStoreDamage.Cache(estate.Root);
        using (EstateService.Open(estate.Root)) { }
        LiveStoreDamage.Malform(database);
        string scratch = CliHarness.TempDirectory("cache-rebuild");
        try
        {
            string principal = CliHarness.WriteJson(scratch, "principal.json", """{"kind":"human","name":"Second Person","roles":["reviewer"]}""");
            string[] write = ["--estate", estate.Root, "--json", "records", "new", "principal", "--by", estate.OperatorId, "--body", principal];
            CliHarness.Outcome refused = CliHarness.Run(write);
            Assert.Equal(2, refused.Code);
            Assert.StartsWith("error: This clone's derived cache of a repository-synced estate could not be used (SQLite Error 11", refused.Err, StringComparison.Ordinal);
            Assert.Contains("'vericairn estate cache rebuild'", refused.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled", refused.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", refused.Err, StringComparison.Ordinal);
            Assert.Equal(2, CliHarness.Run("--estate", estate.Root, "estate", "check").Code);

            CliHarness.Outcome rebuilt = CliHarness.Ok("--estate", estate.Root, "--json", "estate", "cache", "rebuild");
            Assert.Equal(database, rebuilt.Json.GetProperty("cache").GetString());
            Assert.Equal(3, rebuilt.Json.GetProperty("records").GetInt64());
            string retained = rebuilt.Json.GetProperty("retained").GetString()!;
            Assert.True(File.Exists(Path.Combine(retained, "live.db")));
            Assert.Equal("live.db", rebuilt.Json.GetProperty("retainedFiles")[0].GetString());

            CliHarness.Ok("--estate", estate.Root, "estate", "check");
            CliHarness.Ok(write);
            CliHarness.Outcome text = CliHarness.Ok("--estate", estate.Root, "estate", "cache", "rebuild");
            Assert.Contains("rebuilt the derived cache from 4 record files", text.Out, StringComparison.Ordinal);
            Assert.Contains("kept the earlier cache for diagnosis, unchanged", text.Out, StringComparison.Ordinal);
        }
        finally { Directory.Delete(scratch, true); }
    }

    [Fact]
    public void TheRebuildIsRefusedWhileTheEstateIsOpen()
    {
        using var estate = new TestEstate();
        using EstateService open = EstateService.Open(estate.Root);
        CliHarness.Outcome refused = CliHarness.Run("--estate", estate.Root, "estate", "cache", "rebuild");
        Assert.Equal(2, refused.Code);
        Assert.Contains("Another runtime on this machine has this estate open", refused.Err, StringComparison.Ordinal);
        Assert.Contains("Nothing was moved.", refused.Err, StringComparison.Ordinal);
    }
}
