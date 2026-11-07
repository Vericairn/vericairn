// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>
/// DOCS-29: the large private artifacts guide's examples, run as written. The record bodies are read from the guide's named JSON
/// blocks and every command is matched against the guide's text before it runs, so the guide and this check cannot drift apart.
/// </summary>
[Collection("cli")]
public sealed partial class LargeArtifactGuideTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Brief = """{"outcome":"The original of candidate C-0001, kept in custody.","sources":[],"permittedChanges":["none"],"prohibitedEffects":["no publication"],"workspace":["example"],"limits":[],"expectedOutputs":["C-0001 original"],"reviewCriteria":["hash and bytes match the intake"],"recovery":"Read the last result."}""";
    private static readonly string[] Blocks = ["store-credential.json", "store.json", "intake.json", "intake-dispatched.json", "intake-observed.json", "intake-failed.json", "result.json", "readback.json", "readback-failed.json"];
    private static readonly string[] GitSettings = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];

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
        string restored = Path.Combine(isolated, "estate");
        string restoredStore = Path.Combine(isolated, "store");
        string? previousHome = Environment.GetEnvironmentVariable(VericairnHome.EnvironmentVariable);
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["<restore-directory>"] = restored };
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["estate-export.zip"] = Path.Combine(scratch, "estate-export.zip") };
        try
        {
            Git(live, "init", "-q", "-b", "main");
            values["<operator-id>"] = CliHarness.Ok("--estate", live, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Custody of large private artifacts.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
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

            // Cold recovery on another machine: the export and the store's backup, restored in an isolated place.
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, isolatedHome);
            CliHarness.Outcome restore = Step(guide, values, files, blocks, scratch, live, "vericairn estate restore --from estate-export.zip --to <restore-directory>");
            Assert.Equal(0, restore.Json.GetProperty("errors").GetInt32());
            Assert.Equal(0, restore.Json.GetProperty("Objects").GetInt32());
            Assert.DoesNotContain(Directory.EnumerateFiles(restored, "*", SearchOption.AllDirectories), f => new FileInfo(f).Length == size);
            Copy(backup, restoredStore);

            // The restored records say which bytes to recover; only bytes read from the restored store recover them.
            using JsonDocument restoredResult = JsonDocument.Parse(Step(guide, values, files, blocks, scratch, live, "vericairn --estate <restore-directory> records show <result-id>").Out);
            using JsonDocument restoredIntake = JsonDocument.Parse(Step(guide, values, files, blocks, scratch, live, "vericairn --estate <restore-directory> records show <intake-id>").Out);
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
        }
        finally
        {
            Environment.SetEnvironmentVariable(VericairnHome.EnvironmentVariable, previousHome);
            CliHarness.Delete(live, failed, scratch, store, backup, isolated, isolatedHome);
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

    private static string Guide()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root, "docs", "large-artifacts.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
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
}
