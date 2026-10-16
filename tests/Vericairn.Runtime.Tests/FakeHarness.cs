// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// The stand-in for a coding-agent harness in every test that launches one, so that no real agent ever runs: the test executable
/// itself, started with <see cref="Marker"/>, a scenario and a report file, behaves as the scenario says and exits before the test
/// platform starts. It speaks Claude Code's stream-json or Codex's JSON events, reports usage or none, exits, acknowledges a stop, or
/// ignores one; and it appends what it was given, its arguments and its environment, to the report as one JSON line. The same
/// process on every platform the tests run on, since it is the tests' own .NET executable.
/// </summary>
internal static class FakeHarness
{
    private static readonly string[] ReportedTools = ["Bash", "Read", "vericairn"];
    private static readonly string[] GitIdentity = ["-c", "user.name=Stand-in", "-c", "user.email=stand-in@example.invalid", "-c", "commit.gpgsign=false"];

    /// <summary>The argument that makes the test executable a stand-in harness.</summary>
    public const string Marker = "--vericairn-stand-in-harness";

    /// <summary>The command line that starts the stand-in for a scenario, writing what it was given to the report.</summary>
    public static List<string> Command(string scenario, string report)
    {
        string process = Environment.ProcessPath ?? throw new InvalidOperationException("The test process has no path.");
        var command = new List<string> { process };
        if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            command.Add(typeof(FakeHarness).Assembly.Location);
        }

        command.AddRange([Marker, scenario, report]);
        return command;
    }

    internal static List<string> ScopeCommand()
    {
        List<string> command = Command("unused", "unused");
        command.RemoveRange(command.Count - 3, 3);
        return command;
    }

    /// <summary>The command setting a launcher takes to start the stand-in: a JSON array.</summary>
    public static string Setting(string scenario, string report) => JsonSerializer.Serialize(Command(scenario, report));

    /// <summary>What the stand-in reported, one JSON object for each time it was started.</summary>
    public static List<JsonObject> Reports(string report) =>
        File.Exists(report) ? File.ReadAllLines(report).Where(l => l.Length > 0).Select(l => (JsonObject)JsonNode.Parse(l)!).ToList() : [];

    /// <summary>Runs as the stand-in, before the test platform starts, when the process was started with the marker.</summary>
    [ModuleInitializer]
#pragma warning disable CA2255 // The test executable is an application: starting it with the marker makes it the stand-in harness.
    internal static void Intercept()
