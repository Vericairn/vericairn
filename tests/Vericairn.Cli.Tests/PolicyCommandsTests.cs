// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PolicyCommandsTests
{
    [Fact]
    public void WritingReadsSkipGeneratedCopiesAndRetainAuthoredAndRecordWarnings()
    {
        CliHarness.Ok("policies", "--help");
        using var fixture = new GeneratedWritingFixture();
        string root = fixture.Estate.Root;
        int before = fixture.Estate.Load().Records.Count;
        FindingsResponse check = JsonSerializer.Deserialize<FindingsResponse>(CliHarness.Ok("--estate", root, "estate", "check", "--json").Out, ProtocolJson.Options)!;
        WritingExceptionsResponse writing = JsonSerializer.Deserialize<WritingExceptionsResponse>(CliHarness.Ok("--estate", root, "estate", "writing-exceptions", "list", "--json").Out, ProtocolJson.Options)!;
        Assert.True(writing.Complete);
        Assert.Equal(fixture.ExpectedLocations, check.Findings.Where(f => f.Code == "writing.spacing").Select(f => f.Location).Order(StringComparer.Ordinal));
        Assert.Equal(fixture.ExpectedLocations.Count, writing.Warnings.Count);
        Assert.Equal(fixture.ExpectedLocations, writing.Warnings.Select(w => w.Occurrence.Diagnostic.Location).Order(StringComparer.Ordinal));
        Assert.Empty(writing.ExceptedOccurrences);
        string expected = JsonSerializer.Serialize(writing, ProtocolJson.Options);
        Assert.Equal(expected, JsonSerializer.Serialize(check.WritingExceptions, ProtocolJson.Options));
        Assert.Equal(expected, CliHarness.Ok("--estate", root, "status", "--json").Json.GetProperty("writingExceptions").GetRawText());
        Assert.Equal(expected, CliHarness.Ok("--estate", root, "progress", "--json").Json.GetProperty("writingExceptions").GetRawText());
        Assert.Equal(before, fixture.Estate.Load().Records.Count);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void ReadCommandsMatchReleaseDutiesAcrossProductRevisions(int revision, bool matches)
    {
        CliHarness.Ok("policies", "--help");
        using var fixture = new PolicyFixture();
        RecordEnvelope[] products = Revisions("fixture");
        RecordEnvelope[] others = Revisions("other");
        JsonObject definition = fixture.PolicyBody();
        JsonNode parameters = definition["rules"]![0]!["parameters"]!;
        parameters["schedule"] = new JsonObject { ["kind"] = "release", ["product"] = products[0].Id };
        parameters["graceDays"] = 0;
        fixture.Policy(definition);
        fixture.Estate.SetClock(new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.Zero));
        RecordEnvelope candidate = fixture.Add("release", new()
        {
            ["version"] = "0.8.0", ["repository"] = "Example/fixture", ["commit"] = new string('1', 40), ["status"] = "candidate",
            ["artifacts"] = new JsonArray(new JsonObject { ["name"] = "fixture", ["hash"] = ContentHash.Sha256("fixture"u8), ["bytes"] = 7, ["platform"] = "any" }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 },
            ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "Fixture only", ["limitations"] = new JsonArray(),
        }, subject: (matches ? products : others)[revision].Reference);
        fixture.Add("acceptance", new() { ["statement"] = "Accept the fixture publication.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        JsonObject published = PolicyFixture.Body(candidate);
        published["status"] = "published";
        published["publishedAt"] = RecordParser.FormatTimestamp(fixture.Estate.Clock);
        published["url"] = "https://example.invalid/releases/fixture";
        RecordEnvelope publication = fixture.Add("release", published, subject: candidate.Subject, supersedes: candidate.Id);
        fixture.AssertValid();
        int before = fixture.Estate.Load().Records.Count;
        string root = fixture.Estate.Root;
        string[][] commands = [["list"], ["check"], ["show", "maintenance"]];
        foreach (string[] command in commands)
        {
            CliHarness.Outcome read = CliHarness.Ok(["--estate", root, "--json", "policies", .. command]);
            PolicyResponse response = JsonSerializer.Deserialize<PolicyResponse>(read.Out, ProtocolJson.Options)!;
            Assert.True(response.Complete);
            Assert.Equal("recorded", response.Basis.Origin);
            Assert.Equal(matches ? 1 : 0, Assert.Single(response.Rules).Subjects);
            Assert.Equal(matches ? 1 : 0, response.Holds);
            if (matches)
            {
                PolicyFindingDto finding = Assert.Single(response.Findings);
                Assert.Equal("unmet", finding.Verdict);
                Assert.Equal(publication.Id, finding.Duty!.Occurrence.GetProperty("publication").GetProperty("id").GetString());
                Assert.Equal(publication.ComputedHash, finding.Duty.Occurrence.GetProperty("publication").GetProperty("hash").GetString());
                Assert.Equal(publication.At, finding.Duty.DueAt);
            }
            else
            {
                PolicyFindingDto scheduled = Assert.Single(response.Findings);
                Assert.Equal("satisfied", scheduled.Verdict);
                Assert.Equal("scheduled", scheduled.Disposition);
                Assert.Equal(0, scheduled.SubjectCount);
                Assert.Null(scheduled.Duty);
                Assert.False(scheduled.ReleaseHold);
                Assert.Empty(scheduled.Evidence);
            }
        }
        Assert.Equal(before, fixture.Estate.Load().Records.Count);

        RecordEnvelope[] Revisions(string slug)
        {
            RecordEnvelope product = fixture.Add("product", new()
            {
                ["name"] = "Fixture product", ["slug"] = slug, ["summary"] = "Synthetic publication history.",
                ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Fixture edition." }),
                ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Fixture surface." }),
            });
            RecordEnvelope revised = fixture.Add("product", PolicyFixture.Body(product), supersedes: product.Id);
            RecordEnvelope latest = fixture.Add("product", PolicyFixture.Body(revised), supersedes: revised.Id);
            return [product, revised, latest];
        }
    }

    [Fact]
    public void ReadCommandsShowTheirBasisAndDoNotAuthorRecords()
    {
        CliHarness.Ok("policies", "--help");
        using var fixture = new PolicyFixture();
        fixture.Policy();
        int before = fixture.Estate.Load().Records.Count;
        string root = fixture.Estate.Root;
        CliHarness.Outcome list = CliHarness.Ok("--estate", root, "policies", "list", "--json");
        Assert.Equal("recorded", list.Json.GetProperty("basis").GetProperty("origin").GetString());
        CliHarness.Outcome preview = CliHarness.Ok("--estate", root, "policies", "check", "--as-of", "2026-10-16T13:00:00Z", "--json");
        Assert.Equal(1, preview.Json.GetProperty("holds").GetInt32());
        Assert.Contains("Basis sha256:", CliHarness.Ok("--estate", root, "policies", "show", "maintenance").Out, StringComparison.Ordinal);
        Assert.Contains("recurring-duty", CliHarness.Ok("--estate", root, "policies", "kinds").Out, StringComparison.Ordinal);
        Assert.Contains("Writing exceptions", CliHarness.Ok("--estate", root, "estate", "writing-exceptions", "list").Out, StringComparison.Ordinal);
        Assert.Equal(list.Json.GetProperty("basis").GetProperty("digest").GetString(), CliHarness.Ok("--estate", root, "estate", "check", "--json").Json.GetProperty("policies").GetProperty("basis").GetProperty("digest").GetString());
        Assert.Contains("Policies\n", CliHarness.Ok("--estate", root, "status").Out, StringComparison.Ordinal);
        Assert.Contains("Writing exceptions\n", CliHarness.Ok("--estate", root, "progress").Out, StringComparison.Ordinal);
        Assert.Equal(2, CliHarness.Run("--estate", root, "policies", "show", "absent").Code);
        Assert.Equal(2, CliHarness.Run("--estate", root, "estate", "writing-exceptions", "show", "absent").Code);
        Assert.Equal(before, fixture.Estate.Load().Records.Count);
    }
}
