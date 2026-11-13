// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Store;

namespace Vericairn.Cli.Tests;

/// <summary>
/// DOCS-29: the large private artifacts guide's examples, run as written. The record bodies are read from the guide's named JSON
/// blocks and every command is matched against the guide's text before it runs, so the guide and this check cannot drift apart.
/// DOCS-32: every file of the restored copy is classified by the guide's three inventories alone, before and after the copy is
/// written, and the housekeeping the guide lists is the housekeeping the writers make.
/// </summary>
[Collection("cli")]
public sealed partial class LargeArtifactGuideTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Brief = """{"outcome":"The original of candidate C-0001, kept in custody.","sources":[],"permittedChanges":["none"],"prohibitedEffects":["no publication"],"workspace":["example"],"limits":[],"expectedOutputs":["C-0001 original"],"reviewCriteria":["hash and bytes match the intake"],"recovery":"Read the last result."}""";
    private static readonly string[] Blocks = ["store-credential.json", "store.json", "intake.json", "intake-dispatched.json", "intake-observed.json", "intake-failed.json", "result.json", "readback.json", "readback-failed.json", "restore-files.json"];
    private static readonly string[] GitSettings = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];

    // The classes a file of a restored estate can have, as the guide names them.
    private const string Exported = "exported";
    private const string Supplemental = "supplemental";
    private const string WrittenSince = "written after the restore";
    private const string Housekeeping = "housekeeping";
    private const string Unexpected = "unexpected";
    private const string Missing = "missing";

    // The first column of the guide's two housekeeping tables, in order: the fixed files and directories, then a stopped write's residue.
    private static readonly string[] HolderFields = ["pid", "host", "home", "since"];

    private static readonly string[] HousekeepingEntries =
    [
        ".vericairn/record-staging/.gitignore", ".vericairn/write-lock/.gitignore", ".vericairn/write-lock/write.lock", ".vericairn/write-lock/holder.json",
        ".vericairn/document-staging/.gitignore", ".vericairn/toolchain/release/<version>/<platform>/", ".vericairn/toolchain/source/<commit>/bin/",
        ".vericairn/toolchain/vericairn-src/",
        ".vericairn/record-staging/<ulid>.tmp", ".vericairn/write-lock/<ulid>.tmp", ".vericairn/document-staging/<ulid>.tmp",
        "objects/.<64 hex digits>.tmp-<process id>", "estate/manifest.json.upgrading", "estate/manifest.json.naming", "<name>.<32 hex digits>.tmp",
    ];

    [Fact]
    public void TheGuideKeepsAnArtifactAboveTheObjectLimitAndRecoversItCold()
    {
        string guide = Guide();
        Dictionary<string, string> blocks = JsonBlocks(guide);
        Assert.Equal(Blocks.Order(StringComparer.Ordinal), blocks.Keys.Order(StringComparer.Ordinal));
        string live = CliHarness.TempDirectory("docs29-live");
        string failed = string.Empty;
        string scratch = CliHarness.TempDirectory("docs29-scratch");
        string store = CliHarness.TempDirectory("docs29-store");
        string backup = CliHarness.TempDirectory("docs29-backup");
        string isolated = CliHarness.TempDirectory("docs29-isolated");
        string isolatedHome = CliHarness.TempDirectory("docs29-isolated-home");
        string capture = CliHarness.TempDirectory("docs32-capture");
        string restored = Path.Combine(isolated, "estate");
        string restoredStore = Path.Combine(isolated, "store");
        string quarantine = Path.Combine(isolated, "quarantine");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["<restore-directory>"] = restored };
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["estate-export.zip"] = Path.Combine(scratch, "estate-export.zip") };
        try
        {
            Git(live, "init", "-q", "-b", "main");
            JsonElement initialized = CliHarness.Ok("--estate", live, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Custody of large private artifacts.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json;
            values["<operator-id>"] = initialized.GetProperty("operator").GetString()!;
            values["<estate-id>"] = initialized.GetProperty("estate").GetString()!;
            values["<contributor-id>"] = CliHarness.Ok("--estate", live, "--json", "records", "new", "principal", "--by", values["<operator-id>"], "--body", CliHarness.WriteJson(scratch, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;
            CliHarness.Ok("--estate", live, "--json", "records", "new", "work-order", "--by", values["<operator-id>"], "--body", CliHarness.WriteJson(scratch, "order.json", """{"key":"MEDIA-1","kind":"ordinary","title":"Keep the C-0001 original","intent":"Custody of an artifact above the object limit.","scope":["C-0001 original"],"exitQuestion":"Is the original kept with exact bytes?","inputs":[],"deliverables":["C-0001 original"]}"""));
            string assignment = CliHarness.Ok("--estate", live, "--json", "work", "assign", "MEDIA-1", "--contributor", values["<contributor-id>"], "--by", values["<operator-id>"], "--brief", CliHarness.WriteJson(scratch, "brief.json", Brief)).Id;
            values["<attempt-id>"] = CliHarness.Ok("--estate", live, "--json", "work", "attempt", assignment, "--by", values["<contributor-id>"], "--workspace", "example", "--note", "Take in the original.").Id;

            // The artifact: synthetic bytes of the size the guide's result states, above the 8 MiB object limit.
            long size = JsonNode.Parse(blocks["result.json"])!["outputs"]![0]!["bytes"]!.GetValue<long>();
            Assert.True(size > 8 * 1024 * 1024);
            byte[] original = new byte[size];
            new Random(304).NextBytes(original);
            files["original.wav"] = Path.Combine(scratch, "original.wav");
            File.WriteAllBytes(files["original.wav"], original);
            string hash = ContentHash.Sha256(original);
            values["<sha256-hex>"] = hash["sha256:".Length..];

            CliHarness.Outcome tooLarge = Step(guide, values, files, blocks, scratch, live, "vericairn objects put original.wav", refused: true);
            Assert.Contains("object limit", tooLarge.Err, StringComparison.Ordinal);
            Assert.Empty(CliHarness.Ok("--estate", live, "--json", "objects", "list").Json.EnumerateArray());

            values["<credential-id>"] = Step(guide, values, files, blocks, scratch, live, "vericairn records new credential-reference --by <operator-id> --body store-credential.json").Id;
            values["<store-id>"] = Step(guide, values, files, blocks, scratch, live, "vericairn records new resource --by <operator-id> --body store.json").Id;
            values["<intent-id>"] = Step(guide, values, files, blocks, scratch, live, "vericairn records new operation --by <operator-id> --body intake.json").Id;

            // The copy: the store's object at the target's reference. Recording the intent copied nothing.
            JsonNode target = JsonNode.Parse(Fill(blocks["intake.json"], values))!["target"]!;
            Assert.Equal(hash, target["hash"]!.GetValue<string>());
            Assert.Equal(size.ToString(CultureInfo.InvariantCulture), target["bytes"]!.GetValue<string>());
            string reference = target["reference"]!.GetValue<string>();
            Assert.Empty(Directory.EnumerateFileSystemEntries(store));
            string stored = Path.Combine(store, reference.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
            values["<dispatched-at>"] = Now();
            File.Copy(files["original.wav"], stored);
            values["<dispatched-id>"] = Step(guide, values, files, blocks, scratch, live, "vericairn records new operation --by <operator-id> --supersedes <intent-id> --body intake-dispatched.json").Id;

            // A copy that came back short is a known failure, and a further copy names its reconciliation.
            failed = CliHarness.Clone(live, "docs29-failed");
            byte[] incomplete = original[..(4 * 1024 * 1024)];
            values["<found-sha256-hex>"] = ContentHash.Sha256(incomplete)["sha256:".Length..];
            values["<observed-at>"] = Now();
            string failure = Step(guide, values, files, blocks, scratch, failed, "vericairn records new operation --by <operator-id> --supersedes <dispatched-id> --body intake-failed.json").Id;
            CliHarness.Outcome unreconciled = CliHarness.Run("--estate", failed, "--json", "records", "new", "operation", "--by", values["<operator-id>"], "--supersedes", failure, "--body", Path.Combine(scratch, "intake-dispatched.json"));
            Assert.NotEqual(0, unreconciled.Code);
            Assert.Contains("operation.reconciliation", unreconciled.Out + unreconciled.Err, StringComparison.Ordinal);

            // The readback: the stored object, read back from the store, has the target's exact hash and size.
            byte[] readBack = File.ReadAllBytes(stored);
            Assert.Equal(hash, ContentHash.Sha256(readBack));
            Assert.Equal(size, readBack.LongLength);
            values["<observed-at>"] = Now();
            CliHarness.Outcome intake = Step(guide, values, files, blocks, scratch, live, "vericairn records new operation --by <operator-id> --supersedes <dispatched-id> --body intake-observed.json");
            values["<intake-id>"] = intake.Id;
            values["<intake-hash>"] = intake.Hash;

            // A URL in the output's location is refused (FIX-97); the guide's result names the reference in its evidence instead.
            JsonNode schemed = JsonNode.Parse(Fill(blocks["result.json"], values))!;
            schemed["outputs"]![0]!["location"] = schemed["evidence"]![0]!["location"]!.GetValue<string>();
            Assert.Contains("\"location\": \"" + schemed["outputs"]![0]!["location"]!.GetValue<string>() + "\"", guide, StringComparison.Ordinal);
            files["result.json"] = CliHarness.WriteJson(scratch, "result-with-scheme.json", schemed.ToJsonString());
            CliHarness.Outcome scheme = Step(guide, values, files, blocks, scratch, live, "vericairn work result <attempt-id> --by <contributor-id> --body result.json", refused: true);
            Assert.Contains("result.output-scheme", scheme.Out + scheme.Err, StringComparison.Ordinal);
            Assert.Contains("result.output-scheme", guide, StringComparison.Ordinal);
            files.Remove("result.json");
            CliHarness.Outcome result = Step(guide, values, files, blocks, scratch, live, "vericairn work result <attempt-id> --by <contributor-id> --body result.json");
            values["<result-id>"] = result.Id;
            values["<result-hash>"] = result.Hash;

            // The owner's agent instruction file at the root, which the export doesn't carry yet: the supplemental capture keeps it,
            // listed with its hash.
            byte[] agents = Encoding.UTF8.GetBytes("# Working in this estate\n\nThese are the owner's instructions for agents working in this estate.\n");
            File.WriteAllBytes(Path.Combine(live, "AGENTS.md"), agents);
            File.WriteAllBytes(Path.Combine(capture, "AGENTS.md"), agents);
            var supplemental = new Dictionary<string, string>(StringComparer.Ordinal) { ["AGENTS.md"] = ContentHash.Sha256(agents) };

            // The supplemental capture: the store's own backup. Then the estate commit that holds the result, and the export.
            Copy(store, backup);
            Git(live, "add", "-A");
            Git(live, "commit", "-q", "-m", "Keep the C-0001 original");
            Assert.Contains("git rev-parse HEAD", guide, StringComparison.Ordinal);
            values["<estate-commit>"] = Git(live, "rev-parse", "HEAD").Trim();
            CliHarness.Outcome export = Step(guide, values, files, blocks, scratch, live, "vericairn estate export --out estate-export.zip --by <operator-id>");
            Assert.Equal(0, export.Json.GetProperty("Objects").GetInt32());
            using (ZipArchive zip = ZipFile.OpenRead(files["estate-export.zip"]))
            {
                Assert.DoesNotContain(zip.Entries, e => e.Length == size);
            }

            // The inventory the guide lists from the export: no instruction file and no housekeeping, though the live estate holds both.
            foreach (string listing in new[] { "unzip -p estate-export.zip export/inventory.json", "find <restore-directory> -type f -o -type l",
                "Expand-Archive estate-export.zip -DestinationPath export-contents", "Get-Content export-contents/export/inventory.json",
                "Get-ChildItem -Path <restore-directory> -Recurse -Force -File | ForEach-Object FullName" })
                Assert.Contains("\n" + listing + "\n", guide, StringComparison.Ordinal);
            ExportedInventory exported = ReadExport(files["estate-export.zip"]);
            Assert.Equal(values["<estate-id>"], exported.Estate);
            Assert.Equal("repository-synced", exported.Profile);
            Assert.True(File.Exists(Path.Combine(live, ".vericairn", "record-staging", ".gitignore")));
            Assert.DoesNotContain(exported.Members.Keys, path => EstateLayout.OwnerInstructionFiles.Contains(path) || path.StartsWith(".vericairn/", StringComparison.Ordinal));

            // Cold recovery on another machine: the export and the store's backup, restored in an isolated place.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, isolatedHome);
            CliHarness.Outcome restore = Step(guide, values, files, blocks, scratch, live, "vericairn estate restore --from estate-export.zip --to <restore-directory>");
            Assert.Equal(0, restore.Json.GetProperty("errors").GetInt32());
            Assert.Equal(0, restore.Json.GetProperty("Objects").GetInt32());
            Assert.DoesNotContain(Directory.EnumerateFiles(restored, "*", SearchOption.AllDirectories), f => new FileInfo(f).Length == size);

            // Before any write, every file of the restored copy is a member with its inventory's bytes and hash, and every member is there.
            var none = new Dictionary<string, string>(StringComparer.Ordinal);
            Assert.All(Classify(restored, exported, none, none), file => Assert.Equal(Exported, file.Value));
            Assert.Equal(exported.Members.Count, Classify(restored, exported, none, none).Count);
            Copy(backup, restoredStore);

            // The restored records say which bytes to recover; only bytes read from the restored store recover them. Reading them, as
            // records show and estate check do, leaves the copy as the restore wrote it.
            using JsonDocument restoredResult = JsonDocument.Parse(Step(guide, values, files, blocks, scratch, live, "vericairn --estate <restore-directory> records show <result-id>").Out);
            using JsonDocument restoredIntake = JsonDocument.Parse(Step(guide, values, files, blocks, scratch, live, "vericairn --estate <restore-directory> records show <intake-id>").Out);
            CliHarness.Run("--estate", restored, "estate", "check");
            Assert.All(Classify(restored, exported, none, none), file => Assert.Equal(Exported, file.Value));
            Assert.False(Directory.Exists(Path.Combine(restored, ".vericairn")));
            JsonElement output = restoredResult.RootElement.GetProperty("body").GetProperty("outputs")[0];
            Assert.False(output.TryGetProperty("location", out _));
            JsonElement input = Assert.Single(restoredResult.RootElement.GetProperty("body").GetProperty("provenance").GetProperty("inputs").EnumerateArray());
            Assert.Equal(values["<intake-id>"], input.GetProperty("id").GetString());
            JsonElement restoredTarget = restoredIntake.RootElement.GetProperty("body").GetProperty("target");
            Assert.Equal(output.GetProperty("hash").GetString(), restoredTarget.GetProperty("hash").GetString());
            string recovered = Path.Combine(restoredStore, restoredTarget.GetProperty("reference").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            byte[] recoveredBytes = File.ReadAllBytes(recovered);
            Assert.Equal(output.GetProperty("hash").GetString(), ContentHash.Sha256(recoveredBytes));
            Assert.Equal(output.GetProperty("bytes").GetInt64(), recoveredBytes.LongLength);

            // The readback is recorded in the estate of record, pinned to the result by hash; a missing payload fails.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            values["<readback-due>"] = RecordTime(DateTimeOffset.UtcNow.AddDays(180));
            Step(guide, values, files, blocks, scratch, live, "vericairn records new verification --by <operator-id> --subject result:<result-id>:<result-hash> --body readback.json");
            File.Delete(recovered);
            Assert.False(File.Exists(recovered));
            Step(guide, values, files, blocks, scratch, live, "vericairn records new verification --by <operator-id> --subject result:<result-id>:<result-hash> --body readback-failed.json");
            Assert.Equal(0, CliHarness.Run("--estate", live, "records", "validate").Code);

            // The restored copy replaces the lost estate: the instruction file comes back from the capture, checked against its list,
            // then the copy is committed and the readback is recorded there.
            File.Copy(Path.Combine(capture, "AGENTS.md"), Path.Combine(restored, "AGENTS.md"));
            Assert.Equal(supplemental["AGENTS.md"], ContentHash.Sha256File(Path.Combine(restored, "AGENTS.md")));
            foreach (string line in new[] { "git -C <restore-directory> init", "git -C <restore-directory> add -A", "git -C <restore-directory> commit -m \"Restore the estate from its export\"", "git -C <restore-directory> rev-parse HEAD" })
                Assert.Contains("\n" + line + "\n", guide, StringComparison.Ordinal);
            Git(restored, "init", "-q", "-b", "main");
            Git(restored, "add", "-A");
            Git(restored, "commit", "-q", "-m", "Restore the estate from its export");
            values["<estate-commit>"] = Git(restored, "rev-parse", "HEAD").Trim();
            var written = new Dictionary<string, string>(StringComparer.Ordinal);
            CliHarness.Outcome restoredReadback = Step(guide, values, files, blocks, scratch, live, "vericairn --estate <restore-directory> records new verification --by <operator-id> --subject result:<result-id>:<result-hash> --body readback.json");
            written[restoredReadback.Json.GetProperty("path").GetString()!] = restoredReadback.Id + " " + restoredReadback.Hash;
            values["<readback-id>"] = restoredReadback.Id;

            // An unexpected file: a readback written into the restored estate by mistake. Classified as nothing the guide lists, it stops
            // the verification; its bytes are the result's external payload, so it moves, unchanged, to the quarantine beside the restore.
            string stray = Path.Combine(restored, reference.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
            File.Copy(Path.Combine(backup, reference.Replace('/', Path.DirectorySeparatorChar)), stray);
            Dictionary<string, string> found = Classify(restored, exported, supplemental, written);
            Assert.Equal(new[] { reference }, found.Where(f => f.Value == Unexpected).Select(f => f.Key));
            Assert.Equal(output.GetProperty("hash").GetString(), ContentHash.Sha256File(stray));
            Assert.Equal(output.GetProperty("bytes").GetInt64(), new FileInfo(stray).Length);
            Assert.Contains("vault/obj-0001", guide, StringComparison.Ordinal);
            string quarantined = Path.Combine(quarantine, reference.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(quarantined)!);
            File.Move(stray, quarantined);
            Directory.Delete(Path.GetDirectoryName(stray)!);
            Assert.Equal(hash, ContentHash.Sha256File(quarantined));
            Assert.DoesNotContain(Classify(restored, exported, supplemental, written), f => f.Value is Unexpected or Missing);

            // The classification is recorded in the restored copy, the estate of record now, as a verification of the charter.
            Assert.Contains("`records show <estate-id>`", guide, StringComparison.Ordinal);
            values["<estate-hash>"] = CliHarness.Ok("--estate", restored, "--json", "records", "show", values["<estate-id>"]).Hash;
            values["<inventory-hash>"] = exported.InventoryHash;
            values["<agents-sha256-hex>"] = supplemental["AGENTS.md"]["sha256:".Length..];
            CliHarness.Outcome classified = Step(guide, values, files, blocks, scratch, live, "vericairn --estate <restore-directory> records new verification --by <operator-id> --subject estate:<estate-id>:<estate-hash> --body restore-files.json");
            written[classified.Json.GetProperty("path").GetString()!] = classified.Id + " " + classified.Hash;
            values["<restore-files-id>"] = classified.Id;

            // Every file is now in exactly one class, and the files beyond the inventory are exactly the guide's listing, class by class.
            found = Classify(restored, exported, supplemental, written);
            Assert.DoesNotContain(found, f => f.Value is Unexpected or Missing);
            Assert.True(Directory.Exists(Path.Combine(restored, ".git")));
            AssertListing(guide, found.Where(f => f.Value != Exported).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal), values);
            Assert.Equal(0, CliHarness.Run("--estate", restored, "records", "validate").Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(live, failed, scratch, store, backup, isolated, isolatedHome, capture);
        }
    }

    /// <summary>
    /// DOCS-32: the housekeeping the guide lists, exactly, is what the writers make: estate init's record staging, a refused write's and
    /// a stored write's lock, the lock's holder while it is held, and the first generated document's staging. A listed path with other
    /// content and an unlisted file under .vericairn/ are not housekeeping.
    /// </summary>
    [Fact]
    public void TheGuideListsTheHousekeepingTheWritersMakeAndNothingElse()
    {
        string guide = Guide();
        Assert.Equal(HousekeepingEntries, HousekeepingTable(guide));
        string root = CliHarness.TempDirectory("docs32-housekeeping");
        string home = CliHarness.TempDirectory("docs32-housekeeping-home");
        try
        {
            string operatorId = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Housekeeping.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            AssertLocalFiles(root, ".vericairn/record-staging/.gitignore");

            // A refused write takes the write lock too, and leaves its directory; a stored one adds nothing more.
            CliHarness.Outcome refused = CliHarness.Run("--estate", root, "--json", "records", "new", "principal", "--by", operatorId, "--body", CliHarness.WriteJson(home, "refused.json", """{"kind":"agent","name":"","roles":[]}"""));
            Assert.NotEqual(0, refused.Code);
            string[] locked = [".vericairn/record-staging/.gitignore", ".vericairn/write-lock/.gitignore", ".vericairn/write-lock/write.lock"];
            AssertLocalFiles(root, locked);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", operatorId, "--body", CliHarness.WriteJson(home, "builder.json", """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example Agent"}}"""));
            AssertLocalFiles(root, locked);

            // While a writer holds the lock it describes itself in holder.json, which it removes on release.
            using (var writeLock = WorkingTreeLock.For(root, "repository-synced", Path.Combine(home, "live.db"), home))
            {
                using (writeLock.Enter())
                {
                    AssertLocalFiles(root, [.. locked, ".vericairn/write-lock/holder.json"]);
                    Assert.Equal(Housekeeping, ClassOfLocal(root, ".vericairn/write-lock/holder.json"));
                }

                AssertLocalFiles(root, locked);
            }

            // The first generated document written stages it beside the others.
            GeneratedOutputStore.Apply(root, [new DocumentOutputChange("docs/staged.md", null, "# Staged\n"u8.ToArray().ToImmutableArray())], []);
            Assert.True(File.Exists(Path.Combine(root, "docs", "staged.md")));
            AssertLocalFiles(root, [.. locked, ".vericairn/document-staging/.gitignore"]);
            Assert.All(LocalFiles(root), path => Assert.Equal(Housekeeping, ClassOfLocal(root, path)));
            Assert.All(LocalFiles(root).Where(path => path.EndsWith("/.gitignore", StringComparison.Ordinal)), path => Assert.Equal("*\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, path))));
            Assert.Contains("hex `2a0a`", guide, StringComparison.Ordinal);

            // The toolchain rows name the bootstrap scripts' own working directories.
            string scripts = Path.Combine(RepositoryRoot(), "src", "Vericairn.Cli", "Bootstrap");
            string posix = File.ReadAllText(Path.Combine(scripts, "bootstrap")), windows = File.ReadAllText(Path.Combine(scripts, "bootstrap.ps1"));
            foreach (string part in new[] { "work=\"$root/.vericairn/toolchain\"", "dir=\"$work/release/$version/$rid\"", "stamp=\"$dir/.sha256\"", "bin=\"$work/source/$head/bin\"", "stamp=\"$bin/.source\"", "src=\"$work/vericairn-src\"" })
                Assert.Contains(part, posix, StringComparison.Ordinal);
            foreach (string part in new[] { "'.vericairn/toolchain'", "\"release/$version/$rid\"", "'.sha256'", "\"source/$head/bin\"", "'.source'", "'vericairn-src'" })
                Assert.Contains(part, windows, StringComparison.Ordinal);

            // Nothing else under .vericairn/ is housekeeping, nor is a listed path with other content.
            Assert.Equal(Housekeeping, ClassOf(".vericairn/toolchain/release/0.9.3/osx-arm64/bin/vericairn", [1]));
            Assert.Equal(Housekeeping, ClassOf(".vericairn/record-staging/" + Ulid.NewUlid().Value + ".tmp", [1]));
            Assert.Equal(Housekeeping, ClassOf("objects/." + new string('a', 64) + ".tmp-4242", [1]));
            Assert.Equal(Housekeeping, ClassOf(".gitignore." + new string('b', 32) + ".tmp", [1]));
            Assert.Equal(Unexpected, ClassOf(".vericairn/record-staging/.gitignore", "x\n"u8.ToArray()));
            Assert.Equal(Unexpected, ClassOf(".vericairn/write-lock/write.lock", [0]));
            Assert.Equal(Unexpected, ClassOf(".vericairn/write-lock/holder.json", "{}"u8.ToArray()));
            Assert.Equal(Unexpected, ClassOf(".vericairn/guide/init.json", "{}"u8.ToArray()));
            Assert.Equal(Unexpected, ClassOf(".vericairn/notes.txt", [1]));
            Assert.Equal(Unexpected, ClassOf(".vericairn/toolchain/bin/vericairn", [1]));
            Assert.Equal(Unexpected, ClassOf(".vericairn/record-staging/" + Ulid.NewUlid().Value.ToLowerInvariant() + ".tmp", [1]));
            Assert.Equal(Unexpected, ClassOf("README.md." + new string('b', 32) + ".tmp", [1]));
        }
        finally
        {
            CliHarness.Delete(root, home);
        }
    }

    /// <summary>
    /// DOCS-32: the one member a restore writes with other bytes. From a local-only export, the restored manifest is the exported one with
    /// its profile changed to repository-synced, and every other member is restored exactly.
    /// </summary>
    [Fact]
    public void ALocalOnlyExportRestoresWithOnlyItsManifestProfileChanged()
    {
        string guide = Guide();
        Assert.Contains("`\"profile\": \"local-only\"`", guide, StringComparison.Ordinal);
        Assert.Contains("`\"profile\": \"repository-synced\"`", guide, StringComparison.Ordinal);
        string root = CliHarness.TempDirectory("docs32-local");
        string scratch = CliHarness.TempDirectory("docs32-local-scratch");
        string restored = Path.Combine(scratch, "restored");
        try
        {
            string operatorId = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "local-example", "--purpose", "A local-only estate.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit, "--profile", "local-only").Json.GetProperty("operator").GetString()!;
            string zip = Path.Combine(scratch, "estate-export.zip");
            CliHarness.Ok("--estate", root, "--json", "estate", "export", "--out", zip, "--by", operatorId);
            CliHarness.Ok("--json", "estate", "restore", "--from", zip, "--to", restored);
            ExportedInventory exported = ReadExport(zip);
            Assert.Equal("local-only", exported.Profile);
            Assert.NotEqual(exported.Members[EstateLayout.ManifestPath].Hash, ContentHash.Sha256File(Path.Combine(restored, "estate", "manifest.json")));
            var none = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> classes = Classify(restored, exported, none, none);
            Assert.Equal(exported.Members.Keys.Order(StringComparer.Ordinal), classes.Keys.Order(StringComparer.Ordinal));
            Assert.All(classes, file => Assert.Equal(Exported, file.Value));
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    /// <summary>
    /// Runs one of the guide's commands as written: the command must appear verbatim in the guide; its placeholders are filled, and
    /// its file names resolve to the guide's record bodies, filled and saved, unless a file stands in for one. A command without its own
    /// --estate runs in the estate given.
    /// </summary>
    private static CliHarness.Outcome Step(string guide, Dictionary<string, string> values, Dictionary<string, string> files, Dictionary<string, string> blocks,
        string scratch, string estate, string command, bool refused = false)
    {
        Assert.True(guide.Contains("\n" + command + "\n", StringComparison.Ordinal), $"The guide no longer shows the command: {command}");
        string[] words = command.Split(' ')[1..];
        var args = new List<string>();
        if (!words.Contains("--estate") && !(words[0] == "estate" && words[1] == "restore")) args.AddRange(["--estate", estate]);
        args.Add("--json");
        foreach (string word in words)
        {
            if (files.TryGetValue(word, out string? path)) args.Add(path);
            else if (blocks.TryGetValue(word, out string? body))
            {
                string filled = Fill(body, values);
                JsonDocument.Parse(filled).Dispose();
                args.Add(CliHarness.WriteJson(scratch, word, filled));
            }
            else args.Add(Fill(word, values));
        }

        CliHarness.Outcome outcome = CliHarness.Run([.. args]);
        if (refused) Assert.True(outcome.Code != 0, $"{command} was not refused\n{outcome.Out}");
        else Assert.True(outcome.Code == 0, $"{command} exited {outcome.Code}\n{outcome.Out}\n{outcome.Err}");
        return outcome;
    }

    /// <summary>Fills every placeholder; one the check does not know fails it, so a new placeholder in the guide needs a value here.</summary>
    private static string Fill(string text, Dictionary<string, string> values)
    {
        string filled = Placeholder().Replace(text, m => values.TryGetValue(m.Value, out string? value) ? value : m.Value);
        Match left = Placeholder().Match(filled);
        Assert.False(left.Success, $"No value for the guide's placeholder {left.Value}.");
        return filled;
    }

    private static Dictionary<string, string> JsonBlocks(string guide)
    {
        var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match block in Fence().Matches(guide).Where(m => m.Groups["language"].Value == "json"))
        {
            string name = block.Groups["name"].Value;
            Assert.False(string.IsNullOrEmpty(name), "Every JSON block in the guide names the file it is saved as.");
            Assert.True(blocks.TryAdd(name, block.Groups["body"].Value), $"The guide names {name} twice.");
        }

        return blocks;
    }

    private static string Guide() => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "large-artifacts.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return root;
    }

    private sealed record Member(long Bytes, string Hash, string? Mode);

    private sealed record ExportedInventory(string Estate, string Profile, string InventoryHash, Dictionary<string, Member> Members, byte[] Manifest);

    /// <summary>The export's inventory and summary, read from the export itself as the guide lists them.</summary>
    private static ExportedInventory ReadExport(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        byte[] inventory = Entry(zip, "export/inventory.json");
        using JsonDocument summary = JsonDocument.Parse(Entry(zip, "export/export.json"));
        string inventoryHash = summary.RootElement.GetProperty("inventoryHash").GetString()!;
        Assert.Equal(inventoryHash, ContentHash.Sha256(inventory));
        using JsonDocument listed = JsonDocument.Parse(inventory);
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (JsonElement item in listed.RootElement.EnumerateArray())
        {
            members.Add(item.GetProperty("path").GetString()!, new Member(item.GetProperty("bytes").GetInt64(), item.GetProperty("hash").GetString()!,
                item.TryGetProperty("mode", out JsonElement mode) ? mode.GetString() : null));
        }

        return new ExportedInventory(summary.RootElement.GetProperty("estate").GetString()!, summary.RootElement.GetProperty("profile").GetString()!,
            inventoryHash, members, Entry(zip, EstateLayout.ManifestPath));
    }

    private static byte[] Entry(ZipArchive zip, string name)
    {
        using Stream stream = zip.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Classifies every file under a restored estate by the guide's rules alone: a member of the export's inventory with its byte count,
    /// hash and mode (a local-only export's manifest with its profile changed); a supplemental file matching the capture's list; a record
    /// written since, holding the id and hash its write printed; housekeeping as the guide lists it. Anything else, a link among them, is
    /// unexpected, and only .git at the root is left out, by that name. An absent member is missing.
    /// </summary>
    private static Dictionary<string, string> Classify(string restored, ExportedInventory export, Dictionary<string, string> supplemental, Dictionary<string, string> written)
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Walk(restored, restored))
        {
            string path = Path.GetRelativePath(restored, file).Replace(Path.DirectorySeparatorChar, '/');
            if (new FileInfo(file).LinkTarget is not null)
            {
                classes[path] = Unexpected;
                continue;
            }

            byte[] bytes = File.ReadAllBytes(file);
            if (export.Members.TryGetValue(path, out Member? member)) classes[path] = IsMember(export, path, file, bytes, member) ? Exported : Unexpected;
            else if (supplemental.TryGetValue(path, out string? listed)) classes[path] = listed == ContentHash.Sha256(bytes) ? Supplemental : Unexpected;
            else if (written.TryGetValue(path, out string? printed)) classes[path] = RecordIdentity(bytes) == printed ? WrittenSince : Unexpected;
            else classes[path] = ClassOf(path, bytes);
        }

        foreach (string path in export.Members.Keys.Where(path => !classes.ContainsKey(path))) classes[path] = Missing;
        return classes;
    }

    private static IEnumerable<string> Walk(string restored, string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            if (directory == restored && Path.GetFileName(entry) == ".git") continue;
            if (Directory.Exists(entry) && new FileInfo(entry).LinkTarget is null)
            {
                foreach (string file in Walk(restored, entry)) yield return file;
            }
            else yield return entry;
        }
    }

    private static bool IsMember(ExportedInventory export, string path, string file, byte[] bytes, Member member)
    {
        if (path == EstateLayout.ManifestPath && export.Profile == "local-only")
        {
            // A restore makes a repository-synced estate: its manifest is the exported one with that one value changed.
            string changed = Encoding.UTF8.GetString(export.Manifest).Replace("\"profile\": \"local-only\"", "\"profile\": \"repository-synced\"", StringComparison.Ordinal);
            if (!bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(changed))) return false;
        }
        else if (bytes.LongLength != member.Bytes || ContentHash.Sha256(bytes) != member.Hash) return false;

        return OperatingSystem.IsWindows() || member.Mode is null || ((File.GetUnixFileMode(file) & UnixFileMode.UserExecute) != 0) == (member.Mode == "100755");
    }

    private static string? RecordIdentity(byte[] bytes)
    {
        try
        {
            using JsonDocument record = JsonDocument.Parse(bytes);
            return record.RootElement.GetProperty("id").GetString() + " " + record.RootElement.GetProperty("hash").GetString();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The guide's housekeeping, exactly: each fixed file with the content its entry fixes, the toolchain's three working directories, and
    /// a stopped write's residue by its name. Every other path is unexpected.
    /// </summary>
    private static string ClassOf(string path, byte[] bytes)
    {
        bool housekeeping = path switch
        {
            ".vericairn/record-staging/.gitignore" or ".vericairn/write-lock/.gitignore" or ".vericairn/document-staging/.gitignore" => bytes.AsSpan().SequenceEqual("*\n"u8),
            ".vericairn/write-lock/write.lock" => bytes.Length == 0,
            ".vericairn/write-lock/holder.json" => IsHolder(bytes),
            _ => Toolchain().IsMatch(path) || Residue().IsMatch(path),
        };
        return housekeeping ? Housekeeping : Unexpected;
    }

    private static string ClassOfLocal(string root, string path) => ClassOf(path, File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))));

    private static bool IsHolder(byte[] bytes)
    {
        try
        {
            using JsonDocument holder = JsonDocument.Parse(bytes);
            JsonElement root = holder.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("format", out JsonElement format) && format.ValueKind == JsonValueKind.String
                && format.GetString() == "vericairn.write-lock/1" && HolderFields.All(name => root.TryGetProperty(name, out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] LocalFiles(string root)
    {
        string local = Path.Combine(root, ".vericairn");
        return Directory.Exists(local)
            ? [.. Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal)]
            : [];
    }

    private static void AssertLocalFiles(string root, params string[] expected) => Assert.Equal(expected.Order(StringComparer.Ordinal), LocalFiles(root));

    /// <summary>The first path in the first column of each row of the guide's housekeeping tables, in order.</summary>
    private static string[] HousekeepingTable(string guide)
    {
        int start = guide.IndexOf("\n### Housekeeping, listed exactly\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "The guide no longer lists the housekeeping.");
        int end = guide.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
        string section = guide[start..(end < 0 ? guide.Length : end)];
        return [.. section.Split('\n').Where(line => line.StartsWith("| `", StringComparison.Ordinal)).Select(line => line[3..line.IndexOf('`', 3)])];
    }

    /// <summary>
    /// The guide's listing of the files beyond the inventory, filled with this run's values: each line's path matches exactly one file, of
    /// the class the line names, and every file beyond the inventory is on a line. .git/ is git's own and only named.
    /// </summary>
    private static void AssertListing(string guide, Dictionary<string, string> beyond, Dictionary<string, string> values)
    {
        Match listing = Assert.Single(Fence().Matches(guide), m => m.Groups["language"].Value == "text" && m.Groups["body"].Value.Contains(".vericairn/write-lock/write.lock", StringComparison.Ordinal));
        var unmatched = new Dictionary<string, string>(beyond, StringComparer.Ordinal);
        foreach (string line in listing.Groups["body"].Value.Split('\n'))
        {
            string[] parts = line.Split("  ", 2);
            string path = parts[0].Trim(), described = parts[1].Trim();
            if (path == ".git/")
            {
                Assert.Equal("git's own", described);
                continue;
            }

            string pattern = "^" + Placeholder().Replace(Regex.Escape(path), m => m.Value == "<yyyy-mm>" ? "[0-9]{4}-[0-9]{2}" : Regex.Escape(values[m.Value])) + "$";
            string file = Assert.Single(unmatched.Keys, key => Regex.IsMatch(key, pattern));
            string named = described.StartsWith(Supplemental + ",", StringComparison.Ordinal) ? Supplemental : described;
            Assert.Equal(named, unmatched[file]);
            unmatched.Remove(file);
        }

        Assert.Empty(unmatched);
    }

    private static string Now() => RecordTime(DateTimeOffset.UtcNow);

    private static string RecordTime(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static void Copy(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string Git(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in GitSettings.Concat(arguments)) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit((int)TestTiming.HangTimeout.TotalMilliseconds));
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    [GeneratedRegex(@"^```(?<language>[a-z]+)(?: (?<name>[a-z0-9.-]+))?\n(?<body>.*?)\n```$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"<[a-z0-9-]+>")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\A\.vericairn/toolchain/(?:release/[^/]+/[^/]+|source/[^/]+/bin|vericairn-src)/.+\z")]
    private static partial Regex Toolchain();

    [GeneratedRegex(@"\A(?:\.vericairn/(?:record-staging|write-lock|document-staging)/[0-9A-HJKMNP-TV-Z]{26}\.tmp|objects/\.[0-9a-f]{64}\.tmp-[0-9]+|estate/manifest\.json\.(?:upgrading|naming)|(?:bootstrap|bootstrap\.ps1|\.gitattributes|\.gitignore)\.[0-9a-f]{32}\.tmp)\z")]
    private static partial Regex Residue();
}
