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

    internal string Repository => Path.Combine(root, "work");

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

        // The shape is a checkpoint inventory's line as the product writes and reads it, its first position one of the record types the
        // estate format defines and no other value (FIX-150, after the Reviewer's review): a new type means deriving the configuration again.
        var shape = new Regex("^" + GitleaksScanner.InventoryLine + "$", RegexOptions.CultureInvariant);
        foreach (string line in InventoryLines())
        {
            Assert.Matches(shape, line);
            Assert.True(Vericairn.Records.History.HistoryInventory.TryParseLine(line, out _), line);
        }

        Assert.StartsWith("(" + GitleaksScanner.InventoryTypes + ")/", GitleaksScanner.InventoryLine, StringComparison.Ordinal);
        Assert.Equal(Vericairn.Records.Types.RecordTypeRegistry.All.Select(t => t.Name), GitleaksScanner.InventoryTypes.Split('|'));
        Assert.DoesNotMatch(shape, "objectives/2026-11/01M7PV6AXW41AVCKTFJP3J9Y09.json sha256:" + new string('0', 64));
    }

    // FIX-150, after the Reviewer's review: the allowance passes only a value in an inventory line's hash position. A credential in any
    // other position breaks the line's shape, so it is found: alone in an inventory file, where the configuration with the allowance
    // applies and does not pass it, and among inventory lines, where the file mixes lines and the allowance does not apply. A record id
    // cannot hold one and keep its shape, since no rule finds a credential among 26 digits and upper-case letters after a slash.
    [Theory]
    [InlineData("type", true)]
    [InlineData("type", false)]
    [InlineData("month", true)]
    [InlineData("month", false)]
    [InlineData("id", true)]
    [InlineData("id", false)]
    [InlineData("extension", true)]
    [InlineData("extension", false)]
    [InlineData("algorithm", true)]
    [InlineData("algorithm", false)]
    public void ACredentialInAnyPositionButTheHashIsFound(string position, bool alone)
    {
        // Assembled at run time, so that this file holds no credential-shaped text of its own: a lower-case GitLab token, which the old
        // type position's shape admitted, and an AWS key id where the record id stands.
        string gitlab = "glpat" + "-" + RandomNumberGenerator.GetString("abcdefghijkmnopqrstuvwxyz", 20);
        string aws = "AKIA" + RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", 16);
        string[] lines = InventoryLines();
        string line = lines[0];
        string changed = position switch
        {
            "type" => gitlab + line[line.IndexOf('/', StringComparison.Ordinal)..],
            "month" => line.Replace("/2026-11/", "/" + gitlab + "/", StringComparison.Ordinal),
            "id" => line.Replace("01M7PV6AXW41AVCKTFJP3J9Y09", aws, StringComparison.Ordinal),
            "extension" => line.Replace(".json ", "." + gitlab + " ", StringComparison.Ordinal),
            _ => line.Replace(" sha256:", " " + gitlab + ":", StringComparison.Ordinal),
        };
        Assert.NotEqual(line, changed);
        string content = alone ? changed + "\n" : changed + "\n" + lines[2] + "\n";
        SecretScan scan = ScanAdditions([(ObjectPath(content), content)]);
        Assert.Equal("findings", scan.Verdict);
        Assert.Equal(1, scan.FindingCount);
        Assert.EndsWith("; rules " + (alone ? GitleaksScanner.ConfigurationHash : GitleaksScanner.ConfigurationWithoutInventoriesHash), scan.Version, StringComparison.Ordinal);
    }

    // FIX-150, after the Reviewer's review: gitleaks reports no finding when git log -p stops at an unreadable object, and exits 0. A
    // missing, corrupt or truncated object the push carries, or one of a parent's side that a patch reads, now leaves the scan incomplete,
    // though the token in the range's older commit is readable; before, the scan passed.
    [Theory]
    [InlineData("corrupt")]
    [InlineData("truncated")]
    [InlineData("missing")]
    [InlineData("parent's side")]
    public void AnUnreadableObjectBehindAReadableTokenLeavesTheScanIncomplete(string damage)
    {
        string basis = Start();
        Commit([("settings.txt", "credential = " + Token() + "\n")]);
        string head = Commit([damage == "parent's side" ? ("readme.txt", "Changed content.\n") : ("notes.txt", "A plain note.\n")]);
        string blob = Git("rev-parse", (damage == "parent's side" ? basis : head) + ":" + (damage == "parent's side" ? "readme.txt" : "notes.txt"));
        string file = Path.Combine(Repository, ".git", "objects", blob[..2], blob[2..]);
        byte[] stored = File.ReadAllBytes(file);
        File.SetAttributes(file, FileAttributes.Normal);
        if (damage == "missing") File.Delete(file);
        else File.WriteAllBytes(file, damage == "truncated" ? stored[..(stored.Length / 2)] : Encoding.ASCII.GetBytes("not an object"));
        Assert.Equal("incomplete", Scan(basis, head).Verdict);
    }

    // A read gitleaks itself cuts short: git writes on its standard error, here a trace the scanner's own environment never passes on,
    // and gitleaks stops reading the history and exits 0 with no finding. Gitleaks logs that at error level, and the scan is incomplete.
    [Fact]
    public void AHistoryReadGitleaksStopsEarlyLeavesTheScanIncomplete()
    {
        string basis = Start();
        Commit([("settings.txt", "credential = " + Token() + "\n")]);
        string head = Commit([("notes.txt", "A plain note.\n")]);
        Assert.Equal("findings", Scan(basis, head).Verdict);
        Assert.Equal("incomplete", Scan(basis, head, info => { if (info.FileName != "git") info.Environment["GIT_TRACE"] = "1"; }).Verdict);
    }

    // The clone's display settings change nothing the scan reads: color would leave gitleaks nothing to parse.
    [Theory]
    [InlineData("color.ui")]
    [InlineData("color.diff")]
    public void TheClonesColorSettingsChangeNothingTheScanReads(string setting)
    {
        string basis = Start();
        Commit([("settings.txt", "credential = " + Token() + "\n")]);
        string head = Commit([("notes.txt", "A plain note.\n")]);
        Git("config", setting, "always");
        Assert.Equal("findings", Scan(basis, head).Verdict);
    }

    // FIX-150, after the Reviewer's re-check: the scan asks git for merges' patches with -m, whose form the clone's log.diffMerges chose.
    // With off or none git showed no merge's patch, and a token only a merge adds passed; with a combined form, which git refuses to write
    // to a file and gitleaks cannot parse, every range holding a merge was incomplete. Every git process of the scan now reads a merge as
    // its patch against its first parent, whatever the clone says: a clean merge passes, and a merge's own token is found, once.
    [Theory]
    [InlineData(null)]
    [InlineData("off")]
    [InlineData("none")]
    [InlineData("on")]
    [InlineData("m")]
    [InlineData("first-parent")]
    [InlineData("1")]
    [InlineData("separate")]
    [InlineData("combined")]
    [InlineData("c")]
    [InlineData("dense-combined")]
    [InlineData("cc")]
    [InlineData("remerge")]
    [InlineData("r")]
    public void ATokenAddedOnlyByAMergeIsFoundWhateverTheClonesMergeDiffSetting(string? setting)
    {
        string basis = Start();
        string first = Commit([("first.txt", "The first side.\n")]);
        Git("checkout", "--quiet", "--detach", basis);
        string second = Commit([("second.txt", "The second side.\n")]);
        if (setting is not null) Git("config", "log.diffMerges", setting);
        string clean = Merge([first, second], [("second.txt", "The second side.\n")]);
        Assert.Equal("passed", Scan(basis, clean).Verdict);
        string added = Merge([first, second], [("second.txt", "The second side.\n"), ("merged.txt", "credential = " + Token() + "\n")]);
        SecretScan scan = Scan(basis, added);
        Assert.Equal("findings", scan.Verdict);
        Assert.Equal(1, scan.FindingCount);
    }

    // Every merge shape gives gitleaks a patch it parses: a conflict resolved with a token, and a merge of three branches adding one,
    // which remerge, reading only merges of two parents, skipped. Each is found once, even in a clone that turns merge patches off.
    [Theory]
    [InlineData("resolved conflict")]
    [InlineData("three parents")]
    public void EveryMergeShapeIsReadAgainstItsFirstParent(string shape)
    {
        string basis = Start();
        string head;
        if (shape == "resolved conflict")
        {
            string ours = Commit([("readme.txt", "Our line.\n")]);
            Git("checkout", "--quiet", "--detach", basis);
            string theirs = Commit([("readme.txt", "Their line.\n")]);
            Git("config", "log.diffMerges", "off");
            head = Merge([ours, theirs], [("readme.txt", "credential = " + Token() + "\n")]);
        }
        else
        {
            var parents = new List<string>();
            foreach (string name in new[] { "first", "second", "third" })
            {
                Git("checkout", "--quiet", "--detach", basis);
                parents.Add(Commit([(name + ".txt", "The " + name + " branch.\n")]));
            }

            Git("config", "log.diffMerges", "off");
            head = Merge([.. parents], [("second.txt", "The second branch.\n"), ("third.txt", "The third branch.\n"), ("merged.txt", "credential = " + Token() + "\n")]);
        }

        SecretScan scan = Scan(basis, head);
        Assert.Equal("findings", scan.Verdict);
        Assert.Equal(1, scan.FindingCount);
    }

    // A merge's patch against its first parent holds every line the merge adds, its own and those it brings in from its other parents,
    // even from one outside the range: the target branch merged into the pushed branch brings the target's lines into the merge's patch,
    // and they are read, while a merge whose first parent is the target holds only the pushed branch's lines. Here the target already
    // holds a token, outside both ranges: it is found in the first and not in the second.
    [Fact]
    public void AMergesPatchHoldsTheLinesItBringsFromItsOtherParents()
    {
        string basis = Start();
        string branch = Commit([("branch.txt", "The branch's change.\n")]);
        Git("checkout", "--quiet", "--detach", basis);
        string held = "credential = " + Token() + "\n";
        string target = Commit([("target.txt", held)]);
        string intoBranch = Merge([branch, target], [("target.txt", held)]);
        Assert.Equal("findings", Scan(target, intoBranch).Verdict);
        string intoTarget = Merge([target, branch], [("branch.txt", "The branch's change.\n")]);
        Assert.Equal("passed", Scan(target, intoTarget).Verdict);
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
        string basis = Start();
        return Scan(basis, Commit(files));
    }

    // A new repository with one commit, which the scanned range starts from; skipped without an installed gitleaks.
    internal string Start()
    {
        Assert.SkipWhen(Gitleaks is null, "Set VERICAIRN_TEST_GITLEAKS to an installed gitleaks of at least 8.25.0 to scan real commits.");
        TestPrerequisites.RequireGit();
        Directory.CreateDirectory(Repository);
        File.WriteAllText(Path.Combine(root, "gitconfig"), string.Empty);
        Git("init", "--initial-branch=main");
        return Commit([("readme.txt", "Initial content.\n")]);
    }

    internal string Commit((string Path, string Content)[] files)
    {
        foreach ((string path, string content) in files)
        {
            string file = Path.Combine(Repository, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
            Git("add", "--", path);
        }

        Git("commit", "-m", "Add content");
        return Git("rev-parse", "HEAD");
    }

    // A merge commit of these parents whose tree is the first parent's with these files written. A merge is its tree and its parents,
    // so whether git's merge machinery reached that tree cleanly, through a resolved conflict or not at all changes nothing the scan reads.
    private string Merge(string[] parents, (string Path, string Content)[] files)
    {
        Git("checkout", "--quiet", "--force", "--detach", parents[0]);
        foreach ((string path, string content) in files)
        {
            File.WriteAllText(Path.Combine(Repository, path.Replace('/', Path.DirectorySeparatorChar)), content);
            Git("add", "--", path);
        }

        var arguments = new List<string> { "commit-tree", Git("write-tree") };
        foreach (string parent in parents) arguments.AddRange(["-p", parent]);
        arguments.AddRange(["-m", "Merge"]);
        return Git([.. arguments]);
    }

    // Scans base..head with the installed gitleaks; adjust sees each process the scanner starts after the scanner has prepared it. Each
    // process's output is bounded as the product bounds it, by an object of the estate's object limit (FIX-168).
    internal SecretScan Scan(string basis, string head, Action<ProcessStartInfo>? adjust = null)
    {
        var scanner = new GitleaksScanner(info =>
        {
            if (info.FileName == "gitleaks") info.FileName = Gitleaks!;
            adjust?.Invoke(info);
            return BoundedProcess.Run(info, TestTiming.HangTimeout, (int)Vericairn.Records.Estate.EstateLayout.MaxObjectBytes);
        });
        return scanner.Scan(Repository, new PushPlan("work/change", "main", head, basis, null, basis));
    }

    private static string? Gitleaks => Environment.GetEnvironmentVariable("VERICAIRN_TEST_GITLEAKS");

    // A GitHub token, assembled at run time, so that this file holds no credential-shaped text of its own.
    internal static string Token() => "ghp" + "_" + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 36);

    internal string Git(params string[] args)
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

