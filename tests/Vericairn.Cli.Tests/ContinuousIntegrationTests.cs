// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli.Tests;

/// <summary>
/// Repository-map rule 6 asks for a secret scan before every push to a public repository, and continuous integration ran none.
/// The secret-scan job of ci.yml runs on every push, pull request and manual run, beside build-test: gitleaks' own release,
/// pinned by version and by the SHA-256 its checksums file gives, over the commits the event brings, failing on any finding
/// with its secret redacted, and nothing the scanned commits carry can silence one.
/// </summary>
public class ContinuousIntegrationTests
{
    // gitleaks_8.30.1_linux_x64.tar.gz in gitleaks_8.30.1_checksums.txt of the v8.30.1 release on GitHub.
    private const string GitleaksVersion = "8.30.1";
    private const string GitleaksSha256 = "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb";

    [Fact]
    public void EveryPushAndPullRequestIsScannedForSecretsByAPinnedAndVerifiedGitleaks()
    {
        string workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);

        // The triggers ci.yml already has, and a job of its own on Linux that neither waits for build-test nor holds it.
        string triggers = Block(workflow, "on:", 0);
        foreach (string trigger in new[] { "push:", "pull_request:", "workflow_dispatch:" })
        {
            Assert.Contains("\n  " + trigger, triggers, StringComparison.Ordinal);
        }

        string scan = Block(workflow, "secret-scan:", 2);
        Assert.Contains("\n    runs-on: ubuntu-latest\n", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy:", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("needs:", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("needs:", Block(workflow, "build-test:", 2), StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", scan, StringComparison.Ordinal);

        // gitleaks' own release archive, pinned by version and checked against the SHA-256 written here before it is unpacked
        // and run; the job uses no action beyond the pinned checkout (gitleaks-action needs a license key for an organization)
        // and no secret.
        Assert.Contains($"\n      GITLEAKS_VERSION: {GitleaksVersion}\n", scan, StringComparison.Ordinal);
        Assert.Contains($"\n      GITLEAKS_SHA256: {GitleaksSha256}\n", scan, StringComparison.Ordinal);
        Assert.Contains("archive=\"gitleaks_${GITLEAKS_VERSION}_linux_x64.tar.gz\"", scan, StringComparison.Ordinal);
        Assert.Contains("\"https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/$archive\"", scan, StringComparison.Ordinal);
        int verified = scan.IndexOf("echo \"$GITLEAKS_SHA256  $dir/$archive\" | sha256sum --check --strict\n", StringComparison.Ordinal);
        int unpacked = scan.IndexOf("tar -xzf \"$dir/$archive\"", StringComparison.Ordinal);
        int detect = scan.IndexOf("\"$RUNNER_TEMP/gitleaks/gitleaks\" detect ", StringComparison.Ordinal);
        Assert.True(verified > 0 && unpacked > verified && detect > unpacked, "The archive is verified before it is unpacked, and unpacked before it runs.");
        Assert.Equal(["actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09 # v5"], scan.Split('\n').Where(l => l.TrimStart().StartsWith("- uses: ", StringComparison.Ordinal) || l.TrimStart().StartsWith("uses: ", StringComparison.Ordinal)).Select(l => l[(l.IndexOf("uses: ", StringComparison.Ordinal) + 6)..]));
        Assert.DoesNotContain(workflow.Split('\n'), l => l.Contains("uses:", StringComparison.Ordinal) && l.Contains("gitleaks", StringComparison.Ordinal));
        Assert.DoesNotContain("secrets.", scan, StringComparison.Ordinal);

        // The commits each event brings, read with the whole history at hand and no token left in the clone; an unreadable or
        // empty range fails the job, since gitleaks itself reports no leaks when git cannot read its range.
        Assert.Contains("\n          fetch-depth: 0\n", scan, StringComparison.Ordinal);
        Assert.Contains("\n          persist-credentials: false\n", scan, StringComparison.Ordinal);
        Assert.Contains("PR_BASE: ${{ github.event.pull_request.base.sha }}", scan, StringComparison.Ordinal);
        Assert.Contains("PR_HEAD: ${{ github.event.pull_request.head.sha }}", scan, StringComparison.Ordinal);
        Assert.Contains("PUSH_BEFORE: ${{ github.event.before }}", scan, StringComparison.Ordinal);
        Assert.Contains("PUSH_AFTER: ${{ github.event.after }}", scan, StringComparison.Ordinal);
        Assert.Contains("pull_request) range=\"$PR_BASE..$PR_HEAD\" ;;", scan, StringComparison.Ordinal);
        Assert.Contains("push) if git cat-file -e \"$PUSH_BEFORE^{commit}\" 2> /dev/null; then range=\"$PUSH_BEFORE..$PUSH_AFTER\"; else range=--all; fi ;;", scan, StringComparison.Ordinal);
        Assert.Contains("*) range=--all ;;", scan, StringComparison.Ordinal);
        int counted = scan.IndexOf("count=$(git rev-list --count \"$range\")", StringComparison.Ordinal);
        int refused = scan.IndexOf("if [ \"$count\" -eq 0 ]; then", StringComparison.Ordinal);
        Assert.True(counted > 0 && refused > counted && detect > refused, "The range is read, and an empty one refused, before gitleaks runs.");

        // Every finding fails the job with its secret redacted, and nothing the scanned commits carry silences one: gitleaks
        // reads no .gitleaks.toml or .gitleaksignore from the git directory or the ignore path, and gitleaks:allow is ignored.
        string command = scan[detect..].Replace("\\\n", " ", StringComparison.Ordinal).Split('\n')[0];
        foreach (string option in new[] { "--source .git ", "--gitleaks-ignore-path \"$RUNNER_TEMP/gitleaks\" ", "--ignore-gitleaks-allow ", "--redact ", "--log-opts=\"$range\"" })
        {
            Assert.Contains(option, command, StringComparison.Ordinal);
        }

        foreach (string option in new[] { "--no-git", "--exit-code", "--baseline-path", "--config", "--enable-rule", "--max-target-megabytes", "--report" })
        {
            Assert.DoesNotContain(option, command, StringComparison.Ordinal);
        }
    }

    /// <summary>A key's block in the workflow: its own line and every following line indented deeper, blank lines included.</summary>
    private static string Block(string yaml, string key, int indent)
    {
        string own = new string(' ', indent) + key;
        string[] lines = yaml.Split('\n');
        int start = Array.FindIndex(lines, l => l == own || l.StartsWith(own + " ", StringComparison.Ordinal));
        Assert.True(start >= 0, $"ci.yml has no '{key}' indented by {indent}.");
        int end = start + 1;
        while (end < lines.Length && (lines[end].Length == 0 || lines[end].TakeWhile(c => c == ' ').Count() > indent))
        {
            end++;
        }

        return string.Join('\n', lines[start..end]) + "\n";
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        return root;
    }
}
