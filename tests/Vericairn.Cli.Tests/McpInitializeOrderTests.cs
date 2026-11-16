// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// MCP-07's follow-up: a client that pipes initialize, notifications/initialized and a tool call together got the tool call's reply
/// first on Linux once initialize also reported the tool definitions' hash. MCP's lifecycle has the client wait for initialize's
/// result, so the server answers it before it reads the next line.
/// </summary>
[Collection("cli")]
public class McpInitializeOrderTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task AnInitializeStillInProgressIsAnsweredBeforeTheRequestsSentWithIt()
    {
        string root = CliHarness.TempDirectory("mcp-initialize-order");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP initialize order.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            using EstateService api = EstateService.Open(root);
            // initialize asks the event source whether this runtime keeps a journal, and that answer waits until the test releases it.
            var source = new HeldSource();
            var output = new RecordingWriter();
            var input = new ScriptedReader(output,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"order-test","version":"1"}}}""",
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"estate_health","arguments":{}}}""");
            var server = new McpServer(new ToolRegistry(api, null), "test") { Subscriptions = new McpResourceWatch(source) };
            Task serving = server.RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);

            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            source.Release.SetResult();
            await serving.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

            JsonNode[] replies = [.. output.Lines.Select(line => JsonNode.Parse(line)!)];
            Assert.Equal([1, 2], replies.Select(reply => reply["id"]!.GetValue<int>()));
            Assert.Equal("2025-06-18", replies[0]["result"]!["protocolVersion"]!.GetValue<string>());
            Assert.False(replies[1]["result"]!["isError"]!.GetValue<bool>());
            // Every line after initialize was read only once its reply had been written.
            Assert.Equal([true, true], input.AnsweredWhenRead);
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    private sealed class HeldSource : IMcpEventSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> AvailableAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        public Task AddAsync(string key, EventFilter filter, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Remove(string key) { }
        public async Task<McpEventRound> ReadAsync(TimeSpan wait, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return McpEventRound.Empty;
        }
    }

    private sealed class RecordingWriter : StringWriter
    {
        private readonly ConcurrentQueue<string> lines = new();
        public IReadOnlyList<string> Lines => [.. lines];
        public bool InitializeAnswered => lines.Any(line => JsonNode.Parse(line)?["id"]?.ToJsonString() == "1");
        public override Task WriteLineAsync(string? value) { if (value is not null) lines.Enqueue(value); return Task.CompletedTask; }
        public override void WriteLine(string? value) { if (value is not null) lines.Enqueue(value); }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Hands over one line per read and records, for each line after the first, whether initialize had been answered by then.
    private sealed class ScriptedReader(RecordingWriter output, params string[] script) : TextReader
    {
        private int next;
        public List<bool> AnsweredWhenRead { get; } = [];

        public override string? ReadLine()
        {
            lock (AnsweredWhenRead)
            {
                if (next >= script.Length) return null;
                if (next > 0) AnsweredWhenRead.Add(output.InitializeAnswered);
                return script[next++];
            }
        }

        public override Task<string?> ReadLineAsync() => Task.FromResult(ReadLine());
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(ReadLine());
    }
}