#pragma warning restore CA2255
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(arguments, Marker);
        if (at < 0 || at + 2 >= arguments.Length)
        {
            return;
        }

        int code;
        try
        {
            code = Run(arguments[at + 1], arguments[at + 2], arguments[(at + 3)..]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine("stand-in harness: " + exception.Message);
            code = 70;
        }

        Console.Out.Flush();
        Environment.Exit(code);
    }

    private static int Run(string scenario, string report, string[] arguments)
    {
        var environment = new JsonObject();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = entry.Value as string;
        }

        if (arguments.Contains("--vericairn-execution-preflight", StringComparer.Ordinal))
        {
            IReadOnlyList<Vericairn.Runtime.Workers.RunComponent> inventory = ReportedLauncher.Inventory;
            if (scenario == "inventory-unexpected") inventory = [.. inventory, inventory[0] with { Id = "unexpected-hook", Kind = "hook" }];
            if (scenario == "inventory-route") inventory = [inventory[0] with { Destinations = ["undeclared-route"] }, .. inventory.Skip(1)];
            string revision = "revision-1";
            if (scenario == "inventory-changes")
            {
                string counter = report + ".preflights";
                revision = File.Exists(counter) ? "revision-2" : "revision-1";
                File.WriteAllText(counter, "observed");
            }
            Emit(JsonSerializer.Serialize(new Vericairn.Runtime.Workers.RunPreflight(inventory, scenario != "inventory-incomplete", scenario != "inventory-wider-environment", "1", revision), Vericairn.Protocol.ProtocolJson.Options));
            return 0;
        }

        var line = new JsonObject
        {
            ["scenario"] = scenario,
            ["arguments"] = new JsonArray(arguments.Select(a => (JsonNode)a).ToArray()),
            ["environment"] = environment,
            ["directory"] = Directory.GetCurrentDirectory(),
            ["process"] = Environment.ProcessId,
            ["group"] = OperatingSystem.IsWindows() ? 0 : getpgid(0),
        };
        using PosixSignalRegistration? terminate = scenario.StartsWith("watch-", StringComparison.Ordinal) || scenario is "codex-spender" or "codex-headroom" or "ignore-stop" or "ack-stop" or "silent-ignore" or "child-survives-root" ? HoldTerminate() : null;
        // Publish the complete report atomically, after installing the stop handler. Existence is a readiness barrier.
        if (scenario != "child-survives-root")
        {
            PublishReport();
        }

        void PublishReport()
        {
            string prior = File.Exists(report) ? File.ReadAllText(report) : string.Empty;
            File.WriteAllText(report + ".new", prior + line.ToJsonString() + "\n");
            File.Move(report + ".new", report, overwrite: true);
        }
        if (arguments.Contains("mcp") && arguments.Contains("list"))
        {
            var configuration = JsonNode.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("VERICAIRN_MCP_CONFIG")!))!.AsObject();
            JsonNode server = configuration["mcpServers"]!["vericairn"]!;
            var listed = new JsonArray(new JsonObject
            {
                ["name"] = "vericairn", ["enabled"] = true,
                ["transport"] = new JsonObject
                {
                    ["type"] = "stdio", ["command"] = server["command"]!.DeepClone(), ["args"] = server["args"]!.DeepClone(),
                    ["env"] = server["env"]!.DeepClone(), ["env_vars"] = new JsonArray(), ["cwd"] = null,
                },
            });
            if (scenario == "codex-ambient")
            {
                listed.Add(new JsonObject { ["name"] = "ambient", ["enabled"] = true });
            }

            Emit(listed.ToJsonString());
            return 0;
        }

        bool listing = arguments.Contains("features") && arguments.Contains("list");
        if (listing && scenario.StartsWith("watch-codex", StringComparison.Ordinal))
        {
            Emit("goals stable true");
            return 0;
        }

        switch (scenario)
        {
            case "execution-audit":
                Emit("""{"vericairnExecution":{"model":"model-b","effort":"low","tools":["Bash"]},"type":"assistant","message":{"content":[{"type":"tool_use","id":"c1","name":"Bash","input":{"command":"curl https://example.invalid; cat .aws/credentials"}}]}}""");
                return 0;
            case "execution-tool":
                Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"c1","name":"unexpected-tool","input":{"path":".aws/credentials"}}]}}""");
                return 0;
            case "execution" or "execution-different":
                int modelAt = Array.IndexOf(arguments, "--model");
                int effortAt = Array.IndexOf(arguments, "--effort");
                Emit(JsonSerializer.Serialize(new { vericairnExecution = new { model = scenario == "execution-different" ? "model-b" : arguments[modelAt + 1],
                    modelRevision = "revision-1", effort = scenario == "execution-different" ? "low" : arguments[effortAt + 1], tools = ReportedTools } }));
                return 0;

            case "watch-codex-critical-hold":
                Emit("""{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"cat .aws/credentials"}}""");
                return Linger(acknowledge: false);
            case "watch-codex-warning-hold":
                Emit("""{"type":"item.started","item":{"id":"c1","type":"command_execution","command":"curl https://example.invalid"}}""");
                return Linger(acknowledge: true);
            case "watch-codex-loop":
                for (int i = 0; i < 10; i++)
                {
                    Emit(JsonSerializer.Serialize(new { type = "item.started", item = new { id = "call-" + i, type = "command_execution", command = "echo stand-in" } }));
                    Emit(JsonSerializer.Serialize(new { type = "item.completed", item = new { id = "call-" + i, type = "command_execution", command = "echo stand-in", aggregated_output = "stand-in" } }));
                }

                return 0;
            case "watch-codex-burn":
                Emit("""{"type":"turn.completed","usage":{"input_tokens":199990,"output_tokens":10}}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":199990,"output_tokens":10}}""");
                return 0;
            case "watch-claude-usage":
                Emit("""{"type":"assistant","message":{"id":"m1","usage":{"input_tokens":100,"output_tokens":20}}}""");
                Emit("""{"type":"assistant","message":{"id":"m1","usage":{"input_tokens":100,"output_tokens":20}}}""");
                Emit("""{"type":"assistant","message":{"id":"m1","usage":{"input_tokens":100,"output_tokens":25}}}""");
                Emit("""{"type":"result","usage":{"input_tokens":100,"output_tokens":25,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}""");
                return 0;
            case "watch-claude-audit":
                Emit("""{"type":"assistant","message":{"content":[{"type":"text","text":"I claim everything is within scope."},{"type":"tool_use","id":"n1","name":"Bash","input":{"command":"curl https://example.invalid"}},{"type":"tool_use","id":"s1","name":"Read","input":{"file_path":".aws/credentials"}},{"type":"tool_use","id":"p1","name":"Write","input":{"file_path":"outside.txt","content":"synthetic"}}]}}""");
                return 0;
            case "watch-codex-audit":
                Emit("""{"type":"item.started","item":{"id":"n1","type":"command_execution","command":"curl https://example.invalid"}}""");
                Emit("""{"type":"item.completed","item":{"id":"n1","type":"command_execution","command":"curl https://example.invalid","aggregated_output":"No actual command ran.","exit_code":0}}""");
                Emit("""{"type":"item.started","item":{"id":"s1","type":"mcp_tool_call","server":"files","tool":"read","arguments":{"path":".aws/credentials"}}}""");
                Emit("""{"type":"item.completed","item":{"id":"p1","type":"file_change","changes":[{"path":"outside.txt","kind":"add"}]}}""");
                return 0;
            case "claude":
                Emit("""{"type":"system","subtype":"init","session_id":"stand-in","mcp_servers":[{"name":"vericairn","status":"connected"}]}""");
                Emit("""{"type":"assistant","message":{"id":"m1","type":"message","role":"assistant","content":[{"type":"text","text":"Working on the assignment."}],"usage":{"input_tokens":100,"output_tokens":20}}}""");
                Emit("""{"type":"result","subtype":"success","is_error":false,"duration_ms":1200,"num_turns":2,"result":"Done.","total_cost_usd":0.0123,"usage":{"input_tokens":1000,"cache_creation_input_tokens":200,"cache_read_input_tokens":300,"output_tokens":234}}""");
                return 0;
            case "claude-silent":
                Emit("""{"type":"system","subtype":"init","session_id":"stand-in"}""");
                Emit("""{"type":"assistant","message":{"id":"m1","type":"message","role":"assistant","content":[{"type":"text","text":"Working."}]}}""");
                return 0;
            case "claude-fails":
                Emit("""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":1,"usage":{"input_tokens":50,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":5}}""");
                return 1;
            case "codex" or "codex-ambient" or "codex-spender" or "codex-incomplete" or "codex-overflow" or "codex-unfinished" or "codex-headroom" when listing:
                Emit("apps                                     stable             true");
                Emit("goals                                    stable             true");
                Emit("hooks                                    stable             true");
                return 0;
            case "codex":
                Emit("""{"type":"thread.started","thread_id":"stand-in"}""");
                Emit("""{"type":"turn.started"}""");
                Emit("""{"type":"item.completed","item":{"id":"i0","type":"agent_message","text":"Working."}}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":800,"cached_input_tokens":100,"output_tokens":150}}""");
                Emit("""{"type":"turn.started"}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":400,"cached_input_tokens":0,"output_tokens":50}}""");
                return 0;
            case "codex-incomplete":
                Emit("""{"type":"turn.completed","usage":{}}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":2}}""");
                return 0;
            case "codex-overflow":
                Emit("""{"type":"turn.completed","usage":{"input_tokens":9223372036854775807,"output_tokens":0}}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":0}}""");
                return 0;
            case "codex-unfinished":
                Emit("""{"type":"turn.started"}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":2}}""");
                Emit("""{"type":"turn.started"}""");
                return 1;
            case "claude-incomplete":
                Emit("""{"type":"result","usage":{"input_tokens":1,"output_tokens":2}}""");
                return 0;
            case "codex-headroom":
                Emit("""{"type":"turn.completed","usage":{"input_tokens":800,"output_tokens":150}}""");
                return Linger(acknowledge: true);
            case "codex-spender":
                Emit("""{"type":"thread.started","thread_id":"stand-in"}""");
                Emit("""{"type":"turn.completed","usage":{"input_tokens":900,"cached_input_tokens":0,"output_tokens":150}}""");
                return Linger(acknowledge: true);
            case "codex-goals-off" when listing:
                Emit("goals                                    stable             false");
                return 0;
            case "codex-no-goals" when listing:
                Emit("apps                                     stable             true");
                return 0;
            case "codex-broken" when listing:
                Console.Error.WriteLine("error: unrecognized subcommand 'features'");
                return 2;
            case "scope-edits":
                Directory.CreateDirectory("src");
                Directory.CreateDirectory("docs");
                Directory.CreateDirectory("ignored");
                File.WriteAllText("src/allowed.cs", "permitted source");
                File.WriteAllText("docs/allowed.md", "permitted document");
                File.WriteAllText(".gitignore", "ignored/\n");
                File.WriteAllText("ignored/outside.txt", "ignored but outside scope");
                File.WriteAllText("outside.txt", "untracked and outside scope");
                File.WriteAllText("committed.txt", "committed and outside scope");
                File.Move("README.md", "README-old.md");
                Git( "add", "README.md", "README-old.md", "src", "docs", ".gitignore", "committed.txt");
                Git( "commit", "-q", "-m", "Stand-in changes");
                return 0;
            case "generic":
                Emit("The generic harness ran.");
                return 0;
            case "child-survives-root":
                var childInfo = new ProcessStartInfo { FileName = Command("ignore-stop", report + ".child")[0], UseShellExecute = false };
                foreach (string argument in Command("ignore-stop", report + ".child").Skip(1))
                {
                    childInfo.ArgumentList.Add(argument);
                }

                using (Process.Start(childInfo))
                {
                    WaitForFile(report + ".child");
                    PublishReport();
                    return Linger(acknowledge: true);
                }
            case "silent-ignore":
                return Linger(acknowledge: false, output: false);
            case "silent-recovery":
                WaitForFile(report + ".recover");
                Emit("{\"event\":\"working\"}");
                File.WriteAllText(report + ".recovered", "ready");
                WaitForFile(report + ".finish");
                return 0;
            case "ignore-stop":
                return Linger(acknowledge: false);
            case "ack-stop":
                return Linger(acknowledge: true);
            case "scope-hang":
                Thread.Sleep(TimeSpan.FromMinutes(2));
                return 0;
            case "close-output" or "close-output-short":
                Emit("Closing my output and working on.");
                Console.Out.Close();
                Console.Error.Close();
                if (scenario == "close-output")
                {
                    File.WriteAllText(report + ".closed", "ready");
                    WaitForFile(report + ".finish");
                }
                else
                {
                    Thread.Sleep(TimeSpan.FromSeconds(3)); // Native detached-owner test's eventual exit, not an observation window.
                }

                return 0;
            case not null when scenario.StartsWith("mcp:", StringComparison.Ordinal):
                return Mcp(scenario["mcp:".Length..], arguments);
            case "mcp":
                return Mcp(null, arguments);
            default:
                Console.Error.WriteLine($"stand-in harness: no scenario {scenario} for {string.Join(' ', arguments)}");
                return 64;
        }
    }

    /// <summary>
    /// Does what a coding agent does with the worker's MCP configuration: starts the server it names, records an attempt on the
    /// assignment and then its result through the server's tools, and reports done the way Claude Code's stream does. Without an
    /// assignment named, it takes the latest one of its own principal from the brief the worker exported, as an agent reads its brief.
    /// </summary>
    private static int Mcp(string? assignment, string[] arguments)
    {
        string configuration = arguments[Array.IndexOf(arguments, "--mcp-config") + 1];
        JsonObject server = JsonNode.Parse(File.ReadAllText(configuration))!["mcpServers"]!["vericairn"]!.AsObject();
        if (assignment is null)
        {
            List<string> args = server["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
            string agent = args[args.IndexOf("--as") + 1];
            string brief = File.ReadAllText(Environment.GetEnvironmentVariable("VERICAIRN_BRIEF") ?? throw new IOException("No brief was exported."));
            assignment = System.Text.RegularExpressions.Regex.Matches(brief, @"^## Assignment (?<id>[0-9A-Z]{26}) to .* \((?<to>[0-9A-Z]{26})\)$", System.Text.RegularExpressions.RegexOptions.Multiline)
                .Where(m => m.Groups["to"].Value == agent).Select(m => m.Groups["id"].Value).LastOrDefault() ?? throw new IOException("The brief names no assignment of " + agent + ".");
        }

        var info = new ProcessStartInfo(server["command"]!.GetValue<string>()) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (JsonNode? argument in server["args"]!.AsArray())
        {
            info.ArgumentList.Add(argument!.GetValue<string>());
        }

        foreach ((string name, JsonNode? value) in server["env"]!.AsObject())
        {
            info.Environment[name] = value!.GetValue<string>();
        }

        using Process mcp = Process.Start(info)!;
        Task<string> errors = mcp.StandardError.ReadToEndAsync();
        int next = 1;
        JsonObject Call(string method, JsonObject parameters)
        {
            mcp.StandardInput.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = next++, ["method"] = method, ["params"] = parameters }.ToJsonString());
            mcp.StandardInput.Flush();
            return (JsonObject)JsonNode.Parse(mcp.StandardOutput.ReadLine() ?? throw new IOException("The MCP server closed its output: " + errors.Result))!;
        }

        Call("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject { ["name"] = "stand-in", ["version"] = "1" } });
        JsonObject attempt = Call("tools/call", new JsonObject { ["name"] = "work_attempt", ["arguments"] = new JsonObject { ["assignment"] = assignment, ["workspace"] = "widget, branch work/w-1", ["note"] = "Build the widget toward the exit question." } });
        string attempted = Text(attempt);
        string id = JsonNode.Parse(attempted)!["written"]?["id"]?.GetValue<string>() ?? throw new IOException("No attempt was recorded: " + attempted);
        JsonObject result = Call("tools/call", new JsonObject
        {
            ["name"] = "work_result",
            ["arguments"] = new JsonObject
            {
                ["attempt"] = id,
                ["body"] = new JsonObject { ["outcome"] = "completed", ["summary"] = "The widget builds and its tests pass. Done.", ["outputs"] = new JsonArray(), ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(), ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Review the result." },
            },
        });
        mcp.StandardInput.Close();
        mcp.WaitForExit(30_000);
        if (result["result"]?["isError"]?.GetValue<bool>() != false)
        {
            Console.Error.WriteLine("stand-in harness: the result was not recorded: " + Text(result));
            return 1;
        }

        Emit("""{"type":"result","subtype":"success","is_error":false,"num_turns":3,"result":"Done.","usage":{"input_tokens":500,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":100}}""");
        return 0;
    }

    private static void Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in GitIdentity.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process git = Process.Start(info) ?? throw new IOException("The stand-in could not start git.");
        Task<string> output = git.StandardOutput.ReadToEndAsync();
        Task<string> error = git.StandardError.ReadToEndAsync();
        if (!git.WaitForExit(30_000))
        {
            git.Kill(entireProcessTree: true);
            throw new IOException("The stand-in git command timed out.");
        }

        if (git.ExitCode != 0)
        {
            throw new IOException("The stand-in git command failed: " + error.Result + output.Result);
        }
    }

    private static void WaitForFile(string path)
    {
        if (!SpinWait.SpinUntil(() => File.Exists(path), TimeSpan.FromMinutes(2)))
        {
            throw new IOException("The stand-in readiness barrier timed out.");
        }
    }

    private static string Text(JsonObject reply) => reply["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? reply.ToJsonString();

    private static readonly ManualResetEventSlim Terminated = new(false);

    private static PosixSignalRegistration? HoldTerminate()
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                Terminated.Set();
            });
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static int Linger(bool acknowledge, bool output = true)
    {
        string? stopFile = Environment.GetEnvironmentVariable("VERICAIRN_STOP_FILE");
        DateTimeOffset until = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < until)
        {
            if (output)
            {
                Emit("working");
            }
            else
            {
                Console.Error.WriteLine("stderr activity is not the event stream");
            }

            if (acknowledge && (Terminated.IsSet || (stopFile is not null && File.Exists(stopFile))))
            {
                Emit("""{"vericairn":"stop-acknowledged"}""");
                return 0;
            }

            Thread.Sleep(200);
        }

        return 0;
    }

