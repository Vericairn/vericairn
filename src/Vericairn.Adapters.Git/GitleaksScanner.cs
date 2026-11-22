// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Adapters.Git;

/// <summary>Scans a pinned commit range with redaction and a count-only report. Matched text never enters a record.</summary>
public sealed partial class GitleaksScanner : ISecretScanner
{
    /// <summary>
    /// gitleaks' default rules with every built-in allowlist removed, then Vericairn's own allowances, none of which exempts a
    /// path or admits a sample credential. Each scan records this configuration's hash beside the scanner's version.
    /// </summary>
    internal static readonly byte[] Configuration = ReadConfiguration();

    internal static readonly string ConfigurationHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Configuration));

    /// <summary>
    /// The configuration without its last allowance, for history checkpoint inventories (FIX-150): the rules a scan applies when a file
    /// that allowance could reach holds anything besides inventory lines, recorded by its own hash.
    /// </summary>
    internal static readonly byte[] ConfigurationWithoutInventories = WithoutInventories(Configuration);

    internal static readonly string ConfigurationWithoutInventoriesHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(ConfigurationWithoutInventories));

    /// <summary>The path condition of the inventory allowance: a file named by its SHA-256 in the objects folder at the repository's root.</summary>
    internal const string InventoryPath = "^objects/[0-9a-f]{64}$";

    /// <summary>
    /// The record types an inventory line's first position takes: the names the estate format defines (RecordTypeRegistry), and no other
    /// value, so that a credential there breaks the line's shape and is found.
    /// </summary>
    internal const string InventoryTypes = "acceptance|admission|amendment|ask|assignment|attempt|authority|communication-delegation|communication-setting"
        + "|credential-reference|custody-exception|decision|device|discharge|document-review|document-revision|estate|estate-policy|goal|goal-status"
        + "|harness-qualification|history-checkpoint|living-document|migration|observation|operation|operator-request|operator-request-close"
        + "|policy-exception|possibility|principal|product|prompt-preference|qualification|reconciliation|release|resource|result|retirement|review"
        + "|roster|saved-prompt|template-defaults|verification|work-order|work-progress|work-status|writing-exception";

    /// <summary>
    /// The shape of a history checkpoint inventory's line, without its line feed: one of those record types, a month, a record id and .json,
    /// a space, then sha256: and 64 hexadecimal digits (HistoryInventory). Only the hash position can hold a value a rule finds; every
    /// other position holds a fixed name, digits or a record id's characters. It is read both as RE2, by gitleaks, and as POSIX extended,
    /// by git grep.
    /// </summary>
    internal const string InventoryLine = "(" + InventoryTypes + @")/[0-9]{4}-(0[1-9]|1[0-2])/[0-7][0-9A-HJKMNP-TV-Z]{25}\.json sha256:[0-9a-f]{64}";

    // The options of gitleaks' history read, git log -p -U0 over the range, which the scanner's own patch read repeats exactly; -m reads
    // each merge as its patch against its first parent, whatever the clone's log.diffMerges says (ReadSettings).
    private static readonly string[] LogOptions = ["--full-history", "--no-renames", "-m", "--no-ext-diff", "--no-textconv", "--text"];

    // The patch read's output goes nowhere: it is read to establish that git can read every patch, never retained.
    private static readonly string NullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    private const string InventoryAllowance = "\n[[allowlists]]\ndescription = \"History checkpoint inventories";

    // Paths named in one git grep, which keeps its command line well under every platform's limit.
    private const int PathsPerGrep = 64;

    // The configuration's allowances target rules by name, which gitleaks reads from 8.25.0.
    private static readonly Version Minimum = new(8, 25, 0);

    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each scanner invocation is bounded to five minutes.</summary>
    public GitleaksScanner() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(5))) { }

    internal GitleaksScanner(Func<ProcessStartInfo, ProcessResult> run) => this.run = run;

    /// <inheritdoc />
    public SecretScan Scan(string directory, PushPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Patterns.Commit.IsMatch(plan.Base) || !Patterns.Commit.IsMatch(plan.Head))
            throw new ProviderRefusedException("The scan requires two exact commit ids.");
        ProcessResult version = Invoke(directory, "version");
        string number = version.StandardOutput.Trim();
        Match parsed = VersionPattern().Match(number);
        if (version.Outcome != ProcessOutcome.Exited || version.ExitCode != 0 || !parsed.Success)
            return new("gitleaks", "unknown", plan.Range, "unavailable", null);
        if (new Version(int.Parse(parsed.Groups["major"].Value, CultureInfo.InvariantCulture), int.Parse(parsed.Groups["minor"].Value, CultureInfo.InvariantCulture),
                int.Parse(parsed.Groups["patch"].Value, CultureInfo.InvariantCulture)) < Minimum)
            return new("gitleaks", number, plan.Range, "unavailable", null);
        bool inventories = OnlyInventoriesHoldInventoryLines(directory, plan.Range);
        byte[] rules = inventories ? Configuration : ConfigurationWithoutInventories;
        number += "; rules " + (inventories ? ConfigurationHash : ConfigurationWithoutInventoriesHash);
        int? commits = Count(directory, plan.Range);
        if (commits is null) return new("gitleaks", number, plan.Range, "incomplete", null);
        // Gitleaks reports no finding when git log -p stops early, at an unreadable or corrupt object, and still exits 0. Before it runs,
        // every commit, tree and blob the push carries is read and checked against its id, and the range's patches are read exactly as
        // gitleaks reads them, parents' sides included; a read that fails or stops early leaves the scan incomplete (FIX-150).
        if (!Reads(directory, "rev-list", "--objects", "--verify-objects", "--quiet", plan.Range)
            || !Reads(directory, ["log", "-p", "-U0", plan.Range, .. LogOptions, "--output=" + NullDevice]))
            return new("gitleaks", number, plan.Range, "incomplete", null);
        string temporary = Path.Combine(Path.GetTempPath(), "vericairn-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string template = Path.Combine(temporary, "count.tmpl");
            string report = Path.Combine(temporary, "count.txt");
            File.WriteAllText(template, "{{ len . }}");
            string configuration = Path.Combine(temporary, "rules.toml");
            string ignored = Path.Combine(temporary, "ignore.txt");
            File.WriteAllBytes(configuration, rules);
            File.WriteAllText(ignored, string.Empty);
            // Gitleaks writes only the aggregate count to disk, even for a finding. No JSON findings report is created or read.
            ProcessResult result = Invoke(directory, "git", "--redact=100", "--no-banner", "--log-level=error", "--ignore-gitleaks-allow",
                "--config=" + configuration, "--gitleaks-ignore-path=" + ignored,
                "--log-opts=" + plan.Range + " " + string.Join(' ', LogOptions),
                "--report-format=template", "--report-template=" + template, "--report-path=" + report, ".");
            if (result.Outcome != ProcessOutcome.Exited || result.ExitCode is not (0 or 1) || !File.Exists(report) || new FileInfo(report).Length > 16
                || !int.TryParse(File.ReadAllText(report).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
                return new("gitleaks", number, plan.Range, "incomplete", null);
            if (Count(directory, plan.Range) != commits) return new("gitleaks", number, plan.Range, "incomplete", null);
            if (count > 0) return new("gitleaks", number, plan.Range, "findings", count);
            // Gitleaks logs at error level, the only level asked for, when its own read of the history fails or stops early: it relays git's
            // standard error and stops reading, and still exits 0. Such a scan saw only part of the range. Its words never enter the record.
            if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StandardError)) return new("gitleaks", number, plan.Range, "incomplete", count);
            return new("gitleaks", number, plan.Range, "passed", 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return new("gitleaks", number, plan.Range, "incomplete", null); }
        finally
        {
            try { Directory.Delete(temporary, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { /* A count-only temporary report has no finding contents. */ }
        }
    }

    private ProcessResult Invoke(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("gitleaks") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        // The scanner starts git in the clone itself; git's configuration environment carries the same hardening to it (Isolate).
        using GitHardening hardening = Harden();
        Isolate(info, hardening);
        return run(info);
    }

    // A clone's info/grafts, replace objects and derived object caches (the commit-graph, reachability bitmaps and the multi-pack-index)
    // can each misreport a commit's parents, and so hide a commit of the range from the scan while the push, which reads the real objects,
    // still delivers it. Every git process the scan starts, gitleaks' own included through the environment it passes on, reads grafts from
    // a file that never exists and ignores replace objects and those caches, as the push adapter's reads do (CODE-81).
    private static readonly string NoGrafts = Path.Combine(Path.GetTempPath(), "vericairn-no-grafts-" + Guid.NewGuid().ToString("N"), "grafts");

    // A clone's display settings can also change what git log -p shows gitleaks while the push still delivers every object: color makes
    // gitleaks parse nothing, log.showRoot=false hides a root commit's patch, and a signature check's diagnostics stop gitleaks' read.
    // The form of a merge's patch, which -m asks for, is log.diffMerges': off or none shows none, a combined form is one git will not
    // write to a file and gitleaks cannot parse, and remerge skips a merge of more than two parents. A merge is read as its patch against
    // its first parent, which holds every line the merge adds, its own or brought in from another parent, while each commit of another
    // parent that the range holds is read with its own patch. Every git process the scan starts reads these values instead, and none
    // inherits git's trace variables, whose output on standard error would stop gitleaks' read too (FIX-150).
    private static readonly (string Key, string Value)[] ReadSettings = [("core.commitGraph", "false"), ("pack.useBitmaps", "false"), ("core.multiPackIndex", "false"),
        ("color.ui", "false"), ("color.diff", "false"), ("log.showRoot", "true"), ("log.showSignature", "false"), ("log.diffMerges", "first-parent")];

    // The scan reads the history the push carries: its processes drop every routing variable the push adapter's drop, from the same list,
    // since a shallow file, another repository or another object store named in the environment would show git a shorter or another
    // history than the push sends; then every other configuration variable, keeping only the settings forced here, and git's trace
    // variables (FIX-150). The settings forced here are the hardening of every git process on a repository Vericairn did not create
    // (GitHardening: no system or global configuration, an empty hooks directory, and the clone's own program settings overridden),
    // followed by the read settings above, all carried through git's configuration environment, which every git process the scan starts
    // reads, gitleaks' own included. The hardening's directory must outlive the process.
    internal static void Isolate(ProcessStartInfo info, GitHardening hardening)
    {
        ArgumentNullException.ThrowIfNull(hardening);
        GitRouting.Remove(info);
        foreach (string name in info.Environment.Keys.Where(n => n.StartsWith("GIT_CONFIG", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(name);
        hardening.ApplyEnvironment(info, ReadSettings.Select(s => s.Key + "=" + s.Value));
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_GRAFT_FILE"] = NoGrafts;
    }

    /// <summary>
    /// Whether the inventory allowance may apply to this range (FIX-150): true when every file the range's commits add or change at a path
    /// the allowance names, and that holds a line of an inventory's shape, holds only such lines, as a history checkpoint's inventory
    /// does. The scanner cannot tell from the commits which objects a checkpoint names, so a file that mixes inventory lines with any
    /// other line, or a range whose files cannot be read, is scanned without the allowance. Only file names are read back, never contents.
    /// </summary>
    private bool OnlyInventoriesHoldInventoryLines(string directory, string range)
    {
        // The same commits and parent diffs gitleaks reads: --full-history, -m and --no-renames, every change but a deletion.
        ProcessResult changed = InventoryGit(directory, "log", "--no-show-signature", "--format=commit %H", "--name-only", "--root", "--no-renames", "--full-history", "-m",
            "--diff-filter=d", "--no-relative", "--no-ext-diff", "--no-textconv", range, "--", ":(top)objects");
        if (changed.Outcome != ProcessOutcome.Exited || changed.ExitCode != 0) return false;
        var files = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        string? commit = null;
        foreach (string entry in changed.StandardOutput.Split('\n'))
        {
            string line = entry.TrimEnd('\r');
            if (line.StartsWith("commit ", StringComparison.Ordinal))
            {
                commit = line["commit ".Length..];
                if (!Patterns.Commit.IsMatch(commit)) return false;
            }
            else if (InventoryPathPattern().IsMatch(line))
            {
                if (commit is null) return false;
                if (!files.TryGetValue(commit, out SortedSet<string>? paths)) files[commit] = paths = new(StringComparer.Ordinal);
                paths.Add(line);
            }
        }

        foreach ((string at, SortedSet<string> paths) in files)
        {
            foreach (string[] chunk in paths.Chunk(PathsPerGrep))
            {
                // A file can hold an inventory's lines and another line only if it is listed by both searches.
                if (FilesWithLines(directory, at, chunk, matching: true) is not { } shaped
                    || FilesWithLines(directory, at, chunk, matching: false) is not { } other) return false;
                if (shaped.Overlaps(other)) return false;
            }
        }

        return true;
    }

    // The files of the commit's tree among the paths that hold a line of an inventory's shape, or a line of any other; null when unread.
    private HashSet<string>? FilesWithLines(string directory, string commit, string[] paths, bool matching)
    {
        var arguments = new List<string> { "grep", "--no-textconv", "--full-name", "--text", "--files-with-matches", "--extended-regexp" };
        if (!matching) arguments.Add("--invert-match");
        arguments.AddRange(["-e", "^" + InventoryLine + "$", commit, "--"]);
        arguments.AddRange(paths.Select(p => ":(top)" + p));
        ProcessResult result = InventoryGit(directory, [.. arguments]);
        if (result.Outcome != ProcessOutcome.Exited || result.ExitCode is not (0 or 1)) return null;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in result.StandardOutput.Split('\n'))
        {
            string line = entry.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (!line.StartsWith(commit + ":", StringComparison.Ordinal) || !paths.Contains(line[(commit.Length + 1)..], StringComparer.Ordinal)) return null;
            found.Add(line[(commit.Length + 1)..]);
        }

        return result.ExitCode == 1 && found.Count > 0 ? null : found;
    }

    private ProcessResult InventoryGit(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using GitHardening hardening = Harden();
        hardening.Apply(info, ["--no-replace-objects", .. arguments]);
        Isolate(info, hardening);
        // Character ranges in the line's shape read as bytes, and pathspecs keep their magic whatever the environment asks.
        info.Environment["LC_ALL"] = "C";
        foreach (string name in new[] { "GIT_LITERAL_PATHSPECS", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_ICASE_PATHSPECS" }) info.Environment.Remove(name);
        return run(info);
    }

    private static byte[] WithoutInventories(byte[] configuration)
    {
        string text = System.Text.Encoding.UTF8.GetString(configuration);
        int at = text.IndexOf(InventoryAllowance, StringComparison.Ordinal);
        if (at < 0 || text.LastIndexOf("[[allowlists]]", StringComparison.Ordinal) != at + 1)
            throw new InvalidOperationException("The scan configuration's last allowance is not the inventory allowance.");
        return System.Text.Encoding.UTF8.GetBytes(text[..at]);
    }

    // Whether a git read of the range completed: it exited 0, having read everything it was asked to. Its output is not retained.
    private bool Reads(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using GitHardening hardening = Harden();
        hardening.Apply(info, ["--no-replace-objects", .. arguments]);
        Isolate(info, hardening);
        ProcessResult result = run(info);
        return result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0;
    }

    private int? Count(string directory, string range)
    {
        // Gitleaks can report zero findings when git cannot read a range. Establish readability independently,
        // before and after scanning, without asking git for patches or retaining any source text.
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using GitHardening hardening = Harden();
        hardening.Apply(info, ["--no-replace-objects", "rev-list", "--count", range]);
        Isolate(info, hardening);
        ProcessResult result = run(info);
        return result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0
            && int.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count >= 0 ? count : null;
    }

    private static GitHardening Harden()
    {
        try
        {
            return GitHardening.Create();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ProviderRefusedException("git cannot run isolated: a temporary directory could not be created; nothing was scanned.", error);
        }
    }

    private static byte[] ReadConfiguration()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vericairn.Adapters.Git.gitleaks.toml")
            ?? throw new InvalidOperationException("The scan configuration is not embedded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    // Bounded so that the version with the configuration's hash fits the recorded scan's version field.
    [GeneratedRegex(InventoryPath, RegexOptions.CultureInvariant)]
    private static partial Regex InventoryPathPattern();

    [GeneratedRegex(@"^v?(?<major>[0-9]{1,3})\.(?<minor>[0-9]{1,3})\.(?<patch>[0-9]{1,3})(?:-[a-zA-Z0-9.]{1,8})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
