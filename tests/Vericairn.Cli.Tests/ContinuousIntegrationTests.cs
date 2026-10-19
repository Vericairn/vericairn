// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli.Tests;

/// <summary>
/// Repository-map rule 6 asks for a secret scan before every push to a public repository, and continuous integration ran none.
/// A branch pushed to a public repository is visible at once, so secret-scan.yml scans every branch push, pull request and
/// manual run, apart from ci.yml's build: gitleaks' own release, pinned by version and by the SHA-256 its checksums file gives,
/// over the commits the event brings, failing on any finding with its secret redacted, and nothing the scanned commits carry
/// can silence one.
/// </summary>
public class ContinuousIntegrationTests
{
    // gitleaks_8.30.1_linux_x64.tar.gz in gitleaks_8.30.1_checksums.txt of the v8.30.1 release on GitHub.
    private const string GitleaksVersion = "8.30.1";
    private const string GitleaksSha256 = "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb";

    [Fact]
    public void PullRequestsCheckAllowedActionsAndTheirFullCommitPins()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        Assert.True(File.Exists(Path.Combine(workflows, "actions.allow")));
        string ci = Read(Path.Combine(workflows, "ci.yml"));
        string policy = Block(ci, "workflow-policy:", 2);
        Assert.Contains("ruby .github/scripts/check-workflow-actions.rb", policy, StringComparison.Ordinal);
        Assert.Contains("ruby .github/scripts/test-workflow-actions.rb", policy, StringComparison.Ordinal);
        Assert.Contains("python3 -m unittest discover", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void PullRequestsVerifyBuiltNugetPackagesWithoutPublication()
    {
        string workflow = Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "nuget.yml"));
        Assert.Contains("\n  pull_request:\n", Block(workflow, "on:", 0), StringComparison.Ordinal);
        Assert.DoesNotContain("id-token", Block(workflow, "permissions:", 0), StringComparison.Ordinal);
        string verify = Block(workflow, "verify:", 2);
        Assert.Contains("if: github.event_name == 'pull_request'", verify, StringComparison.Ordinal);
        Assert.Contains("dotnet restore --locked-mode", verify, StringComparison.Ordinal);
        Assert.Contains("dotnet pack --no-restore --configuration Release --output packages -warnaserror", verify, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", verify, StringComparison.Ordinal);
        Assert.Contains("hashlib.file_digest(content, \"sha256\")", verify, StringComparison.Ordinal);
        const string verifier = "python3 .github/scripts/verify-packages.py packages";
        Assert.Contains(verifier, verify, StringComparison.Ordinal);
        Assert.DoesNotContain("NuGet/login", verify, StringComparison.Ordinal);
        Assert.DoesNotContain("nuget push", verify, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token", verify, StringComparison.Ordinal);
        string publish = Block(workflow, "publish:", 2);
        Assert.Contains("if: github.event_name == 'release' || github.event_name == 'workflow_dispatch'", publish, StringComparison.Ordinal);
        Assert.Contains("id-token: write", publish, StringComparison.Ordinal);
        Assert.Contains(verifier, publish, StringComparison.Ordinal);
        Assert.True(publish.IndexOf(verifier, StringComparison.Ordinal) < publish.IndexOf("NuGet/login@", StringComparison.Ordinal));
        Assert.DoesNotContain("dotnet pack", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBranchPushAndPullRequestIsScannedForSecretsByAPinnedAndVerifiedGitleaks()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        string workflow = Read(Path.Combine(workflows, "secret-scan.yml"));

        // Every branch push, pull request and manual run, in a workflow of its own; the build stays in ci.yml, without the scan.
        string triggers = Block(workflow, "on:", 0);
        Assert.Contains("\n  push:\n    branches: ['**']\n", triggers, StringComparison.Ordinal);
        Assert.Contains("\n  pull_request:\n", triggers, StringComparison.Ordinal);
        Assert.Contains("\n  workflow_dispatch:\n", triggers, StringComparison.Ordinal);
        Assert.Contains("\npermissions:\n  contents: read\n", workflow, StringComparison.Ordinal);
        string ci = Read(Path.Combine(workflows, "ci.yml"));
        Assert.DoesNotContain("gitleaks", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  secret-scan:", ci, StringComparison.Ordinal);
        Assert.Contains("\n  build-test:\n", ci, StringComparison.Ordinal);

        // One job on Linux, which nothing gates but a push that deletes a branch.
        Assert.Equal(["  secret-scan:"], Block(workflow, "jobs:", 0).Split('\n').Where(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Length > 2 && l[2] != ' '));
        string scan = Block(workflow, "secret-scan:", 2);
        Assert.Contains("\n    if: ${{ !github.event.deleted }}\n", scan, StringComparison.Ordinal);
        Assert.Contains("\n    runs-on: ubuntu-latest\n", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy:", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("needs:", scan, StringComparison.Ordinal);
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

        // The commits each event brings, read with the whole history at hand and no token left in the clone: a pull request's;
        // a push's, or a new branch's own, or the whole history when the default branch's previous tip is gone; everything for a
        // manual run. An unreadable range fails the job, since gitleaks itself reports no leaks when git cannot read its range,
        // and so does an empty one, except for a push.
        Assert.Contains("\n          fetch-depth: 0\n", scan, StringComparison.Ordinal);
        Assert.Contains("\n          persist-credentials: false\n", scan, StringComparison.Ordinal);
        Assert.Contains("PR_BASE: ${{ github.event.pull_request.base.sha }}", scan, StringComparison.Ordinal);
        Assert.Contains("PR_HEAD: ${{ github.event.pull_request.head.sha }}", scan, StringComparison.Ordinal);
        Assert.Contains("PUSH_BEFORE: ${{ github.event.before }}", scan, StringComparison.Ordinal);
        Assert.Contains("PUSH_AFTER: ${{ github.event.after }}", scan, StringComparison.Ordinal);
        Assert.Contains("DEFAULT_BRANCH: ${{ github.event.repository.default_branch }}", scan, StringComparison.Ordinal);
        Assert.Contains("pull_request) range=\"$PR_BASE..$PR_HEAD\" ;;", scan, StringComparison.Ordinal);
        Assert.Contains("if git cat-file -e \"$PUSH_BEFORE^{commit}\" 2> /dev/null; then range=\"$PUSH_BEFORE..$PUSH_AFTER\"\n", scan, StringComparison.Ordinal);
        Assert.Contains("elif [ \"$GITHUB_REF\" = \"refs/heads/$DEFAULT_BRANCH\" ]; then range=--all\n", scan, StringComparison.Ordinal);
        Assert.Contains("else range=\"origin/$DEFAULT_BRANCH..$PUSH_AFTER\"\n", scan, StringComparison.Ordinal);
        Assert.Contains("*) range=--all ;;", scan, StringComparison.Ordinal);
        int counted = scan.IndexOf("count=$(git rev-list --count \"$range\")", StringComparison.Ordinal);
        int empty = scan.IndexOf("if [ \"$count\" -eq 0 ]; then", StringComparison.Ordinal);
        int pushed = scan.IndexOf("if [ \"$EVENT\" = push ]; then", StringComparison.Ordinal);
        int refused = scan.IndexOf("exit 1\n", StringComparison.Ordinal);
        Assert.True(counted > 0 && empty > counted && pushed > empty && refused > pushed && detect > refused, "The range is read, and an empty one passed only for a push, before gitleaks runs.");

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

    [Fact]
    public void TheReleasePackageJobChecksOutTheHistoryItsDocumentationComparisonReads()
    {
        string package = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml")), "package:", 2);

        // A set's references are pinned to the commit that generated them, and the comparison proves that the release commit's
        // build sources agree with that commit; a shallow checkout lacks it, so 0.8.0's first release run stopped there.
        int checkout = package.IndexOf("- uses: actions/checkout@", StringComparison.Ordinal);
        int history = package.IndexOf("\n          fetch-depth: 0\n", StringComparison.Ordinal);
        int compare = package.IndexOf("--project tools/Vericairn.DocsTool -- compare", StringComparison.Ordinal);
        Assert.True(checkout > 0 && history > checkout && compare > history, "The package job checks out the complete history before it compares the documentation set.");
    }

    [Fact]
    public void PullRequestsPublishTheSingleFileApplicationsTheReleaseShips()
    {
        string job = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")), "build-test:", 2);
        int step = job.IndexOf("- name: Publish the single-file applications", StringComparison.Ordinal);
        Assert.True(step > 0, "The build-test job publishes the single-file applications, so the single-file analyzer runs on every change.");
        string publish = job[step..];
        Assert.Contains("-p:PublishSingleFile=true", publish, StringComparison.Ordinal);
        Assert.Contains("src/Vericairn.Cli/Vericairn.Cli.csproj", publish, StringComparison.Ordinal);
        Assert.Contains("src/Vericairn.Workbench/Vericairn.Workbench.csproj", publish, StringComparison.Ordinal);
        Assert.Contains("git checkout -- $(git ls-files '*packages.lock.json')", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTestStepRetriesAFailedTestOnceAndReportsIt()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), ".github", "scripts", "run-tests.py")));
        foreach ((string file, string job) in new[] { ("ci.yml", "build-test:"), ("release.yml", "verify:") })
        {
            string steps = Block(Read(Path.Combine(workflows, file)), job, 2);
            Assert.Contains("        run: python .github/scripts/run-tests.py\n", steps, StringComparison.Ordinal);
            Assert.Contains("            python3 .github/scripts/run-tests.py'\n", steps, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet test --no-build", steps, StringComparison.Ordinal);
        }
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>A key's block in a workflow: its own line and every following line indented deeper, blank lines included.</summary>
    private static string Block(string yaml, string key, int indent)
    {
        string own = new string(' ', indent) + key;
        string[] lines = yaml.Split('\n');
        int start = Array.FindIndex(lines, l => l == own || l.StartsWith(own + " ", StringComparison.Ordinal));
        Assert.True(start >= 0, $"The workflow has no '{key}' indented by {indent}.");
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