/// <summary>Tests that set a git variable in this test process's own environment, standing for the pushing process's, which no other test may see.</summary>
[CollectionDefinition("inherited git environment", DisableParallelization = true)]
public sealed class InheritedGitEnvironmentIsolation;

/// <summary>
/// FIX-150, after the Reviewer's re-check: the scan's processes kept git's routing variables, which the push adapter's drop. A shallow
/// file naming the head, given directly or in a copy of the repository that GIT_DIR names, made git take the head for a commit without
/// parents, so a token in a middle commit of the range passed while the push, reading the real history, still carried that commit.
/// </summary>
[Collection("inherited git environment")]
public sealed class SecretScanInheritedEnvironmentTests : IDisposable
{
    private readonly SecretScanConfigurationTests scans = new();

    [Theory]
    [InlineData("GIT_SHALLOW_FILE")]
    [InlineData("GIT_DIR")]
    public void ATokenInAMiddleCommitIsFoundWhateverRoutingThePushingProcessCarries(string variable)
    {
        string basis = scans.Start();
        scans.Commit([("settings.txt", "credential = " + SecretScanConfigurationTests.Token() + "\n")]);
        string head = scans.Commit([("settings.txt", "A plain setting.\n")]);
        Assert.Equal("findings", scans.Scan(basis, head).Verdict);
        string root = Path.GetDirectoryName(scans.Repository)!;
        string value = Path.Combine(root, variable == "GIT_DIR" ? "shallow.git" : "shallow");
        if (variable == "GIT_DIR") scans.Git("clone", "--bare", "--quiet", scans.Repository, value);
        File.WriteAllText(variable == "GIT_DIR" ? Path.Combine(value, "shallow") : value, head + "\n");
        string? saved = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
        try
        {
            // Under this environment git itself sees the range as the head alone.
            Assert.Equal("1", scans.Git("rev-list", "--count", basis + ".." + head));
            Assert.Equal("findings", scans.Scan(basis, head).Verdict);
        }
        finally { Environment.SetEnvironmentVariable(variable, saved); }
    }

    public void Dispose() => scans.Dispose();
}
