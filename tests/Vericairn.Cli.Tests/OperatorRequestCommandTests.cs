// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Cli;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class OperatorRequestCommandTests
{
    private static readonly string[] Commands = ["list", "record", "unfinished", "close", "request", "carry", "done", "dismiss"];
    public OperatorRequestCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void RequestCommandsAreAdditiveAndDismissRequiresTheOperatorsReason()
    {
        var root = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken);
        var family = root.Subcommands.Single(c => c.Name == "operator");
        Assert.All(Commands, name => Assert.Contains(family.Subcommands, c => c.Name == name));
        Assert.Empty(root.Parse(["operator", "done", "request", "--by", "operator"]).Errors);
        Assert.NotEmpty(root.Parse(["operator", "dismiss", "request", "--by", "operator"]).Errors);
        Assert.Empty(root.Parse(["operator", "dismiss", "request", "--by", "operator", "--reason", "No longer needed."]).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoneAndDismissKeepOneSignedOutcomeAndTheOriginalRequest(bool dismiss)
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope request = OperatorRequestTests.Request(f);
        int before = f.Estate.Load().Records.Count;
        CliHarness.Outcome listed = CliHarness.Run("--estate", f.Estate.Root, "operator", "list");
        Assert.Equal(0, listed.Code);
        Assert.Contains(request.Id, listed.Out, StringComparison.Ordinal);
        string[] reason = dismiss ? ["--reason", "No longer needed."] : [];
        CliHarness.Outcome outcome = CliHarness.Run(["--estate", f.Estate.Root, "--json", "operator", dismiss ? "dismiss" : "done", request.Id, "--by", f.Estate.OperatorId, "--expected", request.ComputedHash, .. reason]);
        Assert.Equal(0, outcome.Code);
        Assert.Equal(Assert.Single(f.Estate.Load().OfType("operator-request-close")).Id, outcome.Json.GetProperty("id").GetString());
        Assert.Equal(before + 1, f.Estate.Load().Records.Count);
        Assert.Equal(request.ComputedHash, f.Estate.Load().ById[request.Id].ComputedHash);
        Assert.Single(f.Estate.Load().OfType("operator-request-close"));
        Assert.DoesNotContain(request.Id, CliHarness.Run("--estate", f.Estate.Root, "operator", "list").Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// FIX-171 (0.9.5): an agent citing the operator's words writes when they were said in the record's timestamp form, a UTC time in whole
    /// seconds: the time of the recording when --said-at is omitted, which up to 0.9.4 wrote an empty time the record refused
    /// (body.timestamp), and a given time with fractional seconds or an offset in that form.
    /// </summary>
    [Fact]
    public void AnAgentCitingTheOperatorsWordsWritesWhenTheyWereSaidInTheRecordsTimestampForm()
    {
        using var f = new OperatorCloseFixture();
        string device = f.Api.DeviceCreate(new(f.Estate.BuilderId, "Agent test device", "file")).Written!.Id;
        Assert.True(f.Api.DeviceAdmit(new(device, f.Estate.OperatorId, "Admit the test agent.")).Ok);
        RecordEnvelope first = OperatorRequestTests.Request(f);
        RecordEnvelope second = OperatorRequestTests.Request(f, 2);

        CliHarness.Outcome omitted = CliHarness.Run("--estate", f.Estate.Root, "--json", "operator", "done", first.Id, "--by", f.Estate.BuilderId,
            "--operator", f.Estate.OperatorId, "--statement", "I have done it.", "--source", "Operator conversation, message 4");
        Assert.True(omitted.Code == 0, omitted.Out + omitted.Err);
        RecordEnvelope close = f.Estate.Load().ById[omitted.Id];
        string at = close.Body.GetProperty("operatorWords").GetProperty("at").GetString()!;
        Assert.True(RecordParser.IsTimestamp(at), at);
        Assert.True(string.CompareOrdinal(first.At, at) <= 0 && string.CompareOrdinal(at, close.At) <= 0, $"{first.At} <= {at} <= {close.At}");

        // A time given with fractional seconds and an offset is written as the same instant in whole seconds, UTC.
        string given = second.AtTime.ToOffset(TimeSpan.FromHours(2)).AddMilliseconds(250).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", System.Globalization.CultureInfo.InvariantCulture);
        CliHarness.Outcome fractional = CliHarness.Run("--estate", f.Estate.Root, "--json", "operator", "dismiss", second.Id, "--by", f.Estate.BuilderId, "--reason", "No longer needed.",
            "--operator", f.Estate.OperatorId, "--statement", "Drop it.", "--source", "Operator conversation, message 5", "--said-at", given);
        Assert.True(fractional.Code == 0, fractional.Out + fractional.Err);
        Assert.Equal(second.At, f.Estate.Load().ById[fractional.Id].Body.GetProperty("operatorWords").GetProperty("at").GetString());
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData("2026-11-24T10:00:00Z", "2026-11-24T10:00:00Z")]
    [InlineData("2026-11-24T10:00:00.999Z", "2026-11-24T10:00:00Z")]
    [InlineData("2026-11-24T12:00:00.5+02:00", "2026-11-24T10:00:00Z")]
    [InlineData("2026-11-24T10:00:00", "2026-11-24T10:00:00Z")]
    [InlineData("24 November 2026", "24 November 2026")]
    [InlineData("", "")]
    public void TheSaidAtTimeIsWrittenInWholeSecondsUtcAndAnythingElseIsLeftForTheRecordToRefuse(string given, string written)
    {
        Assert.Equal(written, OperatorRequestCloseCommand.SaidAt(given, DateTimeOffset.UnixEpoch));
        Assert.Equal("2026-11-24T10:00:00Z", OperatorRequestCloseCommand.SaidAt(null, new DateTimeOffset(2026, 11, 24, 10, 0, 0, 750, TimeSpan.Zero)));
    }
}
