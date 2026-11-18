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
        number += "; rules " + ConfigurationHash;
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
            File.WriteAllBytes(configuration, Configuration);
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
    [GeneratedRegex(@"^v?(?<major>[0-9]{1,3})\.(?<minor>[0-9]{1,3})\.(?<patch>[0-9]{1,3})(?:-[a-zA-Z0-9.]{1,8})?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
