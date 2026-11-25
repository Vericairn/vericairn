// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-172 (issue 439): a secrets.pattern finding on one long line of an offline HTML report holding several embedded images named only
/// the path and the line, so a reviewer could not tell which element needed attention without reading a large block of encoded data. Each
/// finding now names the match's columns and, on a line holding inline data such as data: URIs, the ordinal of the element it lies in,
/// and carries nothing of the line: no matched content, inline data or surrounding text. A credential keeps its severity.
/// </summary>
public sealed class CredentialFindingTests
{
    private const string Path = "docs/report.html";

    [Fact]
    public void AFindingOnALongLineNamesTheElementByItsOrdinalAndColumnsWithNothingOfTheLine()
    {
        InlineDataReport report = InlineDataReport.Generate(4, 3);
        Finding finding = Assert.Single(EstateChecks.Content(Path, Encoding.UTF8.GetBytes(report.Text)), f => f.Code == "secrets.pattern");
        (string credential, int column) = report.Credentials[3];
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal($"{Path}:{InlineDataReport.LineNumber}", finding.Location);
        Assert.Equal($"Looks like a credential at columns {column}-{column + credential.Length - 1}, in inline data element 3 of 4 on this line (data: URIs). "
            + "Decode that element to check it: its file type alone does not show that its content is safe. "
            + "Credentials never enter the estate; reference them by purpose instead.", finding.Message);
        // The columns locate the value exactly, and the ordinal the element that holds it.
        Assert.Equal(credential, report.Line.Substring(column - 1, credential.Length));
        CarriesNothingOfTheLine(finding, report);
    }

    // Several matches on one line are several findings, each with its own columns and element, and a match in the line's own text is placed
    // outside the elements.
    [Fact]
    public void EachMatchOnTheLineIsItsOwnFindingInsideOrOutsideTheElements()
    {
        InlineDataReport report = InlineDataReport.Generate(5, 2, 4);
        string token = "ghp_" + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 36);
        string text = report.Text.Replace("Figure 5: ", "Figure 5 " + token + ": ", StringComparison.Ordinal);
        string line = text.Split('\n')[InlineDataReport.LineNumber - 1];
        Finding[] findings = [.. EstateChecks.Content(Path, Encoding.UTF8.GetBytes(text)).Where(f => f.Code == "secrets.pattern")];
        Assert.Equal(3, findings.Length);
        Assert.All(findings, f => Assert.Equal(Severity.Error, f.Severity));
        Assert.All(findings, f => Assert.Equal($"{Path}:{InlineDataReport.LineNumber}", f.Location));
        foreach (int element in new[] { 2, 4 })
        {
            (string credential, int column) = report.Credentials[element];
            Assert.Single(findings, f => f.Message.StartsWith($"Looks like a credential at columns {column}-{column + credential.Length - 1}, in inline data element {element} of 5 on this line (data: URIs).", StringComparison.Ordinal));
        }

        int at = line.IndexOf(token, StringComparison.Ordinal) + 1;
        Assert.Single(findings, f => f.Message == $"Looks like a credential at columns {at}-{at + token.Length - 1}, outside the 5 inline data elements on this line (data: URIs). "
            + "Credentials never enter the estate; reference them by purpose instead.");
        foreach (Finding finding in findings)
        {
            CarriesNothingOfTheLine(finding, report);
            Assert.DoesNotContain(token[..8], finding.ToString(), StringComparison.Ordinal);
        }
    }

    // A credential on an ordinary line keeps its severity and gains only its columns; "metadata:" is no inline data.
    [Theory]
    [InlineData("docs/notes.md", "token: {0}\n", 8)]
    [InlineData("docs/notes.md", "The metadata:,value field holds {0} for now.\n", 33)]
    [InlineData("records/operation/2026-11/example.json", "{{\"note\":\"{0}\"}}\n", 10)]
    public void AGenuineCredentialKeepsItsSeverityAndGainsItsColumns(string path, string form, int column)
    {
        string token = "ghp_" + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 36);
        string text = string.Format(System.Globalization.CultureInfo.InvariantCulture, form, token);
        Finding finding = Assert.Single(EstateChecks.Content(path, Encoding.UTF8.GetBytes(text)), f => f.Code == "secrets.pattern");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal(path + ":1", finding.Location);
        Assert.Equal($"Looks like a credential at columns {column}-{column + token.Length - 1}. Credentials never enter the estate; reference them by purpose instead.", finding.Message);
        Assert.DoesNotContain(token[..8], finding.ToString(), StringComparison.Ordinal);
    }

    // Neither the finding's text nor its JSON holds the matched value, any element's encoded data or any text around them: no run of eight
    // characters of the line appears in either.
    private static void CarriesNothingOfTheLine(Finding finding, InlineDataReport report)
    {
        foreach (string shown in new[] { finding.ToString(), JsonSerializer.Serialize(finding) })
        {
            foreach ((string credential, _) in report.Credentials.Values) Assert.DoesNotContain(credential[..8], shown, StringComparison.Ordinal);
            foreach (string data in report.Data) Assert.DoesNotContain(data[..8], shown, StringComparison.Ordinal);
            foreach (string words in InlineDataReport.Surrounding) Assert.DoesNotContain(words, shown, StringComparison.Ordinal);
            for (int i = 0; i + 8 <= report.Line.Length; i++) Assert.False(shown.Contains(report.Line.AsSpan(i, 8), StringComparison.Ordinal), $"The finding carries the line's text at column {i + 1}.");
        }
    }
}
