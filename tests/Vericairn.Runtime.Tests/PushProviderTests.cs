// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class PushProviderTests
{
    private static readonly string Head = new('a', 40);
    private static readonly string Basis = new('c', 40);
    private static readonly string Moved = new('d', 40);
    private static readonly PushPlan Plan = new("work/change", "main", Head, Basis, Basis, Basis);

    // Gitleaks logs at error level only when its read of the history failed or stopped early, and still exits 0 (FIX-150).
    [Theory]
    [InlineData(0, 0, false, "passed")]
    [InlineData(0, 0, true, "incomplete")]
    [InlineData(1, 2, true, "findings")]
    [InlineData(1, 2, false, "findings")]
    [InlineData(1, 0, false, "incomplete")]
    [InlineData(2, 0, true, "incomplete")]
    public void ScannerUsesRedactionAnExactRangeAndOnlyACountReport(int exit, int count, bool diagnostics, string verdict)
    {
        var scanner = new GitleaksScanner(info =>
        {
            if (AsksForTheGitDirectory(info)) return Success(GitDirectory + "\n");
            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            Assert.Contains("--redact=100", info.ArgumentList);
            Assert.Contains("--ignore-gitleaks-allow", info.ArgumentList);
            Assert.Contains("--log-opts=" + Plan.Range + " --full-history --no-renames -m --no-ext-diff --no-textconv --text --submodule=short", info.ArgumentList);
            Assert.Contains("--report-format=template", info.ArgumentList);
            string template = info.ArgumentList.Single(a => a.StartsWith("--report-template=", StringComparison.Ordinal)).Split('=', 2)[1];
            Assert.Equal("{{ len . }}", File.ReadAllText(template));
            string configuration = info.ArgumentList.Single(a => a.StartsWith("--config=", StringComparison.Ordinal)).Split('=', 2)[1];
            Assert.Equal(GitleaksScanner.Configuration, File.ReadAllBytes(configuration));
            string ignored = info.ArgumentList.Single(a => a.StartsWith("--gitleaks-ignore-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            Assert.Empty(File.ReadAllText(ignored));
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, count.ToString(CultureInfo.InvariantCulture));
            return new(ProcessOutcome.Exited, true, exit, "untrusted scanner output", diagnostics ? "untrusted scanner diagnostics" : string.Empty);
        });
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.Equal(verdict, scan.Verdict);
        Assert.Equal(Plan.Range, scan.Range);
        Assert.Equal("8.30.1; rules " + GitleaksScanner.ConfigurationHash, scan.Version);
        Assert.DoesNotContain("untrusted", JsonSerializer.Serialize(scan), StringComparison.Ordinal);
        Assert.Equal(verdict == "passed", scan.Passed(Plan));
    }

    // CODE-81: the scan's range reads, its own count and gitleaks' git, ignore the clone's info/grafts, replace objects and derived
    // caches, which could otherwise hide a commit of the range from the scan while the push still delivers it.
    // FIX-150 adds the clone's display settings that change what gitleaks reads, and git's trace variables.
    [Fact]
    public void TheScansRangeReadsIgnoreTheClonesGraftsReplaceObjectsAndCaches()
    {
        // A trace variable of the caller's own: no process the scan starts inherits it. The name is this test's alone.
        const string Trace = "GIT_TRACE_VERICAIRN_SCAN_TEST";
        var started = new List<ProcessStartInfo>();
        var scanner = new GitleaksScanner(info =>
        {
            started.Add(info);
            if (AsksForTheGitDirectory(info)) return Success(GitDirectory + "\n");
            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, "0");
            return Success();
        });
        Environment.SetEnvironmentVariable(Trace, "1");
        try { Assert.Equal("passed", scanner.Scan(Path.GetTempPath(), Plan).Verdict); }
        finally { Environment.SetEnvironmentVariable(Trace, null); }
        Assert.Contains(started, i => i.FileName == "git");
        Assert.Contains(started, i => i.FileName == "gitleaks" && i.ArgumentList[0] == "git");
        Assert.All(started, info =>
        {
            Assert.Equal("1", info.Environment["GIT_NO_REPLACE_OBJECTS"]);
            Assert.Equal("1", info.Environment["GIT_NO_LAZY_FETCH"]);
            string grafts = info.Environment["GIT_GRAFT_FILE"]!;
            Assert.True(Path.IsPathFullyQualified(grafts) && !File.Exists(grafts) && !Directory.Exists(Path.GetDirectoryName(grafts)));
            Assert.False(info.Environment.ContainsKey("GIT_CONFIG_PARAMETERS"));
            Assert.False(info.Environment.ContainsKey(Trace));
            // The hardening of every git process on a repository Vericairn did not create comes first (FIX-147), then the read settings.
            int hardened = GitHardening.Settings.Length + 1;
            Assert.Equal((hardened + 8).ToString(CultureInfo.InvariantCulture), info.Environment["GIT_CONFIG_COUNT"]);
            Assert.Equal("core.hooksPath", info.Environment["GIT_CONFIG_KEY_0"]);
            Assert.Equal(GitHardening.Settings, Enumerable.Range(1, hardened - 1).Select(n => info.Environment["GIT_CONFIG_KEY_" + n] + "=" + info.Environment["GIT_CONFIG_VALUE_" + n]));
            Assert.Equal(["core.commitGraph=false", "pack.useBitmaps=false", "core.multiPackIndex=false", "color.ui=false", "color.diff=false", "log.showRoot=true",
                "log.showSignature=false", "log.diffMerges=first-parent"], Enumerable.Range(hardened, 8).Select(n => info.Environment["GIT_CONFIG_KEY_" + n] + "=" + info.Environment["GIT_CONFIG_VALUE_" + n]));
        });
    }

    // FIX-150, after the Reviewer's re-check: the scan's processes drop every inherited variable the push's processes drop, both taken
    // from one list, so that neither reads another repository, object store, history or configuration than the clone's own. The scan
    // drops every other configuration variable too, keeping only the settings it forces, and git's trace variables.
    [Fact]
    public void TheScanDropsEveryInheritedRoutingVariableThePushDrops()
    {
        string[] routing = ["GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_NAMESPACE", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM",
            "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_REPLACE_REF_BASE", "GIT_NO_REPLACE_OBJECTS",
            "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_REDIRECT_STDIN", "GIT_REDIRECT_STDOUT", "GIT_REDIRECT_STDERR"];
        Assert.Equal(routing, GitRouting.Names);
        string[] indexed = ["GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0", "GIT_CONFIG_KEY_12", "GIT_CONFIG_VALUE_12"];
        ProcessStartInfo Inherited()
        {
            var info = new ProcessStartInfo("git");
            foreach (string name in routing.Concat(indexed).Concat(["GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM", "GIT_TRACE_SETUP"]))
                info.Environment[name] = "inherited";
            return info;
        }

        ProcessStartInfo push = Inherited();
        GitPushAdapter.Isolate(push, []);
        Assert.All(routing.Concat(indexed), name => Assert.NotEqual("inherited", push.Environment.TryGetValue(name, out string? value) ? value : null));
        ProcessStartInfo scan = Inherited();
        using (GitHardening hardening = GitHardening.Create()) GitleaksScanner.Isolate(scan, hardening);
        Assert.DoesNotContain(scan.Environment, variable => variable.Value == "inherited");
        Assert.Equal("1", scan.Environment["GIT_NO_REPLACE_OBJECTS"]);
        Assert.Equal((GitHardening.Settings.Length + 1 + 8).ToString(CultureInfo.InvariantCulture), scan.Environment["GIT_CONFIG_COUNT"]);
    }

    // FIX-160: no variable gitleaks reads its configuration from and no other inherited git variable reaches a process of the scan.
    [Fact]
    public void TheScanDropsEveryGitleaksVariableAndEveryOtherInheritedGitVariable()
    {
        var info = new ProcessStartInfo("gitleaks");
        foreach (string name in new[] { "GITLEAKS_CONFIG", "GITLEAKS_CONFIG_TOML", "GIT_DIFF_OPTS", "GIT_EXTERNAL_DIFF", "GIT_NOTES_REF", "GIT_ATTR_SOURCE", "GIT_LITERAL_PATHSPECS" })
            info.Environment[name] = "inherited";
        using (GitHardening hardening = GitHardening.Create()) GitleaksScanner.Isolate(info, hardening);
        Assert.DoesNotContain(info.Environment, variable => variable.Value == "inherited");
    }

    // FIX-160, after the Reviewer's review: no process of the scan fetches a missing object, whatever the caller's environment says,
    // as no process of the push adapter does.
    [Fact]
    public void NoProcessOfTheScanFetchesAMissingObject()
    {
        var info = new ProcessStartInfo("git");
        info.Environment["GIT_NO_LAZY_FETCH"] = "0";
        using (GitHardening hardening = GitHardening.Create()) GitleaksScanner.Isolate(info, hardening);
        Assert.Equal("1", info.Environment["GIT_NO_LAZY_FETCH"]);
        var push = new ProcessStartInfo("git");
        GitPushAdapter.Isolate(push, []);
        Assert.Equal(push.Environment["GIT_NO_LAZY_FETCH"], info.Environment["GIT_NO_LAZY_FETCH"]);
    }

    // FIX-160: every process the scan starts, gitleaks and git alike, runs in one folder the scan creates outside the clone, still empty
    // when each starts, which is gitleaks' source. The first asks gitleaks its version and the second asks git, from the clone, for the
    // clone's git directory; every later process reads that directory through GIT_DIR alone, with the empty folder as its work tree. The
    // rules and the empty ignore file lie beside that folder, no GITLEAKS_ or other inherited git variable of the caller's reaches any
    // process, and the folder is gone once the scan returns.
    [Fact]
    public void EveryProcessOfTheScanRunsInAnEmptyFolderOfItsOwnAndReadsTheClonesGitDirectoryByName()
    {
        // Variables of the caller's own, each name this test's alone.
        string[] inherited = ["GITLEAKS_VERICAIRN_SCAN_TEST", "GIT_VERICAIRN_SCAN_TEST"];
        string clone = Path.Combine(Path.GetTempPath(), "vericairn-scan-clone-" + Guid.NewGuid().ToString("N"));
        string gitDirectory = Path.Combine(clone, ".git");
        Directory.CreateDirectory(gitDirectory);
        var started = new List<(ProcessStartInfo Info, bool Empty)>();
        var scanner = new GitleaksScanner(info =>
        {
            started.Add((info, Directory.Exists(info.WorkingDirectory) && !Directory.EnumerateFileSystemEntries(info.WorkingDirectory).Any()));
            if (AsksForTheGitDirectory(info)) return Success(gitDirectory + "\n");
            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, "0");
            return Success();
        });
        try
        {
            foreach (string name in inherited) Environment.SetEnvironmentVariable(name, "inherited");
            Assert.Equal("passed", scanner.Scan(clone, Plan).Verdict);
        }
        finally
        {
            foreach (string name in inherited) Environment.SetEnvironmentVariable(name, null);
            Directory.Delete(clone, recursive: true);
        }

        string folder = started[0].Info.WorkingDirectory;
        Assert.True(Path.IsPathFullyQualified(folder));
        Assert.False(folder.StartsWith(clone, StringComparison.Ordinal));
        Assert.False(Directory.Exists(folder));
        Assert.All(started, process =>
        {
            Assert.Equal(folder, process.Info.WorkingDirectory);
            Assert.True(process.Empty);
            Assert.DoesNotContain(process.Info.Environment.Keys, name => name.StartsWith("GITLEAKS_", StringComparison.OrdinalIgnoreCase));
            Assert.False(process.Info.Environment.ContainsKey("GIT_VERICAIRN_SCAN_TEST"));
        });
        Assert.Equal(["version"], started[0].Info.ArgumentList);
        Assert.Equal(["-C", clone, "rev-parse", "--absolute-git-dir"], Requested(started[1].Info));
        Assert.All(started.Take(2), process => Assert.False(process.Info.Environment.ContainsKey("GIT_DIR") || process.Info.Environment.ContainsKey("GIT_WORK_TREE")));
        Assert.All(started.Skip(2), process =>
        {
            Assert.Equal(gitDirectory, process.Info.Environment["GIT_DIR"]);
            Assert.Equal(folder, process.Info.Environment["GIT_WORK_TREE"]);
        });
        ProcessStartInfo scan = Assert.Single(started, process => process.Info.FileName == "gitleaks" && process.Info.ArgumentList[0] == "git").Info;
        Assert.Equal(folder, scan.ArgumentList[^1]);
        foreach (string option in new[] { "--config=", "--gitleaks-ignore-path=" })
            Assert.Equal(Path.GetDirectoryName(folder), Path.GetDirectoryName(scan.ArgumentList.Single(a => a.StartsWith(option, StringComparison.Ordinal))[option.Length..]));
    }

    // FIX-150, after the Reviewer's review: before gitleaks runs, the scanner reads every commit, tree and blob the push carries, checked
    // against its id, and the range's patches exactly as gitleaks reads them. A read that fails, or stops early as a truncated read does,
    // and a gitleaks that logs its own read's failure while it exits 0 with no finding, each leave the scan incomplete, never passed.
    [Theory]
    [InlineData("complete", "passed")]
    [InlineData("objects fail", "incomplete")]
    [InlineData("objects time out", "incomplete")]
    [InlineData("patches fail", "incomplete")]
    [InlineData("patches truncated", "incomplete")]
    [InlineData("patches time out", "incomplete")]
    [InlineData("gitleaks stops early", "incomplete")]
    public void AHistoryReadThatFailsOrStopsEarlyLeavesTheScanIncomplete(string read, string verdict)
    {
        string nothing = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        var reads = new List<string>();
        int scans = 0;
        var scanner = new GitleaksScanner(info =>
        {
            if (AsksForTheGitDirectory(info)) return Success(GitDirectory + "\n");
            if (info.FileName == "git" && info.ArgumentList.Contains("--verify-objects"))
            {
                reads.Add("objects");
                Assert.Equal(["--no-replace-objects", "rev-list", "--objects", "--verify-objects", "--quiet", Plan.Range], Requested(info));
                return read switch
                {
                    "objects fail" => new(ProcessOutcome.Exited, true, 128, string.Empty, "untrusted: object is corrupt"),
                    "objects time out" => new(ProcessOutcome.TimedOut, true, null, string.Empty, string.Empty),
                    _ => Success(),
                };
            }

            if (info.FileName == "git" && info.ArgumentList.Contains("-p"))
            {
                reads.Add("patches");
                Assert.Equal(["--no-replace-objects", "log", "-p", "-U0", Plan.Range, "--full-history", "--no-renames", "-m", "--no-ext-diff", "--no-textconv", "--text",
                    "--submodule=short", "--output=" + nothing], Requested(info));
                return read switch
                {
                    "patches fail" => new(ProcessOutcome.Exited, true, 128, string.Empty, "untrusted: unable to read"),
                    // A read cut off by a broken pipe exits by its signal.
                    "patches truncated" => new(ProcessOutcome.Exited, true, 141, string.Empty, string.Empty),
                    "patches time out" => new(ProcessOutcome.TimedOut, true, null, string.Empty, string.Empty),
                    _ => Success(),
                };
            }

            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            scans++;
            // gitleaks' git read takes exactly the options the scanner's own patch read repeats.
            Assert.Contains("--log-opts=" + Plan.Range + " --full-history --no-renames -m --no-ext-diff --no-textconv --text --submodule=short", info.ArgumentList);
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, "0");
            return read == "gitleaks stops early"
                ? new(ProcessOutcome.Exited, true, 0, string.Empty, "ERR [git] fatal: unable to read " + new string('e', 40) + "\nERR error=\"stderr is not empty\"\n")
                : Success();
        });
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.Equal(verdict, scan.Verdict);
        Assert.Equal(verdict == "passed", scan.Passed(Plan));
        Assert.DoesNotContain("untrusted", JsonSerializer.Serialize(scan), StringComparison.Ordinal);
        Assert.Equal(read.StartsWith("objects", StringComparison.Ordinal) ? ["objects"] : ["objects", "patches"], reads);
        Assert.Equal(read is "complete" or "gitleaks stops early" ? 1 : 0, scans);
    }

    // FIX-150: the inventory allowance applies only when every file it could reach that the range adds or changes holds nothing but
    // inventory lines; a file mixing them with another line, or any read the scanner cannot complete, scans without it.
    [Theory]
    [InlineData("pure", true)]
    [InlineData("none", true)]
    [InlineData("mixed", false)]
    [InlineData("log fails", false)]
    [InlineData("grep fails", false)]
    [InlineData("unasked path", false)]
    [InlineData("other commit", false)]
    [InlineData("path before commit", false)]
    public void TheInventoryAllowanceAppliesOnlyWhenEveryReachableFileHoldsOnlyInventoryLines(string range, bool allowance)
    {
        string inventory = "objects/" + new string('e', 64);
        string other = "objects/" + new string('f', 64);
        string commit = new('b', 40);
        var greps = new List<ProcessStartInfo>();
        byte[]? applied = null;
        var scanner = new GitleaksScanner(info =>
        {
            if (AsksForTheGitDirectory(info)) return Success(GitDirectory + "\n");
            if (info.FileName == "git" && info.ArgumentList.Contains("--name-only"))
            {
                Assert.Equal("C", info.Environment["LC_ALL"]);
                Assert.Equal(["--no-replace-objects", "log", "--no-show-signature", "--format=commit %H", "--name-only", "--root", "--no-renames", "--full-history", "-m",
                    "--diff-filter=d", "--no-relative", "--no-ext-diff", "--no-textconv", Plan.Range, "--", ":(top)objects"], Requested(info));
                return range switch
                {
                    "log fails" => new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty),
                    "none" => Success("commit " + commit + "\n\nobjects/notes.txt\nobjects/sub/" + new string('e', 64) + "\n"),
                    "path before commit" => Success(inventory + "\ncommit " + commit + "\n"),
                    _ => Success("commit " + commit + "\n\n" + inventory + "\n" + other + "\r\n"),
                };
            }

            // FIX-168: the searches for a release basis's objects find none here.
            if (info.FileName == "git" && info.ArgumentList.Contains("grep") && info.ArgumentList[info.ArgumentList.IndexOf("-e") + 1] != "^" + GitleaksScanner.InventoryLine + "$")
                return new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty);
            if (info.FileName == "git" && info.ArgumentList.Contains("grep"))
            {
                greps.Add(info);
                bool invert = info.ArgumentList.Contains("--invert-match");
                Assert.Equal("C", info.Environment["LC_ALL"]);
                Assert.Equal("^" + GitleaksScanner.InventoryLine + "$", info.ArgumentList[info.ArgumentList.IndexOf("-e") + 1]);
                Assert.Equal([commit, "--", ":(top)" + inventory, ":(top)" + other], info.ArgumentList.Skip(info.ArgumentList.IndexOf("-e") + 2));
                return range switch
                {
                    "grep fails" => new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty),
                    "unasked path" when !invert => Success(commit + ":objects/" + new string('d', 64) + "\n"),
                    "other commit" when !invert => Success(new string('c', 40) + ":" + inventory + "\n"),
                    "mixed" => Success(commit + ":" + inventory + "\n"),
                    _ when invert => Success(commit + ":" + other + "\n"),
                    _ => Success(commit + ":" + inventory + "\n"),
                };
            }

            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            string configuration = info.ArgumentList.Single(a => a.StartsWith("--config=", StringComparison.Ordinal)).Split('=', 2)[1];
            applied = File.ReadAllBytes(configuration);
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, "0");
            return Success();
        });
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.Equal("passed", scan.Verdict);
        Assert.Equal(allowance ? GitleaksScanner.Configuration : GitleaksScanner.ConfigurationWithoutInventories, applied);
        Assert.Equal("8.30.1; rules " + (allowance ? GitleaksScanner.ConfigurationHash : GitleaksScanner.ConfigurationWithoutInventoriesHash), scan.Version);
        Assert.Equal(range is "none" or "log fails" or "path before commit" ? 0 : range is "grep fails" or "unasked path" or "other commit" ? 1 : 2, greps.Count);
    }

    // How a release basis's one object and a split object's root begin, which the scan searches the range's objects for (FIX-168).
    private static readonly string[] BasisSearches =
        ["^\\{\"basis\":\\{", "^\\{\"bytes\":[1-9][0-9]*,\"digest\":\"sha256:[0-9a-f]{64}\",\"format\":\"vericairn\\.split-object/1\",\"parts\":\\["];

    // FIX-168: the scan appends one allowance for the release bases the range carries: for the Sourcegraph rule alone, exactly the
    // revisions they record, in exactly the objects that hold their bytes, read whole and checked against their names, a split basis
    // through its root. A file that is no basis, cannot be read or is not named by its hash, or a search that fails, adds nothing.
    [Theory]
    [InlineData("whole", true)]
    [InlineData("split", true)]
    [InlineData("named by another hash", false)]
    [InlineData("another format", false)]
    [InlineData("part without its root", false)]
    [InlineData("read fails", false)]
    [InlineData("grep fails", false)]
    [InlineData("log fails", false)]
    public void TheReleaseBasisAllowanceNamesExactlyTheRevisionsTheRangesBasesRecord(string range, bool allowance)
    {
        string commit = new('b', 40);
        byte[] basis = ReleaseBasisEvidenceFixture.Basis(range is "split" or "part without its root" ? 6_000 : 40,
            format: range == "another format" ? "vericairn.release-plan/1" : "vericairn.release-basis/3");
        (string Path, byte[] Content)[] held = ReleaseBasisEvidenceFixture.Objects(basis);
        if (range == "named by another hash") held = [("objects/" + new string('e', 64), basis)];
        if (range == "part without its root") held = held[..^1];
        Dictionary<string, byte[]> objects = held.ToDictionary(o => o.Path, o => o.Content, StringComparer.Ordinal);
        string other = "objects/" + new string('f', 64);
        byte[]? applied = null;
        var scanner = new GitleaksScanner(info =>
        {
            if (AsksForTheGitDirectory(info)) return Success(GitDirectory + "\n");
            if (info.FileName == "git" && info.ArgumentList.Contains("--name-only"))
                return range == "log fails" ? new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty)
                    : Success("commit " + commit + "\n\n" + string.Concat(objects.Keys.Append(other).Order(StringComparer.Ordinal).Select(p => p + "\n")));
            if (info.FileName == "git" && info.ArgumentList.Contains("grep"))
            {
                string pattern = info.ArgumentList[info.ArgumentList.IndexOf("-e") + 1];
                Assert.Equal([commit, "--", .. objects.Keys.Append(other).Order(StringComparer.Ordinal).Select(p => ":(top)" + p)],
                    info.ArgumentList.Skip(info.ArgumentList.IndexOf("-e") + 2));
                if (pattern == "^" + GitleaksScanner.InventoryLine + "$") return new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty);
                Assert.Contains(pattern, BasisSearches);
                if (range == "grep fails") return new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty);
                string[] found = [.. objects.Where(o => new Regex(pattern).IsMatch(Encoding.UTF8.GetString(o.Value)))
                    .Select(o => commit + ":" + o.Key + "\n")];
                return found.Length == 0 ? new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty) : Success(string.Concat(found));
            }

            if (info.FileName == "git" && info.ArgumentList.Contains("cat-file"))
            {
                string[] request = Requested(info);
                Assert.Equal("--no-replace-objects", request[0]);
                Assert.Equal("cat-file", request[1]);
                Assert.StartsWith(commit + ":objects/", request[3], StringComparison.Ordinal);
                if (!objects.TryGetValue(request[3][(commit.Length + 1)..], out byte[]? content)) return new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty);
                if (request[2] == "-s") return Success(content.Length.ToString(CultureInfo.InvariantCulture) + "\n");
                Assert.Equal("blob", request[2]);
                Assert.Equal(4, request.Length);
                Assert.Equal("utf-8", info.StandardOutputEncoding?.WebName);
                return range == "read fails" ? new(ProcessOutcome.Exited, true, 128, string.Empty, string.Empty) : Success(Encoding.UTF8.GetString(content));
            }

            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            string configuration = info.ArgumentList.Single(a => a.StartsWith("--config=", StringComparison.Ordinal)).Split('=', 2)[1];
            applied = File.ReadAllBytes(configuration);
            string report = info.ArgumentList.Single(a => a.StartsWith("--report-path=", StringComparison.Ordinal)).Split('=', 2)[1];
            File.WriteAllText(report, "0");
            return Success();
        });
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.Equal("passed", scan.Verdict);
        byte[] rules = range == "log fails" ? GitleaksScanner.ConfigurationWithoutInventories : GitleaksScanner.Configuration;
        string[] paths = range == "split" ? [.. held[..^1].Select(o => o.Path).Order(StringComparer.Ordinal)] : [held[0].Path];
        byte[] expected = !allowance ? rules : Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rules)
            + "\n[[allowlists]]\n"
            + "description = \"Release basis evidence: the git revisions a release basis records, in the objects that hold the basis\"\n"
            + "targetRules = [\"sourcegraph-access-token\"]\n"
            + "condition = \"AND\"\n"
            + "paths = [\n    '''^objects/(?:" + string.Join('|', paths.Select(p => p["objects/".Length..])) + ")$''',\n]\n"
            + "regexes = [\n    '''^(?:" + string.Join('|', ReleaseBasisEvidenceFixture.Recorded.Order(StringComparer.Ordinal)) + ")$''',\n]\n");
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(applied!));
        Assert.Equal("8.30.1; rules sha256:" + Convert.ToHexStringLower(SHA256.HashData(expected)), scan.Version);
    }

    [Theory]
    [InlineData("NotStarted")]
    [InlineData("TimedOut")]
    [InlineData("OutputLimitExceeded")]
    [InlineData("Canceled")]
    [InlineData("Failed")]
    public void MissingTimedOutAndIncompleteScannersNeverPass(string outcome)
    {
        var scanner = new GitleaksScanner(info => AsksForTheGitDirectory(info) ? Success(GitDirectory + "\n") : info.FileName == "git" ? Success("2")
            : info.ArgumentList[0] == "version" ? Success("8.30.1")
            : new(Enum.Parse<ProcessOutcome>(outcome), outcome != "NotStarted", null, string.Empty, string.Empty));
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.False(scan.Passed(Plan));
        Assert.Null(scan.FindingCount);
    }

    [Theory]
    [InlineData("8.24.2")]
    [InlineData("v7.6.1")]
    public void AScannerOlderThanItsConfigurationNeedsIsUnavailable(string version)
    {
        int scans = 0;
        var scanner = new GitleaksScanner(info =>
        {
            if (info.FileName == "git") return Success("2");
            if (info.ArgumentList[0] == "version") return Success(version);
            scans++;
            return Success();
        });
        SecretScan scan = scanner.Scan(Path.GetTempPath(), Plan);
        Assert.Equal("unavailable", scan.Verdict);
        Assert.Equal(0, scans);
    }

    [Fact]
    public void AScannerMissingBeforeItsVersionDoesNotRunAScan()
    {
        int calls = 0;
        var scanner = new GitleaksScanner(_ => { calls++; return new(ProcessOutcome.NotStarted, false, null, string.Empty, string.Empty); });
        Assert.Equal("unavailable", scanner.Scan(Path.GetTempPath(), Plan).Verdict);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AnUnreadableRangeIsRefusedEvenWhenTheScannerWouldReportZeroFindings()
    {
        int scans = 0;
        var scanner = new GitleaksScanner(info =>
        {
            if (AsksForTheGitDirectory(info)) return Success(GitDirectory + "\n");
            if (info.FileName == "git") return new(ProcessOutcome.Exited, true, 128, string.Empty, "unreadable range");
            if (info.ArgumentList[0] == "version") return Success("8.30.1");
            scans++;
            return Success();
        });
        Assert.Equal("incomplete", scanner.Scan(Path.GetTempPath(), Plan).Verdict);
        Assert.Equal(0, scans);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PushUsesExactlyTheScannedCommitAndAnExplicitRemoteLease(bool newBranch)
    {
        var scripted = new RecordedGit { NewBranch = newBranch };
        var adapter = new GitPushAdapter(scripted.Run);
        PushPlan plan = adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head);
        Assert.Equal(Basis, plan.Base);
        Assert.Equal(newBranch ? null : Basis, plan.RemoteHead);
        adapter.Push(Path.GetTempPath(), "https://example.invalid/widget", plan, new("scanner", "1.0.0", plan.Range, "passed", 0));
        string[] push = scripted.Calls.Single(c => c.Contains("push"));
        Assert.Contains(Head + ":refs/heads/work/change", push);
        Assert.Contains("--force-with-lease=refs/heads/work/change:" + (newBranch ? string.Empty : Basis), push);
        Assert.Contains("--no-follow-tags", push);
        Assert.Contains("--recurse-submodules=no", push);
        // Remote access runs from a private repository with only the remote's protocol, never in the clone itself.
        Assert.All(scripted.Calls.Where(c => c.Contains("push") || c.Contains("ls-remote")), c =>
        {
            Assert.Contains(c, a => a.StartsWith("--git-dir=", StringComparison.Ordinal));
            Assert.Contains("protocol.allow=never", c);
            Assert.Contains("protocol.https.allow=always", c);
        });
    }

    [Theory]
    [InlineData("http://example.invalid/widget")]
    [InlineData("ssh://example.invalid/widget")]
    [InlineData("git@example.invalid:widget")]
    [InlineData("ext::sh -c widget")]
    [InlineData("widget")]
    public void ACheckedPushGoesOnlyToAnHttpsRemoteOrALocalRepository(string remote)
    {
        var scripted = new RecordedGit();
        var adapter = new GitPushAdapter(scripted.Run);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), remote, "work/change", "main", Head));
        Assert.DoesNotContain(scripted.Calls, c => c.Contains("ls-remote") || c.Contains("push") || c.Contains("config"));
    }

    [Fact]
    public void OnlyTheUsersCredentialAndTrustSettingsReachTheRemote()
    {
        var scripted = new RecordedGit
        {
            Settings = "unknown\0credential.helper\nosxkeychain\0global\0credential.https://example.invalid.helper\n\0"
                + "global\0credential.https://example.invalid.helper\n!helper get\0command\0credential.helper\ninjected\0"
                + "local\0credential.helper\nclone\0system\0http.sslcainfo\n/trust/bundle.pem\0global\0credential.usehttppath\0",
        };
        var adapter = new GitPushAdapter(scripted.Run);
        PushPlan plan = adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head);
        adapter.Push(Path.GetTempPath(), "https://example.invalid/widget", plan, new("scanner", "1.0.0", plan.Range, "passed", 0));
        Assert.Equal(5, scripted.Remote.Count);
        Assert.All(scripted.Remote, environment =>
        {
            Assert.Equal("1", environment["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal(string.Empty, environment["GIT_CONFIG_GLOBAL"]);
            Assert.Equal("5", environment["GIT_CONFIG_COUNT"]);
            Assert.Equal(
                ["credential.helper=osxkeychain", "credential.https://example.invalid.helper=", "credential.https://example.invalid.helper=!helper get",
                    "http.sslcainfo=/trust/bundle.pem", "credential.usehttppath=true"],
                Enumerable.Range(0, 5).Select(i => environment["GIT_CONFIG_KEY_" + i] + "=" + environment["GIT_CONFIG_VALUE_" + i]));
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("remote")]
    [InlineData("target")]
    [InlineData("scan")]
    public void NothingIsPushedIfHeadRemoteBaseOrScanChanges(string moved)
    {
        var scripted = new RecordedGit();
        var adapter = new GitPushAdapter(scripted.Run);
        PushPlan plan = adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head);
        scripted.Moved = moved;
        var scan = new SecretScan("scanner", "1.0.0", moved == "scan" ? Basis + ".." + Moved : plan.Range, "passed", 0);
        Assert.Throws<ProviderRefusedException>(() => adapter.Push(Path.GetTempPath(), "https://example.invalid/widget", plan, scan));
        Assert.DoesNotContain(scripted.Calls, c => c.Contains("push"));
    }

    [Fact]
    public void APushToItsTargetBranchIsRefusedBeforeTheRemoteIsRead()
    {
        var scripted = new RecordedGit();
        var adapter = new GitPushAdapter(scripted.Run);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "main", "main", Head, []));
        Assert.DoesNotContain(scripted.Calls, c => c.Contains("ls-remote") || c.Contains("push"));
    }

    [Fact]
    public void AnAdapterWrittenBeforeTheRecordsHeadsTakesNoCheckedPush()
    {
        IPushAdapter adapter = new OlderAdapter();
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head, []));
        PushPlan plan = new("work/change", "main", Head, Basis, null, Basis);
        Assert.Throws<ProviderRefusedException>(() => adapter.Push(Path.GetTempPath(), "https://example.invalid/widget", plan, new("scanner", "1.0.0", plan.Range, "passed", 0), []));
    }

    private sealed class OlderAdapter : IPushAdapter
    {
        public PushPlan Prepare(string directory, string remote, string branch, string target, string head) => new(branch, target, head, head, head, Basis);
        public void Push(string directory, string remote, PushPlan plan, SecretScan scan) { }
        public string? Observe(string directory, string remote, string branch) => null;
    }

    [Fact]
    public void NonFastForwardUpdatesAreRefusedBeforeScanning()
    {
        var scripted = new RecordedGit { Divergent = true };
        var adapter = new GitPushAdapter(scripted.Run);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head));
    }

    [Fact]
    public void AShallowCloneCannotClaimThatEveryCommitWasScanned()
    {
        var scripted = new RecordedGit { Shallow = true };
        var adapter = new GitPushAdapter(scripted.Run);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(Path.GetTempPath(), "https://example.invalid/widget", "work/change", "main", Head));
        Assert.DoesNotContain(scripted.Calls, c => c.Contains("ls-remote") || c.Contains("push"));
    }

    private static ProcessResult Success(string output = "") => new(ProcessOutcome.Exited, true, 0, output, string.Empty);

    // The clone's git directory, as git names it to the scan's first git process (FIX-160): an existing directory, by an absolute path.
    private static readonly string GitDirectory = Path.GetTempPath();

    private static bool AsksForTheGitDirectory(ProcessStartInfo info) => info.FileName == "git" && info.ArgumentList.Contains("--absolute-git-dir");

    // A git process of the scan starts with the hardened options of every git process on a repository Vericairn did not create (FIX-147):
    // no pager, the empty hooks directory, then the shared settings. What follows them is the scan's own request.
    private static string[] Requested(ProcessStartInfo info)
    {
        Assert.Equal("--no-pager", info.ArgumentList[0]);
        Assert.Equal("-c", info.ArgumentList[1]);
        Assert.StartsWith("core.hooksPath=", info.ArgumentList[2], StringComparison.Ordinal);
        Assert.Equal(GitHardening.Settings.SelectMany(setting => new[] { "-c", setting }), info.ArgumentList.Skip(3).Take(2 * GitHardening.Settings.Length));
        return [.. info.ArgumentList.Skip(3 + 2 * GitHardening.Settings.Length)];
    }

    private sealed class RecordedGit
    {
        internal List<string[]> Calls { get; } = [];
        internal bool NewBranch { get; set; }
        internal bool Divergent { get; set; }
        internal bool Shallow { get; set; }
        internal string? Moved { get; set; }
        internal string Settings { get; set; } = string.Empty;
        internal List<Dictionary<string, string?>> Remote { get; } = [];
        internal ProcessResult Run(ProcessStartInfo info)
        {
            Assert.Equal("git", info.FileName);
            string[] args = info.ArgumentList.ToArray();
            Calls.Add(args);
            if (args.Contains("ls-remote") || args.Contains("push")) Remote.Add(new(info.Environment));
            if (args.Contains("check-ref-format")) return Success();
            if (args.Contains("config")) return Settings.Length == 0 ? new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty) : Success(Settings);
            if (args.Contains("--git-common-dir")) return Success(Path.Combine(Path.GetTempPath(), "clone.git"));
            if (args.Contains("--is-shallow-repository")) return Success(Shallow ? "true" : "false");
            if (args.Contains("rev-parse")) return Success(Moved == "head" ? PushProviderTests.Moved : Head);
            if (args.Contains("ls-remote"))
            {
                bool target = args[^1] == "refs/heads/main";
                if (!target && NewBranch) return Success();
                return Success((Moved == (target ? "target" : "remote") ? PushProviderTests.Moved : Basis) + "\t" + args[^1] + "\n");
            }
            if (args.Contains("merge-base")) return Divergent ? new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty) : Success(Basis);
            if (args.Contains("push")) return Success();
            throw new InvalidOperationException("Unscripted git invocation.");
        }
    }
}
