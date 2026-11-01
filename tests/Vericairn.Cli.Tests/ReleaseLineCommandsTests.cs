// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Cli;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class ReleaseLineCommandsTests
{
    [Theory]
    [InlineData("current", "2.1.0")]
    [InlineData("1.x", "1.9.0")]
    public void EachLinesGeneratedNotesShowTheBackport(string line, string previousVersion)
    {
        string output = CliHarness.TempDirectory("line-notes");
        try
        {
            using var f = new ReleaseLineFixture();
            f.Publish(ReleaseLineFixture.Body("1.9.0", "1.x", "lts"));
            RecordEnvelope source = f.Publish(ReleaseLineFixture.Body("2.1.0"));
            RecordEnvelope backport = f.Publish(ReleaseLineFixture.Body("1.9.1", "1.x", "lts", source));
            f.AssertValid();
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            int code = Docs.Notes(new CommandContext(text, error), f.Estate.Root, previousVersion, "next", output, null, true,
                backport.AtTime.AddSeconds(1), line);
            Assert.True(code == 0, error.ToString());
            string notes = File.ReadAllText(Path.Combine(output, "release-notes.md"));
            Assert.Contains("## Backports across lines", notes, StringComparison.Ordinal);
            Assert.Contains("1.9.1 (1.x) is a backport of 2.1.0 (current)", notes, StringComparison.Ordinal);
            Assert.Contains(source.Id, notes, StringComparison.Ordinal);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert.Equal(line, manifest.RootElement.GetProperty("line").GetString());
        }
        finally { CliHarness.Delete(output); }
    }

    [Theory]
    [InlineData("--line", "1.x")]
    [InlineData("--channel", "nightly")]
    [InlineData("--end-of-support", "2028-01-01T00:00:00Z")]
    public void SubmissionFlagsReachValidationAndFormatTwoRefusesThem(string option, string value)
    {
        CliHarness.Delete(CliHarness.TempDirectory("line-command-bootstrap"));
        using var f = new ReleaseLineFixture(2);
        using var api = EstateService.Open(f.Estate.Root);
        var provider = new FakeReleaseProvider();
        api.Providers = new ProviderRegistry();
        api.Providers.Register(provider);
        ReleaseGateSource.Prepare(api, "Example/widget", provider.HeadSha);
        CliHarness.Outcome outcome = CliHarness.RunWithApi(api, null, "--estate", f.Estate.Root, "--json", "release", "submit", "--check",
            "--product", f.Product.Id, "--version", "1.1.0", "--by", f.Estate.OperatorId, "--provider", provider.Provider,
            "--repository", "Example/widget", "--commit", provider.HeadSha, "--expected", f.Product.ComputedHash, option, value);
        Assert.Equal(2, outcome.Code);
        Assert.Contains("body.format", outcome.Out, StringComparison.Ordinal);
        Assert.Empty(provider.Dispatches);
        Assert.Empty(api.Records("operation"));
    }

    [Fact]
    public void ABackportArgumentNeedsItsExactHash()
    {
        using var f = new ReleaseLineFixture();
        CliHarness.Outcome result = CliHarness.Run("--estate", f.Estate.Root, "release", "submit", "--product", f.Product.Id,
            "--version", "1.1.0", "--by", f.Estate.OperatorId, "--backport-of", f.Product.Id);
        Assert.Equal(2, result.Code);
        Assert.Contains("--backport-of and --backport-hash are supplied together", result.Err, StringComparison.Ordinal);
        Assert.Empty(f.Estate.Load().OfType("operation"));
    }
}
