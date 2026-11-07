// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

// FIX-95: what a submission's basis retains in the estate's objects, and how it reads back.
public sealed partial class ReleasePolicyGateTests
{
    [Fact]
    public void ASubmissionNamesEvidenceRepositoryFilesByHashAndRetainsOnlyWorkingTreeFiles()
    {
        using var f = new Fixture();
        // A monthly duty, at a level that holds nothing, whose first occurrence a result naming a commit completes once due.
        JsonObject duty = f.Records.PolicyBody();
        duty["level"] = "remind";
        duty["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-10));
        duty["rules"]![0]!["parameters"]!["schedule"]!["firstDueAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-10));
        RecordEnvelope policy = f.Records.Policy(duty);
        f.Records.Estate.SetClock(f.Clock.Now);
        // The clone lives outside the estate, as registered clones do.
        using var outside = new Outside();
        string clone = outside.Repository("evidence");
        RecordEnvelope resource = f.Records.Add("resource", new JsonObject
        {
            ["kind"] = "repository", ["name"] = "Evidence repository", ["role"] = "Exact commit evidence fixture.",
            ["location"] = new JsonObject { ["github"] = "Example/evidence" }, ["visibility"] = "private",
        });
        RepositoryClones.Register(f.Records.Estate.Estate.Id, resource.Id, clone);
        GitHistory.Import(clone, history => history.Commit("Evidence",
            ExactCommitReadTests.File("docs/guide.md", "Evidence prose.\n"), ExactCommitReadTests.File("src/tool.cs", "class Tool {}\n")));
        string commit = Assert.Single(GitHistory.Commits(clone));
        JsonObject link = f.Records.DutyLink(policy);
        link["scope"]!["repositories"] = new JsonArray(new JsonObject { ["record"] = PolicyFixture.Ref(resource), ["commit"] = commit });
        f.Records.Review(f.Records.Result(link));
        f.Clock.Now = f.Records.Estate.Clock.AddMinutes(1);
        Directory.CreateDirectory(Path.Combine(f.Records.Estate.Root, "docs"));
        File.WriteAllText(Path.Combine(f.Records.Estate.Root, "docs", "estate.md"), "Estate prose.\n");
        string guide = ContentHash.Sha256("Evidence prose.\n"u8), tool = ContentHash.Sha256("class Tool {}\n"u8), estate = ContentHash.Sha256("Estate prose.\n"u8);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new GitPolicySource();
            ReleaseResponse response = f.Api.ReleaseSubmit(f.Request);
            Assert.True(response.Ok, response.Message);
            string hash = Assert.Single(response.BasisEvidence!);

            using JsonDocument stored = JsonDocument.Parse(f.Api.Session.Objects.Read(hash)!);
            Assert.Equal("vericairn.release-basis/3", stored.RootElement.GetProperty("format").GetString());
            JsonElement evidence = Assert.Single(stored.RootElement.GetProperty("repositories").EnumerateArray(), r => r.GetProperty("identity").GetString() == resource.Id);
            Assert.Equal(commit, evidence.GetProperty("revision").GetString());
            Assert.True(evidence.GetProperty("evidence").GetBoolean());
            Assert.Equal([("docs/guide.md", guide), ("src/tool.cs", tool)],
                evidence.GetProperty("files").EnumerateArray().Select(file => (file.GetProperty("path").GetString(), file.GetProperty("hash").GetString())));
            // The commit's files stay in its git objects; the estate's own working-tree document is retained beside the basis.
            IReadOnlyList<string> objects = [.. f.Api.Session.Objects.List()];
            Assert.DoesNotContain(guide, objects);
            Assert.DoesNotContain(tool, objects);
            Assert.Contains(estate, objects);

            ReleaseBasisEvidence read = Assert.IsType<ReleaseBasisEvidence>(f.Api.ReleaseBasis(hash)!.Evidence);
            ReleaseBasisRepository repository = Assert.Single(read.Repositories, r => r.Identity == resource.Id);
            Assert.Equal(["Evidence prose.\n", "class Tool {}\n"], repository.Files.Select(file => System.Text.Encoding.UTF8.GetString(file.Bytes.ToArray())));
            Assert.Equal("Estate prose.\n", System.Text.Encoding.UTF8.GetString(Assert.Single(read.Repositories.SelectMany(r => r.Files), file => file.Path == "docs/estate.md").Bytes.ToArray()));

            // Read where no clone holds the commit, the evidence files are missing by their commit; nothing else is.
            RepositoryClones.Register(f.Records.Estate.Estate.Id, resource.Id, outside.Repository("empty"));
            ReleaseBasisResponse unavailable = f.Api.ReleaseBasis(hash)!;
            Assert.Null(unavailable.Evidence);
            Assert.Equal([$"source:{resource.Id}@{commit}:docs/guide.md@{guide}", $"source:{resource.Id}@{commit}:src/tool.cs@{tool}"],
                unavailable.Problem!.Findings.Select(finding => finding.Location));
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    [Theory]
    [InlineData(ReleaseBasisRetention.MaximumFiles + 1, 1, false)]
    [InlineData(9, 7_500_000, false)]
    [InlineData(ReleaseBasisRetention.MaximumFiles + 1, 1, true)]
    public void ABasisPastTheRetentionBoundIsRefusedNamingTheBound(int count, int size, bool exact)
    {
        using var f = new Fixture();
        RecordEnvelope resource = Assert.Single(f.Api.Session.Read().Current("resource"));
        JsonObject definition = f.Records.PolicyBody("source");
        definition["level"] = "remind";
        definition["startsAt"] = RecordParser.FormatTimestamp(f.Clock.Now.AddSeconds(-10));
        definition["appliesTo"] = new JsonObject { ["kind"] = "repository", ["repository"] = resource.Id, ["selectors"] = new JsonArray("docs/**") };
        definition["rules"] = new JsonArray(new JsonObject { ["key"] = "revision", ["kind"] = "vericairn.estate/revision-line", ["version"] = 1,
            ["parameters"] = new JsonObject { ["selectors"] = new JsonArray("docs/**"), ["style"] = "estate-design/1" } });
        f.Records.Policy(definition);
        IPolicyRepositorySource? previous = PolicySources.RepositorySource;
        try
        {
            PolicySources.RepositorySource = new Inventory(count, size, exact);
            ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
            if (exact)
            {
                // The same files at an exact commit are named by hash alone, so they count toward no retention bound.
                Assert.DoesNotContain(check.Check!.Objections, o => o.Code == "release.basis-retained");
                return;
            }
            ReleaseObjection objection = Assert.Single(check.Check!.Objections, o => o.Code == "release.basis-retained");
            Assert.Contains($"{count} source files of {(long)count * size} bytes", objection.Message, StringComparison.Ordinal);
            Assert.Contains($"at most {ReleaseBasisRetention.MaximumFiles} files and {ReleaseBasisRetention.MaximumBytes} bytes", objection.Message, StringComparison.Ordinal);
            Assert.Contains("working trees", objection.NextAction, StringComparison.Ordinal);
            ReleaseResponse submit = f.Api.ReleaseSubmit(f.Request);
            Assert.False(submit.Ok);
            Assert.Contains(submit.Problem!.Findings, o => o.Code == "release.basis-retained");
            Assert.Empty(f.Api.Session.Objects.List());
            Assert.Empty(f.Api.Session.Read().OfType("operation"));
            Assert.Equal(0, f.Provider.Calls);
        }
        finally { PolicySources.RepositorySource = previous; }
    }

    // Directories outside the estate for registered clones, removed with their read-only Git objects.
    private sealed class Outside : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "vericairn-outside-" + Vericairn.Records.Identity.Ulid.NewUlid().Value);

        internal string Repository(string name)
        {
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            GitHistory.Run(path, "init", "--quiet");
            return path;
        }

        public void Dispose()
        {
            if (!Directory.Exists(root)) return;
            foreach (string file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, recursive: true);
        }
    }

    // A selected repository's files, read from a working tree or at the exact commit asked for.
    private sealed class Inventory(int count, int size, bool exact) : IPolicyRepositorySource
    {
        private readonly ImmutableArray<PolicyFile> files = [.. Enumerable.Range(0, count).Select(i =>
        {
            byte[] bytes = new byte[size];
            bytes[0] = (byte)(i % 256);
            return new PolicyFile("docs/file-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".md", ContentHash.Sha256(bytes), [.. bytes]);
        })];

        public PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit) =>
            new(repository.Id, exact ? commit! : "working-tree:fixture", PolicyBasis.RepositoryContract, true, files);
    }
}
