// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class HistoryExpectCommandTests
{
    [Fact]
    public void EstateCheckExpectRefusesACopyThatLacksTheCheckpointItNames()
    {
        using var f = new HistoryFixture();
        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("HIS-30"), signer: f.OperatorSigner);
        RecordEnvelope newest = f.Checkpoint();

        CliHarness.Outcome held = CliHarness.Run("--estate", f.Root, "estate", "check", "--expect", newest.Id);
        Assert.True(held.Code == 0, held.Out + held.Err);
        Assert.Contains("history.expected", held.Out, StringComparison.Ordinal);
        CliHarness.Outcome byHash = CliHarness.Run("--estate", f.Root, "--json", "estate", "check", "--expect", newest.ComputedHash[ContentHash.Prefix.Length..][..12]);
        Assert.True(byHash.Code == 0, byHash.Out + byHash.Err);
        FindingsResponse findings = byHash.Json.Deserialize<FindingsResponse>(ProtocolJson.Options)!;
        Assert.Equal("info", Assert.Single(findings.Findings, x => x.Code == "history.expected").Severity);

        // A copy whose newest checkpoint and the record before it were removed together validates, and only --expect refuses it.
        string copy = CliHarness.Clone(f.Root, "history-expect");
        try
        {
            File.Delete(Path.Combine(copy, newest.ExpectedPath));
            File.Delete(Path.Combine(copy, order.ExpectedPath));
            Assert.Equal(0, CliHarness.Run("--estate", copy, "estate", "check").Code);
            CliHarness.Outcome refused = CliHarness.Run("--estate", copy, "--json", "estate", "check", "--expect", newest.Id);
            Assert.Equal(1, refused.Code);
            FindingDto missing = Assert.Single(refused.Json.Deserialize<FindingsResponse>(ProtocolJson.Options)!.Findings, x => x.Code == "history.expected-missing");
            Assert.Equal("error", missing.Severity);
            Assert.Equal(1, CliHarness.Run("--estate", copy, "estate", "check", "--expect", "not-a-checkpoint").Code);
        }
        finally { CliHarness.Delete(copy); }
    }
}
