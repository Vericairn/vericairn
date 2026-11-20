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
    /// The shape of a history checkpoint inventory's line, without its line feed: a record type, a month, a record id and .json, a space,
    /// then sha256: and 64 hexadecimal digits (HistoryInventory). It is read both as RE2, by gitleaks, and as POSIX extended, by git grep.
    /// </summary>
    internal const string InventoryLine = @"[a-z][a-z0-9-]{0,63}/[0-9]{4}-(0[1-9]|1[0-2])/[0-7][0-9A-HJKMNP-TV-Z]{25}\.json sha256:[0-9a-f]{64}";

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
                "--log-opts=" + plan.Range + " --full-history --no-renames -m --no-ext-diff --no-textconv --text",
                "--report-format=template", "--report-template=" + template, "--report-path=" + report, ".");
            if (result.Outcome != ProcessOutcome.Exited || result.ExitCode is not (0 or 1) || !File.Exists(report) || new FileInfo(report).Length > 16
                || !int.TryParse(File.ReadAllText(report).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
                return new("gitleaks", number, plan.Range, "incomplete", null);
            if (Count(directory, plan.Range) != commits) return new("gitleaks", number, plan.Range, "incomplete", null);
            if (result.ExitCode == 0 && count == 0) return new("gitleaks", number, plan.Range, "passed", 0);
            return new("gitleaks", number, plan.Range, count > 0 ? "findings" : "incomplete", count);
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
        Isolate(info);
        return run(info);
    }

    // A clone's info/grafts, replace objects and derived object caches (the commit-graph, reachability bitmaps and the multi-pack-index)
    // can each misreport a commit's parents, and so hide a commit of the range from the scan while the push, which reads the real objects,
    // still delivers it. Every git process the scan starts, gitleaks' own included through the environment it passes on, reads grafts from
    // a file that never exists and ignores replace objects and those caches, as the push adapter's reads do (CODE-81).
    private static readonly string NoGrafts = Path.Combine(Path.GetTempPath(), "vericairn-no-grafts-" + Guid.NewGuid().ToString("N"), "grafts");

    private static readonly (string Key, string Value)[] IgnoredCaches = [("core.commitGraph", "false"), ("pack.useBitmaps", "false"), ("core.multiPackIndex", "false")];

    private static void Isolate(ProcessStartInfo info)
    {
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_GRAFT_FILE"] = NoGrafts;
        info.Environment.Remove("GIT_CONFIG_PARAMETERS");
        foreach (string name in info.Environment.Keys.Where(n => n.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(name);
        info.Environment["GIT_CONFIG_COUNT"] = IgnoredCaches.Length.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < IgnoredCaches.Length; i++)
        {
            info.Environment["GIT_CONFIG_KEY_" + i.ToString(CultureInfo.InvariantCulture)] = IgnoredCaches[i].Key;
            info.Environment["GIT_CONFIG_VALUE_" + i.ToString(CultureInfo.InvariantCulture)] = IgnoredCaches[i].Value;
        }
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
        info.ArgumentList.Add("--no-replace-objects");
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        Isolate(info);
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

    private int? Count(string directory, string range)
    {
        // Gitleaks can report zero findings when git cannot read a range. Establish readability independently,
        // before and after scanning, without asking git for patches or retaining any source text.
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in new[] { "--no-replace-objects", "rev-list", "--count", range }) info.ArgumentList.Add(argument);
        Isolate(info);
        ProcessResult result = run(info);
        return result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0
            && int.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count >= 0 ? count : null;
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
