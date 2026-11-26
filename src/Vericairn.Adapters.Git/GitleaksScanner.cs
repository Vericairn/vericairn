// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Adapters.Git;

/// <summary>Scans a pinned commit range with redaction and a count-only report. Matched text never enters a record.</summary>
public sealed partial class GitleaksScanner : ISecretScanner
{
    /// <summary>
    /// gitleaks' default rules with every built-in allowlist removed, then Vericairn's own allowances, none of which exempts a
    /// path or admits a sample credential. Each scan records the hash of the configuration it applies beside the scanner's version:
    /// this one or the one without its inventory allowance, and from 0.9.5 either with the allowance for the release bases the range
    /// carries appended when it carries one (FIX-168).
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

    /// <summary>
    /// The rule that reads a release basis's git revisions as tokens (FIX-168): wherever a fragment names Sourcegraph, as a basis does that
    /// lists a file such as DocumentSourceGraph.cs, it finds any 40 hexadecimal digits standing alone, which is what a commit id is.
    /// </summary>
    internal const string ReleaseBasisRule = "sourcegraph-access-token";

    /// <summary>How a release basis held as one object begins: its canonical JSON, whose first member is its basis. Read as POSIX extended.</summary>
    internal const string ReleaseBasisStart = "^\\{\"basis\":\\{";

    /// <summary>
    /// How a split object's root begins (FIX-163, SplitObject): its canonical JSON, naming its length, its digest and its format before its
    /// parts. Read as POSIX extended.
    /// </summary>
    internal const string SplitRootStart = "^\\{\"bytes\":[1-9][0-9]*,\"digest\":\"sha256:[0-9a-f]{64}\",\"format\":\"vericairn\\.split-object/1\",\"parts\":\\[";

    /// <summary>
    /// The most characters the scan keeps of one process's standard output: an object of the estate's object limit, the most a release
    /// basis's object or part holds, read whole (FIX-168).
    /// </summary>
    internal const int OutputLimit = (int)EstateLayout.MaxObjectBytes;

    // The release basis formats the product writes and reads (EstateService.ReleasePolicies).
    private static readonly string[] ReleaseBasisFormats = ["vericairn.release-basis/1", "vericairn.release-basis/2", "vericairn.release-basis/3"];

    // The options of gitleaks' history read, git log -p -U0 over the range, which the scanner's own patch read repeats exactly; -m reads
    // each merge as its patch against its first parent, whatever the clone's log.diffMerges says (ReadSettings). --submodule=short shows a
    // submodule's change as its two commit ids, whatever the clone's diff.submodule says, so neither read starts a git process of its own
    // in the submodule, which would read the submodule's configuration and attributes without these options.
    private static readonly string[] LogOptions = ["--full-history", "--no-renames", "-m", "--no-ext-diff", "--no-textconv", "--text", "--submodule=short"];

    // The patch read's output goes nowhere: it is read to establish that git can read every patch, never retained.
    private static readonly string NullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    private const string InventoryAllowance = "\n[[allowlists]]\ndescription = \"History checkpoint inventories";

    // Paths named in one git grep, which keeps its command line well under every platform's limit.
    private const int PathsPerGrep = 64;

