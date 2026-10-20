// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class StoredCompanionCommandsTests
{
    private static readonly string[] ReceiptProperties = ["path", "id", "hash"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmbeddedAndDaemonCommandsReportStoredWithRecoveryAndKeepTheirExitAndJsonShape(bool daemon, bool json)
    {
        // Initialize the harness's isolated test home before starting either runtime.
        CliHarness.Ok("work", "attempt", "--help");
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        RecordWritten assignment = fixture.Assign();
        fixture.DeclareCompanions("attempt");
        fixture.InterruptPublication();
        string[] args = ["--estate", fixture.Estate.Root, "work", "attempt", assignment.Id, "--by", fixture.Author, "--workspace", "test repository", "--note", "Store once."];
        CliHarness.Outcome result = CliHarness.RunWithApi(fixture.Api, null, json ? [.. args, "--json"] : args);
        Assert.Equal(2, result.Code);
        RecordResponse record = Assert.Single(fixture.Api.Records("attempt"));
        RecordWritten written;
        if (json)
        {
            Assert.Equal(ReceiptProperties, result.Json.EnumerateObject().Select(property => property.Name));
            written = JsonSerializer.Deserialize<RecordWritten>(result.Out, ProtocolJson.Options)!;
        }
        else
        {
            string[] fields = result.Out.TrimEnd().Split("  ", StringSplitOptions.None);
            Assert.Equal(3, fields.Length);
            written = new(fields[0], fields[1], fields[2]);
        }
        Assert.Equal(record.Id, written.Id);
        Assert.Equal(record.Hash, written.Hash);
        Assert.True(File.Exists(Path.Combine(fixture.Estate.Root, written.Path)));
        Assert.StartsWith("error: Record " + record.Id + " was stored.", result.Err, StringComparison.Ordinal);
        Assert.Contains("Companion publication stopped", result.Err, StringComparison.Ordinal);
        Assert.Contains("batch journal", result.Err, StringComparison.Ordinal);
        Assert.Contains("Reopen the estate or run docs regenerate", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", result.Err, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outcome is unknown", result.Err, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Publications);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ARealRefusalStillExitsTwoWithNoReceipt(bool daemon, bool json)
    {
        CliHarness.Ok("work", "attempt", "--help");
        await using var fixture = await CompanionWriteFixture.StartAsync(daemon);
        string[] args = ["--estate", fixture.Estate.Root, "work", "attempt", fixture.Estate.Estate.Id, "--by", fixture.Author, "--workspace", "test repository", "--note", "Missing assignment."];
        CliHarness.Outcome result = CliHarness.RunWithApi(fixture.Api, null, json ? [.. args, "--json"] : args);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Out);
        Assert.StartsWith("error: ", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("was stored", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("outcome is unknown", result.Err, StringComparison.Ordinal);
        Assert.Empty(fixture.Api.Records("attempt"));
    }
}
