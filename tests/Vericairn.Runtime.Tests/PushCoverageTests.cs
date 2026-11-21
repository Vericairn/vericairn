// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-154 (design/checked-changes.md §§ 6 and 16): a checked push covers every commit its target does not hold, from the newest complete
/// recorded push of the branch that the remote branch still holds or else the merge base, and a checked merge takes only a complete push of
/// its exact head. Each case pushes real commits through the git adapter to a local bare repository; the scanner finds the commits a case
/// plants, or, where an installed gitleaks is named, scans with it.
/// </summary>
public sealed class PushCoverageTests : IDisposable
{
    private const string Branch = "work/change";
    private const string Destination = "Example/widget";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "vericairn-coverage-" + Guid.NewGuid().ToString("N"));
    private readonly string clone;
    private readonly string remote;
    private readonly string configuration;
    private readonly string hooks;
    private readonly TestEstate estate = new(recordFormat: 3);
    private readonly RecordEnvelope repository;
    private RecordEnvelope product;
    private readonly LocalRemote provider;
    private readonly PlantedScanner planted;
    private EstateService? service;

    public PushCoverageTests()
    {
        TestPrerequisites.RequireGit();
        clone = Path.Combine(directory, "work");
        remote = Path.Combine(directory, "remote.git");
        configuration = Path.Combine(directory, "gitconfig");
        hooks = Path.Combine(directory, "hooks");
        Directory.CreateDirectory(clone);
        Directory.CreateDirectory(hooks);
        File.WriteAllText(configuration, string.Empty);
        repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Widget","role":"Source","location":{"github":"Example/widget"},"visibility":"private"}""");
        product = estate.Add("product", estate.OperatorId, ProductBody("main"));
        RepositoryClones.Register(estate.Estate.Id, repository.Id, clone);
        provider = new LocalRemote(remote);
        planted = new PlantedScanner(this);
        Git(clone, "init", "--initial-branch=main");
        Commit("readme.txt", "Initial content.\n", "Initial content");
        Git(directory, "clone", "--bare", "--", clone, remote);
        Git(clone, "checkout", "-b", Branch);
    }

    private EstateService Service => service ??= Open(planted);

    // J157: a commit holding a private key reaches the branch around the guard, and a release before 0.9.4 recorded a checked push of
    // that head from the remote head, a scan of nothing. The checked merge refuses that head, whose only recorded push was not complete;
    // pushing it again is scanned from the merge base and refused; and the next checked push of a later head scans from the merge base
    // and refuses it too, leaving the remote branch where it was.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void J157_ACommitPushedAroundTheGuardIsScannedByTheNextCheckedPushAndBlocksTheMerge(bool installed)
    {
        ISecretScanner scanner = installed ? Installed() : planted;
        string key = Commit("deploy.pem", PrivateKey(), "Add deployment material");
        planted.Secrets.Add(key);
        Git(clone, "push", "--no-verify", remote, Branch);
        RecordEnvelope? incomplete = null;
        Reopen(() => incomplete = RecordedPush(key, key, key, MergeBase()), scanner);

        ChangeResponse merge = Merge(key);
        Assert.False(merge.Ok, merge.Message);
        Assert.Contains("No complete checked push of this exact head", merge.Message, StringComparison.Ordinal);
        Assert.Null(provider.MergedHead);

        ChangeResponse again = Service.ChangesPush(Push(key));
        Assert.False(again.Ok, again.Message);
        Assert.EndsWith(":after:" + incomplete!.Id, again.Identity, StringComparison.Ordinal);
        RecordEnvelope scanned = Current(again);
        Assert.Null(Target(scanned, "basis"));
        Assert.Equal(MergeBase() + ".." + key, scanned.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.Equal("findings", scanned.Body.GetProperty("scan").GetProperty("verdict").GetString());

        string later = Commit("notes.txt", "A later, clean change.\n", "Add notes");
        ChangeResponse next = Service.ChangesPush(Push(later));
        Assert.False(next.Ok, next.Message);
        Assert.Equal("known-failure", next.Status);
        RecordEnvelope refused = Current(next);
        Assert.Equal(MergeBase(), Target(refused, "base"));
        Assert.Null(Target(refused, "basis"));
        Assert.Equal("findings", refused.Body.GetProperty("scan").GetProperty("verdict").GetString());
        Assert.Equal(key, RemoteHead());
        Assert.Null(provider.MergedHead);
        Assert.Equal(0, Service.Validate().Errors);
    }

    [Fact]
    public void APushContinuesFromTheNewestCompletePushTheRemoteBranchStillHoldsAndCoversWhatArrivedSince()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        RecordEnvelope one = Pushed(first);
        Assert.Equal("merge-base", Target(one, "basis"));
        string second = Commit("two.txt", "Two.\n", "Add two");
        RecordEnvelope two = Pushed(second);
        Assert.Equal(one.Id, Target(two, "basis"));
        Assert.Equal(first, Target(two, "base"));

        // A plain push lands a commit on the branch outside checked changes; the next checked push still scans it.
        string slipped = Commit("slipped.txt", "Slipped.\n", "Add slipped");
        Git(clone, "push", "--no-verify", remote, Branch);
        string head = Commit("three.txt", "Three.\n", "Add three");
        RecordEnvelope three = Pushed(head);
        Assert.Equal(two.Id, Target(three, "basis"));
        Assert.Equal(second + ".." + head, three.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.Contains(planted.Scanned, range => range == second + ".." + head);
        Assert.True(Ancestor(slipped, head) && !Ancestor(slipped, second));
        ChangeResponse merged = Merge(head);
        Assert.True(merged.Ok, merged.Message);
        Assert.Equal(head, provider.MergedHead);
        Assert.Equal(0, Service.Validate().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARewrittenRemoteBranchFallsBackToTheNewestCompletePushItStillHoldsOrTheMergeBase(bool keepsFirst)
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        RecordEnvelope one = Pushed(first);
        string second = Commit("two.txt", "Two.\n", "Add two");
        Pushed(second);

        // The remote branch is rewritten outside checked changes: to a commit beside the first push, or beside the very start.
        Git(clone, "reset", "--hard", keepsFirst ? first : MergeBase());
        string rewritten = Commit("other.txt", "Rewritten.\n", "Rewrite the branch");
        planted.Secrets.Add(rewritten);
        Git(clone, "push", "--force", "--no-verify", remote, Branch);
        string head = Commit("three.txt", "Three.\n", "Add three");
        ChangeResponse pushed = Service.ChangesPush(Push(head));
        Assert.False(pushed.Ok, pushed.Message);
        RecordEnvelope refused = Current(pushed);
        Assert.True(one.Id.Length > 0);
        Assert.Equal(keepsFirst ? first : MergeBase(), Target(refused, "base"));
        Assert.Equal((keepsFirst ? first : MergeBase()) + ".." + head, refused.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.Equal(rewritten, RemoteHead());
    }

    [Fact]
    public void ABranchWhoseLastPushWasIncompleteIsScannedFromTheCompletePushBeforeIt()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        string slipped = Commit("slipped.txt", "Slipped.\n", "Add slipped");
        Git(clone, "push", "--no-verify", remote, Branch);
        string second = Commit("two.txt", "Two.\n", "Add two");
        Git(clone, "push", "--no-verify", remote, Branch);
        // Two pushes a release before 0.9.4 recorded: the branch's first, from the merge base, complete; then one from a remote head
        // that a plain push left, which scanned only the commit after it.
        RecordEnvelope? complete = null;
        Reopen(() =>
        {
            complete = RecordedPush(first, MergeBase(), string.Empty, MergeBase());
            RecordedPush(second, slipped, slipped, MergeBase());
        });
        Assert.False(Merge(second).Ok);

        string head = Commit("three.txt", "Three.\n", "Add three");
        RecordEnvelope three = Pushed(head);
        Assert.Equal(complete!.Id, Target(three, "basis"));
        Assert.Equal(first + ".." + head, three.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.True(Ancestor(slipped, head) && !Ancestor(slipped, first));
        Assert.Equal(0, Service.Validate().Errors);
    }

    [Fact]
    public void AnOlderPushWithoutTheFieldIsCompleteWhenItsBranchWasNewOrItContinuedACompletePush()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        Git(clone, "push", "--no-verify", remote, Branch);
        string second = Commit("two.txt", "Two.\n", "Add two");
        Git(clone, "push", "--no-verify", remote, Branch);
        Reopen(() =>
        {
            RecordedPush(first, MergeBase(), string.Empty, MergeBase());
            RecordedPush(second, first, first, MergeBase());
        });
        ChangeResponse merged = Merge(second);
        Assert.True(merged.Ok, merged.Message);
        Assert.Equal(second, provider.MergedHead);
    }

    [Fact]
    public void AHeadWhoseOnlyPushWasIncompleteIsPushedAgainFromTheMergeBaseAndThenMerges()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        string head = Commit("two.txt", "Two.\n", "Add two");
        Git(clone, "push", "--no-verify", remote, Branch);
        RecordEnvelope? incomplete = null;
        Reopen(() => incomplete = RecordedPush(head, head, head, MergeBase()));
        Assert.False(Merge(head).Ok);

        ChangeResponse again = Service.ChangesPush(Push(head));
        Assert.True(again.Ok, again.Message);
        RecordEnvelope pushed = Current(again);
        Assert.EndsWith(":after:" + incomplete!.Id, again.Identity, StringComparison.Ordinal);
        Assert.Equal("merge-base", Target(pushed, "basis"));
        Assert.Equal(MergeBase() + ".." + head, pushed.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.True(Ancestor(first, head));
        ChangeResponse merged = Merge(head, "The head's complete push is recorded now.");
        Assert.True(merged.Ok, merged.Message);
        Assert.Equal(head, provider.MergedHead);
        // A further push of the same head is the complete push already recorded, never a second effect.
        Assert.False(Service.ChangesPush(Push(head)).Ok);
        Assert.Equal(0, Service.Validate().Errors);
    }

    [Fact]
    public void ARetryAfterAKnownFailureFindsItsBaseAgain()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        string head = Commit("two.txt", "Two.\n", "Add two");
        planted.Unavailable = true;
        ChangeResponse failed = Service.ChangesPush(Push(head));
        Assert.Equal("known-failure", failed.Status);
        // Refused before its dispatch, the push recorded no basis, so its retry may record one.
        Assert.Null(Target(Current(failed), "basis"));
        planted.Unavailable = false;

        // A complete push of an earlier head of the branch lands meanwhile; the retry continues from it.
        Git(clone, "checkout", "-q", "-B", Branch, first);
        RecordEnvelope one = Pushed(first);
        Git(clone, "checkout", "-q", "-B", Branch, head);
        ChangeResponse retried = Service.ChangesPush(Push(head) with { Reconciliation = "The scanner is installed again; nothing was pushed." });
        Assert.True(retried.Ok, retried.Message);
        Assert.Equal(failed.Identity, retried.Identity);
        Assert.Equal(one.Id, Target(Current(retried), "basis"));
        Assert.Equal(first, Target(Current(retried), "base"));
        Assert.Equal(0, Service.Validate().Errors);
    }

    [Fact]
    public void APushToTheDeclaredTargetIsRefusedBeforeAnythingIsRecorded()
    {
        Git(clone, "checkout", "-q", "main");
        string initial = MergeBase();
        string head = Commit("direct.txt", "Direct.\n", "A direct change");
        int before = Service.Session.Load().OfType("operation").Count();
        ChangeResponse response = Service.ChangesPush(new(product.Id, "main", head, estate.OperatorId));
        Assert.False(response.Ok);
        Assert.Equal("change.target-push", response.Problem?.Code);
        Assert.Contains("change.target-push", response.Message, StringComparison.Ordinal);
        Assert.Equal(before, Service.Session.Load().OfType("operation").Count());
        Assert.Empty(planted.Scanned);
        Assert.Equal(initial, Git(remote, "rev-parse", "refs/heads/main"));
        // The adapter refuses it too, whatever calls it.
        Assert.Throws<ProviderRefusedException>(() => Adapter().Prepare(clone, remote, "main", "main", head, []));
    }

    // The Reviewer's probe: a push complete against the target the declaration named then counts for nothing once the operator moves the
    // declaration to another target. dev holds a commit main does not hold, which no push scanned; a branch off dev is pushed while dev is
    // the target; the declaration moves to main; the next head's push scans from the merge base with main and is refused, and neither
    // head merges into main, the first on its old push included.
    [Fact]
    public void APushCompleteAgainstAnEarlierTargetCountsForNothingOnceTheDeclarationMoves()
    {
        Git(clone, "checkout", "-q", "-b", "dev", "main");
        string devOnly = Commit("dev.txt", "Only dev holds this.\n", "A change only dev holds");
        planted.Secrets.Add(devOnly);
        Git(clone, "push", "-q", "--no-verify", remote, "dev");
        Reopen(() => product = estate.Add("product", estate.OperatorId, ProductBody("dev"), supersedes: product.Id));
        Git(clone, "checkout", "-q", "-B", Branch, "dev");
        string first = Commit("one.txt", "One.\n", "Add one");
        RecordEnvelope one = Pushed(first);
        Assert.Equal(devOnly, Target(one, "base"));
        Assert.Equal("dev", Target(one, "target"));

        Reopen(() => product = estate.Add("product", estate.OperatorId, ProductBody("main"), supersedes: product.Id));
        string second = Commit("two.txt", "Two.\n", "Add two");
        ChangeResponse next = Service.ChangesPush(Push(second));
        Assert.False(next.Ok, next.Message);
        RecordEnvelope refused = Current(next);
        Assert.Equal(MergeBase(), Target(refused, "base"));
        Assert.Equal("findings", refused.Body.GetProperty("scan").GetProperty("verdict").GetString());
        Assert.Equal(first, RemoteHead());

        ChangeResponse merge = Merge(second);
        Assert.False(merge.Ok, merge.Message);
        Assert.Contains("No complete checked push of this exact head", merge.Message, StringComparison.Ordinal);
        ChangeResponse old = Merge(first);
        Assert.False(old.Ok, old.Message);
        Assert.Contains("No complete checked push of this exact head", old.Message, StringComparison.Ordinal);
        Assert.Null(provider.MergedHead);
        Assert.Equal(0, Service.Validate().Errors);
    }

    // Pushes whose bases name each other, one by the older rule and one by its recorded basis, rest on nothing: neither is complete,
    // neither head merges, and the next push scans from the merge base.
    [Fact]
    public void ACycleOfBasisReferencesIsNotComplete()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        string second = Commit("two.txt", "Two.\n", "Add two");
        Git(clone, "push", "-q", "--no-verify", remote, Branch);
        Reopen(() =>
        {
            RecordEnvelope a = RecordedPush(second, first, first, MergeBase());
            RecordedPush(first, second, second, MergeBase(), a.Id);
        });
        Assert.False(Merge(second).Ok);
        Assert.False(Merge(first).Ok);
        Assert.Null(provider.MergedHead);
        string head = Commit("three.txt", "Three.\n", "Add three");
        RecordEnvelope three = Pushed(head);
        Assert.Equal("merge-base", Target(three, "basis"));
        Assert.Equal(MergeBase() + ".." + head, three.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.Equal(0, Service.Validate().Errors);
    }

    // A push offers its adapter the sixteen newest complete pushes' heads: an older complete push the remote branch still holds is not
    // among them, so the scan starts at the merge base.
    [Fact]
    public void OnlyTheSixteenNewestCompletePushesAreOffered()
    {
        string[] heads = [.. Enumerable.Range(1, 17).Select(i => Commit($"file{i}.txt", $"Content {i}.\n", $"Add file {i}"))];
        var pushes = new List<RecordEnvelope>();
        Reopen(() =>
        {
            for (int i = 0; i < heads.Length; i++)
                pushes.Add(i == 0 ? RecordedPush(heads[0], MergeBase(), string.Empty, MergeBase()) : RecordedPush(heads[i], heads[i - 1], heads[i - 1], MergeBase()));
        });
        IReadOnlyList<Changes.PushCoverage.Covered> covered = Changes.PushCoverage.Covering(Service.Session.Load(), repository.Id, Destination, Branch, "main");
        Assert.Equal(16, covered.Count);
        Assert.Equal(heads[16], covered[0].Head);
        Assert.DoesNotContain(covered, c => c.Operation == pushes[0].Id);
        Assert.True(Changes.PushCoverage.Complete(Service.Session.Load(), pushes[0], "main"));

        // The remote branch is rewritten to hold only the oldest of them.
        Git(clone, "checkout", "-q", "-B", Branch, heads[0]);
        Commit("rewritten.txt", "Rewritten.\n", "Rewrite the branch");
        Git(clone, "push", "-q", "--force", "--no-verify", remote, Branch);
        string head = Commit("next.txt", "Next.\n", "Add next");
        RecordEnvelope pushed = Pushed(head);
        Assert.Equal("merge-base", Target(pushed, "basis"));
        Assert.Equal(MergeBase() + ".." + head, pushed.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.Equal(0, Service.Validate().Errors);
    }

    // A branch that takes in a newer target is scanned from its last complete push, which its merge descends from; a branch never pushed
    // that takes in the target is scanned from the merge base, which is then the target's newer head, so none of the target's commits.
    [Fact]
    public void AMergeOfTheTargetIntoTheBranchIsCoveredFromItsLastCompletePushOrTheMergeBase()
    {
        string first = Commit("one.txt", "One.\n", "Add one");
        RecordEnvelope one = Pushed(first);
        Git(clone, "checkout", "-q", "main");
        string landed = Commit("main.txt", "Landed.\n", "Land another change");
        Git(clone, "push", "-q", "--no-verify", remote, "main");
        Git(clone, "checkout", "-q", Branch);
        Git(clone, "merge", "-q", "--no-ff", "-m", "Take in main", "main");
        string merged = Git(clone, "rev-parse", "HEAD");
        RecordEnvelope two = Pushed(merged);
        Assert.Equal(one.Id, Target(two, "basis"));
        Assert.Equal(first + ".." + merged, two.Body.GetProperty("scan").GetProperty("range").GetString());
        Assert.Equal(landed, Target(two, "targetHead"));
        ChangeResponse merge = Merge(merged);
        Assert.True(merge.Ok, merge.Message);

        Git(clone, "checkout", "-q", "-b", "work/fresh", Git(clone, "rev-parse", "main~1"));
        Commit("fresh.txt", "Fresh.\n", "Add fresh");
        Git(clone, "merge", "-q", "--no-ff", "-m", "Take in main", "main");
        string fresh = Git(clone, "rev-parse", "HEAD");
        ChangeResponse pushed = Service.ChangesPush(new(product.Id, "work/fresh", fresh, estate.OperatorId));
        Assert.True(pushed.Ok, pushed.Message);
        Assert.Equal(landed, Target(Current(pushed), "base"));
        Assert.Equal("merge-base", Target(Current(pushed), "basis"));
        Assert.Equal(0, Service.Validate().Errors);
    }

    private string ProductBody(string target) => $$"""
        {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],
         "changes":{"provider":"memory-pull-requests","repository":"{{repository.Id}}","target":"{{target}}","requiredChecks":["tests"]} }
        """;

    private ChangesPushRequest Push(string head) => new(product.Id, Branch, head, estate.OperatorId);

    // Pushes the head through checked changes, which must pass, and returns the push's current record.
    private RecordEnvelope Pushed(string head)
    {
        ChangeResponse response = Service.ChangesPush(Push(head));
        Assert.True(response.Ok, response.Message);
        return Current(response);
    }

    // Opens and merges the head's pull request through checked changes, its required check passing at the head.
    private ChangeResponse Merge(string head, string? reconciliation = null)
    {
        provider.Memory.Checks = [new("tests", head, "completed", "success")];
        provider.Memory.SetTarget(Destination, new("main", provider.Memory.MergeCommit, [new("tests", provider.Memory.MergeCommit, "completed", "success")]));
        provider.Memory.Set(Destination, new("1", Branch, "main", head, "open", null, provider.Memory.Checks));
        return Service.ChangesMerge(new(product.Id, "1", head, estate.OperatorId, reconciliation));
    }

    private RecordEnvelope Current(ChangeResponse response)
    {
        EstateModel model = Service.Session.Load();
        return model.Latest(model.ById[response.Operation!.Id]);
    }

    private static string? Target(RecordEnvelope record, string name) =>
        record.Body.GetProperty("target").TryGetProperty(name, out var value) ? value.GetString() : null;

    private string MergeBase() => Git(clone, "rev-parse", "main");

    private string? RemoteHead()
    {
        string listed = Git(remote, "for-each-ref", "--format=%(objectname)", "refs/heads/" + Branch);
        return listed.Length == 0 ? null : listed;
    }

    private bool Ancestor(string ancestor, string commit) =>
        Run(Info(clone, "merge-base", "--is-ancestor", ancestor, commit)) is { Outcome: ProcessOutcome.Exited, ExitCode: 0 };

    // A checked push of the branch as a release before 0.9.4 recorded it: no basis, its base the remote head or, for a new branch, the
    // merge base, its scan passed over exactly that range, observed at the remote. Written before the service opens.
    private RecordEnvelope RecordedPush(string head, string basis, string remoteHead, string targetHead, string? recordedBasis = null)
    {
        var body = new JsonObject
        {
            ["provider"] = "memory-pull-requests", ["action"] = "change-push",
            ["identity"] = $"memory-pull-requests:{repository.Id}:change-push:{Branch}:{head}:{product.ComputedHash[7..]}",
            ["intent"] = "change-push at the pinned head, with the declared repository and checks.", ["status"] = "not-started",
            ["target"] = new JsonObject
            {
                ["product"] = product.Id, ["productHash"] = product.ComputedHash, ["repository"] = repository.Id, ["repositoryHash"] = repository.ComputedHash,
                ["destination"] = Destination, ["target"] = "main", ["head"] = head, ["branch"] = Branch, ["base"] = basis, ["remoteHead"] = remoteHead, ["targetHead"] = targetHead,
            },
            ["requiredChecks"] = new JsonArray("tests"), ["evidence"] = new JsonArray(),
        };
        if (recordedBasis is not null) body["target"]!["basis"] = recordedBasis;
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, body.ToJsonString(), product.Reference);
        body["status"] = "dispatched";
        body["dispatchedAt"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1));
        body["scan"] = new JsonObject { ["tool"] = "gitleaks", ["version"] = "8.30.1", ["range"] = basis + ".." + head, ["verdict"] = "passed", ["findingCount"] = 0 };
        RecordEnvelope dispatched = estate.Add("operation", estate.OperatorId, body.ToJsonString(), product.Reference, supersedes: intent.Id);
        body["status"] = "observed-success";
        body["observed"] = new JsonObject { ["summary"] = "The scanned commit was observed at the remote branch.", ["at"] = RecordParser.FormatTimestamp(estate.Clock.AddSeconds(1)) };
        return estate.Add("operation", estate.OperatorId, body.ToJsonString(), product.Reference, supersedes: dispatched.Id);
    }

    private void Reopen(Action write, ISecretScanner? scanner = null)
    {
        scanner ??= service?.Providers.SecretScanner ?? planted;
        service?.Dispose();
        service = null;
        write();
        service = Open(scanner);
    }

    private EstateService Open(ISecretScanner scanner)
    {
        service?.Dispose();
        EstateService opened = EstateService.Open(estate.Root);
        opened.Providers = new() { PushAdapter = Adapter(), SecretScanner = scanner };
        opened.Providers.Register(provider);
        return opened;
    }

    // The adapter isolates its own git processes; only its read of the user's credential settings sees this test's stand-in for them.
    private GitPushAdapter Adapter() => new(info =>
    {
        if (info.ArgumentList.Contains("ls-remote") || info.ArgumentList.Contains("push")) Assert.Contains(remote, info.ArgumentList);
        if (info.ArgumentList.Contains("config"))
        {
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = configuration;
        }
        return BoundedProcess.Run(info, TestTiming.HangTimeout);
    });

    // The installed gitleaks, scanning with Vericairn's own configuration; skipped where none is named.
    private static GitleaksScanner Installed()
    {
        string? gitleaks = Environment.GetEnvironmentVariable("VERICAIRN_TEST_GITLEAKS");
        Assert.SkipWhen(gitleaks is null, "Set VERICAIRN_TEST_GITLEAKS to an installed gitleaks of at least 8.25.0 to scan real commits.");
        return new GitleaksScanner(info =>
        {
            if (info.FileName == "gitleaks") info.FileName = gitleaks!;
            return BoundedProcess.Run(info, TestTiming.HangTimeout);
        });
    }

    // A private key, assembled at run time so that this file holds no credential-shaped text of its own.
    private static string PrivateKey() => "-----BEGIN " + "RSA PRIVATE KEY-----\n"
        + string.Join("\n", Enumerable.Range(0, 6).Select(_ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))))
        + "\n-----END " + "RSA PRIVATE KEY-----\n";

    private string Commit(string path, string content, string message)
    {
        File.WriteAllText(Path.Combine(clone, path), content);
        Git(clone, "add", "--", path);
        Git(clone, "commit", "-q", "-m", message);
        return Git(clone, "rev-parse", "HEAD");
    }

    private string Git(string workingDirectory, params string[] args)
    {
        ProcessResult result = Run(Info(workingDirectory, args));
        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.True(result.ExitCode == 0, "The isolated git command did not succeed: " + string.Join(' ', args));
        return result.StandardOutput.Trim();
    }

    private static ProcessStartInfo Info(string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        return info;
    }

    private ProcessResult Run(ProcessStartInfo info)
    {
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = configuration;
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        string[] arguments = info.ArgumentList.ToArray();
        info.ArgumentList.Clear();
        foreach (string value in new[] { "user.name=Test", "user.email=test@example.invalid", "commit.gpgsign=false", "core.autocrlf=false", "core.hooksPath=" + hooks })
        { info.ArgumentList.Add("-c"); info.ArgumentList.Add(value); }
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return BoundedProcess.Run(info, TestTiming.HangTimeout);
    }

    public void Dispose()
    {
        service?.Dispose();
        estate.Dispose();
        WorkspaceCleanup.Delete(directory);
    }

    // Finds the commits a case planted when the scanned range holds them, by the real objects; records each range it scanned.
    private sealed class PlantedScanner(PushCoverageTests test) : ISecretScanner
    {
        internal HashSet<string> Secrets { get; } = new(StringComparer.Ordinal);
        internal List<string> Scanned { get; } = [];
        internal bool Unavailable { get; set; }
        public SecretScan Scan(string directory, PushPlan plan)
        {
            Scanned.Add(plan.Range);
            if (Unavailable) return new("planted", "1.0.0", plan.Range, "unavailable", null);
            int found = Secrets.Count(s => test.Ancestor(s, plan.Head) && !test.Ancestor(s, plan.Base));
            return new("planted", "1.0.0", plan.Range, found == 0 ? "passed" : "findings", found);
        }
    }

    // The memory provider, with pushes going to this test's local bare repository.
    private sealed class LocalRemote(string path) : IPullRequestProvider
    {
        internal MemoryPullRequestProvider Memory { get; } = new();
        internal string? MergedHead { get; private set; }
        public string Provider => Memory.Provider;
        public ProviderDescriptor Descriptor => Memory.Descriptor;
        public string PushRemote(string destination) => path;
        public ChangeReceipt Open(string destination, string branch, string target, string head, string title, string body) => Memory.Open(destination, branch, target, head, title, body);
        public PullRequestObservation? Observe(string destination, string numberOrBranch) => Memory.Observe(destination, numberOrBranch);
        public ChangeReceipt Merge(string destination, string number, string head) { MergedHead = head; return Memory.Merge(destination, number, head); }
        public TargetObservation ObserveTarget(string destination, string branch, string commit) => Memory.ObserveTarget(destination, branch, commit);
        public MergeQueueObservation ObserveQueue(string destination, string number) => Memory.ObserveQueue(destination, number);
    }
}
