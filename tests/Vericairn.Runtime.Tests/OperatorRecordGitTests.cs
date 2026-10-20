// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class OperatorRecordGitTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "vericairn-operator-git-" + Guid.NewGuid().ToString("N"));
    private readonly string root;
    private readonly string configuration;
    private readonly string hooks;
    private readonly GitRecordCommitAdapter adapter;
    private readonly RecordCommitBasis basis;
    private const string RecordPath = "records/acceptance/2026-10/example.json";
    private const string RecordText = "{\"statement\":\"An exact record with Unicode: café.\"}\n";
    private static readonly string ByteHash = ContentHash.Sha256(Encoding.UTF8.GetBytes(RecordText));
    private static readonly HashSet<string> NoReceipts = new(StringComparer.Ordinal);
    private bool loseCommitReceipt;
    private int commits;

    public OperatorRecordGitTests()
    {
        root = Path.Combine(scratch, "estate");
        configuration = Path.Combine(scratch, "gitconfig");
        hooks = Path.Combine(scratch, "hooks");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(hooks);
        File.WriteAllText(configuration, string.Empty);
        Git("init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "Initial content.\n");
        Git("add", "readme.txt");
        Git("commit", "-m", "Initial content");
        commits = 0;
        adapter = new(Run);
        basis = adapter.Prepare(root);
    }

    [Fact]
    public void TheCommitContainsOnlyTheExactRecordAndLeavesRecordedReceiptsUntracked()
    {
        adapter.Branch(root, basis, "operator/example");
        WriteRecord();
        const string receipt = "records/operation/2026-10/receipt.json";
        Write(receipt, "{}\n");
        var receipts = new HashSet<string>(StringComparer.Ordinal) { receipt };
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, receipts);
        Assert.Equal(RecordPath, Git("diff-tree", "--no-commit-id", "--name-only", "-r", basis.Head, head));
        Assert.Equal(RecordText.TrimEnd(), Git("show", head + ":" + RecordPath));
        Assert.Equal("?? " + receipt, Git("status", "--porcelain=v1", "--untracked-files=all"));
        Assert.Equal(basis.Head, Git("rev-parse", "main"));
        adapter.Check(root, "operator/example", head, receipts);
        Assert.Equal(head, adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, receipts));
        Assert.Equal(1, commits);
        Git("checkout", "main");
        adapter.Verify(root, head, basis.Head, RecordPath, ByteHash);
        Assert.Equal("main", Git("branch", "--show-current"));
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("modified")]
    [InlineData("staged")]
    public void OtherChangesRefuseBothPreparationAndCommitWithoutTouchingThem(string kind)
    {
        if (kind == "untracked") Write("other.txt", "Keep this.\n");
        else Write("readme.txt", "Keep this.\n");
        if (kind == "staged") Git("add", "readme.txt");
        string before = Git("status", "--porcelain=v1");
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(root));
        Assert.Equal(before, Git("status", "--porcelain=v1"));
        Assert.Equal(0, commits);
        // Starting a branch outside the adapter does not make unrelated changes acceptable at the commit boundary.
        Git("checkout", "-b", "operator/example");
        WriteRecord();
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts));
        Assert.Equal(basis.Head, Git("rev-parse", "HEAD"));
        Assert.Equal("Keep this.\n", File.ReadAllText(Path.Combine(root, kind == "untracked" ? "other.txt" : "readme.txt")));
    }

    [Fact]
    public void BranchingAndCommittingDoNotInvokeUserHooks()
    {
        foreach (string name in new[] { "post-checkout", "pre-commit" })
        {
            string hook = Path.Combine(hooks, name);
            File.WriteAllText(hook, "exit 1\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        adapter.Branch(root, basis, "operator/example");
        WriteRecord();
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts);
        Assert.Equal(RecordPath, Git("diff-tree", "--no-commit-id", "--name-only", "-r", basis.Head, head));
        Assert.Equal(1, commits);
    }

    [Fact]
    public void ACommitThatSucceededBeforeAnInterruptedReceiptIsRecognized()
    {
        adapter.Branch(root, basis, "operator/example");
        WriteRecord();
        loseCommitReceipt = true;
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts));
        string head = Git("rev-parse", "HEAD");
        loseCommitReceipt = false;
        var reopened = new GitRecordCommitAdapter(Run);
        reopened.Branch(root, basis, "operator/example");
        Assert.Equal(head, reopened.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts));
        Assert.Equal(1, commits);
    }

    [Fact]
    public void ChangedRecordBytesCannotBeCommittedOrAcceptedAsARecoveredCommit()
    {
        adapter.Branch(root, basis, "operator/example");
        WriteRecord();
        Write(RecordPath, "{}\n");
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts));
        Assert.Equal(0, commits);
        Git("add", "--", RecordPath);
        Git("commit", "-m", "A different record");
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts));
    }

    [Fact]
    public void ABranchThatMovedOrHasExtraCommitsCannotResume()
    {
        adapter.Branch(root, basis, "operator/example");
        WriteRecord();
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts);
        Write("other.txt", "Additional content.\n");
        Git("add", "other.txt");
        Git("commit", "-m", "Another change");
        Assert.Throws<ProviderRefusedException>(() => adapter.Check(root, "operator/example", head, NoReceipts));
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, NoReceipts));
    }

    [Fact]
    public void AStagedReceiptCannotBeSmuggledIntoTheRecordCommit()
    {
        adapter.Branch(root, basis, "operator/example");
        WriteRecord();
        const string receipt = "records/operation/2026-10/receipt.json";
        Write(receipt, "{}\n");
        Git("add", "--", receipt);
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordPath, ByteHash, new HashSet<string>(StringComparer.Ordinal) { receipt }));
        Assert.Equal(0, commits);
        Assert.Equal(receipt, Git("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void ADirectoryInsideAnotherRepositoryIsNotTheEstateRepository()
    {
        string nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(nested));
    }

    [Fact]
    public void ASignedAcceptanceUsesARealLocalCommitAndResumesPastItsOwnUntrackedOperationReceipts()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Estate","role":"Estate","location":{"github":"Example/estate"},"visibility":"private"}""");
        estate.Add("product", estate.OperatorId, $$"""
            {"name":"Estate","slug":"estate","summary":"Test estate.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"memory-pull-requests","repository":"{{repository.Id}}","target":"main","requiredChecks":["tests"]} }
            """);
        RecordEnvelope milestone = estate.Add("work-order", estate.OperatorId, """{"key":"M-1","kind":"milestone","title":"Milestone","intent":"Finish.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
        estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Ready."}""", milestone.Reference);
        foreach (string file in Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories))
            Write(Path.GetRelativePath(estate.Root, file), File.ReadAllText(file));
        RepositoryClones.Register(estate.Estate.Id, repository.Id, root);
        using EstateService service = EstateService.Open(root);
        string device = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the operator.")).Ok);
        Git("add", ".");
        Git("commit", "-m", "Prepare the test estate");
        string before = Git("rev-parse", "HEAD");
        var remote = new RecordedRemote(before);
        service.Providers = new() { RecordCommitAdapter = adapter, PushAdapter = remote, SecretScanner = remote };
        service.Providers.Register(remote.Memory);
        var request = new OperatorRecordRequest("acceptance", estate.OperatorId,
            JsonSerializer.SerializeToElement(new { statement = "I accept this milestone.", basis = Array.Empty<string>(), self = true }),
            $"work-order:{milestone.Id}:{milestone.ComputedHash}", WaitSeconds: 0);
        OperatorRecordResponse first = service.OperatorRecord(request, TestContext.Current.CancellationToken);
        Assert.False(first.Ok);
        Assert.Equal("known-failure", first.Change!.Status);
        Assert.Equal(0, remote.Pushes);
        Assert.Equal(first.Record!.Path, Git("diff-tree", "--no-commit-id", "--name-only", "-r", before, first.Head!));
        remote.ScanPasses = true;
        OperatorRecordResponse resumed = service.OperatorRecord(request with { Reconciliation = "The scanner is available and the failed scan had no external effect." }, TestContext.Current.CancellationToken);
        Assert.True(resumed.Ok, resumed.Message + " " + resumed.NextStep);
        Assert.Equal(first.Record, resumed.Record);
        Assert.Equal(first.Head, resumed.Head);
        Assert.Equal(1, remote.Pushes);
        Assert.Equal(2, remote.Memory.Effects);
        Assert.Equal(device, Assert.Single(service.Session.Load().ById[first.Record.Id].Signatures).Device);
        Assert.All(Git("status", "--porcelain=v1", "--untracked-files=all").Split('\n'), line => Assert.StartsWith("?? records/operation/", line, StringComparison.Ordinal));
        Assert.Equal(0, service.Validate().Errors);
    }

    private sealed class RecordedRemote(string basis) : IPushAdapter, ISecretScanner
    {
        internal MemoryPullRequestProvider Memory { get; } = new();
        internal bool ScanPasses { get; set; }
        internal int Pushes { get; private set; }
        private string? pushed;
        public PushPlan Prepare(string directory, string remote, string branch, string target, string head)
        {
            Memory.Checks = [new("tests", head, "completed", "success")];
            Memory.SetTarget("Example/estate", new("main", Memory.MergeCommit, [new("tests", Memory.MergeCommit, "completed", "success")]));
            return new(branch, target, head, basis, null, basis);
        }
        public SecretScan Scan(string directory, PushPlan plan) => new("scanner", "1.0.0", plan.Range, ScanPasses ? "passed" : "unavailable", ScanPasses ? 0 : null);
        public void Push(string directory, string remote, PushPlan plan, SecretScan scan) { Assert.True(scan.Passed(plan)); pushed = plan.Head; Pushes++; }
        public string? Observe(string directory, string remote, string branch) => pushed;
    }

    private void WriteRecord() => Write(RecordPath, RecordText);
    private void Write(string relative, string text)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
    private string Git(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        ProcessResult result = Run(info);
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.True(result.ExitCode == 0, "The isolated git command did not succeed.");
        return result.StandardOutput.Trim();
    }
    private ProcessResult Run(ProcessStartInfo info)
    {
        Assert.Equal("git", info.FileName);
        Assert.DoesNotContain(info.ArgumentList, a => a is "push" or "fetch" or "ls-remote");
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = configuration;
        string[] args = info.ArgumentList.ToArray();
        info.ArgumentList.Clear();
        foreach (string value in new[] { "user.name=Test", "user.email=test@example.invalid", "commit.gpgsign=false", "core.autocrlf=false", "core.hooksPath=" + hooks })
        { info.ArgumentList.Add("-c"); info.ArgumentList.Add(value); }
        foreach (string argument in args) info.ArgumentList.Add(argument);
        ProcessResult result = BoundedProcess.Run(info, TestTiming.HangTimeout);
        if (args.Contains("commit") && result.ExitCode == 0)
        {
            commits++;
            if (loseCommitReceipt) return new(ProcessOutcome.TimedOut, true, null, string.Empty, string.Empty);
        }
        return result;
    }

    public void Dispose() => WorkspaceCleanup.Delete(scratch);
}
