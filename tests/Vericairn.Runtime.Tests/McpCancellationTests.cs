// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class McpCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryMapsDeadlinesAndCancellationWithoutLosingWriteUncertainty(bool cancel)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        using var client = Client(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/devices") return Devices();
            writes++;
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        }, cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100));
        using var stop = new CancellationTokenSource();
        var registry = new ToolRegistry(client, "contributor");
        Task<ToolCallResult> pending = registry.CallAsync("work_attempt", AttemptArguments(), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (cancel) stop.Cancel();
        ToolCallResult result = await pending;
        Assert.True(result.IsError);
        Assert.False(result.Unknown);
        Exception expected = cancel ? new ClientCancelledException(true, stop.Token) : new ClientTimeoutException(true);
        Assert.Equal(ClientFailure.FromException(expected)!.Message, result.Text);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task CancelledAdmissionCheckNeverDispatchesAWriteOrCallsItUnknown()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var client = Client(async (_, token) =>
        {
            calls++;
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Devices();
        });
        using var stop = new CancellationTokenSource();
        Task<ToolCallResult> pending = new ToolRegistry(client, "contributor").CallAsync("work_attempt", AttemptArguments(), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stop.Cancel();
        ToolCallResult result = await pending;
        Assert.True(result.IsError);
        Assert.False(result.Unknown);
        Assert.Equal("The request was cancelled.", result.Text);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ASlowToolDoesNotBlockPingAndCancellationTargetsOnlyItsRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        });
        using var input = new Input();
        using var output = new Output();
        Task serving = new McpServer(new ToolRegistry(client, null), "test").RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
        input.Add("""{"id":"slow","method":"tools/call","params":{"name":"estate_health"}}""");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        input.Add("""{"id":2,"method":"ping"}""");
        JsonObject ping = await output.Next();
        Assert.Equal(2, ping["id"]!.GetValue<int>());
        Assert.Empty(ping["result"]!.AsObject());
        input.Add("""{"method":"notifications/cancelled","params":{"requestId":"unrelated"}}""");
        input.Add("""{"method":"notifications/cancelled","params":17}""");
        input.Add("""{"method":"notifications/cancelled","params":{"requestId":"slow"}}""");
        JsonObject cancelled = await output.Next();
        Assert.Equal("slow", cancelled["id"]!.GetValue<string>());
        Assert.True(cancelled["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("The request was cancelled.", cancelled["result"]!["content"]![0]!["text"]!.GetValue<string>());
        input.Complete();
        await serving.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ServerCancellationStopsWaitingForASynchronousReader()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var input = new BlockingInput();
        using var output = new StringWriter();
        using var stop = new CancellationTokenSource();
        Task serving = new McpServer(new ToolRegistry(client, null), "test").RunAsync(input, output, cancellationToken: stop.Token);
        try
        {
            await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serving.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Empty(output.ToString());
        }
        finally
        {
            input.Release.Set();
            await input.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ServerCancellationSettlesEveryPendingRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var client = Client(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        });
        using var input = new Input();
        using var output = new Output();
        using var stop = new CancellationTokenSource();
        Task serving = new McpServer(new ToolRegistry(client, null), "test").RunAsync(input, output, cancellationToken: stop.Token);
        input.Add("""{"id":1,"method":"tools/call","params":{"name":"estate_health"}}""");
        input.Add("""{"id":2,"method":"tools/call","params":{"name":"estate_health"}}""");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serving.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        JsonObject[] responses = [await output.Next(), await output.Next()];
        Assert.Equal([1, 2], responses.Select(response => response["id"]!.GetValue<int>()).Order());
        Assert.All(responses, response => Assert.Equal("The request was cancelled.", response["result"]!["content"]![0]!["text"]!.GetValue<string>()));
    }

    [Fact]
    public async Task TimedOutListingReturnsAnIntendedProtocolError()
    {
        using var client = Client(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Devices();
        }, TimeSpan.FromMilliseconds(50));
        using var input = new StringReader("""{"id":1,"method":"tools/list"}""");
        using var output = new StringWriter();
        await new McpServer(new ToolRegistry(client, "contributor"), "test").RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
        JsonObject result = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal(-32603, result["error"]!["code"]!.GetValue<int>());
        Assert.Equal("The request timed out.", result["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUnreadableAdmissionResponseIsAProtocolErrorAndLeavesPingUsable()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("truncated response") }));
        using var input = new StringReader("{\"id\":1,\"method\":\"tools/list\"}\n{\"id\":2,\"method\":\"ping\"}\n");
        using var output = new StringWriter();
        await new McpServer(new ToolRegistry(client, "contributor"), "test").RunAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
        JsonObject[] responses = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        Assert.Equal(-32603, responses.Single(response => response["id"]!.GetValue<int>() == 1)["error"]!["code"]!.GetValue<int>());
        Assert.Empty(responses.Single(response => response["id"]!.GetValue<int>() == 2)["result"]!.AsObject());
    }

    [Fact]
    public async Task ARefusedWriteAndUnknownToolKeepTheirExistingRepresentations()
    {
        using var client = Client((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/devices"
            ? Devices() : new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            { Content = JsonContent.Create(WriteOutcome.Fail(new Problem("refused", "deliberate refusal", [])), options: ProtocolJson.Options) }));
        var registry = new ToolRegistry(client, "contributor");
        ToolCallResult refusal = await registry.CallAsync("work_attempt", AttemptArguments(), TestContext.Current.CancellationToken);
        Assert.True(refusal.IsError);
        Assert.Equal("refused", JsonNode.Parse(refusal.Text)!["problem"]!["code"]!.GetValue<string>());
        Assert.True((await registry.CallAsync("absent", null, TestContext.Current.CancellationToken)).Unknown);
        Assert.True((await new ToolRegistry(client, null).CallAsync("work_attempt", AttemptArguments(), TestContext.Current.CancellationToken)).Unknown);
    }

    private static JsonObject AttemptArguments() => new() { ["assignment"] = "assignment", ["workspace"] = "workspace", ["note"] = "note" };
    private static HttpResponseMessage Devices() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new[] { new DeviceRow("device", "contributor", null, null, null, null, "admitted", true) }, options: ProtocolJson.Options),
    };

    private static VericairnClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://localhost/") }) { OperationTimeout = _ => timeout };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class Input : TextReader
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>();
        public void Add(string line) => Assert.True(lines.Writer.TryWrite(line));
        public void Complete() => lines.Writer.TryComplete();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            return await lines.Reader.WaitToReadAsync(cancellationToken) ? await lines.Reader.ReadAsync(cancellationToken) : null;
        }
    }

    private sealed class Output : TextWriter
    {
        private readonly Channel<string> lines = Channel.CreateUnbounded<string>();
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(string? value) { Assert.True(lines.Writer.TryWrite(value!)); return Task.CompletedTask; }
        public async Task<JsonObject> Next() => JsonNode.Parse(await lines.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))!.AsObject();
    }

    private sealed class BlockingInput : TextReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
            Finished.TrySetResult();
            return ValueTask.FromResult<string?>(null);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Release.Dispose();
            base.Dispose(disposing);
        }
    }
}
