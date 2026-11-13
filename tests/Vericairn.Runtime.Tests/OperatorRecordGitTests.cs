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
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class OperatorRecordGitTests : IDisposable
{
    private static readonly AdditionalRegister ExtraRegister = InstallRegister();
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "vericairn-operator-git-" + Guid.NewGuid().ToString("N"));
    private readonly string root;
    private readonly string configuration;
    private readonly string hooks;
    private readonly GitRecordCommitAdapter adapter;
    private readonly RecordCommitBasis basis;
    private const string RecordPath = "records/acceptance/2026-10/example.json";
    private const string RecordText = "{\"statement\":\"An exact record with Unicode: café.\"}\n";
    private static readonly string ByteHash = ContentHash.Sha256(Encoding.UTF8.GetBytes(RecordText));
    private static readonly IReadOnlyDictionary<string, string> RecordFiles = new Dictionary<string, string>(StringComparer.Ordinal) { [RecordPath] = ByteHash };
    private static readonly HashSet<string> NoReceipts = new(StringComparer.Ordinal);
    private static readonly string[] MilestoneKeys = ["M-1", "M-2"];
    private bool loseCommitReceipt;
    private bool refuseBranch;
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
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        const string receipt = "records/operation/2026-10/receipt.json";
        Write(receipt, "{}\n");
        var receipts = new HashSet<string>(StringComparer.Ordinal) { receipt };
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordFiles, receipts);
        Assert.Equal(RecordPath, Git("diff-tree", "--no-commit-id", "--name-only", "-r", basis.Head, head));
        Assert.Equal(RecordText.TrimEnd(), Git("show", head + ":" + RecordPath));
        Assert.Equal("?? " + receipt, Git("status", "--porcelain=v1", "--untracked-files=all"));
        Assert.Equal(basis.Head, Git("rev-parse", "main"));
        adapter.Check(root, "operator/example", head, receipts);
        Assert.Equal(head, adapter.Commit(root, "operator/example", basis.Head, RecordFiles, receipts));
        Assert.Equal(1, commits);
        Git("checkout", "main");
        adapter.Verify(root, head, basis.Head, RecordFiles);
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
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts));
        Assert.Equal(basis.Head, Git("rev-parse", "HEAD"));
        Assert.Equal("Keep this.\n", File.ReadAllText(Path.Combine(root, kind == "untracked" ? "other.txt" : "readme.txt")));
    }

    [Fact]
    public void ANewActionToleratesOnlyTheNamedReceiptsOfTheActionItFinished()
    {
        const string receipt = "records/operation/2026-10/finished-receipt.json";
        Write(receipt, "{}\n");
        var receipts = new HashSet<string>(StringComparer.Ordinal) { receipt };
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(root));
        Assert.Equal(basis, adapter.Prepare(root, receipts));
        adapter.Branch(root, basis, "operator/example", RecordFiles, receipts);
        Assert.Equal("operator/example", Git("branch", "--show-current"));
        WriteRecord();
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordFiles, receipts);
        Assert.Equal(RecordPath, Git("diff-tree", "--no-commit-id", "--name-only", "-r", basis.Head, head));
        Assert.Equal("?? " + receipt, Git("status", "--porcelain=v1", "--untracked-files=all"));
        Git("checkout", "main");
        Write("other.txt", "Unrelated.\n");
        Assert.Throws<ProviderRefusedException>(() => adapter.Prepare(root, receipts));
        Assert.Throws<ProviderRefusedException>(() => adapter.Branch(root, basis, "operator/another", RecordFiles, receipts));
        Assert.Equal("main", Git("branch", "--show-current"));
    }

    [Fact]
    public void CarriedTrailRecordsAreCommittedWithTheRecordAndVerifiedExactly()
    {
        const string trail = "records/operation/2026-10/earlier-trail.json";
        const string committed = "records/operation/2026-10/committed.json";
        const string trailText = "{\"earlier\":true}\n";
        Write(committed, "{}\n");
        Git("add", "--", committed);
        Git("commit", "-m", "A record already committed");
        RecordCommitBasis start = adapter.Prepare(root);
        commits = 0;
        Write(trail, trailText);
        var carried = new Dictionary<string, string>(StringComparer.Ordinal) { [trail] = ContentHash.Sha256(Encoding.UTF8.GetBytes(trailText)) };
        var receipts = new HashSet<string>(StringComparer.Ordinal) { trail };
        // Only an untracked path is reported untracked: not a committed record, nor one the working tree does not hold.
        Assert.Equal([trail], adapter.Untracked(root, new HashSet<string>(StringComparer.Ordinal) { trail, committed, "records/operation/2026-10/absent.json" })!);
        adapter.Branch(root, start, "operator/example", RecordFiles, receipts);
        WriteRecord();
        string head = adapter.Commit(root, "operator/example", start.Head, RecordFiles, receipts, carried);
        Assert.Equal(new[] { RecordPath, trail }.Order(StringComparer.Ordinal), Git("diff-tree", "--no-commit-id", "--name-only", "-r", start.Head, head).Split('\n'));
        Assert.Equal(trailText.TrimEnd(), Git("show", head + ":" + trail));
        Assert.Contains("and carry 1 trail record of earlier actions", Git("log", "-1", "--format=%s", head), StringComparison.Ordinal);
        Assert.Empty(Git("status", "--porcelain=v1", "--untracked-files=all"));
        Assert.Empty(adapter.Untracked(root, receipts)!);
        Assert.Equal(head, adapter.Commit(root, "operator/example", start.Head, RecordFiles, receipts, carried));
        Assert.Equal(1, commits);
        adapter.Verify(root, head, start.Head, RecordFiles, carried);
        // The commit holds exactly the record and the carried records: without them it is not the original commit.
        Assert.Throws<ProviderRefusedException>(() => adapter.Verify(root, head, start.Head, RecordFiles));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("not-a-record")]
    [InlineData("own-file")]
    public void ACarriedTrailRecordWithOtherBytesOrPathsRefusesBeforeCommitting(string difference)
    {
        const string trail = "records/operation/2026-10/earlier-trail.json";
        const string trailText = "{\"earlier\":true}\n";
        Write(trail, difference == "changed" ? "{\"changed\":true}\n" : trailText);
        string path = difference switch { "not-a-record" => "docs/registers/decisions/member.md", "own-file" => RecordPath, _ => trail };
        var carried = new Dictionary<string, string>(StringComparer.Ordinal) { [path] = difference == "own-file" ? ByteHash : ContentHash.Sha256(Encoding.UTF8.GetBytes(trailText)) };
        var receipts = new HashSet<string>(StringComparer.Ordinal) { trail };
        Git("checkout", "-b", "operator/example");
        WriteRecord();
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, receipts, carried));
        Assert.Equal(basis.Head, Git("rev-parse", "HEAD"));
        Assert.Equal(0, commits);
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
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts);
        Assert.Equal(RecordPath, Git("diff-tree", "--no-commit-id", "--name-only", "-r", basis.Head, head));
        Assert.Equal(1, commits);
    }

    [Fact]
    public void ACommitThatSucceededBeforeAnInterruptedReceiptIsRecognized()
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        loseCommitReceipt = true;
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts));
        string head = Git("rev-parse", "HEAD");
        loseCommitReceipt = false;
        var reopened = new GitRecordCommitAdapter(Run);
        reopened.Branch(root, basis, "operator/example", RecordFiles);
        Assert.Equal(head, reopened.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts));
        Assert.Equal(1, commits);
    }

    [Fact]
    public void ChangedRecordBytesCannotBeCommittedOrAcceptedAsARecoveredCommit()
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        Write(RecordPath, "{}\n");
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts));
        Assert.Equal(0, commits);
        Git("add", "--", RecordPath);
        Git("commit", "-m", "A different record");
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts));
    }

    [Fact]
    public void ABranchThatMovedOrHasExtraCommitsCannotResume()
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        string head = adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts);
        Write("other.txt", "Additional content.\n");
        Git("add", "other.txt");
        Git("commit", "-m", "Another change");
        Assert.Throws<ProviderRefusedException>(() => adapter.Check(root, "operator/example", head, NoReceipts));
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, NoReceipts));
    }

    [Fact]
    public void AStagedReceiptCannotBeSmuggledIntoTheRecordCommit()
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        const string receipt = "records/operation/2026-10/receipt.json";
        Write(receipt, "{}\n");
        Git("add", "--", receipt);
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, RecordFiles, new HashSet<string>(StringComparer.Ordinal) { receipt }));
        Assert.Equal(0, commits);
        Assert.Equal(receipt, Git("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void TheCommitContainsEveryPreparedMemberAndNoReceipts()
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        var files = new Dictionary<string, string>(RecordFiles, StringComparer.Ordinal);
        foreach (string path in new[] { "docs/registers/decisions/member.md", "docs/another-register/member.md" })
        {
            const string text = "# An exact generated member\n\nRecorded terms: café.\n";
            Write(path, text);
            files.Add(path, ContentHash.Sha256(Encoding.UTF8.GetBytes(text)));
        }
        const string receipt = "records/operation/2026-10/receipt.json";
        Write(receipt, "{}\n");
        var receipts = new HashSet<string>(StringComparer.Ordinal) { receipt };
        string head = adapter.Commit(root, "operator/example", basis.Head, files, receipts);
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Git("diff-tree", "--no-commit-id", "--name-only", "-r", basis.Head, head).Split('\n'));
        Assert.All(files, file => Assert.Equal(file.Value, ContentHash.Sha256(Encoding.UTF8.GetBytes(RunGitShow(head, file.Key)))));
        Assert.Equal("?? " + receipt, Git("status", "--porcelain=v1", "--untracked-files=all"));
        Assert.Equal(head, adapter.Commit(root, "operator/example", basis.Head, files, receipts));
        Assert.Equal(1, commits);
        Git("checkout", "main");
        adapter.Verify(root, head, basis.Head, files);
        Assert.Equal("main", Git("branch", "--show-current"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("extra")]
    [InlineData("executable")]
    public void ARecoveredCommitMustContainExactlyTheRecordAndEveryMemberWithTheirOriginalBytesAndModes(string difference)
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        const string member = "docs/registers/decisions/member.md";
        const string text = "# Exact member\n";
        var files = new Dictionary<string, string>(RecordFiles, StringComparer.Ordinal)
        {
            [member] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)),
        };
        if (difference != "missing") Write(member, difference == "changed" ? "# Changed member\n" : text);
        if (difference == "extra") Write("docs/registers/decisions/extra.md", "# Extra file\n");
        Git("add", ".");
        if (difference == "executable") Git("update-index", "--chmod=+x", "--", member);
        Git("commit", "-m", "A mismatched record commit");
        string head = Git("rev-parse", "HEAD");
        Assert.Throws<ProviderRefusedException>(() => adapter.Verify(root, head, basis.Head, files));
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, files, NoReceipts));
        Assert.Equal(head, Git("rev-parse", "HEAD"));
        Assert.Equal(1, commits);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("extra")]
    public void UnexpectedMemberChangesRefuseBeforeCommitting(string difference)
    {
        adapter.Branch(root, basis, "operator/example", RecordFiles);
        WriteRecord();
        const string member = "docs/registers/decisions/member.md";
        const string text = "# Exact member\n";
        var files = new Dictionary<string, string>(RecordFiles, StringComparer.Ordinal)
        {
            [member] = ContentHash.Sha256(Encoding.UTF8.GetBytes(text)),
        };
        Write(member, difference == "changed" ? "# Changed member\n" : text);
        if (difference == "extra") Write("docs/registers/decisions/extra.md", "# Extra file\n");
        Assert.Throws<ProviderRefusedException>(() => adapter.Commit(root, "operator/example", basis.Head, files, NoReceipts));
        Assert.Equal(basis.Head, Git("rev-parse", "HEAD"));
        Assert.Equal(0, commits);
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

    [Fact]
    public void AnActionStoppedAfterItsMergeIsFinishedByTheNextActionWithoutRecreatingItsBranch()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Estate","role":"Estate","location":{"github":"Example/estate"},"visibility":"private"}""");
        estate.Add("product", estate.OperatorId, $$"""
            {"name":"Estate","slug":"estate","summary":"Test estate.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"memory-pull-requests","repository":"{{repository.Id}}","target":"main","requiredChecks":["tests"]} }
            """);
        RecordEnvelope[] milestones = [.. MilestoneKeys.Select(key =>
        {
            RecordEnvelope order = estate.Add("work-order", estate.OperatorId, $$"""{"key":"{{key}}","kind":"milestone","title":"Milestone {{key}}","intent":"Finish.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
            estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Ready."}""", order.Reference);
            return order;
        })];
        foreach (string file in Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories))
            Write(Path.GetRelativePath(estate.Root, file), File.ReadAllText(file));
        RepositoryClones.Register(estate.Estate.Id, repository.Id, root);
        using EstateService service = EstateService.Open(root);
        string device = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the operator.")).Ok);
        Git("add", ".");
        Git("commit", "-m", "Prepare the test estate");
        var remote = new RecordedRemote(Git("rev-parse", "HEAD")) { ScanPasses = true, TargetPending = true };
        service.Providers = new() { RecordCommitAdapter = adapter, PushAdapter = remote, SecretScanner = remote };
        service.Providers.Register(remote.Memory);
        OperatorRecordRequest Accept(RecordEnvelope order) => new("acceptance", estate.OperatorId,
            JsonSerializer.SerializeToElement(new { statement = "I accept this milestone.", basis = Array.Empty<string>(), self = true }),
            $"work-order:{order.Id}:{order.ComputedHash}", WaitSeconds: 0);
        OperatorRecordResponse first = service.OperatorRecord(Accept(milestones[0]), TestContext.Current.CancellationToken);
        Assert.False(first.Ok);
        Assert.Equal("pending", first.Change!.CheckStatus);
        Assert.Equal(first.Branch, Git("branch", "--show-current"));

        // As in the operator's own clone: the pull request merged, its branch was deleted, the receipts were committed separately
        // and the clone returned to main before main's run passed.
        Git("checkout", "main");
        Git("merge", "--no-ff", "--no-edit", first.Branch!);
        Git("branch", "-D", first.Branch!);
        Git("add", "records");
        Git("commit", "-m", "Commit the first action's receipts");
        Assert.Empty(Git("status", "--porcelain=v1", "--untracked-files=all"));
        string main = Git("rev-parse", "HEAD");
        remote.TargetPending = false;
        remote.Memory.SetTarget("Example/estate", new("main", remote.Memory.MergeCommit, [new("tests", remote.Memory.MergeCommit, "completed", "success")]));
        int before = commits;

        OperatorRecordResponse next = service.OperatorRecord(Accept(milestones[1]), TestContext.Current.CancellationToken);
        Assert.True(next.Ok, next.Message + " " + next.NextStep);
        Assert.Equal(before + 1, commits);
        Assert.Empty(Git("branch", "--list", first.Branch!));
        // From 0.9.3 the commit also carries what finishing the first action wrote: the observation of its merge's target run (FIX-126).
        string[] committed = Git("diff-tree", "--no-commit-id", "--name-only", "-r", main, next.Head!).Split('\n');
        Assert.Contains(next.Record!.Path, committed);
        string[] carried = [.. committed.Where(p => p != next.Record.Path)];
        Assert.NotEmpty(carried);
        EstateModel model = service.Session.Load();
        HashSet<string> merge = [.. model.Chain(model.Current("operation").Single(o => EstateModel.BodyString(o, "action") == "change-merge"
            && o.Body.GetProperty("target").GetProperty("head").GetString() == first.Head))];
        Assert.All(carried, path =>
        {
            RecordEnvelope record = Assert.Single(model.Records, r => r.ExpectedPath == path);
            JsonElement target = record.Body.GetProperty("target");
            Assert.Equal(("change-observe", "target-run"), (EstateModel.BodyString(record, "action"), target.GetProperty("observation").GetString()));
            Assert.Contains(target.GetProperty("operation").GetString()!, merge);
        });
        Assert.Equal(next.Head + " " + main, Git("rev-list", "--parents", "-n", "1", next.Head!));
        Assert.All(Git("status", "--porcelain=v1", "--untracked-files=all").Split('\n'), line => Assert.StartsWith("?? records/operation/", line, StringComparison.Ordinal));
        Assert.Null(service.OperatorUnfinished().Action);
        Assert.Equal(2, remote.Pushes);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void ACompletedActionsTrailIsReportedUntilTheNextActionCarriesItIntoItsCommit()
    {
        using var estate = new TestEstate(3);
        (EstateService service, RecordedRemote remote, RecordEnvelope[] milestones) = TwoMilestones(estate);
        using EstateService owned = service;
        OperatorRecordResponse first = service.OperatorRecord(Accept(estate, milestones[0]), TestContext.Current.CancellationToken);
        Assert.True(first.Ok, first.Message + " " + first.NextStep);
        Assert.Contains("until the next operator action carries it", first.NextStep, StringComparison.Ordinal);
        byte[] firstRecord = File.ReadAllBytes(Path.Combine(root, first.Record!.Path));

        // The first action's push and merge trail is written after its commit and stays untracked; status and the check name the action.
        string[] trail = Untracked();
        Assert.NotEmpty(trail);
        Assert.All(trail, path => Assert.StartsWith("records/operation/", path, StringComparison.Ordinal));
        FindingDto reported = Assert.Single(service.Check().Findings, f => f.Code == "operator.trail-uncommitted");
        Assert.Equal(("warning", "operator-action:" + first.Record.Id), (reported.Severity, reported.Location));
        Assert.Contains($"{trail.Length} records of the push and merge trail of operator action acceptance {first.Record.Id} of work-order:{milestones[0].Id}", reported.Message, StringComparison.Ordinal);
        StatusResponse status = service.Status();
        Assert.Contains("operator.trail-uncommitted", status.Text, StringComparison.Ordinal);
        Assert.Contains("acceptance " + first.Record.Id, status.Text, StringComparison.Ordinal);
        Assert.False(status.HasErrors, status.Text);

        // As in the operator's clone: the pull request merged and the clone returned to main, the trail still untracked beside it.
        Git("checkout", "main");
        Git("merge", "--no-ff", "--no-edit", first.Branch!);
        Git("branch", "-D", first.Branch!);
        Assert.Equal(trail, Untracked());

        // Only the settled trail travels: an unrelated untracked record still refuses the next action, which writes nothing.
        WriteOutcome unrelated = service.NewRecord(new("work-status", estate.OperatorId, JsonSerializer.SerializeToElement(new { status = "submitted", note = "Again." }),
            $"work-order:{milestones[1].Id}:{milestones[1].ComputedHash}"));
        Assert.True(unrelated.Ok, unrelated.Problem?.Message);
        int recorded = service.Session.Load().Records.Count;
        OperatorRecordResponse refused = service.OperatorRecord(Accept(estate, milestones[1]), TestContext.Current.CancellationToken);
        Assert.False(refused.Ok);
        Assert.Contains("other changes", refused.Message, StringComparison.Ordinal);
        Assert.Equal(recorded, service.Session.Load().Records.Count);
        Git("add", "--", unrelated.Written!.Path);
        Git("commit", "-m", "Commit the unrelated record");
        string main = Git("rev-parse", "HEAD");

        // The next action carries every untracked record of the first trail into its own commit; the first record stays as it was.
        OperatorRecordResponse next = service.OperatorRecord(Accept(estate, milestones[1]), TestContext.Current.CancellationToken);
        Assert.True(next.Ok, next.Message + " " + next.NextStep);
        Assert.Contains($"Its commit carried {trail.Length} trail records of earlier actions.", next.NextStep, StringComparison.Ordinal);
        Assert.Equal(new[] { next.Record!.Path }.Concat(trail).Order(StringComparer.Ordinal), Git("diff-tree", "--no-commit-id", "--name-only", "-r", main, next.Head!).Split('\n'));
        Assert.Equal(firstRecord, File.ReadAllBytes(Path.Combine(root, first.Record.Path)));
        Assert.Contains($"and carry {trail.Length} trail records of earlier actions", Git("log", "-1", "--format=%s", next.Head!), StringComparison.Ordinal);

        // Now only the next action's own trail waits, and the check names that action alone.
        string[] left = Untracked();
        Assert.NotEmpty(left);
        Assert.Empty(left.Intersect(trail));
        FindingDto now = Assert.Single(service.Check().Findings, f => f.Code == "operator.trail-uncommitted");
        Assert.Contains("acceptance " + next.Record.Id, now.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Record.Id, now.Message, StringComparison.Ordinal);
        Assert.Equal(2, remote.Pushes);
        Assert.Equal(0, service.Validate().Errors);

        // The carried records validate in the commit itself, beside the record they followed.
        string checkout = Path.Combine(scratch, "checked-commit");
        Git("worktree", "add", "--detach", checkout, next.Head!);
        FindingList findings = EstateChecks.Run(EstateLoader.Load(checkout));
        Assert.False(findings.HasErrors, string.Join('\n', findings));
    }

    [Fact]
    public void AnInterruptedCommitResumesWithTheTrailItPinnedToCarry()
    {
        using var estate = new TestEstate(3);
        (EstateService service, RecordedRemote remote, RecordEnvelope[] milestones) = TwoMilestones(estate);
        using EstateService owned = service;
        Assert.True(service.OperatorRecord(Accept(estate, milestones[0]), TestContext.Current.CancellationToken).Ok);
        string[] trail = Untracked();
        string before = Git("rev-parse", "HEAD");
        int committed = commits;
        loseCommitReceipt = true;
        OperatorRecordResponse first = service.OperatorRecord(Accept(estate, milestones[1]), TestContext.Current.CancellationToken);
        Assert.False(first.Ok);
        Assert.Equal(committed + 1, commits);
        loseCommitReceipt = false;
        service.Dispose();
        using EstateService reopened = EstateService.Open(root);
        reopened.Providers = new() { RecordCommitAdapter = adapter, PushAdapter = remote, SecretScanner = remote };
        reopened.Providers.Register(remote.Memory);
        OperatorRecordResponse resumed = reopened.OperatorRecord(Accept(estate, milestones[1]), TestContext.Current.CancellationToken);
        Assert.True(resumed.Ok, resumed.Message + " " + resumed.NextStep);
        Assert.Equal(committed + 1, commits);
        Assert.Equal(new[] { resumed.Record!.Path }.Concat(trail).Order(StringComparer.Ordinal), Git("diff-tree", "--no-commit-id", "--name-only", "-r", before, resumed.Head!).Split('\n'));
        Assert.Equal(2, remote.Pushes);
    }

    // An estate with two submitted milestones, committed in the repository, the operator's device admitted and a remote whose scans pass.
    private (EstateService Service, RecordedRemote Remote, RecordEnvelope[] Milestones) TwoMilestones(TestEstate estate)
    {
        Git("rm", "--", "readme.txt");
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Estate","role":"Estate","location":{"github":"Example/estate"},"visibility":"private"}""");
        estate.Add("product", estate.OperatorId, $$"""
            {"name":"Estate","slug":"estate","summary":"Test estate.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"memory-pull-requests","repository":"{{repository.Id}}","target":"main","requiredChecks":["tests"]} }
            """);
        RecordEnvelope[] milestones = [.. MilestoneKeys.Select(key =>
        {
            RecordEnvelope order = estate.Add("work-order", estate.OperatorId, $$"""{"key":"{{key}}","kind":"milestone","title":"Milestone {{key}}","intent":"Finish.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["result"]}""");
            estate.Add("work-status", estate.OperatorId, """{"status":"submitted","note":"Ready."}""", order.Reference);
            return order;
        })];
        foreach (string file in Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories))
            Write(Path.GetRelativePath(estate.Root, file), File.ReadAllText(file));
        RepositoryClones.Register(estate.Estate.Id, repository.Id, root);
        EstateService service = EstateService.Open(root);
        string device = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the operator.")).Ok);
        Git("add", ".");
        Git("commit", "-m", "Prepare the test estate");
        var remote = new RecordedRemote(Git("rev-parse", "HEAD")) { ScanPasses = true };
        service.Providers = new() { RecordCommitAdapter = adapter, PushAdapter = remote, SecretScanner = remote };
        service.Providers.Register(remote.Memory);
        return (service, remote, milestones);
    }

    private static OperatorRecordRequest Accept(TestEstate estate, RecordEnvelope order) => new("acceptance", estate.OperatorId,
        JsonSerializer.SerializeToElement(new { statement = "I accept this milestone.", basis = Array.Empty<string>(), self = true }),
        $"work-order:{order.Id}:{order.ComputedHash}", WaitSeconds: 0);

    // The working tree's changes, each an untracked file, in path order.
    private string[] Untracked() => [.. Git("status", "--porcelain=v1", "--untracked-files=all").Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line =>
        {
            Assert.StartsWith("?? ", line, StringComparison.Ordinal);
            return line[3..];
        }).Order(StringComparer.Ordinal)];

    [Theory]
    [InlineData("decision", "branch")]
    [InlineData("release", "branch")]
    [InlineData("decision", "scan")]
    [InlineData("release", "scan")]
    [InlineData("decision", "commit")]
    [InlineData("release", "commit")]
    [InlineData("decision", "members")]
    [InlineData("release", "members")]
    public void DeclaredRegistersTravelWithTheOperatorRecordAndResumeWithTheSameBytes(string type, string interruption)
    {
        using var estate = new TestEstate(3);
        Git("rm", "--", "readme.txt");
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Estate","role":"Estate","location":{"github":"Example/estate"},"visibility":"private"}""");
        RecordEnvelope product = estate.Add("product", estate.OperatorId, $$"""
            {"name":"Estate","slug":"estate","summary":"Test estate.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
             "changes":{"provider":"memory-pull-requests","repository":"{{repository.Id}}","target":"main","requiredChecks":["tests"]} }
            """);
        foreach (string file in Directory.EnumerateFiles(estate.Root, "*", SearchOption.AllDirectories))
            Write(Path.GetRelativePath(estate.Root, file), File.ReadAllText(file));
        RepositoryClones.Register(estate.Estate.Id, repository.Id, root);
        using EstateService service = EstateService.Open(root);
        string device = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the operator.")).Ok);
        foreach (string register in new[] { "decisions", "releases" })
        {
            WriteOutcome declaration = service.NewRecord(new("living-document", estate.OperatorId, JsonSerializer.Deserialize<JsonElement>($$"""
                {"pathPattern":"docs/registers/{{register}}/{month}/{date}-{slug}-{id}.md","title":"Recorded {{register}}","kind":"generated","source":"estate-file",
                 "generator":{"id":"vericairn.records/{{register}}","inputs":["format=1","audience=Internal","selection=all-revisions"]},
                 "triggers":[],"checks":["matches-generator","links-resolve"],"summary":"One file per exact revision."}
                """)));
            Assert.True(declaration.Ok, declaration.Problem?.Message);
        }
        WriteOutcome additional = service.NewRecord(new("living-document", estate.OperatorId, JsonSerializer.Deserialize<JsonElement>($$"""
            {"pathPattern":"{{ExtraRegister.Descriptor.RequiredPattern}}","title":"Operator revisions","kind":"generated","source":"estate-file",
             "generator":{"id":"{{ExtraRegister.Id}}","inputs":["format=1","audience=Internal","selection=all-revisions"]},
             "triggers":[],"checks":["matches-generator","links-resolve"],"summary":"An additional register of the same exact records."}
            """)));
        Assert.True(additional.Ok, additional.Problem?.Message);
        Git("add", ".");
        Git("commit", "-m", "Prepare declared registers");
        string before = Git("rev-parse", "HEAD");
        commits = 0;
        var remote = new RecordedRemote(before);
        service.Providers = new() { RecordCommitAdapter = adapter, PushAdapter = remote, SecretScanner = remote };
        service.Providers.Register(remote.Memory);
        string body = type == "decision" ? $$"""
            {"title":"A recorded decision","stake":"low","decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-20T12:00:00Z",
             "statement":"Keep the recorded terms.","summary":"A test decision.","synthesis":"An exact revision.","effects":["Record the terms."],"affects":[],"notAuthorized":[]}
            """ : $$"""
            {"version":"1.0.0","status":"candidate","repository":"Example/estate","commit":"{{before}}",
             "artifacts":[{"name":"example.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],
             "recordFormat":{"reads":[1,2,3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}
            """;
        var request = new OperatorRecordRequest(type, estate.OperatorId, JsonSerializer.Deserialize<JsonElement>(body),
            type == "release" ? $"product:{product.Id}:{product.ComputedHash}" : null, WaitSeconds: 0);
        if (interruption == "branch") refuseBranch = true;
        if (interruption == "commit") loseCommitReceipt = true;
        int publications = 0;
        if (interruption == "members") service.BeforePublishingDocuments = _ => { if (++publications == 2) throw new IOException("Interrupted member publication."); };
        OperatorRecordResponse first = service.OperatorRecord(request, TestContext.Current.CancellationToken);
        Assert.False(first.Ok);
        Assert.NotNull(first.Record);
        if (interruption == "scan") Assert.Equal("known-failure", first.Change!.Status);
        else Assert.Null(first.Change);
        Assert.Equal(0, remote.Pushes);
        EstateModel model = service.Session.Load();
        RecordEnvelope declarationForType = model.Current("living-document")
            .Single(d => d.Body.GetProperty("generator").GetProperty("id").GetString() == "vericairn.records/" + (type == "decision" ? "decisions" : "releases"));
        GeneratedDocumentPlan plan = GeneratedDocumentVerification.Plan(model, declarationForType);
        DocumentFile member = Assert.Single(plan.Expected);
        DocumentFile extra = Assert.Single(GeneratedDocumentVerification.Plan(model, model.ById[additional.Written!.Id]).Expected);
        if (interruption == "members")
        {
            Assert.Equal(2, publications);
            Assert.True(Assert.Single(BatchJournal.All(service.Session).Journals).HasOutputs);
            Assert.Single(new[] { member, extra }, f => File.Exists(Path.Combine(root, f.Path)));
            Assert.Equal(0, commits);
        }
        else
        {
            Assert.True(plan.Current, string.Join('\n', plan.Problems));
            Assert.Equal(0, service.Validate().Errors);
            Assert.Equal(interruption == "branch" ? 0 : 1, commits);
        }
        byte[] original = File.ReadAllBytes(Path.Combine(root, first.Record.Path));
        byte[] originalMember = member.Bytes.ToArray();
        string originalHead = Git("rev-parse", "HEAD");
        service.Dispose();
        using EstateService reopened = EstateService.Open(root);
        reopened.Providers = new() { RecordCommitAdapter = adapter, PushAdapter = remote, SecretScanner = remote };
        reopened.Providers.Register(remote.Memory);
        remote.ScanPasses = true;
        loseCommitReceipt = false;
        refuseBranch = false;
        OperatorRecordResponse resumed = reopened.OperatorRecord(request with { Reconciliation = "The interrupted local step is recovered; the scanner is available." }, TestContext.Current.CancellationToken);
        Assert.True(resumed.Ok, resumed.Message + " " + resumed.NextStep);
        Assert.Equal(first.Record, resumed.Record);
        if (interruption is not ("members" or "branch")) Assert.Equal(originalHead, resumed.Head);
        if (interruption == "scan") Assert.Equal(first.Head, resumed.Head);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, first.Record.Path)));
        Assert.Equal(originalMember, File.ReadAllBytes(Path.Combine(root, member.Path)));
        Assert.Equal(extra.Bytes.ToArray(), File.ReadAllBytes(Path.Combine(root, extra.Path)));
        Assert.Equal(device, Assert.Single(reopened.Session.Load().ById[first.Record.Id].Signatures).Device);
        Assert.Single(reopened.Session.Load().OfType(type));
        Assert.Empty(BatchJournal.All(reopened.Session).Journals);
        Assert.Equal(1, commits);
        Assert.Equal(1, remote.Pushes);
        Assert.Equal(2, remote.Memory.Effects);
        Assert.Equal(0, reopened.Validate().Errors);
        string[] expected = new[] { first.Record.Path, member.Path, extra.Path }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, Git("diff-tree", "--no-commit-id", "--name-only", "-r", before, resumed.Head!).Split('\n'));
        Assert.Equal(member.Hash, ContentHash.Sha256(Encoding.UTF8.GetBytes(RunGitShow(resumed.Head!, member.Path))));
        Assert.Equal(extra.Hash, ContentHash.Sha256(Encoding.UTF8.GetBytes(RunGitShow(resumed.Head!, extra.Path))));
        string checkout = Path.Combine(scratch, "checked-commit");
        Git("worktree", "add", "--detach", checkout, resumed.Head!);
        FindingList findings = EstateChecks.Run(EstateLoader.Load(checkout));
        Assert.False(findings.HasErrors, string.Join('\n', findings));
        OperatorRecordResponse repeated = reopened.OperatorRecord(request, TestContext.Current.CancellationToken);
        Assert.True(repeated.Ok, repeated.Message);
        Assert.Equal(resumed.Head, repeated.Head);
        Assert.Equal(1, commits);
        Write(member.Path, "Changed member.\n");
        OperatorRecordResponse changed = reopened.OperatorRecord(request, TestContext.Current.CancellationToken);
        Assert.False(changed.Ok);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, first.Record.Path)));
        Assert.Equal("Changed member.\n", File.ReadAllText(Path.Combine(root, member.Path)));
        Assert.Equal(1, commits);
        Assert.Equal(1, remote.Pushes);
        Assert.Equal(2, remote.Memory.Effects);
    }

    private static AdditionalRegister InstallRegister()
    {
        var generator = new AdditionalRegister();
        DocumentGenerators.Register(generator);
        return generator;
    }

    private sealed class AdditionalRegister : IDocumentGenerator
    {
        public string Id => "example.operator/records";
        public DocumentGeneratorDescriptor Descriptor { get; } = DocumentGenerators.Find("vericairn.records/decisions")!.Descriptor! with
        {
            Id = "example.operator/records", RecordTypes = ["decision", "release"],
            RequiredPattern = "docs/registers/operator/{month}/{date}-{slug}-{id}.md",
        };
        public DocumentRenderResult Generate(DocumentGenerationContext context) => DocumentGenerators.Find(
            "vericairn.records/" + (context.Sources[0].RecordType == "decision" ? "decisions" : "releases"))!.Generate(context);
    }

    private string RunGitShow(string head, string path)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "show", head + ":" + path }) info.ArgumentList.Add(argument);
        ProcessResult result = Run(info);
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput;
    }

    private sealed class RecordedRemote(string basis) : IPushAdapter, ISecretScanner
    {
        internal MemoryPullRequestProvider Memory { get; } = new();
        internal bool ScanPasses { get; set; }
        internal bool TargetPending { get; set; }
        internal int Pushes { get; private set; }
        private string? pushed;
        public PushPlan Prepare(string directory, string remote, string branch, string target, string head)
        {
            Memory.Checks = [new("tests", head, "completed", "success")];
            Memory.SetTarget("Example/estate", new("main", Memory.MergeCommit, [TargetPending
                ? new("tests", Memory.MergeCommit, "in_progress", null) : new("tests", Memory.MergeCommit, "completed", "success")]));
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
        if (refuseBranch && args.Contains("checkout")) return new(ProcessOutcome.Exited, true, 1, string.Empty, string.Empty);
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
