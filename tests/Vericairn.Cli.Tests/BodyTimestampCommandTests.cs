// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Cli.Tests;

/// <summary>A record whose body timestamp is in another form is refused with the field's name and the required form, and nothing is written.</summary>
[Collection("cli")]
public sealed class BodyTimestampCommandTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Form = "a UTC time in whole seconds, YYYY-MM-DDTHH:MM:SSZ, such as 2026-10-13T14:00:00Z, without fractional seconds or an offset";

    [Theory]
    [InlineData("\"asOf\":\"2026-10-13T14:00:00.123456Z\",", "body.timestamp", "'asOf' must be " + Form)]
    [InlineData("\"asOf\":\"2026-10-13T14:00:00+02:00\",", "body.timestamp", "'asOf' must be " + Form)]
    [InlineData("\"asOf\":\"13 October 2026\",", "body.timestamp", "'asOf' must be " + Form)]
    [InlineData("", "body.missing", "'asOf' is required: " + Form)]
    public void AnObservationTimestampInAnotherFormIsRefusedWithTheRequiredFormAndNothingIsWritten(string asOf, string code, string message)
    {
        string root = CliHarness.TempDirectory("body-timestamp");
        string scratch = CliHarness.TempDirectory("body-timestamp-scratch");
        try
        {
            (string op, string subject) = Initialize(root);
            int before = CommandContext.LoadCopy(root).Records.Count;
            string body = CliHarness.WriteJson(scratch, "observation.json", "{\"kind\":\"repository\"," + asOf + "\"summary\":\"Observed the charter.\",\"facts\":{},\"limitations\":[]}");

            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "records", "new", "observation", "--by", op, "--body", body, "--subject", subject);

            Assert.NotEqual(0, refused.Code);
            JsonElement finding = Assert.Single(refused.Json.GetProperty("findings").EnumerateArray());
            Assert.Equal(code, finding.GetProperty("Code").GetString());
            Assert.EndsWith("#body.asOf", finding.GetProperty("Location").GetString(), StringComparison.Ordinal);
            Assert.Equal(message + ".", finding.GetProperty("Message").GetString());
            Assert.Equal(before, CommandContext.LoadCopy(root).Records.Count);
        }
        finally { CliHarness.Delete(root, scratch); }
    }

    [Fact]
    public void AWholeSecondUtcTimestampIsWrittenAndDescribeShowsTheForm()
    {
        string root = CliHarness.TempDirectory("body-timestamp-accepted");
        string scratch = CliHarness.TempDirectory("body-timestamp-accepted-scratch");
        try
        {
            (string op, string subject) = Initialize(root);
            string body = CliHarness.WriteJson(scratch, "observation.json", """{"kind":"repository","asOf":"2026-10-13T14:00:00Z","summary":"Observed the charter.","facts":{},"limitations":[]}""");
            string id = CliHarness.Ok("--estate", root, "--json", "records", "new", "observation", "--by", op, "--body", body, "--subject", subject).Id;
            Assert.Equal("2026-10-13T14:00:00Z", CommandContext.LoadCopy(root).ById[id].Body.GetProperty("asOf").GetString());

            string described = CliHarness.Ok("records", "describe", "observation").Out;
            Assert.Contains("asOf               timestamp (YYYY-MM-DDTHH:MM:SSZ)", described, StringComparison.Ordinal);
            Assert.Contains("A timestamp is a UTC time in whole seconds, YYYY-MM-DDTHH:MM:SSZ, such as 2026-10-13T14:00:00Z", described, StringComparison.Ordinal);
            Assert.DoesNotContain("A timestamp is", CliHarness.Ok("records", "describe", "work-order").Out, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(root, scratch); }
    }

    private static (string Operator, string Subject) Initialize(string root)
    {
        string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Timestamps.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
        RecordEnvelope charter = CommandContext.LoadCopy(root).CurrentEstate!;
        return (op, $"estate:{charter.Id}:{charter.ComputedHash}");
    }
}
