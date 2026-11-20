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
        // The one path condition is the inventory allowance's (FIX-150), which holds only together with its line's shape.
        Assert.Equal(1, Regex.Count(text, @"^paths\s*=", RegexOptions.Multiline));
        string[] rules = Regex.Matches(text, @"^id = ""(?<id>[a-z0-9-]+)""$", RegexOptions.Multiline).Select(m => m.Groups["id"].Value).ToArray();
        Assert.Contains("aws-access-token", rules);
        Assert.Contains("github-pat", rules);

        // Every allowance follows the rules, targets rules that exist and admits no sample credential.
        string allowances = text[text.IndexOf("[[allowlists]]", StringComparison.Ordinal)..];
        Assert.DoesNotContain("[[rules]]", allowances, StringComparison.Ordinal);
        Assert.Equal(10, Regex.Count(allowances, @"^\[\[allowlists\]\]$", RegexOptions.Multiline));
        Assert.DoesNotContain("EXAMPLE", allowances, StringComparison.Ordinal);
        Assert.DoesNotContain("AIza", allowances, StringComparison.Ordinal);
        foreach (Match target in Regex.Matches(allowances, @"^targetRules = \[""(?<id>[a-z0-9-]+)""\]$", RegexOptions.Multiline))
            Assert.Contains(target.Groups["id"].Value, rules);
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(GitleaksScanner.Configuration)), GitleaksScanner.ConfigurationHash);
    }

    // FIX-150: the inventory allowance is the last, for every rule, and only where its path condition and its line's shape both hold.
    [Fact]
    public void TheInventoryAllowanceIsTheLastAndHoldsOnlyWithItsPathAndLineShape()
    {
        string text = Encoding.UTF8.GetString(GitleaksScanner.Configuration);
        string last = text[text.LastIndexOf("[[allowlists]]", StringComparison.Ordinal)..];
        Assert.Equal(
            "[[allowlists]]\n"
            + "description = \"History checkpoint inventories: a record's path and hash alone on a line, in the estate's objects\"\n"
            + "condition = \"AND\"\n"
            + "paths = [\n    '''" + GitleaksScanner.InventoryPath + "''',\n]\n"
            + "regexTarget = \"line\"\n"
            + "regexes = [\n    '''\\A\\n?" + GitleaksScanner.InventoryLine + "\\z''',\n]\n", last);
        Assert.DoesNotContain("targetRules", last, StringComparison.Ordinal);

        // The configuration without it is the same file up to it, recorded by its own hash.
        string without = Encoding.UTF8.GetString(GitleaksScanner.ConfigurationWithoutInventories);
        Assert.Equal(text[..(text.Length - last.Length - 1)], without);
        Assert.Equal(9, Regex.Count(without, @"^\[\[allowlists\]\]$", RegexOptions.Multiline));
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(GitleaksScanner.ConfigurationWithoutInventories)),
            GitleaksScanner.ConfigurationWithoutInventoriesHash);
        Assert.NotEqual(GitleaksScanner.ConfigurationHash, GitleaksScanner.ConfigurationWithoutInventoriesHash);

        // The shape is a checkpoint inventory's line as the product writes and reads it.
        var shape = new Regex("^" + GitleaksScanner.InventoryLine + "$", RegexOptions.CultureInvariant);
        foreach (string line in InventoryLines())
        {
            Assert.Matches(shape, line);
            Assert.True(Vericairn.Records.History.HistoryInventory.TryParseLine(line, out _), line);
        }
    }

    [Fact]
    public void AnInventoryNamingARecordIdThatSpellsAKeyWordPasses()
    {
        string content = string.Concat(InventoryLines().Select(l => l + "\n"));
        SecretScan scan = ScanAdditions([(ObjectPath(content), content)]);
        Assert.Equal("passed", scan.Verdict);
        Assert.EndsWith("; rules " + GitleaksScanner.ConfigurationHash, scan.Version, StringComparison.Ordinal);
    }

    // The same inventory anywhere but a file named by its SHA-256 in the root's objects folder is scanned as every other file is.
    [Theory]
    [InlineData("inventory.txt")]
    [InlineData("objects/sub/{0}")]
    [InlineData("objects/{0}.txt")]
    [InlineData("records/objects/{0}")]
    [InlineData("estate/objects/{0}")]
    [InlineData("objects/{1}")]
    [InlineData("Objects/{0}")]
    public void TheAllowanceDoesNotApplyOutsideTheObjectsFolder(string path)
    {
        string content = string.Concat(InventoryLines().Select(l => l + "\n"));
        string name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        SecretScan scan = ScanAdditions([(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, name, name.ToUpperInvariant()), content)]);
        Assert.Equal("findings", scan.Verdict);
        Assert.True(scan.FindingCount > 0);
    }

    [Fact]
    public void TheSameInventoryWithOneSecretShapedLineIsRefused()
    {
        string secret = "ghp" + "_" + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 36);
        string[] lines = InventoryLines();
        string content = lines[0] + "\n" + "credential = " + secret + "\n" + string.Concat(lines.Skip(1).Select(l => l + "\n"));
        SecretScan scan = ScanAdditions([(ObjectPath(content), content)]);
        Assert.Equal("findings", scan.Verdict);
        // The secret's line and the inventory line spelling a key word: the file mixes lines, so the allowance does not apply in it.
        Assert.True(scan.FindingCount >= 2);
        Assert.EndsWith("; rules " + GitleaksScanner.ConfigurationWithoutInventoriesHash, scan.Version, StringComparison.Ordinal);
    }

    // A line of any other shape is found as it was before: a near miss of the shape, and a harmless line, which takes the allowance from
    // the whole file, since the allowance holds only in files whose every line has the shape.
    [Theory]
    [InlineData("{0} extra")]
    [InlineData("{0}\r")]
    [InlineData(" {0}")]
    [InlineData("records/{0}")]
    [InlineData("lower")]
    [InlineData("month")]
    [InlineData("tab")]
    [InlineData("upper hash")]
    [InlineData("harmless")]
    public void ALineOfAnyOtherShapeIsScannedAsBefore(string form)
    {
        string[] lines = InventoryLines();
        string keyed = lines[1];
        string changed = form switch
        {
            "lower" => keyed[..keyed.IndexOf(' ', StringComparison.Ordinal)].ToLowerInvariant() + keyed[keyed.IndexOf(' ', StringComparison.Ordinal)..],
            "month" => keyed.Replace("/2026-11/", "/2026-13/", StringComparison.Ordinal),
            "tab" => keyed.Replace(" sha256:", "\tsha256:", StringComparison.Ordinal),
            "upper hash" => keyed[..keyed.IndexOf(':', StringComparison.Ordinal)] + keyed[keyed.IndexOf(':', StringComparison.Ordinal)..].ToUpperInvariant(),
            "harmless" => keyed,
            _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, form, keyed),
        };
        string content = lines[0] + "\n" + changed + "\n" + lines[2] + "\n" + (form == "harmless" ? "A plain note.\n" : string.Empty);
        SecretScan scan = ScanAdditions([(ObjectPath(content), content)]);
        Assert.Equal("findings", scan.Verdict);
        Assert.Equal(1, scan.FindingCount);
    }

    // Inventory lines as HistoryInventory writes them; the second names a record id spelling KEY just before its end, which gitleaks'
    // generic key rule reads as a key name with the hash as its value. Assembled at run time, so that this file holds no such line.
    private static string[] InventoryLines()
    {
        static string Hash(string seed) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        return
        [
            "operation/2026-11/01M7PV6AXW41AVCKTFJP3J9Y09.json " + Hash("first"),
            "decision/2026-11/01M7PV6AXW41AVCKTFJ" + "K" + "EY" + "9Y09.json " + Hash("second"),
            "work-order/2026-11/01M7PV6AXW41AVCKTFJP3J9Y0Z.json " + Hash("third"),
        ];
    }

    private static string ObjectPath(string content) => "objects/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

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

    private SecretScan ScanAddition(string path, string content) => ScanAdditions([(path, content)]);

    private SecretScan ScanAdditions((string Path, string Content)[] files)
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
        foreach ((string path, string content) in files)
        {
            string file = Path.Combine(Repository, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
            Git("add", "--", path);
        }

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
