// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-160: a checked push, through the runtime with the git push adapter and the installed gitleaks, scans its commits with the
/// product's rules and nothing else: whatever the clone holds beside them, a private key the push carries is found and the push refused,
/// while a push of plain content still passes. Each input this test places is first shown to be one gitleaks honors when it reads it,
/// and each push's scan is shown to have given gitleaks an empty folder of its own, outside the clone, as its source, with no GITLEAKS_
/// variable. Skipped without an installed gitleaks.
/// </summary>
public sealed class CheckedPushScanTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vericairn-checked-scan-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(root, "work");

    private string Remote => Path.Combine(root, "remote.git");

    [Theory]
    [InlineData("an ignore list at the root")]
    [InlineData("a committed ignore list")]
    [InlineData("an ignore list in the git directory")]
    [InlineData("a configuration at the root")]
    public void ACheckedPushCarryingAPrivateKeyIsRefusedWhateverTheCloneHolds(string placed)
    {
        (string basis, string head, string fingerprint) = Start(key: true);
        switch (placed)
        {
            case "an ignore list at the root":
                File.WriteAllText(Path.Combine(Work, ".gitleaksignore"), fingerprint + "\n");
                Assert.Equal(0, Read(basis, head, ".", rules: true));
                break;
            case "a committed ignore list":
                head = Commit(".gitleaksignore", fingerprint + "\n");
                Assert.Equal(0, Read(basis, head, ".", rules: true));
                break;
            case "an ignore list in the git directory":
                File.WriteAllText(Path.Combine(Work, ".git", ".gitleaksignore"), fingerprint + "\n");
                Assert.Equal(0, Read(basis, head, ".git", rules: true));
                break;
            default:
                File.WriteAllText(Path.Combine(Work, ".gitleaks.toml"), AllowEverything);
                Assert.Equal(0, Read(basis, head, ".", rules: false));
                break;
        }

        Refused(head);
    }

    [Fact]
    public void ACheckedPushOfPlainContentStillPasses()
    {
        (_, string head, _) = Start(key: false);
        (ChangeResponse response, JsonElement scan) = Push(head);
        Assert.True(response.Ok, response.Message);
        Assert.Equal("passed", scan.GetProperty("verdict").GetString());
        Assert.Equal(0, scan.GetProperty("findingCount").GetInt32());
        Assert.Equal(head, Git(Remote, "rev-parse", "refs/heads/work/change"));
    }

    /// <summary>A configuration that allows every path and has no rule.</summary>
    internal const string AllowEverything = "title = \"Allow every path\"\n\n[[allowlists]]\npaths = ['''.*''']\n";

    /// <summary>
    /// A clone whose branch work/change adds a private key, or a plain note, on top of main, which a bare remote holds: the push's base,
    /// its head and the key's fingerprint as gitleaks reports it. Skipped without an installed gitleaks.
    /// </summary>
    internal (string Basis, string Head, string Fingerprint) Start(bool key)
    {
        Assert.SkipWhen(Gitleaks is null, "Set VERICAIRN_TEST_GITLEAKS to an installed gitleaks of at least 8.25.0 to scan real commits.");
        TestPrerequisites.RequireGit();
        Directory.CreateDirectory(Work);
        File.WriteAllText(Path.Combine(root, "gitconfig"), string.Empty);
        Git(Work, "init", "--quiet", "--initial-branch=main");
        string basis = Commit("readme.txt", "Initial content.\n");
        Git(root, "clone", "--bare", "--quiet", "--", Work, Remote);
        Git(Work, "checkout", "--quiet", "-b", "work/change");
        string head = key ? Commit("deploy/key.pem", PrivateKey()) : Commit("notes.txt", "A plain note.\n");
        return (basis, head, head + ":deploy/key.pem:private-key:1");
    }

    /// <summary>The checked push refused the head for the key it found, and the remote never received the branch.</summary>
    internal void Refused(string head)
    {
        (ChangeResponse response, JsonElement scan) = Push(head);
        Assert.False(response.Ok);
        Assert.Contains("did not pass", response.Message, StringComparison.Ordinal);
        Assert.Equal("findings", scan.GetProperty("verdict").GetString());
        Assert.True(scan.GetProperty("findingCount").GetInt32() >= 1);
        Assert.Empty(Git(Remote, "for-each-ref", "refs/heads/work"));
    }

    // The runtime's checked push of the head to the bare remote, with the git push adapter and the gitleaks scanner, and the scan it recorded.
    private (ChangeResponse Response, JsonElement Scan) Push(string head)
    {
        using var f = new ChangesFlowTests.Fixture(clone: Work);
        f.Provider.Remote = Remote;
        f.Service.Providers = new() { PushAdapter = Adapter(), SecretScanner = Scanner() };
        f.Service.Providers.Register(f.Provider);
        scans.Clear();
        ChangeResponse response = f.Service.ChangesPush(new(f.Product, "work/change", head, f.Estate.OperatorId));
        (bool emptySource, string[] variables) = Assert.Single(scans);
        Assert.True(emptySource, "gitleaks' source was not an empty folder outside the clone.");
        Assert.Empty(variables);
        return (response, f.Current("change-push").Body.GetProperty("scan").Clone());
    }

    // The adapter isolates its own git processes; only its read of the user's credential settings sees this test's stand-in for the
    // user's global configuration.
    private GitPushAdapter Adapter() => new(info =>
    {
        if (info.ArgumentList.Contains("config"))
        {
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "gitconfig");
        }
        return BoundedProcess.Run(info, TestTiming.HangTimeout);
    });

    // What each gitleaks read of the history was given as it started: whether its source was an empty folder outside this test's own,
    // which holds the clone, and the GITLEAKS_ variables it carried.
    private readonly List<(bool EmptySource, string[] Variables)> scans = [];

    private GitleaksScanner Scanner() => new(info =>
    {
        if (info.FileName == "gitleaks" && info.ArgumentList[0] == "git")
        {
            string source = Path.GetFullPath(Path.Combine(info.WorkingDirectory, info.ArgumentList[^1]));
            scans.Add((!source.StartsWith(root, StringComparison.Ordinal) && Directory.Exists(source) && !Directory.EnumerateFileSystemEntries(source).Any(),
                [.. info.Environment.Keys.Where(n => n.StartsWith("GITLEAKS_", StringComparison.OrdinalIgnoreCase))]));
        }
        if (info.FileName == "gitleaks") info.FileName = Gitleaks!;
        return BoundedProcess.Run(info, TestTiming.HangTimeout);
    });

    /// <summary>
    /// The findings the installed gitleaks reports for the range when it reads the clone itself: from the source named, relative to the
    /// working tree, with the product's rules and an empty ignore file or with no configuration option, and with the variables given.
    /// </summary>
    internal int Read(string basis, string head, string source, bool rules, IReadOnlyDictionary<string, string>? variables = null)
    {
        string folder = Path.Combine(root, "read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string template = Path.Combine(folder, "count.tmpl");
        string report = Path.Combine(folder, "count.txt");
        File.WriteAllText(template, "{{ len . }}");
        var info = new ProcessStartInfo(Gitleaks!) { WorkingDirectory = Work, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string name in info.Environment.Keys.Where(n => n.StartsWith("GITLEAKS_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(name);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "gitconfig");
        foreach ((string name, string value) in variables ?? new Dictionary<string, string>()) info.Environment[name] = value;
        var arguments = new List<string> { "git", "--no-banner", "--log-level=error", "--redact=100", "--log-opts=" + basis + ".." + head };
        if (rules)
        {
            string configuration = Path.Combine(folder, "rules.toml");
            string ignored = Path.Combine(folder, "ignore.txt");
            File.WriteAllBytes(configuration, GitleaksScanner.Configuration);
            File.WriteAllText(ignored, string.Empty);
            arguments.AddRange(["--config=" + configuration, "--gitleaks-ignore-path=" + ignored]);
        }
        arguments.AddRange(["--report-format=template", "--report-template=" + template, "--report-path=" + report, source]);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        ProcessResult result = BoundedProcess.Run(info, TestTiming.HangTimeout);
        Assert.True(result.Outcome == ProcessOutcome.Exited && result.ExitCode is 0 or 1 && string.IsNullOrWhiteSpace(result.StandardError), "gitleaks could not read the range.");
        return int.Parse(File.ReadAllText(report).Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    // A private key, assembled at run time, so that this file holds no credential-shaped text of its own.
    private static string PrivateKey()
    {
        string body = Convert.ToBase64String(RandomNumberGenerator.GetBytes(600));
        var key = new StringBuilder("-----BEGIN " + "RSA PRIVATE" + " KEY-----\n");
        for (int at = 0; at < body.Length; at += 64) key.Append(body, at, Math.Min(64, body.Length - at)).Append('\n');
        return key.Append("-----END " + "RSA PRIVATE" + " KEY-----\n").ToString();
    }

    private string Commit(string path, string content)
    {
        string file = Path.Combine(Work, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        Git(Work, "add", "--", path);
        Git(Work, "commit", "--quiet", "-m", "Add content");
        return Git(Work, "rev-parse", "HEAD");
    }

    private string Git(string directory, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
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

    private static string? Gitleaks => Environment.GetEnvironmentVariable("VERICAIRN_TEST_GITLEAKS");

    public void Dispose()
    {
        if (Directory.Exists(root)) WorkspaceCleanup.Delete(root);
    }
}

/// <summary>
/// FIX-160: the variables gitleaks reads its configuration from, set in this test process's own environment, standing for the runtime's,
/// change nothing a checked push's scan reports. Each value is first shown to be one gitleaks honors when it reads it.
/// </summary>
[Collection("inherited git environment")]
public sealed class CheckedPushScanEnvironmentTests : IDisposable
{
    private readonly CheckedPushScanTests pushes = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "vericairn-checked-scan-variables-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("GITLEAKS_CONFIG")]
    [InlineData("GITLEAKS_CONFIG_TOML")]
    public void ACheckedPushCarryingAPrivateKeyIsRefusedWhateverGitleaksVariableTheRuntimeCarries(string variable)
    {
        (string basis, string head, _) = pushes.Start(key: true);
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "allow.toml");
        File.WriteAllText(file, CheckedPushScanTests.AllowEverything);
        string value = variable == "GITLEAKS_CONFIG" ? file : CheckedPushScanTests.AllowEverything;
        Assert.Equal(0, pushes.Read(basis, head, ".", rules: false, new Dictionary<string, string> { [variable] = value }));
        string? saved = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
        try { pushes.Refused(head); }
        finally { Environment.SetEnvironmentVariable(variable, saved); }
    }

    public void Dispose()
    {
        pushes.Dispose();
        if (Directory.Exists(root)) WorkspaceCleanup.Delete(root);
    }
}
