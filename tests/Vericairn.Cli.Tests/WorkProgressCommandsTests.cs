// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class WorkProgressCommandsTests
{
    [Theory]
    [InlineData("{\"type\":\"accurate\",\"done\":7,\"total\":20,\"unit\":\"files\"}", "7 / 20 files")]
    [InlineData("{\"type\":\"best-effort\",\"percent\":60}", "Best-effort estimate 60%")]
    [InlineData("{\"type\":\"not-measurable\"}", "In progress (not measurable)")]
    [InlineData("{\"type\":\"accurate\",\"done\":7,\"total\":20,\"unit\":\"files\",\"estimatePercent\":60}", "7 / 20 files; best-effort estimate 60%")]
    [InlineData("{\"type\":\"accurate\",\"done\":7,\"unit\":\"files\"}", "7 files")]
    public void CommandRetainsEachTypedValueAndLabelsCountsAndEstimates(string value, string expected)
    {
        string scratch = CliHarness.TempDirectory("progress-command");
        try
        {
            using var fixture = new ProgressFixture();
            fixture.Set("brief", ProgressRequestTests.FullRequest);
            using EstateService service = EstateService.Open(fixture.Estate.Root);
            string file = CliHarness.WriteJson(scratch, "progress.json", value);
            string[] arguments = ["--estate", fixture.Estate.Root, "work", "progress", fixture.Attempt.Id, "--by", fixture.Estate.BuilderId,
                "--phase", "review", "--note", "Reviewing the widget.", "--state", "active", "--status-text", "Reading the widget."];
            int records = service.Session.Load().Records.Count;
            CliHarness.Outcome text = CliHarness.RunWithApi(service, null, [.. arguments, "--progress", file]);
            Assert.Equal(0, text.Code);
            Assert.Contains(expected, text.Out, StringComparison.Ordinal);
            Assert.Contains("Reading the widget.", text.Out, StringComparison.Ordinal);
            Assert.Contains("Live state; no record written.", text.Out, StringComparison.Ordinal);
            Assert.Equal(records, service.Session.Load().Records.Count);
            CliHarness.Outcome json = CliHarness.RunWithApi(service, null, ["--json", .. arguments, "--progress", file]);
            Assert.Equal(0, json.Code);
            using JsonDocument original = JsonDocument.Parse(value);
            Assert.True(JsonElement.DeepEquals(original.RootElement, json.Json.GetProperty("claim").GetProperty("progress")));
            Assert.Equal("Reading the widget.", json.Json.GetProperty("claim").GetProperty("statusText").GetString());
            Assert.Equal(JsonValueKind.Null, json.Json.GetProperty("written").ValueKind);

            arguments[Array.IndexOf(arguments, "active")] = "completed";
            CliHarness.Outcome completed = CliHarness.RunWithApi(service, null, ["--json", .. arguments, "--progress", file]);
            Assert.Equal(0, completed.Code);
            Assert.Equal(records + 1, service.Session.Load().Records.Count);
            CliHarness.Outcome retry = CliHarness.RunWithApi(service, null, ["--json", .. arguments, "--progress", file]);
            Assert.Equal(completed.Json.GetProperty("written").GetProperty("id").GetString(), retry.Json.GetProperty("written").GetProperty("id").GetString());
            Assert.Equal(records + 1, service.Session.Load().Records.Count);
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void CommandRefusesMalformedClaimsAndMissingRequiredStatusWithoutWriting()
    {
        string scratch = CliHarness.TempDirectory("progress-refused");
        try
        {
            using var fixture = new ProgressFixture();
            fixture.Set("estate", ProgressRequestTests.FullRequest);
            using EstateService service = EstateService.Open(fixture.Estate.Root);
            string[] arguments = ["--estate", fixture.Estate.Root, "--json", "work", "progress", fixture.Attempt.Id, "--by", fixture.Estate.BuilderId,
                "--phase", "review", "--note", "Reviewing the widget.", "--state", "active"];
            int records = service.Session.Load().Records.Count;
            // The in-process harness cannot feed the raw standard input that "-" reads, so each claim is passed as a file; a parse
            // failure of an empty input must not stand in for the refusals under test.
            int index = 0;
            string Claim(string value) => CliHarness.WriteJson(scratch, $"claim-{index++}.json", value);
            string measurable = Claim("""{"type":"not-measurable"}""");
            foreach (string malformed in new[] { "{}", "null", """{"type":"best-effort","percent":101}""", """{"type":"accurate","done":2,"total":1,"unit":"files"}""" })
                Assert.NotEqual(0, CliHarness.RunWithApi(service, null, [.. arguments, "--progress", Claim(malformed), "--status-text", "Reading the widget."]).Code);
            Assert.NotEqual(0, CliHarness.RunWithApi(service, null, [.. arguments, "--progress", measurable]).Code);
            arguments[Array.IndexOf(arguments, "active")] = "unknown";
            Assert.NotEqual(0, CliHarness.RunWithApi(service, null, [.. arguments, "--progress", measurable, "--status-text", "Reading the widget."]).Code);
            Assert.Equal(records, service.Session.Load().Records.Count);
            Assert.Null(service.Session.Live.Meta("work-progress:" + fixture.Attempt.Id));
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public void CommandKeepsUnrequestedLegacyNotesAndDiscoversTheNewOptions()
    {
        string scratch = CliHarness.TempDirectory("progress-legacy");
        try
        {
            using var fixture = new ProgressFixture(2);
            using EstateService service = EstateService.Open(fixture.Estate.Root);
            CliHarness.Outcome result = CliHarness.RunWithApi(service, null, "--estate", fixture.Estate.Root, "--json", "work", "progress", fixture.Attempt.Id,
                "--by", fixture.Estate.BuilderId, "--phase", "review", "--note", "Reading the widget.", "--state", "active");
            Assert.Equal(0, result.Code);
            Assert.False(result.Json.GetProperty("claim").TryGetProperty("progress", out _));
            Assert.Empty(service.Records("work-progress"));
            string help = CliHarness.Ok("work", "progress", "--help").Out;
            foreach (string option in new[] { "--by", "--phase", "--note", "--state", "--progress", "--status-text" })
                Assert.Contains(option, help, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(scratch); }
    }
}