#pragma warning disable SYSLIB1054
    [DllImport("libc")]
    private static extern int getpgid(int pid);
#pragma warning restore SYSLIB1054

    private static void Emit(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }
}

/// <summary>A qualified synthetic inventory around an existing command adapter; production launchers never use this declaration.</summary>
internal sealed class ReportedLauncher(Vericairn.Runtime.Workers.IAgentLauncher inner) : Vericairn.Runtime.Workers.IAgentLauncher
{
    public string Launcher => inner.Launcher;
    public Vericairn.Runtime.Providers.ProviderDescriptor Descriptor => inner.Descriptor;
    public string? Harness => inner.Harness;
    public bool Generic => inner.Generic;
    public string Limitations => inner.Limitations;
    public TimeSpan StallLimit => inner.StallLimit;
    public Vericairn.Runtime.Workers.RunWatchSettings WatchDefaults => inner.WatchDefaults;
    public IReadOnlyList<string> EnvironmentNames => inner.EnvironmentNames;
    public Vericairn.Runtime.Workers.RunCapabilities ExecutionCapabilities => inner.ExecutionCapabilities.Scopes.Count > 0
        ? inner.ExecutionCapabilities with { ClosedEnvironment = true } : new(Vericairn.Runtime.Workers.RunSelection.Scopes, ["low", "high"], ["provider"], ["inference"], ["worker-user"], ClosedEnvironment: true);

