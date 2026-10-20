// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// Until 0.8.1 the scan extended gitleaks' default configuration, whose built-in allowlists passed an AWS key id ending in
/// EXAMPLE and any secret under an exempt path, such as a lock file, a vendored tree or a document extension.
/// </summary>
public sealed partial class SecretScanConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vericairn-scan-test-" + Guid.NewGuid().ToString("N"));

    private string Repository => Path.Combine(root, "work");

    [Fact]
    public void TheConfigurationKeepsEveryRuleAndOnlyVericairnsOwnAllowances()
    {
        string text = Encoding.UTF8.GetString(GitleaksScanner.Configuration);
        Assert.DoesNotContain("useDefault", text, StringComparison.Ordinal);
        Assert.Equal(222, Regex.Count(text, @"^\[\[rules\]\]$", RegexOptions.Multiline));
        Assert.DoesNotMatch(new Regex(@"^\[allowlist\]$|^\[\[rules\.allowlists?\]\]$|^\[rules\.allowlist\]$", RegexOptions.Multiline), text);
        Assert.DoesNotMatch(new Regex(@"^paths\s*=", RegexOptions.Multiline), text);
        string[] rules = Regex.Matches(text, @"^id = ""(?<id>[a-z0-9-]+)""$", RegexOptions.Multiline).Select(m => m.Groups["id"].Value).ToArray();
        Assert.Contains("aws-access-token", rules);
        Assert.Contains("github-pat", rules);

        // Every allowance follows the rules, targets rules that exist and admits no sample credential.
        string allowances = text[text.IndexOf("[[allowlists]]", StringComparison.Ordinal)..];
        Assert.DoesNotContain("[[rules]]", allowances, StringComparison.Ordinal);
        Assert.Equal(9, Regex.Count(allowances, @"^\[\[allowlists\]\]$", RegexOptions.Multiline));
        Assert.DoesNotContain("EXAMPLE", allowances, StringComparison.Ordinal);
        Assert.DoesNotContain("AIza", allowances, StringComparison.Ordinal);
        foreach (Match target in Regex.Matches(allowances, @"^targetRules = \[""(?<id>[a-z0-9-]+)""\]$", RegexOptions.Multiline))
            Assert.Contains(target.Groups["id"].Value, rules);
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(GitleaksScanner.Configuration)), GitleaksScanner.ConfigurationHash);
    }

    [Theory]
    [InlineData("aws", "settings.txt")]
    [InlineData("github", "package-lock.json")]
    [InlineData("github", "vendor/github.com/example/widget/client.go")]
    [InlineData("github", "node_modules/widget/index.js")]
    [InlineData("github", "docs/handbook.docx")]
    [InlineData("github", "brand/mark.svg")]
    public void ASecretThatTheBuiltInAllowlistsPassedIsFound(string kind, string path)
    {
        // Assembled at run time, so that this file holds no credential-shaped text of its own.
        string secret = kind == "aws" ? "AKIA" + "IOSFODNN7" + "EXAMPLE" : "ghp" + "_" + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 36);
        SecretScan scan = ScanAddition(path, "credential = " + secret + "\n");
        Assert.Equal("findings", scan.Verdict);
        Assert.True(scan.FindingCount > 0);
    }

    [Theory]
    [InlineData("api_key = \"${WIDGET_API_KEY}\"\n")]
    [InlineData("token = \"widget-release-notes\"\n")]
    [InlineData("A plain sentence about keys and tokens.\n")]
    public void ValuesThatAreNotCredentialsPass(string content)
    {
        SecretScan scan = ScanAddition("notes.txt", content);
        Assert.Equal("passed", scan.Verdict);
        Assert.EndsWith("; rules " + GitleaksScanner.ConfigurationHash, scan.Version, StringComparison.Ordinal);
    }

    private SecretScan ScanAddition(string path, string content)
    {
        string? gitleaks = Environment.GetEnvironmentVariable("VERICAIRN_TEST_GITLEAKS");
        Assert.SkipWhen(gitleaks is null, "Set VERICAIRN_TEST_GITLEAKS to an installed gitleaks of at least 8.25.0 to scan real commits.");
        TestPrerequisites.RequireGit();
        Directory.CreateDirectory(Repository);
        File.WriteAllText(Path.Combine(root, "gitconfig"), string.Empty);
        Git("init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(Repository, "readme.txt"), "Initial content.\n");
        Git("add", "readme.txt");
        Git("commit", "-m", "Initial content");
        string basis = Git("rev-parse", "HEAD");
        string file = Path.Combine(Repository, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        Git("add", "--", path);
        Git("commit", "-m", "Add content");
        string head = Git("rev-parse", "HEAD");
        var scanner = new GitleaksScanner(info =>
        {
            if (info.FileName == "gitleaks") info.FileName = gitleaks!;
            return BoundedProcess.Run(info, TestTiming.HangTimeout);
        });
        return scanner.Scan(Repository, new PushPlan("work/change", "main", head, basis, null, basis));
    }

    private string Git(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = Repository, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "gitconfig");
        foreach (string value in new[] { "user.name=Test", "user.email=test@example.invalid", "commit.gpgsign=false", "core.autocrlf=false" })
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(value);
        }
        foreach (string argument in args) info.ArgumentList.Add(argument);
        ProcessResult result = BoundedProcess.Run(info, TestTiming.HangTimeout);
        Assert.True(result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0, "git " + string.Join(' ', args) + ": " + result.StandardError);
        return result.StandardOutput.Trim();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) WorkspaceCleanup.Delete(root);
    }
}
