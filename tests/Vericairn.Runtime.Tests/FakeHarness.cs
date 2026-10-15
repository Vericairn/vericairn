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

        var line = new JsonObject
        {
            ["scenario"] = scenario,
            ["arguments"] = new JsonArray(arguments.Select(a => (JsonNode)a).ToArray()),
            ["environment"] = environment,
            ["directory"] = Directory.GetCurrentDirectory(),
            ["process"] = Environment.ProcessId,
            ["group"] = OperatingSystem.IsWindows() ? 0 : getpgid(0),
        };
        using PosixSignalRegistration? terminate = scenario is "codex-spender" or "ignore-stop" or "ack-stop" ? HoldTerminate() : null;
        File.AppendAllText(report, line.ToJsonString() + "\n");
        bool listing = arguments.Contains("features") && arguments.Contains("list");
        switch (scenario)
        {
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
            case "codex" or "codex-spender" or "codex-incomplete" or "codex-overflow" or "codex-unfinished" or "codex-headroom" when listing:
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
                    return Linger(acknowledge: true);
                }
            case "silent-ignore":
                return Linger(acknowledge: false, output: false);
            case "silent-recovery":
                Thread.Sleep(1200);
                for (int i = 0; i < 6; i++)
                {
                    Emit("{\"event\":\"working\"}");
                    Thread.Sleep(400);
                }

                return 0;
            case "ignore-stop":
                return Linger(acknowledge: false);
            case "ack-stop":
                return Linger(acknowledge: true);
            case "close-output":
                Emit("Closing my output and working on.");
                Console.Out.Close();
                Console.Error.Close();
                Thread.Sleep(TimeSpan.FromSeconds(3));
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
