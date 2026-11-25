// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// FIX-172 (issue 439): `estate check` over an estate holding a generated report with several inline diagrams on one line names the
/// affected diagram by its ordinal and the match's columns, in its text and its JSON diagnostics, with no matched content, inline data
/// or surrounding text, and the credential stays an error.
/// </summary>
[Collection("cli")]
public sealed class CredentialFindingJourneyTests
{
    [Fact]
    public void EstateCheckNamesTheElementOnALongLineWithoutPrintingAnyOfIt()
    {
        string root = CliHarness.TempDirectory("credential-element");
        try
        {
            CliHarness.Ok("--estate", root, "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Credential findings.", "--operator-name", "Operator", "--organization", "Example",
                "--commit", new string('a', 40));
            InlineDataReport report = InlineDataReport.Generate(4, 3);
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "docs", "report.html"), report.Text);
            (string credential, int column) = report.Credentials[3];
            string location = "docs/report.html:" + InlineDataReport.LineNumber;
            string where = $"at columns {column}-{column + credential.Length - 1}, in inline data element 3 of 4 on this line (data: URIs).";

            CliHarness.Outcome text = CliHarness.Run("--estate", root, "estate", "check");
            Assert.NotEqual(0, text.Code);
            string line = Assert.Single(text.Out.Split('\n'), l => l.Contains("secrets.pattern", StringComparison.Ordinal));
            Assert.StartsWith("ERROR secrets.pattern " + location + ": Looks like a credential " + where, line.Trim(), StringComparison.Ordinal);

            CliHarness.Outcome json = CliHarness.Run("--estate", root, "--json", "estate", "check");
            Assert.NotEqual(0, json.Code);
            JsonElement finding = Assert.Single(json.Json.GetProperty("findings").EnumerateArray(), f => Text(f, "code") == "secrets.pattern");
            Assert.Equal("error", Text(finding, "severity"));
            Assert.Equal(location, Text(finding, "location"));
            Assert.StartsWith("Looks like a credential " + where, Text(finding, "message"), StringComparison.Ordinal);

            foreach (string shown in new[] { text.Out + text.Err, json.Out + json.Err })
            {
                Assert.DoesNotContain(credential[..8], shown, StringComparison.Ordinal);
                foreach (string data in report.Data) Assert.DoesNotContain(data[..8], shown, StringComparison.Ordinal);
                foreach (string words in InlineDataReport.Surrounding) Assert.DoesNotContain(words, shown, StringComparison.Ordinal);
            }
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    // A finding's member, whatever the case of its name: the JSON check names severity in lower case and the other members as the
    // finding record does.
    private static string? Text(JsonElement finding, string name) =>
        finding.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value.GetString();
}
