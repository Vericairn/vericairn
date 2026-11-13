// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// An estate holding an unsigned device record whose key is in a form Vericairn does not use (<see cref="UnusableKeyTests"/>)
/// is checked and reported like any estate with an invalid record: <c>estate check</c> and <c>status</c> answer with their findings.
/// </summary>
[Collection("cli")]
public sealed class UnusableKeyCommandTests
{
    private static readonly string[] JsonFlag = ["--json"];

    [Theory]
    [MemberData(nameof(UnusableKeyTests.Kinds), MemberType = typeof(UnusableKeyTests))]
    public void EstateCheckAndStatusReportTheRecordWithTheirFindings(string kind)
    {
        using var estate = new TestEstate();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string key = UnusableKeyTests.Key(kind, signer);
        RecordEnvelope device = estate.Add("device", estate.OperatorId, UnusableKeyTests.Device(estate.OperatorId, key));

        var outcomes = new List<(string Shown, CliHarness.Outcome Outcome)>();
        foreach (string[] command in (string[][])[["estate", "check"], ["status"]])
        {
            foreach (bool json in (bool[])[false, true])
            {
                string[] args = ["--estate", estate.Root, .. json ? JsonFlag : [], .. command];
                CliHarness.Outcome outcome = CliHarness.Run(args);
                outcomes.Add(($"vericairn {string.Join(' ', args)} exited {outcome.Code}\n{outcome.Out}\n{outcome.Err}", outcome));
            }
        }

        // Each answers with the estate's findings, the record's among them, and nothing escapes.
        foreach ((string shown, CliHarness.Outcome outcome) in outcomes)
        {
            Assert.True(outcome.Code == 1, shown);
            Assert.DoesNotContain("Unhandled", outcome.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", outcome.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(PlatformNotSupportedException), outcome.Out + outcome.Err, StringComparison.Ordinal);
            Assert.True(outcome.Out.Contains(device.Id, StringComparison.Ordinal), shown);
        }

        // The key is reported where the record holds it.
        Assert.False(RecordSigning.IsPublicKey(key));
        Assert.All(outcomes, o => Assert.True(o.Outcome.Out.Contains(device.ExpectedPath + "#body.publicKey", StringComparison.Ordinal), o.Shown));
    }
}
