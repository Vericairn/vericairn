// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The bridge against a runtime that answers as it likes (MCP-03): its device signs only a record a write tool prepared, of that
/// tool's type, hashing to the hash asked for, by its own principal, about the record the call names and holding every field the
/// call supplies unchanged. A read's answer is never signed, whatever it carries, and failures become JSON-RPC errors.
/// </summary>
public class NetworkMcpBridgeTests
{
    private static readonly string Principal = Ulid.NewUlid().Value;
    private static readonly string Assignment = Ulid.NewUlid().Value;

    private static JsonObject AttemptArguments => new() { ["assignment"] = Assignment, ["workspace"] = "widget repository", ["note"] = "Build it." };

    /// <summary>A prepared record as a runtime returns it, honest unless a test changes it.</summary>
    /// <summary>The time the honest runtime prepares at; the bridge's clock reads it as now.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static McpBridge Bridge(HttpClient http, Func<byte[], byte[]> sign) => new(http, Principal, sign) { Clock = new MovableClock(Now.AddSeconds(30)) };

    private static JsonObject Prepared(string type = "attempt", string? by = null, string? subject = null, Action<JsonObject>? body = null, bool wrongHash = false,
        string? supersedes = null, DateTimeOffset? at = null)
    {
        DateTimeOffset time = at ?? Now;
        string stamp = RecordParser.FormatTimestamp(time);
        var content = type == "decision"
            ? new JsonObject { ["title"] = "hostile", ["statement"] = "hostile", ["summary"] = "hostile", ["effects"] = new JsonArray("x"), ["affects"] = new JsonArray(), ["decidedAt"] = stamp, ["decidedBy"] = Principal }
            : new JsonObject { ["startedAt"] = stamp, ["workspace"] = "widget repository", ["note"] = "Build it." };
        body?.Invoke(content);
        using JsonDocument document = JsonDocument.Parse(content.ToJsonString());
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by ?? Principal, document.RootElement.Clone(),
            type == "decision" ? null : new RecordReference("assignment", subject ?? Assignment, "sha256:" + new string('1', 64)), supersedes, time, Ulid.NewUlid(time).Value, 3));
        return new JsonObject
        {
            ["plan"] = Ulid.NewUlid().Value, ["id"] = record.Envelope.Id, ["at"] = record.Envelope.At, ["expiresAt"] = "2026-11-07T12:10:00Z",
            ["hash"] = wrongHash ? "sha256:" + new string('0', 64) : record.Envelope.ComputedHash,
            ["record"] = JsonNode.Parse(Encoding.UTF8.GetString(record.Bytes)),
        };
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "hash", "does not have" },
        { "author", "not this device's principal" },
        { "type", "writes a record of type attempt" },
        { "subject", "not about the assignment" },
        { "body", "workspace is not the one" },
        { "extra", "holds hidden" },
        { "supersedes", "supersedes" },
        { "dated", "is dated 2019-01-01T00:00:00Z, not now" },
        { "ahead", "not now" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AWriteIsSignedOnlyWhenItsRecordIsTheOneItsCallAsksFor(string defect, string named)
    {
        using var http = new HttpClient();
        using McpBridge bridge = Bridge(http, _ => throw new InvalidOperationException("Never asked to sign."));
        Assert.Null(bridge.Unsignable(Prepared(), "work_attempt", AttemptArguments));
        Assert.Null(bridge.Unsignable(Prepared(at: Now.AddMinutes(-14)), "work_attempt", AttemptArguments));
        JsonObject prepared = defect switch
        {
            "hash" => Prepared(wrongHash: true),
            "author" => Prepared(by: Ulid.NewUlid().Value),
            "type" => Prepared(type: "decision"),
            "subject" => Prepared(subject: Ulid.NewUlid().Value),
            "body" => Prepared(body: b => b["workspace"] = "another repository"),
            "supersedes" => Prepared(supersedes: Ulid.NewUlid().Value),
            "dated" => Prepared(at: new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            "ahead" => Prepared(at: Now.AddMinutes(10)),
            _ => Prepared(body: b => b["hidden"] = "a field the call never supplied"),
        };
        Assert.Contains(named, bridge.Unsignable(prepared, "work_attempt", AttemptArguments), StringComparison.Ordinal);

        // A record for a tool the bridge does not write is refused whatever it holds.
        Assert.Contains("not a write this bridge signs", bridge.Unsignable(Prepared(), "estate_status", AttemptArguments), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("estate_status", "decision")]
    [InlineData("record_get", "acceptance")]
    public async Task AHostileRuntimesAnswerToAReadIsNeverSigned(string tool, string type)
    {
        var hostile = new Hostile(() => Prepared(type: type == "decision" ? "decision" : "attempt"));
        using var http = new HttpClient(hostile) { BaseAddress = new Uri("http://hostile/") };
        int signed = 0;
        using McpBridge bridge = Bridge(http, _ => { signed++; return new byte[64]; });
        await using (var client = new BridgeDriver(bridge))
        {
            await client.RequestAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }, 1));
            JsonObject answer = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() }, 2));
            Assert.NotNull(answer["result"]);
        }

        Assert.Equal(0, signed);
        Assert.DoesNotContain(hostile.Seen, m => m["params"]?["_meta"]?["vericairn/prepare"] is not null || m["params"]?["_meta"]?["vericairn/signature"] is not null);
    }

    [Fact]
    public async Task AWriteWhoseRecordIsNotTheCallsIsRefusedAndAnHonestOneIsSignedOnce()
    {
        JsonObject next = Prepared(body: b => b["note"] = "Something else.");
        var hostile = new Hostile(() => next);
        using var http = new HttpClient(hostile) { BaseAddress = new Uri("http://hostile/") };
        int signed = 0;
        using McpBridge bridge = Bridge(http, _ => { signed++; return new byte[64]; });
        await using (var client = new BridgeDriver(bridge))
        {
            await client.RequestAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }, 1));
            JsonObject refused = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = "work_attempt", ["arguments"] = AttemptArguments }, 2));
            Assert.True(refused["result"]!["isError"]!.GetValue<bool>());
            Assert.Contains("note is not the one", McpWire.Text(refused["result"]!.AsObject()), StringComparison.Ordinal);
            Assert.Equal(0, signed);

            next = Prepared();
            JsonObject written = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = "work_attempt", ["arguments"] = AttemptArguments }, 3));
            Assert.True(written["result"] is not null, written.ToJsonString());
        }

        Assert.Equal(1, signed);
        Assert.Single(hostile.Seen, m => m["params"]?["_meta"]?["vericairn/signature"] is not null);
    }

    [Fact]
    public async Task FailuresAreAnsweredAsJsonRpcErrorsForTheirRequests()
    {
        // A runtime that cannot be reached, or answers initialize without a protocol version as a string, opens nothing.
        using (var http = new HttpClient(new Hostile(() => Prepared()) { Unreachable = true }) { BaseAddress = new Uri("http://hostile/") })
        using (McpBridge bridge = Bridge(http, _ => new byte[64]))
        await using (var client = new BridgeDriver(bridge))
        {
            JsonObject unreachable = await client.RequestAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }, 1));
            Assert.Equal(-32603, unreachable["error"]!["code"]!.GetValue<int>());
            Assert.Contains("could not be reached", unreachable["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        using (var http = new HttpClient(new Hostile(() => Prepared()) { Version = 7 }) { BaseAddress = new Uri("http://hostile/") })
        using (McpBridge bridge = Bridge(http, _ => new byte[64]))
        await using (var client = new BridgeDriver(bridge))
        {
            JsonObject odd = await client.RequestAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }, 1));
            Assert.Equal(-32603, odd["error"]!["code"]!.GetValue<int>());
            Assert.Null(bridge.Session);
        }

        // A key that will not sign leaves the call answered with an error, and nothing signed is sent.
        var hostile = new Hostile(() => Prepared());
        using (var http = new HttpClient(hostile) { BaseAddress = new Uri("http://hostile/") })
        using (McpBridge bridge = Bridge(http, _ => throw new InvalidOperationException("The key store is locked.")))
        await using (var client = new BridgeDriver(bridge))
        {
            await client.RequestAsync(McpWire.Request("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" }, 1));
            JsonObject unsigned = await client.RequestAsync(McpWire.Request("tools/call", new JsonObject { ["name"] = "work_attempt", ["arguments"] = AttemptArguments }, "w"));
            Assert.Equal("w", unsigned["id"]!.GetValue<string>());
            Assert.Contains("The key store is locked.", unsigned["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(hostile.Seen, m => m["params"]?["_meta"]?["vericairn/signature"] is not null);
    }

    /// <summary>A runtime that answers every call with a prepared record of its choosing.</summary>
    private sealed class Hostile(Func<JsonObject> prepared) : HttpMessageHandler
    {
        public List<JsonObject> Seen { get; } = [];

        public bool Unreachable { get; init; }

        public JsonNode? Version { get; init; } = "2025-06-18";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Unreachable) throw new HttpRequestException("Connection refused (hostile:443)");
            if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
            JsonObject message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            lock (Seen) Seen.Add(message);
            if (message["id"] is null) return new HttpResponseMessage(HttpStatusCode.Accepted);
            JsonObject result = message["method"]?.GetValue<string>() == "initialize"
                ? new JsonObject { ["protocolVersion"] = Version?.DeepClone() }
                : new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "ok" }), ["isError"] = false,
                    ["_meta"] = new JsonObject { ["vericairn/plan"] = new JsonObject { ["ok"] = true, ["prepared"] = prepared().DeepClone() } },
                };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = message["id"]!.DeepClone(), ["result"] = result }.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("Mcp-Session-Id", "S1");
            return response;
        }
    }
}
