// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// From 0.9.4 (FIX-152; design/performance.md § 3.5): human status's three calls (status, communication, attention) share one
/// derivation of the repository checks, the policy sections and the progress projection within the command, and work brief parses each
/// record once, counted in the runtime's own context on estates of two sizes; nothing derived is carried from one command to the next,
/// so a credential-shaped file added between two commands to one runtime shows in the second.
/// </summary>
[Collection("cli")]
public sealed class StatusDerivationCommandTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(15)]
    public void HumanStatusRunsTheRepositoryChecksOnceAndWorkBriefParsesEachRecordOnce(int orders)
    {
        using var fixture = Estate(orders);
        int records = Directory.EnumerateFiles(Path.Combine(fixture.Estate.Root, "records"), "*.json", SearchOption.AllDirectories).Count();

        var status = new JudgmentContext(new SignatureTables());
        using (EstateService service = OneShot(fixture.Estate.Root, status))
        {
            CliHarness.Outcome outcome = CliHarness.RunWithApi(service, null, "--estate", fixture.Estate.Root, "status");
            Assert.True(outcome.Code == 0, outcome.Err);
            Assert.Contains("Required attention", outcome.Out, StringComparison.Ordinal);
        }

        Assert.Equal((1L, 1L), (status.Judgments, status.RepositoryCheckPasses));

        var brief = new JudgmentContext(new SignatureTables());
        using (EstateService service = OneShot(fixture.Estate.Root, brief))
        {
            CliHarness.Outcome outcome = CliHarness.RunWithApi(service, null, "--estate", fixture.Estate.Root, "work", "brief", "TASK-000");
            Assert.True(outcome.Code == 0, outcome.Err);
        }

        Assert.Equal((1L, (long)records), (brief.Judgments, brief.Parses));
    }

    [Fact]
    public void ACredentialShapedFileAddedBetweenTwoCommandsToOneRuntimeShowsInTheSecond()
    {
        using var fixture = Estate(2);
        var judgments = new JudgmentContext(new SignatureTables());
        using EstateService service = OneShot(fixture.Estate.Root, judgments);
        CliHarness.Outcome first = CliHarness.RunWithApi(service, null, "--estate", fixture.Estate.Root, "status");
        Assert.DoesNotContain("secrets.pattern", first.Out, StringComparison.Ordinal);

        // Split, so the repository's own secret scan does not take the test's text for a credential.
        fixture.Estate.WriteFile("docs/notes.md", "# Notes\n\nkey " + "AKIA" + "ABCDEFGHIJKLMNOP" + "\n");
        CliHarness.Outcome second = CliHarness.RunWithApi(service, null, "--estate", fixture.Estate.Root, "status");
        Assert.Contains("secrets.pattern", second.Out, StringComparison.Ordinal);
        Assert.Contains("docs/notes.md", second.Out, StringComparison.Ordinal);
        // The records did not change, so the runtime kept its judgment, and each command ran the repository checks once.
        Assert.Equal((1L, 2L), (judgments.Judgments, judgments.RepositoryCheckPasses));
    }

    private static EstateService OneShot(string root, JudgmentContext judgments)
    {
        EstateService service = EstateService.Open(root, null, null, oneShot: true, judgments: judgments);
        service.HostsEmbedded = true;
        return service;
    }

    private static PolicyFixture Estate(int orders)
    {
        var fixture = new PolicyFixture();
        for (int i = 0; i < orders; i++)
        {
            RecordEnvelope order = fixture.Add("work-order", new()
            {
                ["key"] = "TASK-" + i.ToString("D3", CultureInfo.InvariantCulture), ["kind"] = "ordinary", ["title"] = "Counted work",
                ["intent"] = "Exercise the count.", ["scope"] = new JsonArray("test fixtures"), ["exitQuestion"] = "Is the result ready?",
                ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("checked result"),
            });
            RecordEnvelope attempt = fixture.BeginAttempt(order: order);
            fixture.Add("result", PolicyFixture.ResultBody(), builder: true, subject: attempt.Reference);
        }

        return fixture;
    }
}
