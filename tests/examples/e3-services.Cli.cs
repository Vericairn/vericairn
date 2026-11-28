// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using ExampleCommand = Vericairn.Cli.Tests.ExampleJourneyRunner.ExampleCommand;

namespace Vericairn.Cli.Tests;

/// <summary>J160's scratch workspace, connected scheduler stand-in and headless readers.</summary>
internal sealed class ServicesJourneySurface : IExampleJourneySurface
{
    private static readonly string[] GitOptions =
    [
        "-c", "user.name=Example Services", "-c", "user.email=services@example.invalid",
        "-c", "commit.gpgsign=false", "-c", "core.hooksPath=.git/hooks",
    ];

    public string Name => "services";

    public CliHarness.Outcome Run(ExampleCommand command, Func<ExampleCommand, CliHarness.Outcome> invoke)
    {
        if (command.Arguments is ["workspace", var workspace])
        {
            PrepareWorkspace(workspace);
            return new(0, "{}", "");
        }

        if (command.Arguments is ["launcher"])
        {
            return new(0, JsonSerializer.Serialize(new
            {
                command = ServicesSchedulerHarness.Setting(Path.Combine(command.Scratch, "scheduler-launch.jsonl")),
                expires = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(2)),
            }), "");
        }

        using EstateService service = EstateService.Open(command.Estate);
        service.Providers = command.Providers;
        service.FlowClock = command.Clock;
        return command.Arguments switch
        {
            ["goal", "step", ..] => RouteDraft(service, command),
            ["mcp", var tool, var principal, var file] => CallMcp(service, tool, principal, file),
            _ => throw new InvalidDataException("Unknown services journey surface command."),
        };
    }

    private static CliHarness.Outcome RouteDraft(EstateService service, ExampleCommand command)
    {
        var providers = new ProviderRegistry();
        Composition.Register(providers);
        providers.Register(new ReportedLauncher(providers.Launcher("codex")!));
        service.Providers = providers;

        return CliHarness.RunWithApi(service, null, ["--estate", command.Estate, "--json", .. command.Arguments]);
    }

    private static CliHarness.Outcome CallMcp(EstateService service, string tool, string principal, string file)
    {
        var arguments = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        string call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = tool, arguments },
        });
        const string initialize = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"Services scheduler stand-in","version":"1"}}}
            """;
        using var input = new StringReader(initialize + "\n" + call + "\n");
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        new McpServer(new ToolRegistry(service, principal), "test").Run(input, output);
        string reply = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
        JsonNode? result = JsonNode.Parse(reply)?["result"];
        string? content = result?["content"]?[0]?["text"]?.GetValue<string>();
        return result?["isError"]?.GetValue<bool>() == false && content is not null ? new(0, content, "")
            : new(2, "", content ?? reply);
    }

    private static void PrepareWorkspace(string root)
    {
        Directory.CreateDirectory(root);
        Git(root, "init", "-q", "-b", "main");
        Git(root, "config", "core.autocrlf", "false");
        File.WriteAllText(Path.Combine(root, "README.md"), "# Example Services appointments\n");
        Git(root, "add", "README.md");
        Git(root, "commit", "-q", "-m", "Start the appointments workspace");
    }

    private static void Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in GitOptions.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new IOException("Git did not start for the example workspace.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Git did not finish preparing the example workspace.");
        }

        if (process.ExitCode != 0) throw new IOException(output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
}
