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
    private static readonly string[] ProjectDirectories = ["src", "tests", "tools"];

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
    public void ThePackagingPathVerifiesThePackagesAndNugetRunsOnlyForPublication()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        string workflow = Read(Path.Combine(workflows, "nuget.yml"));
        // A job skipped on every pull request is a check that never passes, which a checked merge refuses.
        string triggers = Block(workflow, "on:", 0);
        Assert.DoesNotContain("pull_request", triggers, StringComparison.Ordinal);
        Assert.Contains("\n  release:\n", triggers, StringComparison.Ordinal);
        Assert.Contains("\n  workflow_dispatch:\n", triggers, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token", Block(workflow, "permissions:", 0), StringComparison.Ordinal);
        Assert.DoesNotContain("\n  verify:\n", workflow, StringComparison.Ordinal);
        const string verifier = "python3 .github/scripts/verify-packages.py";
        string publish = Block(workflow, "publish:", 2);
        Assert.Contains("if: github.event_name == 'release' || github.event_name == 'workflow_dispatch'", publish, StringComparison.Ordinal);
        Assert.Contains("id-token: write", publish, StringComparison.Ordinal);
        Assert.Contains(verifier + " packages", publish, StringComparison.Ordinal);
        Assert.True(publish.IndexOf(verifier, StringComparison.Ordinal) < publish.IndexOf("NuGet/login@", StringComparison.Ordinal));
        Assert.DoesNotContain("dotnet pack", publish, StringComparison.Ordinal);

        string package = Block(Read(Path.Combine(workflows, "package.yml")), "package:", 2);
        Assert.Contains("dotnet restore --locked-mode", package, StringComparison.Ordinal);
        Assert.Contains("dotnet pack --no-restore --configuration Release --output dist", package, StringComparison.Ordinal);
        Assert.Contains(verifier + " dist", package, StringComparison.Ordinal);
        Assert.True(package.IndexOf("sha256sum * > SHA256SUMS", StringComparison.Ordinal) < package.IndexOf(verifier, StringComparison.Ordinal));
        foreach (string forbidden in new[] { "NuGet/login", "nuget push", "id-token", "gh release" })
            Assert.DoesNotContain(forbidden, package, StringComparison.Ordinal);
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
        // ci.yml installs the same pinned gitleaks for the secret scan's own tests, and scans nothing with it.
        string ci = Read(Path.Combine(workflows, "ci.yml"));
        Assert.DoesNotContain("gitleaks\" detect", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("gitleaks git", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  secret-scan:", ci, StringComparison.Ordinal);
        string install = Block(ci, "- name: Install gitleaks for the scanner's tests", 6);
        Assert.Contains($"GITLEAKS_VERSION: {GitleaksVersion}\n", install, StringComparison.Ordinal);
        Assert.Contains($"GITLEAKS_SHA256: {GitleaksSha256}\n", install, StringComparison.Ordinal);
        Assert.Contains("sha256sum --check --strict", install, StringComparison.Ordinal);
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

        // Only the exact product configuration may be supplied; another config flag still fails the bypass checks below.
        const string productConfig = "--config \"$GITHUB_WORKSPACE/src/Vericairn.Adapters.Git/gitleaks.toml\" ";
        Assert.Contains(productConfig, command, StringComparison.Ordinal);
        Assert.Equal(2, command.Split(productConfig, StringSplitOptions.None).Length);
        command = command.Remove(command.IndexOf(productConfig, StringComparison.Ordinal), productConfig.Length);
        foreach (string option in new[] { "--no-git", "--exit-code", "--baseline-path", "--config", "--enable-rule", "--max-target-megabytes", "--report" })
        {
            Assert.DoesNotContain(option, command, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThePackageJobChecksOutTheHistoryItsDocumentationComparisonReads()
    {
        string package = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml")), "package:", 2);

        // A set's references are pinned to the commit that generated them, and the comparison proves that the release commit's
        // build sources agree with that commit; a shallow checkout lacks it, so 0.8.0's first release run stopped there.
        int checkout = package.IndexOf("- uses: actions/checkout@", StringComparison.Ordinal);
        int history = package.IndexOf("\n          fetch-depth: 0\n", StringComparison.Ordinal);
        int compare = package.IndexOf("--project tools/Vericairn.DocsTool -- compare", StringComparison.Ordinal);
        Assert.True(checkout > 0 && history > checkout && compare > history, "The package job checks out the complete history before it compares the documentation set.");
        // A release checks its own set, which release preparation freezes before the release build, so frozen or not; a change
        // compares a provisional set generated from its build once its version's set is frozen or before it exists.
        string docs = Block(package, "- name: Check the documentation set", 6);
        Assert.Contains("docs check \"$docs\" --documents docs/releases/documents.json", docs, StringComparison.Ordinal);
        Assert.Contains("if [ \"$RELEASE\" = true ] && [ ! -d \"$docs\" ]; then", docs, StringComparison.Ordinal);
        Assert.Contains("elif [ -d \"$docs\" ] && { [ \"$RELEASE\" = true ] || [ \"$frozen\" = false ]; }; then", docs, StringComparison.Ordinal);
        Assert.Contains("-- generate --out \"$docs\"", docs, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBillOfMaterialsUsesAPinnedToolAndDescribesWhatTheDistributablesShip()
    {
        string package = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml")), "package:", 2);

        // The components report reads CycloneDX specifications 1.2 to 1.6. The tool, installed unpinned, moved to 1.7 by default,
        // so 0.8.0's third release run stopped retaining its readable build reports.
        int step = package.IndexOf("- name: Software bill of materials", StringComparison.Ordinal);
        Assert.True(step > 0, "The package job writes the software bill of materials.");
        string sbom = package[step..package.IndexOf("- name: Documentation set", step, StringComparison.Ordinal)];
        Assert.Matches(@"dotnet tool install --global CycloneDX --version \d+\.\d+\.\d+\n", sbom);
        Assert.Contains(" --spec-version 1.6 --disable-package-restore\n", sbom, StringComparison.Ordinal);
        // Until 0.8.1 the bill described the solution's locked portable graph while the bundles were resolved live and discarded.
        Assert.True(package.IndexOf("- name: Restore the distributables (locked)", StringComparison.Ordinal) < step);
        Assert.Contains("python3 .github/scripts/verify-sbom.py sbom/bom.json src/Vericairn.Cli/packages.distributable.lock.json src/Vericairn.Workbench/packages.distributable.lock.json", sbom, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryChangeRunsTheReleasesPackagingPathFromItsLockedPlatformGraphs()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        // One definition: a release-only step cannot first fail during a release, as three of 0.8.0's release runs did.
        string ci = Block(Read(Path.Combine(workflows, "ci.yml")), "package:", 2);
        Assert.Contains("    uses: ./.github/workflows/package.yml\n", ci, StringComparison.Ordinal);
        Assert.Contains("      release: false\n", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("needs:", ci, StringComparison.Ordinal);
        string release = Block(Read(Path.Combine(workflows, "release.yml")), "package:", 2);
        Assert.Contains("    uses: ./.github/workflows/package.yml\n", release, StringComparison.Ordinal);
        Assert.Contains("      release: true\n", release, StringComparison.Ordinal);
        Assert.DoesNotContain("Publish the single-file applications", Read(Path.Combine(workflows, "ci.yml")), StringComparison.Ordinal);

        string package = Block(Read(Path.Combine(workflows, "package.yml")), "package:", 2);
        string restore = Block(package, "- name: Restore the distributables (locked)", 6);
        Assert.Contains("--locked-mode -p:VericairnDistributable=true -p:SelfContained=true -p:PublishSingleFile=true", restore, StringComparison.Ordinal);
        foreach (string step in new[] { "- name: Publish self-contained distributables", "- name: Publish Workbench distributables" })
        {
            string publish = Block(package, step, 6);
            Assert.Contains("--no-restore", publish, StringComparison.Ordinal);
            Assert.Contains("-p:VericairnDistributable=true -p:PublishSingleFile=true", publish, StringComparison.Ordinal);
            Assert.DoesNotContain("RestoreLockedMode=false", publish, StringComparison.Ordinal);
            Assert.DoesNotContain("git checkout --", publish, StringComparison.Ordinal);
        }
        Assert.Contains("git diff --exit-code -- '*.lock.json'", package, StringComparison.Ordinal);
        foreach (string project in new[] { "Vericairn.Cli", "Vericairn.Workbench" })
        {
            using System.Text.Json.JsonDocument locks = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", project, "packages.distributable.lock.json")));
            foreach (string rid in new[] { "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64" })
                Assert.True(locks.RootElement.GetProperty("dependencies").TryGetProperty("net10.0/" + rid, out _), $"{project} locks its {rid} graph.");
        }
    }

    [Fact]
    public void ThePackageJobRunsItsPublishedSingleFileCommandLineOnACommandThatStartsAProcess()
    {
        string package = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml")), "package:", 2);

        // The tests run framework-dependent code, which resolves native libraries that a self-contained single-file build does not:
        // 0.8.1's candidate passed them all and its macOS and Linux command lines could start no process. Only the published build shows it.
        int publish = package.IndexOf("- name: Publish self-contained distributables", StringComparison.Ordinal);
        int run = package.IndexOf("- name: Run the published single-file command line", StringComparison.Ordinal);
        Assert.True(publish > 0 && run > publish, "The package job runs the command line it has just published.");
        Assert.Contains("\n    runs-on: ubuntu-latest\n", package, StringComparison.Ordinal);
        string step = Block(package, "- name: Run the published single-file command line", 6);
        Assert.Contains("set -eu\n", step, StringComparison.Ordinal);
        Assert.Contains("out/linux-x64/bin/vericairn sources inspect \"$GITHUB_WORKSPACE\"\n", step, StringComparison.Ordinal);
    }

    [Fact]
    public void MacAndWindowsRunThePackageJobsOwnArchivesAndPropagateProcessFailures()
    {
        string workflow = Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml"));
        string job = Block(workflow, "published-cli:", 2);
        Assert.Contains("\n    needs: [package]\n", job, StringComparison.Ordinal);
        Assert.Contains("\n      fail-fast: false\n", job, StringComparison.Ordinal);
        Assert.Contains("\n    runs-on: ${{ matrix.os }}\n", job, StringComparison.Ordinal);
        Assert.Equal("      matrix:\n        include:\n          - os: macos-latest\n            rid: osx-arm64\n          - os: windows-latest\n            rid: win-x64\n", Block(job, "matrix:", 6));
        Assert.Contains("uses: actions/download-artifact@634f93cb2916e3fdff6788551b99b062d0335ce0", job, StringComparison.Ordinal);
        Assert.Contains("name: vericairn-${{ needs.package.outputs.version }}\n          path: dist\n", job, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", job, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", job, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    if:", job, StringComparison.Ordinal);
        string mac = Block(job, "- name: Run the published macOS command line", 6);
        Assert.Contains("if: runner.os == 'macOS'\n", mac, StringComparison.Ordinal);
        Assert.Contains("shell: bash\n", mac, StringComparison.Ordinal);
        Assert.Contains("set -eu\n", mac, StringComparison.Ordinal);
        Assert.Contains("tar -xzf \"dist/vericairn-$VERSION-$RID.tar.gz\" -C smoke\n", mac, StringComparison.Ordinal);
        Assert.Contains("smoke/bin/vericairn sources inspect \"$GITHUB_WORKSPACE\"\n", mac, StringComparison.Ordinal);
        string windows = Block(job, "- name: Run the published Windows command line", 6);
        Assert.Contains("if: runner.os == 'Windows'\n", windows, StringComparison.Ordinal);
        Assert.Contains("shell: pwsh\n", windows, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'\n", windows, StringComparison.Ordinal);
        Assert.Contains("Expand-Archive -LiteralPath \"dist/vericairn-$env:VERSION-$env:RID.zip\" -DestinationPath smoke\n", windows, StringComparison.Ordinal);
        Assert.Contains("& ./smoke/bin/vericairn.exe --version\n          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n", windows, StringComparison.Ordinal);
        Assert.Contains("& ./smoke/bin/vericairn.exe sources inspect \"$env:GITHUB_WORKSPACE\"\n          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n", windows, StringComparison.Ordinal);
        foreach (string step in new[] { mac, windows })
        {
            Assert.Contains("VERSION: ${{ needs.package.outputs.version }}\n", step, StringComparison.Ordinal);
            Assert.Contains("RID: ${{ matrix.rid }}\n", step, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DependabotGroupsMonthlyActionUpdatesAndLeavesApprovalToTheWorkflowPolicy()
    {
        string github = Path.Combine(RepositoryRoot(), ".github");
        Assert.Equal("version: 2\nupdates:\n  - package-ecosystem: github-actions\n    directory: /\n    schedule:\n      interval: monthly\n    groups:\n      github-actions:\n        patterns: ['*']\n    open-pull-requests-limit: 1\n", Read(Path.Combine(github, "dependabot.yml")));
        string policy = Read(Path.Combine(github, "scripts", "check-workflow-actions.rb"));
        Assert.Contains("!OWNER.match?(action)", policy, StringComparison.Ordinal);
        Assert.Contains("!allowed.include?(action)", policy, StringComparison.Ordinal);
        Assert.Contains("{40}", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePackageJobRequiresEqualLibraryAssembliesAcrossEveryPublishedPlatform()
    {
        string package = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml")), "package:", 2);
        const string name = "- name: Compare every platform's library assemblies";
        int compare = package.IndexOf(name, StringComparison.Ordinal);
        int cli = package.IndexOf("- name: Publish self-contained distributables", StringComparison.Ordinal);
        int workbench = package.IndexOf("- name: Publish Workbench distributables", StringComparison.Ordinal);
        int checksums = package.IndexOf("- name: Checksums and notes", StringComparison.Ordinal);
        Assert.True(cli > 0 && workbench > cli && compare > workbench && checksums > compare,
            "Every CLI and Workbench bundle is compared before the package job accepts its artifacts.");
        string step = Block(package, name, 6);
        Assert.Contains("set -eu\n", step, StringComparison.Ordinal);
        Assert.Contains("python3 -m unittest discover -s tools -p 'test_distributable_libraries.py'\n", step, StringComparison.Ordinal);
        Assert.Contains("python3 tools/verify-distributable-libraries.py out out-workbench\n", step, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", package, StringComparison.Ordinal);
        Assert.DoesNotContain("if:", step, StringComparison.Ordinal);
        Assert.DoesNotContain("||", step, StringComparison.Ordinal);
        // The checker must cover exactly the RIDs each publishing loop ships, not a subset that could hide a differing build.
        string checker = Read(Path.Combine(RepositoryRoot(), "tools", "verify-distributable-libraries.py"));
        const string platforms = "linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64 win-arm64";
        foreach (string publish in new[] { "- name: Publish self-contained distributables", "- name: Publish Workbench distributables" })
            Assert.Contains("for rid in " + platforms + "; do", Block(package, publish, 6), StringComparison.Ordinal);
        Assert.Contains("RIDS = (" + string.Join(", ", platforms.Split(' ').Select(rid => "\"" + rid + "\"")) + ")", checker, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCheckoutKeepsNoCredentialsAndOnlyTheDraftStepCanWrite()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        foreach (string file in Directory.GetFiles(workflows, "*.yml"))
        {
            string text = Read(file);
            foreach (string checkout in Steps(text).Where(step => step.Contains("uses: actions/checkout@", StringComparison.Ordinal)))
                Assert.Contains("persist-credentials: false", checkout, StringComparison.Ordinal);
            if (Path.GetFileName(file) != "release.yml") Assert.DoesNotContain("contents: write", text, StringComparison.Ordinal);
        }

        string release = Read(Path.Combine(workflows, "release.yml"));
        Assert.Single(release.Split('\n'), line => line.Trim() == "contents: write");
        string draft = Block(release, "draft:", 2);
        Assert.Contains("      contents: write\n", draft, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout", draft, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", Block(draft, "- name: Create the draft release", 6), StringComparison.Ordinal);
        Assert.Single(release.Split('\n'), line => line.Contains("github.token", StringComparison.Ordinal));
        Assert.DoesNotContain("github.token", Read(Path.Combine(workflows, "package.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void NoWorkflowInterpolatesAnInputOrEventValueIntoAScript()
    {
        // 0.8.0's package job interpolated the dispatch input version unquoted into a bash step.
        foreach (string file in Directory.GetFiles(Path.Combine(RepositoryRoot(), ".github", "workflows"), "*.yml"))
            foreach (string script in Scripts(Read(file)))
                foreach (string untrusted in new[] { "${{ inputs.", "${{ github.event", "${{ github.head_ref", "${{ secrets.", "${{ vars.", "${{ steps." })
                    Assert.False(script.Contains(untrusted, StringComparison.Ordinal), $"{Path.GetFileName(file)} interpolates {untrusted} into a script.");
    }

    [Fact]
    public void EveryToolThePackagingPathInstallsIsPinned()
    {
        string package = Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml"));
        string[] installs = package.Split('\n').Where(line => line.TrimStart().StartsWith("dotnet tool install", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(installs);
        Assert.All(installs, line => Assert.Matches(@"--version \d+\.\d+\.\d+\s*$", line));
        foreach (string unpinned in new[] { "pip install", "npm install", "npx ", "apt-get install", "brew install", "curl ", "wget " })
            Assert.DoesNotContain(unpinned, package, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProjectIsInTheSolutionContinuousIntegrationBuilds()
    {
        // A project outside the solution builds only as a reference, in its default configuration, so a Release pack without a
        // build could not find it; DIST-02's registry provider first met this.
        string root = RepositoryRoot();
        string solution = Read(Path.Combine(root, "Vericairn.slnx"));
        string[] projects = ProjectDirectories.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(projects);
        string[] missing = projects.Where(project => !solution.Contains($"Path=\"{project}\"", StringComparison.Ordinal)).ToArray();
        Assert.True(missing.Length == 0, "Not in Vericairn.slnx: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryTestStepRetriesAFailedTestOnceAndReportsIt()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), ".github", "scripts", "run-tests.py")));
        foreach ((string file, string job) in new[] { ("ci.yml", "build-test:"), ("release.yml", "verify:") })
        {
            string steps = Block(Read(Path.Combine(workflows, file)), job, 2);
            Assert.Contains("        run: python .github/scripts/run-tests.py --share ${{ matrix.share }}\n", steps, StringComparison.Ordinal);
            Assert.Contains("            python3 .github/scripts/run-tests.py --share ${{ matrix.share }}'\n", steps, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet test --no-build", steps, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("ci.yml", "build-test:")]
    [InlineData("release.yml", "verify:")]
    public void EveryPlatformBuildsOnceInEachOfThreeIndependentTestShares(string file, string job)
    {
        string steps = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", file)), job, 2);
        Assert.Equal("      matrix:\n        os: [ubuntu-latest, macos-latest, windows-latest]\n        share: [1, 2, 3]\n", Block(steps, "matrix:", 6));
        Assert.Contains("\n      fail-fast: false\n", steps, StringComparison.Ordinal);
        Assert.Contains("\n    runs-on: ${{ matrix.os }}\n", steps, StringComparison.Ordinal);
        Assert.DoesNotContain("max-parallel:", steps, StringComparison.Ordinal);
        Assert.DoesNotContain("needs:", steps, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", steps, StringComparison.Ordinal);
        foreach (string step in new[] { "Restore (locked)", "Build" })
        {
            string build = Block(steps, "- name: " + step, 6);
            Assert.DoesNotContain("if:", build, StringComparison.Ordinal);
            Assert.Contains(step == "Build" ? "dotnet build --no-restore --configuration Release" : "dotnet restore --locked-mode", build, StringComparison.Ordinal);
            Assert.Single(steps.Split('\n'), line => line == "      - name: " + step);
        }

        string companions = Block(steps, "- name: Verify retained companion inputs and bytes", 6);
        Assert.Contains("        if: matrix.share == 1\n", companions, StringComparison.Ordinal);
        Assert.Contains("run: python tests/companion-build.py", companions, StringComparison.Ordinal);
    }

    [Fact]
    public void NonTestBuildChecksRunInOneSharePerPlatform()
    {
        string steps = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")), "build-test:", 2);
        foreach (string name in new[] { "Smoke the CLI", "Pack the Community packages", "Install the tool and SDK from the local feed" })
        {
            string step = Block(steps, "- name: " + name, 6);
            Assert.Contains("        if: matrix.share == 1\n", step, StringComparison.Ordinal);
            Assert.Single(steps.Split('\n'), line => line == "      - name: " + name);
        }
    }

    [Theory]
    [InlineData("ci.yml", "build-test:")]
    [InlineData("release.yml", "verify:")]
    public void EverySharePreparesThePlatformPrerequisitesBeforeTesting(string file, string job)
    {
        string steps = Block(Read(Path.Combine(RepositoryRoot(), ".github", "workflows", file)), job, 2);
        string enclave = Block(steps, "- name: Build the Secure Enclave helper", 6);
        Assert.Contains("        if: runner.os == 'macOS'\n", enclave, StringComparison.Ordinal);
        Assert.Contains("swiftc -O -o artifacts/enclave/vericairn-enclave", enclave, StringComparison.Ordinal);
        Assert.Contains("VERICAIRN_ENCLAVE_HELPER=", enclave, StringComparison.Ordinal);
        Assert.DoesNotContain("matrix.share", enclave, StringComparison.Ordinal);
        if (file == "release.yml")
        {
            Assert.Contains("          artifacts/enclave/vericairn-enclave available\n", enclave, StringComparison.Ordinal);
            Assert.Contains("VERICAIRN_EXPECT_ENCLAVE=1", enclave, StringComparison.Ordinal);
        }

        string service = Block(steps, "- name: Install a secret service", 6);
        Assert.Contains("        if: runner.os == 'Linux'\n", service, StringComparison.Ordinal);
        Assert.Contains("gnome-keyring libsecret-tools dbus-daemon", service, StringComparison.Ordinal);
        Assert.DoesNotContain("matrix.share", service, StringComparison.Ordinal);
        string linux = Block(steps, "- name: Test, with a secret service", 6);
        Assert.Contains("        if: runner.os == 'Linux'\n", linux, StringComparison.Ordinal);
        Assert.Contains("VERICAIRN_EXPECT_SECRET_SERVICE: \"1\"", linux, StringComparison.Ordinal);
        Assert.Contains("dbus-run-session -- bash", linux, StringComparison.Ordinal);
        Assert.Contains("gnome-keyring-daemon --unlock", linux, StringComparison.Ordinal);
        Assert.Contains("secret-tool lookup", linux, StringComparison.Ordinal);
        Assert.Contains("        if: runner.os != 'Linux'\n", Block(steps, "- name: Test", 6), StringComparison.Ordinal);
        Assert.True(steps.IndexOf(enclave, StringComparison.Ordinal) < steps.IndexOf("- name: Test\n", StringComparison.Ordinal));
        Assert.True(steps.IndexOf(service, StringComparison.Ordinal) < steps.IndexOf("- name: Test\n", StringComparison.Ordinal));
    }

    [Fact]
    public void TheReleaseEvidenceWaitsForEveryPlatformAndShare()
    {
        string workflows = Path.Combine(RepositoryRoot(), ".github", "workflows");
        string release = Read(Path.Combine(workflows, "release.yml"));
        Assert.Contains("\n    needs: [verify]\n", Block(release, "package:", 2), StringComparison.Ordinal);
        Assert.Contains("\n    needs: [package]\n", Block(release, "draft:", 2), StringComparison.Ordinal);
        Assert.DoesNotContain("always()", release, StringComparison.Ordinal);
        string package = Block(Read(Path.Combine(workflows, "package.yml")), "package:", 2);
        Assert.Contains("\n    needs: [enclave-helper]\n", package, StringComparison.Ordinal);
        Assert.DoesNotContain("always()", package, StringComparison.Ordinal);
        Assert.Contains("built and tested on ubuntu-latest, macos-latest and windows-latest, with every test class in exactly one of three shares per platform.", package, StringComparison.Ordinal);
    }

    /// <summary>Each step of a workflow: from its "- " line to the next step or the end of its job.</summary>
    private static IEnumerable<string> Steps(string yaml)
    {
        string[] lines = yaml.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("- ", StringComparison.Ordinal) || !lines[i].StartsWith("      - ", StringComparison.Ordinal)) continue;
            int end = i + 1;
            while (end < lines.Length && (lines[end].Length == 0 || lines[end].StartsWith("        ", StringComparison.Ordinal))) end++;
            yield return string.Join('\n', lines[i..end]);
        }
    }

    /// <summary>Every script a workflow runs: each run value, inline or as a block.</summary>
    private static IEnumerable<string> Scripts(string yaml)
    {
        string[] lines = yaml.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("run:", StringComparison.Ordinal)) continue;
            int indent = lines[i].Length - trimmed.Length;
            int end = i + 1;
            while (end < lines.Length && (lines[end].Length == 0 || lines[end].TakeWhile(c => c == ' ').Count() > indent)) end++;
            yield return string.Join('\n', lines[i..end]);
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
