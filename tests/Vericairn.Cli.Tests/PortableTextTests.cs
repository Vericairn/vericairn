// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Estate;

namespace Vericairn.Cli.Tests;

/// <summary>
/// What estate verify and docs pass --before record carries no machine's path on any platform: a Windows drive or pipe, a Unix root,
/// a temporary directory. Windows-style roots and addresses are simulated here, so every platform checks them.
/// </summary>
public sealed class PortableTextTests
{
    [Theory]
    [InlineData(@"cannot reach C:\Users\op\other-estate\records")]
    [InlineData(@"the daemon at \\.\pipe\vericairn-4242 answered")]
    [InlineData(@"stored under D:/estates/example")]
    [InlineData("cannot open /home/op/estate/records/x.json")]
    [InlineData("in /tmp/vericairn-test/estate")]
    [InlineData("(/var/folders/ab/T/estate-verify)")]
    [InlineData("/Users/op/estate")]
    public void EveryMachinePathIsReplaced(string text)
    {
        string portable = PortableText.Of(text);
        Assert.Contains("<path>", portable, StringComparison.Ordinal);
        Assert.False(PortableText.HasMachinePath(portable), portable);
        Assert.False(SecretShapes.AbsolutePath().IsMatch(portable), portable);
    }

    [Theory]
    [InlineData("records/verification/2026-11/01M6VJK9B34RDJ5JCTTCDW2KEP.json")]
    [InlineData("docs/install.md:3: prior-version token 0.9.1")]
    [InlineData("see https://example.invalid/a/b and ./bootstrap runtime start")]
    [InlineData(@"or '.\bootstrap.ps1 runtime start' on Windows")]
    [InlineData("<estate>/records/estate/2026-10/01M6VJK9AXWT4A4435HK5BR4SP.json")]
    [InlineData("1/2 of 0.9.1/0.9.2")]
    public void RepositoryRelativePathsAndAddressesAreKept(string text) => Assert.Equal(text, PortableText.Of(text));

    [Fact]
    public void AKnownRootIsNamedByWhatTheRecordCallsIt()
    {
        Assert.Equal("<estate>/records/x.json is invalid", PortableText.Of(@"C:\Users\op\estate/records/x.json is invalid", (@"C:\Users\op\estate", "<estate>")));
        Assert.Equal("<estate>/records/x.json is invalid", PortableText.Of("/home/op/estate/records/x.json is invalid", ("/home/op/estate", "<estate>")));
    }

    [Theory]
    [InlineData(@"C:\Users\op\estate", @"C:\Users\op\elsewhere", @"\\.\pipe\vericairn-4242")]
    [InlineData("/home/op/estate", "/home/op/elsewhere", "/tmp/vericairn-501/daemon.sock")]
    public void EstateVerifyRecordsNoPathOfTheMachineItRanOn(string root, string other, string address)
    {
        FindingsResponse check = new("1 error", 1, 0, [new FindingDto("error", "record.invalid", "records/estate/2026-10/01M6VJK9AXWT4A4435HK5BR4SP.json", $"{root}{Path.DirectorySeparatorChar}records is unreadable from {other}")]);
        string where = "the daemon serving it here (process 4242)";
        foreach ((bool elsewhere, bool passed) in new[] { (true, false), (false, false), (false, true) })
        {
            string runtime = EstateVerification.RuntimeSummary(elsewhere, "01M6VJK9AXWT4A4435HK5BR4SP", passed ? "0.9.2" : "0.9.1", "0.9.2", where, passed);
            string estate = EstateVerification.EstateSummary(root, passed, runtime, "0.9.2", check, $"the status could not be read through {address} at {root}");
            foreach ((string method, string summary) in new[] { (EstateVerifyCommand.ServingRuntimeMethod, runtime), (EstateVerifyCommand.EstateCheckMethod, estate) })
            {
                var body = EstateVerification.Body(method, passed, PortableText.Of(summary, (root, "<estate>")), "0.9.2", passed ? "0.9.2" : "0.9.1", "01M6VJK9B34RDJ5JCTTCDW2KER", new string('e', 40),
                    [$"The estate's working tree at {root} had changes."], recordFormat: 3);
                string text = body.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

                // The portability rule the estate check applies to every line of a record, and the stricter rule for every rooted path.
                Assert.DoesNotContain(text.Split('\n'), line => SecretShapes.AbsolutePath().IsMatch(line));
                Assert.False(PortableText.HasMachinePath(body["summary"]!.GetValue<string>() + "\n" + string.Join("\n", body["limitations"]!.AsArray().Select(l => l!.GetValue<string>()))), text);
                Assert.DoesNotContain(root, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(address, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
