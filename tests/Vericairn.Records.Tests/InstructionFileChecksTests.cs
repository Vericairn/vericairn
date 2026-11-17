// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    public void TheFormsTheReviewFoundAreReportedAndProseThatForbidsIsNot()
    {
        string next = Value(), dbPass = Value(), pwd = Value(), suffixed = Value(), spaced = Value(), letters = Letters(), basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("deploy:" + Value()));
        string dollar = Mixed(), percent = Value(), angle = Value(), square = Value(), round = Value(), block = Value(), dotted = Value(), starred = Value();
        (string Line, string? Code)[][] groups =
        [
            // Hooks: the generic Stop and Notification events in a one-line block, a hooks key after a quote or list marker, a key and its
            // colon on separate lines, a TOML dotted key and another event's name.
            [("""{"Notification": [{"command": "./notify.sh"}]}""", Hook)],
            [("""{"Stop": [{"command": "./after.sh"}]}""", Hook)],
            [("> hooks:", Hook)],
            [("1. hooks:", Hook)],
            [("\"hooks\"", Hook), (": {}", null)],
            [("hooks.PreToolUse = []", Hook)],
            [("BeforeToolSelection: ./select.sh", Hook)],

            // Approvals: a default mode that accepts edits, allow lists whose items follow on the next lines, flags with their values
            // after a space, a launch flag that never asks, and keys that accept or trust on their own.
            [("""{"permissions": {"defaultMode": "acceptEdits"}}""", Approval)],
            [("\"allow\":", Approval), ("[", null), ("  \"Bash(*)\"", null), ("]", null)],
            [("allow:", Approval), ("  - Bash(*)", null)],
            [("Launch it with --allowedTools Bash.", Approval)],
            [("Launch it with --allowed-tools \"Bash Edit\".", Approval)],
            [("Launch it with --ask-for-approval never.", Approval)],
            [("Launch it with -a never.", Approval)],
            [("""{"autoAccept": true}""", Approval)],
            [("""{"trust": true}""", Approval)],
            [("Launch it with --allow-tool shell.", Approval)],

            // Secrets: a value on the next line, after a block indicator or as a continuation, keys that only contain a secret's name or
            // write it with a space, a password of letters alone, a basic credential, and real values that start as a placeholder might.
            [("password: |", null), ("  " + block, Secret)],
            [("api_key:", null), ("  " + next, Secret)],
            [("DB_PASS=" + dbPass, Secret)],
            [("pwd: " + pwd, Secret)],
            [("GITHUB_TOKEN_VALUE=" + suffixed, Secret)],
            [("API key: " + spaced, Secret)],
            [("password: " + letters, Secret)],
            [("Authorization: Basic " + basic, Secret)],
            [("password: $" + dollar, Secret)],
            [("password: %" + percent, Secret)],
            [("password: <" + angle, Secret)],
            [("password: [" + square + "]", Secret)],
            [("password: (" + round + ")", Secret)],
            [("credentials:", null), ("  token: " + Value(), Secret)],
            [("password: ..." + dotted, Secret)],
            [("password: " + starred + "***", Secret)],

            // Not reported: prose that forbids or describes, a version, a word-built identifier, references and placeholders.
            [("Never pass --dangerously-skip-permissions.", null)],
            [("- Do not set core.hooksPath in this estate.", null)],
            [("Hooks: none are used in this estate.", null)],
            [("The allowed-tools: key is forbidden here.", null)],
            [("The SessionStart: event is not used.", null)],
            [("token: v1.2.3-beta4", null)],
            [("See VericairnRecordsEstateInstructionFileChecks2026Tests for the rules.", null)],
            [("password: $DEPLOY_PASSWORD", null)],
            [("password: %DEPLOY_PASSWORD%", null)],
            [("password: [your password]", null)],
            [("password: required", null)],
            [("password: stored in the deployment vault", null)],
        ];

        var lines = new List<string>();
        var expected = new List<string>();
        foreach ((string Line, string? Code)[] group in groups)
        {
            foreach ((string line, string? code) in group)
            {
                lines.Add(line);
                if (code is not null) expected.Add($"{code} AGENTS.md:{lines.Count}");
            }

            lines.Add(string.Empty);
        }

        var findings = new FindingList();
        InstructionFileChecks.Scan(string.Join("\n", lines), "AGENTS.md", findings);
        Assert.Equal(expected.Order(StringComparer.Ordinal), findings.Select(f => $"{f.Code} {f.Location}").Order(StringComparer.Ordinal));
        Assert.False(Echoes(findings, next, dbPass, pwd, suffixed, spaced, letters, basic, dollar, percent, angle, square, round, block, dotted, starred), "A finding echoes a value it found.");
    }

    // The second round of the review: a value of punctuation alone, a placeholder anywhere a value may stand, made the scan throw, which
    // failed the whole estate check. Each is now a placeholder, read without failing, and none is reported.
    [Theory]
    [InlineData("password: ...")]
    [InlineData("token: .")]
    [InlineData("API_KEY=,")]
    [InlineData("export GITHUB_TOKEN=...")]
    [InlineData("Set `API_KEY=...` first.")]
    [InlineData("\"password\": \"...\",")]
    [InlineData("Authorization: Bearer ............")]
    [InlineData("https://deploy:...@example.invalid")]
    [InlineData("curl -u deploy" + ":... https://example.invalid")] // split so the repository's secret scan reads no credential shape
    [InlineData("password: ;;;")]
    [InlineData("password: \u2026")]
    public void APlaceholderOfPunctuationIsReadWithoutFailing(string line)
    {
        var findings = new FindingList();
        Exception? failure = Record.Exception(() => InstructionFileChecks.Scan(line + "\n", "AGENTS.md", findings));
        Assert.True(failure is null, $"The scan failed with {failure?.GetType().Name}.");
        Assert.Empty(findings);
    }

    [Fact]
    public void AnEstateCheckOverPlaceholdersReportsAndDoesNotFail()
    {
        using var estate = new TestEstate();
        File.WriteAllText(Path.Combine(estate.Root, "AGENTS.md"), "password: ...\nexport GITHUB_TOKEN=...\nhooks:\n");
        FindingList findings = EstateChecks.Run(estate.Load());
        Assert.Equal(["instructions.hook AGENTS.md:3"], findings.Where(f => f.Code.StartsWith("instructions.", StringComparison.Ordinal)).Select(f => $"{f.Code} {f.Location}"));
    }

    [Fact]
    public void AFailureOfOneFilesScanIsThatFilesFindingAndNeverItsContent()
    {
        string folder = Path.Combine(Path.GetTempPath(), "vericairn-instructions-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(folder);
        try
        {
            string value = Value(), file = Path.Combine(folder, "AGENTS.md");
            File.WriteAllText(file, "password: " + value + "\n");
            var findings = new FindingList();
            EstateChecks.ScanInstructions(file, "AGENTS.md", findings, (text, relative, scanned) =>
            {
                scanned.Warning(Secret, relative + ":1", "A partial finding.");
                throw new ArgumentOutOfRangeException(nameof(text), text, "A failure that carries the text.");
            });

            Finding unreadable = Assert.Single(findings);
            Assert.Equal(("instructions.unreadable", "AGENTS.md", Severity.Warning), (unreadable.Code, unreadable.Location, unreadable.Severity));
            Assert.Contains("ArgumentOutOfRangeException", unreadable.Message, StringComparison.Ordinal);
            Assert.False(Echoes(findings, value), "The finding echoes the file's content.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The second round of the review: a line that opens by forbidding exempts only the clause it opens, up to a clause end or a word
    // that turns it around, and only when its forbidding word opens a sentence and is followed by a space.
    [Fact]
    public void AForbiddingClauseExemptsOnlyWhatItForbids()
    {
        (string Line, string? Code)[][] groups =
        [
            [("Never ask for approval: always launch with --dangerously-skip-permissions.", Approval)],
            [("Never stop to ask. Always launch with --dangerously-skip-permissions.", Approval)],
            [("Never run without --dangerously-skip-permissions.", Approval)],
            [("Do not forget to pass --yolo.", Approval)],
            [("Avoid approval prompts by launching with --dangerously-skip-permissions.", Approval)],
            [("No prompts: approval_policy = \"never\"", Approval)],
            [("Not optional: run git config core.hooksPath .githooks first.", Hook)],
            [("Agents in this estate should work quickly and", null), ("never start without --dangerously-skip-permissions.", Approval)],
            [("NO_COLOR=1 agent-tool --dangerously-skip-permissions", Approval)],
            [("Never pass --dangerously-skip-permissions.", null)],
            [("- Do not set core.hooksPath in this estate.", null)],
            [("Avoid --yolo in this estate.", null)],
        ];

        var lines = new List<string>();
        var expected = new List<string>();
        foreach ((string Line, string? Code)[] group in groups)
        {
            foreach ((string line, string? code) in group)
            {
                lines.Add(line);
                if (code is not null) expected.Add($"{code} AGENTS.md:{lines.Count}");
            }

            lines.Add(string.Empty);
        }

        var findings = new FindingList();
        InstructionFileChecks.Scan(string.Join("\n", lines), "AGENTS.md", findings);
        Assert.Equal(expected.Order(StringComparer.Ordinal), findings.Select(f => $"{f.Code} {f.Location}").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("a-")]
    [InlineData("a.")]
    [InlineData("1_")]
    [InlineData("pwd:")]
    [InlineData("password:<")]
    [InlineData("\"")]
    [InlineData("> ")]
    [InlineData("- ")]
    [InlineData("1. ")]
    [InlineData("- --yolo ")]
    [InlineData("Basic ")]
    [InlineData("https://a:")]
    public void ALineAtTheSizeLimitIsReadInLinearTime(string unit)
    {
        // The ceiling only detects a stall: a pattern that backtracks takes hours on such a line.
        string text = string.Concat(Enumerable.Repeat(unit, (int)((EstateLayout.MaxFileBytes - 1) / unit.Length))) + "\n";
        var watch = Stopwatch.StartNew();
        InstructionFileChecks.Scan(text, "AGENTS.md", new FindingList());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Reading a one-line file of {text.Length:N0} characters took {watch.Elapsed}.");
    }

    [Fact]
    public void AnInstructionFileAtTheSizeLimitDoesNotStallTheEstateCheck()
    {
        using var estate = new TestEstate();
        File.WriteAllText(Path.Combine(estate.Root, "AGENTS.md"), string.Concat(Enumerable.Repeat("a-", (int)((EstateLayout.MaxFileBytes - 1) / 2))) + "\n");
        var watch = Stopwatch.StartNew();
        FindingList findings = EstateChecks.Run(estate.Load());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"The estate check took {watch.Elapsed}.");
        Assert.DoesNotContain(findings, f => f.Code is "layout.size" or "layout.unexpected");
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
            string pointer = EstateLayout.OwnerInstructionFiles.First(name => name != "AGENTS.md");
            Directory.CreateDirectory(Path.Combine(clone, pointer));
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

            // A file of an instruction file's name in another case is the one a case-insensitive volume opens under that name, and a link
            // of that name is reported wherever it leads, a directory outside the clone included.
            File.Delete(Path.Combine(clone, "AGENTS.md"));
            File.WriteAllText(Path.Combine(clone, "agents.md"), "# Widget\n");
            Directory.Delete(Path.Combine(clone, pointer));
            string outside = Path.Combine(clone + "-outside");
            Directory.CreateDirectory(outside);
            bool linked;
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(clone, pointer), outside);
                linked = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                linked = false;
            }

            string[] expected = linked ? [$"resource:{repository.Id}/agents.md", $"resource:{repository.Id}/{pointer}"] : [$"resource:{repository.Id}/agents.md"];
            Assert.Equal(expected.Order(StringComparer.Ordinal), EstateChecks.Run(estate.Load()).Where(f => f.Code == "instructions.repository").Select(f => f.Location).Order(StringComparer.Ordinal));
            Directory.Delete(outside);
        }
        finally
        {
            Directory.Delete(clone, recursive: true);
        }
    }

    // Twenty-four letters and digits, at least one of each.
    private static string Value() => "Q7" + Convert.ToHexString(RandomNumberGenerator.GetBytes(11));

    // Forty-two random token characters that cycle through upper case, lower case and digits, as a random token does.
    private static string LongToken()
    {
        const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", Lower = "abcdefghijklmnopqrstuvwxyz", Digits = "0123456789";
        var token = new StringBuilder();
        for (int i = 0; i < 42; i++)
        {
            string from = (i % 3) switch { 0 => Upper, 1 => Lower, _ => Digits };
            token.Append(from[RandomNumberGenerator.GetInt32(from.Length)]);
        }

        return token.ToString();
    }

    // Twenty random lower-case letters, a password with no digit.
    private static string Letters() => string.Concat(Enumerable.Range(0, 20).Select(_ => (char)('a' + RandomNumberGenerator.GetInt32(26))));

    // A value mixing lower case and digits, which no variable reference is.
    private static string Mixed() => "x9" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(11));

    private static bool Echoes(FindingList findings, params string[] values) =>
        findings.Any(f => values.Any(v => f.ToString().Contains(v, StringComparison.Ordinal)));
}
