// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>The scheduler's stand-in, started by the worker with the same brief and MCP configuration as a coding harness.</summary>
internal static class ServicesSchedulerHarness
{
    private const string Marker = "--example-services-appointment";

    internal static string Setting(string report) => JsonSerializer.Serialize(FakeHarness.Command("codex", report).Append(Marker));

    internal static int? Run(string[] arguments)
    {
        int marker = Array.IndexOf(arguments, Marker);
        if (marker < 0 || arguments.Skip(marker + 1).FirstOrDefault() != "exec") return null;

        try
        {
            DraftAppointment();
            Console.WriteLine("""{"type":"thread.started","thread_id":"services-scheduler"}""");
            Console.WriteLine("""{"type":"turn.completed","usage":{"input_tokens":300,"cached_input_tokens":0,"output_tokens":100}}""");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine("Scheduler stand-in: " + exception.Message);
            return 1;
        }
    }

    private static void DraftAppointment()
    {
        string run = WorkerVariable("VERICAIRN_RUN");
        JsonNode server = JsonNode.Parse(File.ReadAllText(WorkerVariable("VERICAIRN_MCP_CONFIG")))!["mcpServers"]!["vericairn"]!;
        string[] serverArguments = server["args"]!.AsArray().Select(argument => argument!.GetValue<string>()).ToArray();
        int mcp = Array.IndexOf(serverArguments, "mcp");
        int asOption = Array.IndexOf(serverArguments, "--as");
        int runOption = Array.IndexOf(serverArguments, "--run");
        if (mcp < 0 || asOption < 0 || runOption < 0 || serverArguments[runOption + 1] != run)
            throw new InvalidDataException("The worker did not bind its MCP server to the scheduler's run.");

        string scheduler = serverArguments[asOption + 1];
        string brief = File.ReadAllText(WorkerVariable("VERICAIRN_BRIEF"));
        string assignment = Regex.Matches(brief, @"^## Assignment (?<id>[0-9A-Z]{26}) to .* \((?<holder>[0-9A-Z]{26})\)$", RegexOptions.Multiline)
            .Where(heading => heading.Groups["holder"].Value == scheduler).Select(heading => heading.Groups["id"].Value).LastOrDefault()
            ?? throw new InvalidDataException("The worker's brief contains no assignment held by this scheduler.");

        ProcessStartInfo StartInfo(IEnumerable<string> args)
        {
            var start = new ProcessStartInfo(server["command"]!.GetValue<string>())
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in args) start.ArgumentList.Add(argument);
            foreach ((string name, JsonNode? value) in server["env"]!.AsObject()) start.Environment[name] = value!.GetValue<string>();
            return start;
        }

        ProcessStartInfo mcpStart = StartInfo(serverArguments);
        mcpStart.RedirectStandardInput = true;
        using Process process = Process.Start(mcpStart) ?? throw new IOException("The worker's MCP server did not start.");
        Task<string> serverError = process.StandardError.ReadToEndAsync();
        try
        {
            int id = 0;
            JsonNode Send(string method, object parameters)
            {
                process.StandardInput.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = ++id, method, @params = parameters }));
                process.StandardInput.Flush();
                string reply = process.StandardOutput.ReadLineAsync().WaitAsync(TestTiming.HangTimeout).GetAwaiter().GetResult()
                    ?? throw new IOException("The MCP server closed its output.");
                return JsonNode.Parse(reply)?["result"] ?? throw new InvalidDataException(reply);
            }

            JsonNode Call(string name, object parameters)
            {
                JsonNode response = Send("tools/call", new { name, arguments = parameters });
                string content = response["content"]?[0]?["text"]?.GetValue<string>() ?? response.ToJsonString();
                if (response["isError"]?.GetValue<bool>() != false) throw new InvalidDataException(name + ": " + content);
                return JsonNode.Parse(content)!;
            }

            Send("initialize", new { protocolVersion = "2025-06-18", capabilities = new JsonObject(), clientInfo = new { name = "Services scheduler stand-in", version = "1" } });
            JsonNode assignmentRecord = Call("record_get", new { id = assignment });
            string order = assignmentRecord["envelope"]!["subject"]!["id"]!.GetValue<string>();
            JsonNode request = Call("record_get", new { id = order })["envelope"]!["body"]!;
            if (request["scope"]?[0]?.GetValue<string>() != "Draft an appointment for a pool cleaning visit at 1 Sample Lane.")
                throw new InvalidDataException("The draft did not receive the sample work order's prose request.");
            string requestKey = request["key"]!.GetValue<string>();
            string attempt = Call("work_attempt", new { assignment, workspace = "Example services workspace, branch work/sr-0001", note = "Draft the appointment." })
                ["written"]!["id"]!.GetValue<string>();

            string path = Path.GetFullPath(Path.Combine("appointments", requestKey + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                request = requestKey, service = "pool cleaning", site = "1 Sample Lane", start = "2026-12-08T09:00:00-06:00",
                durationMinutes = 60, technician = "Sample Technician",
            }));
            using Process put = Process.Start(StartInfo([.. serverArguments[..mcp], "--json", "objects", "put", path]))
                ?? throw new IOException("The object-store command did not start.");
            Task<string> objectOutput = put.StandardOutput.ReadToEndAsync();
            Task<string> objectError = put.StandardError.ReadToEndAsync();
            if (!put.WaitForExit(TestTiming.HangTimeoutMilliseconds))
            {
                put.Kill(entireProcessTree: true);
                put.WaitForExit();
                throw new IOException("The object-store command timed out.");
            }
            if (put.ExitCode != 0) throw new IOException(objectError.GetAwaiter().GetResult());
            string hash = JsonNode.Parse(objectOutput.GetAwaiter().GetResult())!["hash"]!.GetValue<string>();

            Call("work_result", new
            {
                attempt,
                body = new
                {
                    outcome = "completed", summary = $"Proposed Tuesday morning for {requestKey}.",
                    outputs = new[] { new { name = "appointment", hash, mediaType = "application/json" } },
                    evidence = Array.Empty<object>(), checks = Array.Empty<object>(), limitations = Array.Empty<object>(), unresolvedEffects = Array.Empty<object>(),
                    nextStep = "The approver reviews the exact draft.",
                },
            });
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(TestTiming.HangTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        if (process.ExitCode != 0) throw new IOException(serverError.GetAwaiter().GetResult());
    }

    private static string WorkerVariable(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidDataException($"The worker did not supply {name}.");
}
