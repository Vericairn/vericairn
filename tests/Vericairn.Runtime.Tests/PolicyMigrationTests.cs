// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Tests;

[CollectionDefinition("upgrade", DisableParallelization = true)]
public sealed class UpgradeTestGroup;

/// <summary>Format-3 compatibility is exercised only on disposable fixture estates.</summary>
[Collection("upgrade")]
[Trait("Journey", "D19-I")]
public class PolicyMigrationTests
{
    [Fact]
    public void TheFormatTwoToThreeMigrationIsForwardOnlyIdempotentAndDoesNotInventRecords()
    {
        using var f = new PolicyFixture(format: 2);
        Publish(f, "0.8.0", 3);
        Dictionary<string, byte[]> before = Snapshot(f.Estate.Root);
        using var service = EstateService.Open(f.Estate.Root);
        (UpgradeResponse? plan, Problem? problem) = service.Upgrade(new UpgradeRequest("0.8.0", null, true));
        Assert.Null(problem);
        Assert.Equal(["record-format-3"], plan!.Migrations);
        AssertSnapshot(before, f.Estate.Root);
        (UpgradeResponse? applied, Problem? failed) = service.Upgrade(new UpgradeRequest("0.8.0", f.Estate.OperatorId, false));
        Assert.Null(failed);
        Assert.True(applied!.Applied);
        EstateModel moved = service.Session.Load();
        Assert.Equal(3, moved.Manifest!.RecordFormat);
        Assert.False(moved.Findings.HasErrors, string.Join('\n', moved.Findings));
        Assert.All(before.Where(p => p.Key.StartsWith("records/", StringComparison.Ordinal)), p => Assert.Equal(p.Value, File.ReadAllBytes(Path.Combine(f.Estate.Root, p.Key))));
        Assert.Single(moved.OfType("migration"));
        Assert.All(moved.Records.Where(r => r.Type != "migration"), r => Assert.Equal(2, r.Format));
        Assert.Empty(moved.OfType("estate-policy"));
        Assert.Empty(moved.OfType("policy-exception"));
        Assert.Empty(moved.OfType("writing-exception"));
        Assert.Empty(moved.OfType("authority"));
        Assert.Empty(moved.OfType("result"));
        Dictionary<string, byte[]> once = Snapshot(f.Estate.Root);
        (UpgradeResponse? repeated, Problem? repeatProblem) = service.Upgrade(new UpgradeRequest("0.8.0", f.Estate.OperatorId, false));
        Assert.Null(repeatProblem);
        Assert.False(repeated!.Applied);
        Assert.Null(repeated.Migration);
        AssertSnapshot(once, f.Estate.Root);

        f.Estate.MoveTo(3, writeManifest: false);
        Publish(f, "0.7.3", 2);
        Dictionary<string, byte[]> forward = Snapshot(f.Estate.Root);
        Assert.Throws<InvalidOperationException>(() => EstateUpgrade.Plan(f.Estate.Load(), "0.7.3"));
        AssertSnapshot(forward, f.Estate.Root);
    }

    [Fact]
    public void AFailedFormatThreeStepLeavesEveryRepositoryByteAndDirectoryUntouched()
    {
        using var f = new PolicyFixture(format: 2);
        Publish(f, "0.8.0", 3);
        using var service = EstateService.Open(f.Estate.Root);
        Dictionary<string, byte[]> before = Snapshot(f.Estate.Root);
        string[] directories = Directories(f.Estate.Root);
        using var migrations = EstateUpgrade.UseMigrations([new FailedStep()]);
        (UpgradeResponse? applied, Problem? problem) = service.Upgrade(new UpgradeRequest("0.8.0", f.Estate.OperatorId, false));
        Assert.Null(applied);
        Assert.Contains("nothing changed", problem!.Message, StringComparison.Ordinal);
        AssertSnapshot(before, f.Estate.Root);
        Assert.Equal(directories, Directories(f.Estate.Root));
    }

    [Fact]
    public void ARefusedMigrationRecordAlsoRestoresTheManifest()
    {
        using var f = new PolicyFixture(format: 2);
        Publish(f, "0.8.0", 3);
        using var service = EstateService.Open(f.Estate.Root);
        Dictionary<string, byte[]> before = Snapshot(f.Estate.Root);
        string[] directories = Directories(f.Estate.Root);
        using var migrations = EstateUpgrade.UseMigrations([new StagedStep()]);
        (UpgradeResponse? applied, Problem? problem) = service.Upgrade(new UpgradeRequest("0.8.0", "01ARZ3NDEKTSV4RRFFQ69G5FAV", false));
        Assert.Null(applied);
        Assert.NotNull(problem);
        AssertSnapshot(before, f.Estate.Root);
        Assert.Equal(directories, Directories(f.Estate.Root));
    }