    // The configuration's allowances target rules by name, which gitleaks reads from 8.25.0.
    private static readonly Version Minimum = new(8, 25, 0);

    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each scanner invocation is bounded to five minutes, and its output to an object of the estate's object limit.</summary>
    public GitleaksScanner() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(5), OutputLimit)) { }

    internal GitleaksScanner(Func<ProcessStartInfo, ProcessResult> run) => this.run = run;

    /// <inheritdoc />
    public SecretScan Scan(string directory, PushPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!Patterns.Commit.IsMatch(plan.Base) || !Patterns.Commit.IsMatch(plan.Head))
            throw new ProviderRefusedException("The scan requires two exact commit ids.");
        // The scan's own folder holds the rules, the ignore file and the report it writes, beside the empty folder in which every process
        // of the scan runs (FIX-160, ScanFolder).
        string temporary = Path.Combine(Path.GetTempPath(), "vericairn-scan-" + Guid.NewGuid().ToString("N"));
        string number = "unknown";
        using GitHardening hardening = Harden();
        try
        {
            var folder = new ScanFolder(Path.Combine(temporary, "source"), null, hardening);
            Directory.CreateDirectory(folder.Source);
            ProcessResult version = run(Start("gitleaks", folder, ["version"]));
            number = version.StandardOutput.Trim();
            Match parsed = VersionPattern().Match(number);
            if (version.Outcome != ProcessOutcome.Exited || version.ExitCode != 0 || !parsed.Success)
                return new("gitleaks", "unknown", plan.Range, "unavailable", null);
            if (new Version(int.Parse(parsed.Groups["major"].Value, CultureInfo.InvariantCulture), int.Parse(parsed.Groups["minor"].Value, CultureInfo.InvariantCulture),
                    int.Parse(parsed.Groups["patch"].Value, CultureInfo.InvariantCulture)) < Minimum)
                return new("gitleaks", number, plan.Range, "unavailable", null);
            folder = folder with { GitDirectory = GitDirectory(folder, directory) };
            SortedDictionary<string, SortedSet<string>>? objects = folder.GitDirectory is null ? null : ObjectFiles(folder, plan.Range);
            bool inventories = objects is not null && OnlyInventoriesHoldInventoryLines(folder, objects);
            byte[] rules = inventories ? Configuration : ConfigurationWithoutInventories;
            // FIX-168: the git revisions each release basis the range carries records, in the objects that hold that basis's bytes.
            if (objects is not null && ReleaseBasisEvidence(folder, objects) is { } evidence)
                rules = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rules) + "\n" + ReleaseBasisAllowance(evidence.Paths, evidence.Revisions));
            number += "; rules sha256:" + Convert.ToHexStringLower(SHA256.HashData(rules));
            int? commits = folder.GitDirectory is null ? null : Count(folder, plan.Range);
            if (commits is null) return new("gitleaks", number, plan.Range, "incomplete", null);
            // Gitleaks reports no finding when git log -p stops early, at an unreadable or corrupt object, and still exits 0. Before it runs,
            // every commit, tree and blob the push carries is read and checked against its id, and the range's patches are read exactly as
            // gitleaks reads them, parents' sides included; a read that fails or stops early leaves the scan incomplete (FIX-150).
            if (!Reads(folder, "rev-list", "--objects", "--verify-objects", "--quiet", plan.Range)
                || !Reads(folder, ["log", "-p", "-U0", plan.Range, .. LogOptions, "--output=" + NullDevice]))
                return new("gitleaks", number, plan.Range, "incomplete", null);
            string template = Path.Combine(temporary, "count.tmpl");
            string report = Path.Combine(temporary, "count.txt");
            File.WriteAllText(template, "{{ len . }}");
            string configuration = Path.Combine(temporary, "rules.toml");
            string ignored = Path.Combine(temporary, "ignore.txt");
            File.WriteAllBytes(configuration, rules);
            File.WriteAllText(ignored, string.Empty);
            // Gitleaks' source is still the empty folder the scan created, so nothing gitleaks reads from it is the clone's.
            if (Directory.EnumerateFileSystemEntries(folder.Source).Any()) return new("gitleaks", number, plan.Range, "incomplete", null);
            // Gitleaks writes only the aggregate count to disk, even for a finding. No JSON findings report is created or read.
            ProcessResult result = run(Start("gitleaks", folder, ["git", "--redact=100", "--no-banner", "--log-level=error", "--ignore-gitleaks-allow",
                "--config=" + configuration, "--gitleaks-ignore-path=" + ignored,
                "--log-opts=" + plan.Range + " " + string.Join(' ', LogOptions),
                "--report-format=template", "--report-template=" + template, "--report-path=" + report, folder.Source]));
            if (result.Outcome != ProcessOutcome.Exited || result.ExitCode is not (0 or 1) || !File.Exists(report) || new FileInfo(report).Length > 16
                || !int.TryParse(File.ReadAllText(report).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
                return new("gitleaks", number, plan.Range, "incomplete", null);
            if (Count(folder, plan.Range) != commits) return new("gitleaks", number, plan.Range, "incomplete", null);
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

    // Gitleaks takes an ignore list from the folder it scans as well as from its ignore path, and its rules from GITLEAKS_CONFIG,
    // GITLEAKS_CONFIG_TOML or that folder's .gitleaks.toml when no --config is given; its own git inherits its environment. The scan gives
    // it nothing but what the scan writes (FIX-160). Every process of the scan, gitleaks and git alike, runs in an empty folder the scan
    // creates for itself, which is gitleaks' source; once git has named the clone's git directory from the clone, each reads it through
    // GIT_DIR alone, with that empty folder as its work tree, so no file of the clone's working tree is read and no file in the git
    // directory is taken for gitleaks' own; and none inherits a GITLEAKS_ or git variable (Isolate).
    // Each also runs with the hardening of every git process on a repository Vericairn did not create (FIX-147): a git process takes it on
    // its command line and, as gitleaks' own git does, through git's configuration environment (Isolate). The scan's one hardening outlives
    // its last process.
    private sealed record ScanFolder(string Source, string? GitDirectory, GitHardening Hardening);

    private static ProcessStartInfo Start(string file, ScanFolder folder, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(file) { WorkingDirectory = folder.Source, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (file == "git") folder.Hardening.Apply(info, arguments);
        else foreach (string argument in arguments) info.ArgumentList.Add(argument);
        Isolate(info, folder.Hardening);
        if (folder.GitDirectory is not null)
        {
            info.Environment["GIT_DIR"] = folder.GitDirectory;
            info.Environment["GIT_WORK_TREE"] = folder.Source;
        }
        return info;
    }

    // The clone's git directory as git finds it from the clone, the repository the push reads; null unless git names an existing directory
    // by an absolute path.
    private string? GitDirectory(ScanFolder folder, string directory)
    {
        string clone;
        try { clone = Path.GetFullPath(directory); }
        catch (ArgumentException) { return null; }
        ProcessResult found = run(Start("git", folder, ["-C", clone, "rev-parse", "--absolute-git-dir"]));
        if (found.Outcome != ProcessOutcome.Exited || found.ExitCode != 0) return null;
        string path = found.StandardOutput.TrimEnd('\r', '\n');
        return path.Length > 0 && !path.Contains('\n', StringComparison.Ordinal) && Path.IsPathFullyQualified(path) && Directory.Exists(path) ? path : null;
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
    // history than the push sends; then every configuration variable, keeping only the settings forced here, and git's trace variables
    // (FIX-150); and every other inherited git variable, such as those choosing a diff's context, an external diff, notes or attributes,
    // and every GITLEAKS_ variable (FIX-160), so that the environment the scan was started in changes nothing it reads. No process of the
    // scan fetches a missing object either, whatever remote the clone's configuration names for one, as none of the push adapter's does:
    // a read that meets one fails, and the scan is incomplete (FIX-160). The settings forced here are the hardening of every git process
    // on a repository Vericairn did not create (GitHardening: no system or global configuration, an empty hooks directory, and the clone's
    // own program settings overridden; FIX-147), followed by the read settings above, all carried through git's configuration environment,
    // which every git process the scan starts reads, gitleaks' own included. The hardening's directory must outlive the process.
    internal static void Isolate(ProcessStartInfo info, GitHardening hardening)
    {
        ArgumentNullException.ThrowIfNull(hardening);
        GitRouting.Remove(info);
        foreach (string name in info.Environment.Keys.Where(n => n.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("GITLEAKS_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(name);
        hardening.ApplyEnvironment(info, ReadSettings.Select(s => s.Key + "=" + s.Value));
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_NO_LAZY_FETCH"] = "1";
        info.Environment["GIT_GRAFT_FILE"] = NoGrafts;
    }

    /// <summary>
    /// The files the range's commits add or change at the path the inventory allowance names, a file named by its SHA-256 in the objects
    /// folder at the repository's root, by commit: the same commits and parent diffs gitleaks reads. Null when git cannot list them. Only
    /// file names are read back.
    /// </summary>
    private SortedDictionary<string, SortedSet<string>>? ObjectFiles(ScanFolder folder, string range)
    {
        // The same commits and parent diffs gitleaks reads: --full-history, -m and --no-renames, every change but a deletion.
        ProcessResult changed = InventoryGit(folder, "log", "--no-show-signature", "--format=commit %H", "--name-only", "--root", "--no-renames", "--full-history", "-m",
            "--diff-filter=d", "--no-relative", "--no-ext-diff", "--no-textconv", range, "--", ":(top)objects");
        if (changed.Outcome != ProcessOutcome.Exited || changed.ExitCode != 0) return null;
        var files = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        string? commit = null;
        foreach (string entry in changed.StandardOutput.Split('\n'))
        {
            string line = entry.TrimEnd('\r');
            if (line.StartsWith("commit ", StringComparison.Ordinal))
            {
                commit = line["commit ".Length..];
                if (!Patterns.Commit.IsMatch(commit)) return null;
            }
            else if (InventoryPathPattern().IsMatch(line))
            {
                if (commit is null) return null;
                if (!files.TryGetValue(commit, out SortedSet<string>? paths)) files[commit] = paths = new(StringComparer.Ordinal);
                paths.Add(line);
            }
        }

        return files;
    }

    /// <summary>
    /// Whether the inventory allowance may apply to this range (FIX-150): true when every file the range's commits add or change at a path
    /// the allowance names, and that holds a line of an inventory's shape, holds only such lines, as a history checkpoint's inventory
    /// does. The scanner cannot tell from the commits which objects a checkpoint names, so a file that mixes inventory lines with any
    /// other line, or a range whose files cannot be read, is scanned without the allowance. Only file names are read back, never contents.
    /// </summary>
    private bool OnlyInventoriesHoldInventoryLines(ScanFolder folder, SortedDictionary<string, SortedSet<string>> files)
    {
        foreach ((string at, SortedSet<string> paths) in files)
        {
            foreach (string[] chunk in paths.Chunk(PathsPerGrep))
            {
                // A file can hold an inventory's lines and another line only if it is listed by both searches.
                if (FilesWithLines(folder, at, chunk, "^" + InventoryLine + "$", matching: true) is not { } shaped
                    || FilesWithLines(folder, at, chunk, "^" + InventoryLine + "$", matching: false) is not { } other) return false;
                if (shaped.Overlaps(other)) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The git revisions the release bases the range carries record, and the objects that hold those bases' bytes (FIX-168): a basis held
    /// as one object, named by its own SHA-256 in the objects folder at the repository's root, or every part of a split basis whose root
    /// the range adds or changes there (FIX-163), joined and checked as the product's reader checks it. A basis is a release basis
    /// serialization the product writes, of one of its formats; its revisions are the commit ids, 40 lower-case hexadecimal digits, of
    /// its repositories and their tree comparisons and of its submission, in its own basis, in the basis its policy view holds and in its
    /// repositories. Null when no basis of the range records one, or when the files cannot be listed. A file that is no such basis, or
    /// that cannot be read whole and checked against its name, holds no evidence, and is scanned as before. The bases' bytes are read
    /// back and parsed in memory to find the revisions, and nothing else of them is kept.
    /// </summary>
    private (SortedSet<string> Paths, SortedSet<string> Revisions)? ReleaseBasisEvidence(ScanFolder folder, SortedDictionary<string, SortedSet<string>> files)
    {
        var roots = new List<(string Commit, string Path)>();
        var wholes = new List<(string Commit, string Path)>();
        foreach ((string at, SortedSet<string> paths) in files)
        {
            foreach (string[] chunk in paths.Chunk(PathsPerGrep))
            {
                if (FilesWithLines(folder, at, chunk, SplitRootStart, matching: true) is not { } split
                    || FilesWithLines(folder, at, chunk, ReleaseBasisStart, matching: true) is not { } whole) return null;
                roots.AddRange(split.Order(StringComparer.Ordinal).Select(path => (at, path)));
                wholes.AddRange(whole.Order(StringComparer.Ordinal).Select(path => (at, path)));
            }
        }

        var held = new SortedSet<string>(StringComparer.Ordinal);
        var revisions = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((string at, string path) in roots)
        {
            // A root is bounded before it is read, and its parts are measured before any is read and joined, each checked against its
            // hash and the whole against the root's digest, as the product's basis reader does.
            var source = new CommitObjects(this, folder, at);
            if (source.Object(path, SplitObject.MaxRootBytes(ReleaseBasisRetention.MaximumBasisBytes)) is not { } root || !SplitObject.IsRoot(root)) continue;
            if (SplitObject.Join(root, source, ReleaseBasisRetention.MaximumBasisBytes).Content is not { } content || Revisions(content) is not { } found) continue;
            held.UnionWith(source.Parts);
            revisions.UnionWith(found);
        }

        // The first part of a split basis begins as a whole basis does; it is evidence through its root alone.
        foreach ((string at, string path) in wholes.Where(w => !held.Contains(w.Path)))
        {
            if (new CommitObjects(this, folder, at).Object(path, EstateLayout.MaxObjectBytes) is not { } content || SplitObject.IsRoot(content)
                || Revisions(content) is not { } found) continue;
            held.Add(path);
            revisions.UnionWith(found);
        }

        return revisions.Count > 0 ? (held, revisions) : null;
    }

    /// <summary>
    /// The commit ids a release basis records (FIX-168), or null when the content is no release basis: one JSON object of a release basis
    /// format with a basis. Only values of 40 lower-case hexadecimal digits are taken, the commit ids gitleaks' rule can read; a revision
    /// of a working tree or of 64 digits is not one.
    /// </summary>
    internal static SortedSet<string>? Revisions(byte[] content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String
                || !ReleaseBasisFormats.Contains(format.GetString(), StringComparer.Ordinal)
                || !root.TryGetProperty("basis", out JsonElement basis) || basis.ValueKind != JsonValueKind.Object) return null;
            var revisions = new SortedSet<string>(StringComparer.Ordinal);
            void Take(JsonElement holder, string name)
            {
                if (holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    && CommitId().IsMatch(value.GetString()!)) revisions.Add(value.GetString()!);
            }

            void Repositories(JsonElement holder)
            {
                if (holder.ValueKind != JsonValueKind.Object || !holder.TryGetProperty("repositories", out JsonElement list) || list.ValueKind != JsonValueKind.Array) return;
                foreach (JsonElement repository in list.EnumerateArray())
                {
                    Take(repository, "revision");
                    if (repository.ValueKind != JsonValueKind.Object || !repository.TryGetProperty("treeComparisons", out JsonElement comparisons)
                        || comparisons.ValueKind != JsonValueKind.Array) continue;
                    foreach (JsonElement comparison in comparisons.EnumerateArray())
                    {
                        Take(comparison, "sourceCommit");
                        Take(comparison, "selectedCommit");
                    }
                }
            }

            JsonElement view = root.TryGetProperty("policies", out JsonElement policies) && policies.ValueKind == JsonValueKind.Object
                && policies.TryGetProperty("basis", out JsonElement viewed) ? viewed : default;
            foreach (JsonElement held in new[] { basis, view })
            {
                if (held.ValueKind != JsonValueKind.Object) continue;
                if (held.TryGetProperty("submission", out JsonElement submission)) Take(submission, "commit");
                Repositories(held);
            }

            Repositories(root);
            return revisions;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The allowance for the release bases a range carries (FIX-168), appended to the configuration a scan applies: for the rule that reads
    /// revisions as tokens alone, a value that is exactly one of the revisions those bases record, and only in the objects that hold their
    /// bytes. Every other value, rule and path is scanned as before.
    /// </summary>
    internal static string ReleaseBasisAllowance(IEnumerable<string> paths, IEnumerable<string> revisions) =>
        "[[allowlists]]\n"
        + "description = \"Release basis evidence: the git revisions a release basis records, in the objects that hold the basis\"\n"
        + "targetRules = [\"" + ReleaseBasisRule + "\"]\n"
        + "condition = \"AND\"\n"
        + "paths = [\n    '''^objects/(?:" + string.Join('|', paths.Select(p => p["objects/".Length..])) + ")$''',\n]\n"
        + "regexes = [\n    '''^(?:" + string.Join('|', revisions) + ")$''',\n]\n";

    // The objects of one commit's tree, by content hash, for reading a release basis and the parts of a split one; every part read is
    // remembered. An object is read whole, after it is measured, and only when it is no longer than the bound the caller gives.
    private sealed class CommitObjects(GitleaksScanner scanner, ScanFolder folder, string commit) : ISplitObjectSource
    {
        internal SortedSet<string> Parts { get; } = new(StringComparer.Ordinal);

        public long? Length(string hash, CancellationToken cancellationToken) => ContentHash.IsValid(hash) ? scanner.ObjectLength(folder, commit, ObjectPath(hash)) : null;

        public bool Read(string hash, Span<byte> destination, CancellationToken cancellationToken)
        {
            if (!ContentHash.IsValid(hash) || scanner.ObjectBytes(folder, commit, ObjectPath(hash)) is not { } bytes || bytes.Length != destination.Length) return false;
            bytes.CopyTo(destination);
            Parts.Add(ObjectPath(hash));
            return true;
        }

        // The object at a path the inventory allowance names, when it is at most the bound and its bytes are those its name hashes.
        internal byte[]? Object(string path, long bound)
        {
            if (scanner.ObjectLength(folder, commit, path) is not { } length || length > bound || scanner.ObjectBytes(folder, commit, path) is not { } bytes
                || bytes.LongLength != length || ContentHash.Sha256(bytes) != ContentHash.Prefix + path["objects/".Length..]) return null;
            return bytes;
        }

        private static string ObjectPath(string hash) => "objects/" + hash[ContentHash.Prefix.Length..];
    }

    // The size of a blob at a path of a commit's tree; null when git cannot name one.
    private long? ObjectLength(ScanFolder folder, string commit, string path)
    {
        ProcessResult result = InventoryGit(folder, "cat-file", "-s", commit + ":" + path);
        return result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0
            && long.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long length) ? length : null;
    }

    // A blob's bytes, read as UTF-8 text, which a release basis is, and encoded again; a blob that is not UTF-8 comes back as other bytes,
    // which its hash check refuses. Null when git cannot read it.
    private byte[]? ObjectBytes(ScanFolder folder, string commit, string path)
    {
        ProcessStartInfo info = Start("git", folder, ["--no-replace-objects", "cat-file", "blob", commit + ":" + path]);
        info.Environment["LC_ALL"] = "C";
        info.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        ProcessResult result = run(info);
        return result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0 ? Encoding.UTF8.GetBytes(result.StandardOutput) : null;
    }

    // The files of the commit's tree among the paths that hold a line of the pattern, or a line of any other; null when unread.
    private HashSet<string>? FilesWithLines(ScanFolder folder, string commit, string[] paths, string pattern, bool matching)
    {
        var arguments = new List<string> { "grep", "--no-textconv", "--full-name", "--text", "--files-with-matches", "--extended-regexp" };
        if (!matching) arguments.Add("--invert-match");
        arguments.AddRange(["-e", pattern, commit, "--"]);
        arguments.AddRange(paths.Select(p => ":(top)" + p));
        ProcessResult result = InventoryGit(folder, [.. arguments]);
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

    private ProcessResult InventoryGit(ScanFolder folder, params string[] arguments)
    {
        ProcessStartInfo info = Start("git", folder, ["--no-replace-objects", .. arguments]);
        // Character ranges in the line's shape read as bytes; pathspecs keep their magic, since no inherited git variable reaches git.
        info.Environment["LC_ALL"] = "C";
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
    private bool Reads(ScanFolder folder, params string[] arguments)
    {
        ProcessResult result = run(Start("git", folder, ["--no-replace-objects", .. arguments]));
        return result.Outcome == ProcessOutcome.Exited && result.ExitCode == 0;
    }

    private int? Count(ScanFolder folder, string range)
    {
        // Gitleaks can report zero findings when git cannot read a range. Establish readability independently,
        // before and after scanning, without asking git for patches or retaining any source text.
        ProcessResult result = run(Start("git", folder, ["--no-replace-objects", "rev-list", "--count", range]));
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

    [GeneratedRegex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CommitId();

    [GeneratedRegex(@"^v?(?<major>[0-9]{1,3})\.(?<minor>[0-9]{1,3})\.(?<patch>[0-9]{1,3})(?:-[a-zA-Z0-9.]{1,8})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
