// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-139 (decisions 01M7DGWZSJMTNPA68V84ERTCAM and 01M7H0HMT1YDZ2XVD3XXDZ5BNN): estate check warns about a hook, an automatic approval
/// and a secret-like value in the owner's agent instruction files at the estate's root, naming the file, the line and the kind and never
/// the value, and reports a repository's own instruction files as untrusted input. The secret-like values are made when the test runs,
/// so no source file holds one, and no assertion is given a value or a line to print.
/// </summary>
public sealed class InstructionFileChecksTests
{
    private const string Hook = "instructions.hook";
    private const string Approval = "instructions.approval";
    private const string Secret = "instructions.secret";

    // The owner's file in the MCP guide's example, which carries none of the three kinds.
    private const string Clean = """
        # Working in this estate

        These are the owner's instructions for agents working in this estate. Read them first, then read
        the generated orientation in `docs/vericairn-orientation/AGENTS.md`, which says how the estate works
        and how to reach it. Regenerate that directory with
        `vericairn mcp orientation --out docs/vericairn-orientation`; never edit it.

        - The owner's own instructions follow here.

        """;

    [Fact]
    public void ACleanInstructionFileAndItsPointersHaveNoInstructionFinding()
    {
        using var estate = new TestEstate();
        File.WriteAllText(Path.Combine(estate.Root, "AGENTS.md"), Clean);
        foreach (string pointer in EstateLayout.OwnerInstructionFiles.Where(name => name != "AGENTS.md"))
            File.WriteAllText(Path.Combine(estate.Root, pointer), "Read `AGENTS.md`.\n");

        FindingList findings = EstateChecks.Run(estate.Load());
        Assert.DoesNotContain(findings, f => f.Code.StartsWith("instructions.", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Code == "layout.unexpected");
    }

    [Fact]
    public void EachKindIsAWarningNamingTheFileTheLineAndTheKindAndNeverTheValue()
    {
        string assigned = Value(), password = Value(), bearer = Value(), inUrl = Value(), token = LongToken();
        (string Line, string? Code)[] lines =
        [
            // Hooks: an event key in an embedded settings block, a hooks key in front matter and in TOML, an editor task run when a
            // folder opens, and a Git hooks path.
            ("""{"hooks": {"PreToolUse": [{"matcher": "*", "hooks": [{"type": "command", "command": "./check.sh"}]}]}}""", Hook),
            ("hooks:", Hook),
            ("[hooks]", Hook),
            ("""{"label": "prepare", "type": "shell", "command": "./prepare.sh", "runOptions": {"runOn": "folderOpen"}}""", Hook),
            ("Before you start, run `git config core.hooksPath .githooks`.", Hook),

            // Automatic approvals: allowed tools in front matter, a permission allow list, launch flags that skip approvals, a policy
            // that never asks and an editor setting that approves on its own.
            ("allowed-tools: Bash(*), Edit", Approval),
            ("""{"permissions": {"allow": ["Bash(*)"]}}""", Approval),
            ("Start every session with --dangerously-skip-permissions so it never stops to ask.", Approval),
            ("""approval_policy = "never" """, Approval),
            ("""{"chat.tools.autoApprove": true}""", Approval),

            // Secret-like values: a secret-named key with a literal value, a bearer token, a password in a URL and a long random string.
            ("API_KEY=" + assigned, Secret),
            ("The deployment password: " + password, Secret),
            ("Send `Authorization: Bearer " + bearer + "` with every request.", Secret),
            ("Clone https://deploy:" + inUrl + "@example.invalid/widget.git first.", Secret),
            ("Sign in with " + token + " when asked.", Secret),

            // Not one of the kinds: placeholders and references, prose about hooks and approvals, and paths of record ids.
            ("API_KEY=<your key>", null),
            ("password: ${DEPLOY_PASSWORD}", null),
            ("token: read it from the store's credential reference", null),
            ("Never add hooks or approvals here; ask the operator first.", null),
            ("See records/decision/2026-11/01M7DGWZSJMTNPA68V84ERTCAM.json for why.", null),
            ("Compare with commit 0123456789abcdef0123456789abcdef01234567 and sha256:" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) + ".", null),
        ];

        using var estate = new TestEstate();
        int first = Clean.Split('\n').Length;
        File.WriteAllText(Path.Combine(estate.Root, "AGENTS.md"), Clean + string.Join("\n", lines.Select(l => l.Line)) + "\n");
        FindingList findings = EstateChecks.Run(estate.Load());

        string[] expected = [.. lines.Select((l, i) => l.Code is null ? null : $"{l.Code} AGENTS.md:{first + i}").OfType<string>().Order(StringComparer.Ordinal)];
        string[] found = [.. findings.Where(f => f.Code.StartsWith("instructions.", StringComparison.Ordinal)).Select(f => $"{f.Code} {f.Location}").Order(StringComparer.Ordinal)];
        Assert.Equal(expected, found);
        Assert.All(findings.Where(f => f.Code.StartsWith("instructions.", StringComparison.Ordinal)), f => Assert.Equal(Severity.Warning, f.Severity));
        Assert.False(Echoes(findings, assigned, password, bearer, inUrl, token), "A finding echoes a value it found.");

        // The same lines anywhere but an owner's instruction file at the root are not read as instructions.
        File.Delete(Path.Combine(estate.Root, "AGENTS.md"));
        Directory.CreateDirectory(Path.Combine(estate.Root, "docs"));
        File.WriteAllText(Path.Combine(estate.Root, "docs", "AGENTS.md"), string.Join("\n", lines.Select(l => l.Line)) + "\n");
        Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code.StartsWith("instructions.", StringComparison.Ordinal));
    }

    [Fact]
    public void ACredentialInAKnownFormatStaysAnErrorWithoutASecondFinding()
    {
        using var estate = new TestEstate();
        string known = "ghp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        File.WriteAllText(Path.Combine(estate.Root, "AGENTS.md"), Clean + "Use " + known + " to push.\n");
        FindingList findings = EstateChecks.Run(estate.Load());
        string location = $"AGENTS.md:{Clean.Split('\n').Length}";
        Assert.Contains(findings, f => f.Code == "secrets.pattern" && f.Severity == Severity.Error && f.Location == location);
        Assert.DoesNotContain(findings, f => f.Code == Secret);
        Assert.False(Echoes(findings, known), "A finding echoes a value it found.");
    }

    [Fact]
    public void ARepositorysOwnInstructionFilesAreReportedAsUntrustedInput()
    {
        using var estate = new TestEstate();
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""");
        string clone = Path.Combine(Path.GetTempPath(), "vericairn-clone-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(clone);
        try
        {
            // The repository's own file carries a hook; a directory of a pointer file's name is no instruction file.
            File.WriteAllText(Path.Combine(clone, "AGENTS.md"), "# Widget\n\nhooks:\n");
            Directory.CreateDirectory(Path.Combine(clone, EstateLayout.OwnerInstructionFiles.First(name => name != "AGENTS.md")));
            Assert.DoesNotContain(EstateChecks.Run(estate.Load()), f => f.Code == "instructions.repository");

            RepositoryClones.Register(estate.Estate.Id, repository.Id, clone);
            FindingList findings = EstateChecks.Run(estate.Load());
            Finding untrusted = Assert.Single(findings, f => f.Code == "instructions.repository");
            Assert.Equal(Severity.Warning, untrusted.Severity);
            Assert.Equal($"resource:{repository.Id}/AGENTS.md", untrusted.Location);
            Assert.Contains("untrusted input", untrusted.Message, StringComparison.Ordinal);
            Assert.False(untrusted.ToString().Contains(clone, StringComparison.Ordinal), "The finding names a machine path.");

            // The repository's file is not the owner's: only the owner's files are read for the three kinds.
            Assert.DoesNotContain(findings, f => f.Code == Hook);

            // A revision of the resource still finds the clone registered under its earlier id, and names the resource by its first.
            RecordEnvelope revised = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"The widget's source.","location":{"github":"Example/widget"},"visibility":"private"}""", supersedes: repository.Id);
            Assert.NotEqual(repository.Id, revised.Id);
            Assert.Equal($"resource:{repository.Id}/AGENTS.md", Assert.Single(EstateChecks.Run(estate.Load()), f => f.Code == "instructions.repository").Location);
        }
        finally
        {
            Directory.Delete(clone, recursive: true);
        }
    }

    // Twenty-four letters and digits, at least one of each.
    private static string Value() => "Q7" + Convert.ToHexString(RandomNumberGenerator.GetBytes(11));

    // Forty-odd token characters mixing upper and lower case and digits, with no slash.
    private static string LongToken() => "Aa1" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(30)).Replace('/', '_').TrimEnd('=');

    private static bool Echoes(FindingList findings, params string[] values) =>
        findings.Any(f => values.Any(v => f.ToString().Contains(v, StringComparison.Ordinal)));
}