    [Fact]
    public void AFormatTwoReaderRefusesARefuseDutyIncludingInARestoredCopyBeforeWriting()
    {
        using var f = new PolicyFixture(format: 2);
        Publish(f, "0.8.0", 3);
        using (var service = EstateService.Open(f.Estate.Root))
        {
            Assert.Null(service.Upgrade(new UpgradeRequest("0.8.0", f.Estate.OperatorId, false)).Problem);
        }
        f.Estate.MoveTo(3, writeManifest: false);
        RecordEnvelope duty = f.Policy();
        Assert.Equal("refuse", EstateModel.BodyString(duty, "level"));
        f.AssertValid();
        using var restored = new TestEstate(recordFormat: 3);
        Directory.Delete(restored.Root, true);
        foreach ((string path, byte[] bytes) in Snapshot(f.Estate.Root))
        {
            string full = Path.Combine(restored.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        foreach (string root in new[] { f.Estate.Root, restored.Root })
        {
            Dictionary<string, byte[]> before = Snapshot(root);
            using (RecordFormats.ReadThrough(2))
            {
                FindingList findings = [];
                Assert.Null(EstateManifest.Load(root, findings));
                Assert.Contains(findings, x => x.Code == "manifest.record-format");
                InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => EstateService.Open(root));
                Assert.Contains("record format 3", refused.Message, StringComparison.Ordinal);
                AssertSnapshot(before, root);
            }
            Assert.False(EstateLoader.Load(root).Findings.HasErrors);
        }
    }

    private static void Publish(PolicyFixture f, string version, long format)
    {
        RecordEnvelope product = f.Add("product", new JsonObject { ["name"] = "Example", ["slug"] = "example-" + format, ["summary"] = "Test migration product.", ["editions"] = new JsonArray(new JsonObject { ["name"] = "Community", ["slug"] = "community", ["summary"] = "Test edition." }), ["surfaces"] = new JsonArray(new JsonObject { ["name"] = "CLI", ["slug"] = "cli", ["summary"] = "Test surface." }) });
        JsonObject body = new()
        {
            ["version"] = version, ["repository"] = "Example/example", ["commit"] = new string('1', 40), ["status"] = "candidate", ["artifacts"] = new JsonArray(new JsonObject { ["name"] = EstateUpgrade.DistributableName(version, "linux-x64"), ["hash"] = "sha256:" + new string('1', 64), ["bytes"] = 10, ["platform"] = "linux-x64" }),
            ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(Enumerable.Range(1, (int)format).Select(n => (JsonNode)n).ToArray()), ["writes"] = format }, ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "unsigned test artifact", ["limitations"] = new JsonArray(),
        };
        RecordEnvelope candidate = f.Add("release", body, subject: new RecordReference("product", product.Id, null));
        f.Add("acceptance", new JsonObject { ["statement"] = "Fixture release accepted.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        body["status"] = "published";
        body["publishedAt"] = RecordParser.FormatTimestamp(f.Estate.Clock);
        body["url"] = "https://example.invalid/releases/" + version;
        f.Add("release", body, subject: candidate.Subject, supersedes: candidate.Id);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
    private static string[] Directories(string root) => Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p)).Order(StringComparer.Ordinal).ToArray();
    private static void AssertSnapshot(Dictionary<string, byte[]> expected, string root)
    {
        Dictionary<string, byte[]> actual = Snapshot(root);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        Assert.All(expected, p => Assert.Equal(p.Value, actual[p.Key]));
    }

    private sealed class FailedStep : IRecordFormatMigration
    {
        public string Name => "failed-format-three-fixture";
        public long FromFormat => 2;
        public long ToFormat => 3;
        public void Apply(MigrationContext context)
        {
            context.Stage("docs/staged.txt", "never published"u8.ToArray());
            throw new InvalidOperationException("Fixture failure after staging.");
        }
    }

    private sealed class StagedStep : IRecordFormatMigration
    {
        public string Name => "staged-format-three-fixture";
        public long FromFormat => 2;
        public long ToFormat => 3;
        public void Apply(MigrationContext context) => context.Stage("docs/new-directory/staged.txt", "rolled back"u8.ToArray());
    }
}
