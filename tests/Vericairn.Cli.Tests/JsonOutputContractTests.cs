// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.CommandLine.Completions;
using System.Diagnostics;
using System.Text.Json;
using Vericairn.Records.Updates;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

/// <summary>
/// Every command accepts the global --json. Each one writes decodable JSON with it, or refuses the option before it runs; doctor writes
/// one documented result with each probe's state, its text report is unchanged, and release notices stay on standard error.
/// </summary>
[Collection("cli")]
public sealed class JsonOutputContractTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Absent = "01ARZ3NDEKTSV4RRFFQ69G5FAV";

    // Commands this test does not run, each for its effect beyond the test's estate and home or for running until it is stopped.
    private static readonly Dictionary<string, string> NotRun = new(StringComparer.Ordinal)
    {
        ["runtime start"] = "starts a daemon process",
        ["runtime install"] = "registers a service with the operating system",
        ["runtime uninstall"] = "removes a service from the operating system",
        ["events watch"] = "follows the event stream until interrupted",
        ["worker run"] = "launches an agent harness",
        ["worker qualify"] = "runs an agent harness's probes",
        ["check run"] = "runs the named command",
        ["connections add"] = "connects to a runtime at the named address",
        ["devices pair"] = "waits for a device to pair",
        ["estate upgrade"] = "reads the published releases",
        ["release submit"] = "dispatches to a release provider",
        ["release observe"] = "reads a release provider",
        ["release withdraw"] = "reads a release provider",
        ["changes push"] = "pushes to a repository host",
        ["changes open"] = "opens a pull request at a repository host",
        ["changes merge"] = "merges at a repository host",
        ["changes observe"] = "reads a repository host",
        ["packages observe"] = "reads a package registry",
        ["packages unlist"] = "records a package registry action",
        ["packages deprecate"] = "records a package registry action",
        ["security-reports poll"] = "reads a repository host's advisories",
        ["security-reports triage"] = "reads a repository host's advisory",
    };

    private static readonly HashSet<string> PathNames = new(StringComparer.Ordinal)
    {
        "path", "file", "files", "directory", "package", "questions", "request", "candidate", "command",
        "--out", "--output", "--body", "--brief", "--catalog", "--contracts", "--index", "--plan", "--destination", "--inventory",
        "--observations", "--before", "--after", "--from", "--to", "--with", "--judgments",
    };

    private readonly string root = CliHarness.TempDirectory("json-contract");
    private readonly string scratch = CliHarness.TempDirectory("json-contract-scratch");

    [Fact]
    public void EveryCommandWritesDecodableJsonOrRefusesTheOption()
    {
        Init(root);
        var commands = new List<(string Path, Command Command)>();
        Walk(CliApp.Build(cancellationToken: TestContext.Current.CancellationToken), string.Empty, commands);
        Assert.All(NotRun.Keys, path => Assert.Contains(commands, c => c.Path == path));

        var failures = new List<string>();
        int ran = 0;
        foreach ((string path, Command command) in commands.Where(c => !NotRun.ContainsKey(c.Path)))
        {
            List<string> args = ["--estate", root, "--json", .. path.Split(' ')];
            foreach (Argument argument in command.Arguments.Where(a => a.Arity.MinimumNumberOfValues > 0)) args.Add(Value(argument.Name));
            foreach (Option option in command.Options.Where(o => o.Required))
            {
                // An option restricted to named values takes the first of them, so the command itself runs.
                string? named = option.GetCompletions(CompletionContext.Empty).Select(c => c.Label).FirstOrDefault();
                args.AddRange([option.Name, option.ValueType == typeof(bool) ? "true" : named ?? Value(option.Name)]);
            }

            string[] errors = CliApp.Build(cancellationToken: TestContext.Current.CancellationToken).Parse(args).Errors.Select(e => e.Message).ToArray();
            if (errors.Any(e => !e.StartsWith("--json does not apply to ", StringComparison.Ordinal)))
            {
                failures.Add($"{path}: the test's invocation does not parse: {string.Join("; ", errors)}");
                continue;
            }

            CliHarness.Outcome outcome = CliHarness.Run([.. args]);
            ran++;
            if (command is ITextOutputCommand)
            {
                // Refused before it runs: the parse error names --json, and the command writes nothing of its own.
                if (outcome.Code != 1 || !outcome.Err.Contains("--json does not apply to vericairn " + path, StringComparison.Ordinal))
                    failures.Add($"{path}: exit {outcome.Code}, expected a refusal of --json\n{outcome.Err}");
            }
            else if (!Decodable(outcome.Out))
            {
                failures.Add($"{path}: exit {outcome.Code}, standard output is not JSON:\n{outcome.Out}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
        Assert.True(ran > 140, $"Only {ran} commands ran.");
    }

    [Fact]
    public void DoctorWritesOneResultWithEveryProbeAndKeepsItsTextReport()
    {
        Init(root);
        CliHarness.Outcome json = CliHarness.Ok("--estate", root, "--json", "doctor");
        using JsonDocument document = JsonDocument.Parse(json.Out);
        JsonElement result = document.RootElement;
        string[] probes = ["version", "runtime", "git", "dotnet", "estate", "custody", "enclave", "records", "search", "daemon"];
        Assert.Equal(probes.Append("stateWarnings").Order(StringComparer.Ordinal), result.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        foreach (string name in probes)
        {
            JsonElement probe = result.GetProperty(name);
            Assert.Equal(["detail", "state", "value"], probe.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.True(probe.GetProperty("state").GetString() is "present" or "missing" or "unavailable" or "not-applicable", name);
        }

        Assert.Equal(JsonValueKind.Array, result.GetProperty("stateWarnings").ValueKind);
        Assert.Equal("present", State(result, "estate"));
        Assert.Equal(Path.GetFullPath(root), result.GetProperty("estate").GetProperty("value").GetString());
        Assert.Equal("missing", State(result, "daemon"));
        Assert.Equal("1, 2, 3", result.GetProperty("records").GetProperty("value").GetString());
        Assert.Equal(OperatingSystem.IsMacOS() ? JsonValueKind.String : JsonValueKind.Null, result.GetProperty("enclave").GetProperty("detail").ValueKind);
        if (!OperatingSystem.IsMacOS()) Assert.Equal("not-applicable", State(result, "enclave"));

        // The text report keeps its lines, and they state the same facts.
        string[] lines = CliHarness.Ok("--estate", root, "doctor").Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("vericairn " + Value(result, "version"), lines[0]);
        Assert.Equal($"runtime   {Value(result, "runtime")} on {Detail(result, "runtime")}", lines[1]);
        Assert.Equal("git       " + (Value(result, "git") ?? Detail(result, "git")), lines[2]);
        Assert.Equal("dotnet    " + (Value(result, "dotnet") ?? Detail(result, "dotnet")), lines[3]);
        Assert.Equal($"estate    {Value(result, "estate")} (manifest present)", lines[4]);
        Assert.StartsWith($"custody   {Value(result, "custody")} (", lines[5], StringComparison.Ordinal);
        Assert.EndsWith(Detail(result, "custody")!.Split("; per-user state at ")[1], lines[5], StringComparison.Ordinal);
        int next = 6;
        if (OperatingSystem.IsMacOS()) Assert.StartsWith("enclave   Secure Enclave custody ", lines[next++], StringComparison.Ordinal);
        Assert.Equal($"records   reads record formats 1, 2, 3; {Detail(result, "records")}", lines[next++]);
        Assert.StartsWith("search    ", lines[next++], StringComparison.Ordinal);
        Assert.Equal("daemon    none; commands run the embedded runtime", lines[^1]);
        Assert.Equal(next + 1 + result.GetProperty("stateWarnings").GetArrayLength(), lines.Length);
    }

    [Fact]
    public void DoctorOnAPathThatIsNotAnEstateSaysSoInJson()
    {
        string elsewhere = Path.Combine(scratch, "not-an-estate");
        Directory.CreateDirectory(elsewhere);
        CliHarness.Outcome json = CliHarness.Ok("--estate", elsewhere, "--json", "doctor");
        using JsonDocument document = JsonDocument.Parse(json.Out);
        JsonElement estate = document.RootElement.GetProperty("estate");
        Assert.Equal("missing", estate.GetProperty("state").GetString());
        Assert.Equal(Path.GetFullPath(elsewhere), estate.GetProperty("value").GetString());
        Assert.Equal("not an estate root (no estate/manifest.json)", estate.GetProperty("detail").GetString());
        Assert.Equal("not-applicable", State(document.RootElement, "daemon"));
        Assert.DoesNotContain("daemon", CliHarness.Ok("--estate", elsewhere, "doctor").Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorNamesAMissingOptionalToolAsMissing()
    {
        // The built command line with a PATH that holds neither git nor dotnet.
        string empty = Path.Combine(scratch, "empty-path");
        Directory.CreateDirectory(empty);
        (int code, string output, string error) = await Built(empty, "--estate", scratch, "--json", "doctor");
        Assert.True(code == 0, error);
        using JsonDocument document = JsonDocument.Parse(output);
        foreach ((string tool, string detail) in new[] { ("git", "not found on PATH"), ("dotnet", "not found on PATH (needed only to build from source)") })
        {
            JsonElement probe = document.RootElement.GetProperty(tool);
            Assert.Equal("missing", probe.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, probe.GetProperty("value").ValueKind);
            Assert.Equal(detail, probe.GetProperty("detail").GetString());
        }

        (int textCode, string text, _) = await Built(empty, "--estate", scratch, "doctor");
        Assert.Equal(0, textCode);
        Assert.Contains("git       not found on PATH\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("notice")]
    [InlineData("disabled")]
    [InlineData("offline")]
    public void ReleaseNoticesStayOnStandardErrorWhateverTheSetting(string setting)
    {
        Init(root);
        string home = Path.Combine(scratch, "updates-" + setting);
        Directory.CreateDirectory(home);
        var store = new ReleaseUpdateStore(home);
        if (setting == "disabled") store.Configure(false);
        var source = new ReleaseUpdatesTests.Source();
        if (setting == "offline") source.Answer = _ => throw new HttpRequestException("offline stand-in");
        var updates = new ReleaseUpdates(store, source, new ReleaseUpdatesTests.Clock());
        foreach (string command in new[] { "doctor", "version" })
        {
            CliHarness.Outcome outcome = CliHarness.RunWithUpdates(updates, "--estate", root, "--json", command);
            Assert.Equal(0, outcome.Code);
            using JsonDocument document = JsonDocument.Parse(outcome.Out);
            Assert.DoesNotContain("Release check", outcome.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("Newer Vericairn release", outcome.Out, StringComparison.Ordinal);
            Assert.Contains(setting == "disabled" ? "Release check: off" : "Release check: on", outcome.Err, StringComparison.Ordinal);
            Assert.Equal(setting == "notice", outcome.Err.Contains("Newer Vericairn release: 9.2.0", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheCommandsThatWroteTextNowWriteJson()
    {
        Init(root);
        Assert.Equal(CommandContext.VersionText(), CliHarness.Ok("--json", "version").Json.GetProperty("version").GetString());

        JsonElement types = CliHarness.Ok("--json", "records", "describe").Json;
        Assert.Contains(types.GetProperty("types").EnumerateArray(), t => t.GetProperty("name").GetString() == "observation");
        Assert.Equal(3, types.GetProperty("writesAtMost").GetInt32());
        JsonElement observation = CliHarness.Ok("--json", "records", "describe", "observation").Json;
        JsonElement asOf = observation.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "asOf");
        Assert.Equal("timestamp (YYYY-MM-DDTHH:MM:SSZ)", asOf.GetProperty("kind").GetString());
        Assert.True(asOf.GetProperty("required").GetBoolean());

        string charter = Directory.EnumerateFiles(Path.Combine(root, "records", "estate"), "*.json", SearchOption.AllDirectories).First();
        string hash = CliHarness.Ok("records", "hash", charter).Out.Trim();
        Assert.Equal(hash, CliHarness.Ok("--json", "records", "hash", charter).Json.GetProperty("hash").GetString());
        JsonElement formatted = CliHarness.Ok("--json", "records", "format", charter).Json;
        Assert.Empty(formatted.GetProperty("formatted").EnumerateArray());
        Assert.Equal(charter, Assert.Single(formatted.GetProperty("unchanged").EnumerateArray()).GetString());

        JsonElement compared = CliHarness.Ok("--estate", root, "--json", "recover", "compare", "--with", root).Json;
        Assert.True(compared.GetProperty("sameEstate").GetBoolean());
        Assert.Empty(compared.GetProperty("onlyHere").EnumerateArray());
        Assert.Empty(compared.GetProperty("onlyThere").EnumerateArray());
        Assert.Empty(compared.GetProperty("differing").EnumerateArray());

        // A command whose report is text refuses --json and names where the facts are as JSON; without it, the report is unchanged.
        CliHarness.Outcome inspect = CliHarness.Run("--estate", root, "--json", "recover", "inspect");
        Assert.Equal(1, inspect.Code);
        Assert.Contains("--json does not apply to vericairn recover inspect: its report is text. status --json", inspect.Err, StringComparison.Ordinal);
        Assert.Contains("Authoring from this machine", CliHarness.Ok("--estate", root, "recover", "inspect").Out, StringComparison.Ordinal);
    }

    public void Dispose() => CliHarness.Delete(root, scratch);

    private string Value(string name) => PathNames.Contains(name) ? Path.Combine(scratch, "absent", name.TrimStart('-')) : Absent;

    private static void Walk(Command command, string path, List<(string, Command)> commands)
    {
        if (command.Action is not null && path.Length > 0) commands.Add((path, command));
        foreach (Command child in command.Subcommands) Walk(child, path.Length == 0 ? child.Name : path + " " + child.Name, commands);
    }

    // Empty, one JSON value, or one JSON value per line for a command that writes a stream.
    private static bool Decodable(string output)
    {
        if (output.Trim().Length == 0) return true;
        try { using JsonDocument _ = JsonDocument.Parse(output); return true; }
        catch (JsonException)
        {
            try
            {
                foreach (string line in output.Split('\n').Where(l => l.Trim().Length > 0)) { using JsonDocument __ = JsonDocument.Parse(line); }
                return true;
            }
            catch (JsonException) { return false; }
        }
    }

    private static string? State(JsonElement result, string probe) => result.GetProperty(probe).GetProperty("state").GetString();

    private static string? Value(JsonElement result, string probe) => result.GetProperty(probe).GetProperty("value").GetString();

    private static string? Detail(JsonElement result, string probe) => result.GetProperty(probe).GetProperty("detail").GetString();

    private static void Init(string estate) =>
        CliHarness.Ok("--estate", estate, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "JSON contract.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);

    private static async Task<(int Code, string Output, string Error)> Built(string path, params string[] args)
    {
        var info = new ProcessStartInfo(CliHarness.BuiltExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        info.Environment["PATH"] = path;
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(TestContext.Current.CancellationToken), output, error)
                .WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }

        return (process.ExitCode, await output, await error);
    }
}