    public static IReadOnlyList<Vericairn.Runtime.Workers.RunComponent> Inventory { get; } = new[]
        { "stand-in", "stand-in-tool", "command_execution", "file_change", "Bash", "Read", "Write", "Edit", "vericairn" }
        .Select(id => new Vericairn.Runtime.Workers.RunComponent(id, id == "stand-in" ? "harness" : id == "vericairn" ? "mcp" : "tool", "1",
            Vericairn.Records.Hashing.ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(id)),
            Vericairn.Records.Hashing.ContentHash.Sha256([]), [], [], [])).ToArray();

    public Vericairn.Runtime.Workers.RunConfiguration Configuration => new(Launcher, Launcher,
        JsonSerializer.SerializeToElement(new { choices = new { model = "model-a", effort = "high", provider = ExecutionCapabilities.Providers[0],
            destination = ExecutionCapabilities.Destinations[0], dataBoundary = "worker-user" } }), ["model-a", "model-b"],
        EnvironmentPolicy: new("stand-in-policy", Inventory));

    public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => inner.Verify(settings, environment);
    public Vericairn.Runtime.Workers.LaunchContext Prepare(Vericairn.Runtime.Workers.LaunchContext context) => inner.Prepare(context);
    public IReadOnlyList<string> Command(Vericairn.Runtime.Workers.LaunchContext context) => inner.Command(context);
    public Vericairn.Runtime.Workers.HarnessSignal? Read(string line) => inner.Read(line);
    public IReadOnlyList<Vericairn.Runtime.Workers.HarnessAction> ReadActions(string line) => inner.ReadActions(line);
    public Vericairn.Runtime.Workers.RunPreflight Preflight(Vericairn.Runtime.Workers.LaunchContext context)
    {
        List<string>? command = context.Settings.TryGetValue("command", out string? setting) && setting.StartsWith('[')
            ? JsonSerializer.Deserialize<List<string>>(setting) : null;
        List<string> expected = FakeHarness.Command("unused", "unused");
        int prefix = expected.IndexOf(FakeHarness.Marker);
        if (command is null || command.Count < prefix + 3 || command[prefix] != FakeHarness.Marker || !command.Take(prefix).SequenceEqual(expected.Take(prefix)))
        {
            throw new Vericairn.Runtime.Providers.ProviderRefusedException("Only the synthetic harness has this inventory qualification.");
        }
        var info = new ProcessStartInfo(command[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = context.Workspace };
        foreach (string argument in command.Skip(1).Append("--vericairn-execution-preflight")) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        foreach ((string name, string value) in context.Environment) info.Environment[name] = value;
        using Process process = Process.Start(info)!;
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new Vericairn.Runtime.Providers.ProviderRefusedException("The stand-in inventory probe did not finish.");
        }
        if (process.ExitCode != 0) throw new Vericairn.Runtime.Providers.ProviderRefusedException("The stand-in inventory probe failed.");
        return JsonSerializer.Deserialize<Vericairn.Runtime.Workers.RunPreflight>(process.StandardOutput.ReadToEnd(), Vericairn.Protocol.ProtocolJson.Options)!;
    }

    public Vericairn.Runtime.Workers.HarnessExecution? ReadExecution(string line)
    {
        if (line.StartsWith('{'))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("vericairnExecution", out JsonElement report))
            {
                return JsonSerializer.Deserialize<Vericairn.Runtime.Workers.HarnessExecution>(report, Vericairn.Protocol.ProtocolJson.Options);
            }
        }
        return inner.ReadExecution(line);
    }
}
