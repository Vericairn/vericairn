// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Vericairn.Mcp;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// MCP-07: clients could not tell Vericairn's read tools from its writes without reading every description, and a changed tool set
/// went unannounced. Every tool carries its behavior hints, each release publishes its definitions' SHA-256 in initialize and the
/// release notes, and the MCP guide keeps an entry for every definition.
/// </summary>
[Collection("cli")]
public partial class McpToolDefinitionTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    // Every write's hints, as the guide states the rule: destructive when it supersedes or withdraws a rule or entry in force,
    // idempotent where the runtime refuses a repeat because the expected hash is no longer current or the declared key is taken.
    private static readonly Dictionary<string, (bool Destructive, bool Idempotent)> Writes = new(StringComparer.Ordinal)
    {
        ["policy_declare"] = (false, true),
        ["policy_revise"] = (true, true),
        ["policy_retire"] = (true, true),
        ["policy_except"] = (false, false),
        ["policy_revoke_exception"] = (true, true),
        ["writing_exception_add"] = (false, false),
        ["writing_exception_remove"] = (true, true),
        ["work_attempt"] = (false, false),
        ["work_progress"] = (false, false),
        ["work_result"] = (false, false),
        ["work_review"] = (false, false),
        ["document_review"] = (false, false),
        ["goal_decide"] = (false, false),
        ["work_amend"] = (false, false),
        ["prompt_save"] = (true, false),
        ["prompt_preference"] = (true, false),
        ["prompt_copy"] = (false, false),
        ["prompt_use"] = (false, false),
    };

    [Fact]
    public void EveryToolCarriesItsBehaviorHints()
    {
        JsonArray definitions = ToolRegistry.Definitions();
        Assert.Equal(definitions.Select(d => d!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal), definitions.Select(d => d!["name"]!.GetValue<string>()));
        Assert.Equal(Writes.Keys.Order(StringComparer.Ordinal), definitions.Where(d => !d!["annotations"]!["readOnlyHint"]!.GetValue<bool>()).Select(d => d!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        foreach (JsonObject definition in definitions.Select(d => d!.AsObject()))
        {
            string name = definition["name"]!.GetValue<string>();
            JsonObject annotations = definition["annotations"]!.AsObject();
            Assert.Equal(["destructiveHint", "idempotentHint", "readOnlyHint"], annotations.Select(p => p.Key).Order(StringComparer.Ordinal));
            (bool destructive, bool idempotent) = Writes.TryGetValue(name, out var write) ? write : (false, true);
            Assert.Equal(!Writes.ContainsKey(name), annotations["readOnlyHint"]!.GetValue<bool>());
            Assert.Equal(destructive, annotations["destructiveHint"]!.GetValue<bool>());
            Assert.Equal(idempotent, annotations["idempotentHint"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void ARepeatedIdempotentWriteIsRefusedAndRecordsNothingMore()
    {
        // writing_exception_remove names the entry's current hash, which its own success replaces.
        string root = CliHarness.TempDirectory("mcp-idempotent");
        string scratch = CliHarness.TempDirectory("mcp-idempotent-scratch");
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP hints.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "The first authority.");
            using EstateService api = EstateService.Open(root);
            var registry = new ToolRegistry(api, op);
            JsonObject entry = Vericairn.Records.Tests.PolicyFixture.WritingBody();
            ToolCallResult added = registry.Call("writing_exception_add", new JsonObject { ["expected"] = Hash(entry), ["body"] = entry.DeepClone() });
            Assert.False(added.IsError, added.Text);
            using JsonDocument written = JsonDocument.Parse(added.Text);
            string id = written.RootElement.GetProperty("written").GetProperty("id").GetString()!;
            string hash = written.RootElement.GetProperty("written").GetProperty("hash").GetString()!;
            var remove = new JsonObject { ["id"] = id, ["expected"] = hash, ["reason"] = "No longer needed." };
            ToolCallResult removed = registry.Call("writing_exception_remove", remove.DeepClone().AsObject());
            Assert.False(removed.IsError, removed.Text);
            int records = api.Session.Load().Records.Count;
            ToolCallResult again = registry.Call("writing_exception_remove", remove.DeepClone().AsObject());
            Assert.True(again.IsError, again.Text);
            Assert.Equal(records, api.Session.Load().Records.Count);
        }
        finally
        {
            CliHarness.Delete(root);
            CliHarness.Delete(scratch);
        }
    }

    [Fact]
    public void TheDefinitionsHashIsTheirCanonicalSha256AndInitializeCarriesIt()
    {
        JsonArray definitions = ToolRegistry.Definitions();
        using JsonDocument document = JsonDocument.Parse(definitions.ToJsonString());
        // The record formats' RFC 8785 serializer gives the same bytes as the server's own.
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializeUtf8(document.RootElement)), ToolRegistry.DefinitionsHash);
        JsonObject first = definitions[0]!.AsObject();
        using JsonDocument one = JsonDocument.Parse(first.ToJsonString());
        Assert.Equal(ContentHash.Sha256(CanonicalJson.SerializeUtf8(one.RootElement)), ToolRegistry.DefinitionHash(first));
        // A changed description changes both hashes.
        JsonObject changed = first.DeepClone().AsObject();
        changed["description"] = first["description"]!.GetValue<string>() + " Changed.";
        Assert.NotEqual(ToolRegistry.DefinitionHash(first), ToolRegistry.DefinitionHash(changed));
        definitions[0] = changed;
        using JsonDocument edited = JsonDocument.Parse(definitions.ToJsonString());
        Assert.NotEqual(ToolRegistry.DefinitionsHash, ContentHash.Sha256(CanonicalJson.SerializeUtf8(edited.RootElement)));
        // Definitions hands out a copy: changing it changes nothing the server publishes.
        Assert.Equal(first.ToJsonString(), ToolRegistry.Definitions()[0]!.ToJsonString());

        string root = CliHarness.TempDirectory("mcp-definitions");
        try
        {
            CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "MCP definitions.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            using EstateService api = EstateService.Open(root);
            var registry = new ToolRegistry(api, null);
            var input = new StringReader("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""" + "\n" + """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""" + "\n");
            var output = new StringWriter { NewLine = "\n" };
            new McpServer(registry, "test").Run(input, output);
            JsonObject[] replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => (JsonObject)JsonNode.Parse(l)!).ToArray();
            JsonNode meta = replies[0]["result"]!["_meta"]![McpServer.ToolDefinitionsMeta]!;
            Assert.Equal(ToolRegistry.Definitions().Count, meta["count"]!.GetValue<int>());
            Assert.Equal(ToolRegistry.DefinitionsHash, meta["hash"]!.GetValue<string>());
            Assert.Equal(registry.All.Count, ToolRegistry.Definitions().Count);
            // tools/list gives each visible tool exactly as the published definitions hold it.
            Dictionary<string, string> published = ToolRegistry.Definitions().ToDictionary(d => d!["name"]!.GetValue<string>(), d => d!.ToJsonString(), StringComparer.Ordinal);
            foreach (JsonNode? tool in replies[1]["result"]!["tools"]!.AsArray())
                Assert.Equal(published[tool!["name"]!.GetValue<string>()], tool.ToJsonString());
        }
        finally
        {
            CliHarness.Delete(root);
        }
    }

    [Fact]
    public void EveryDefinitionHasItsCurrentEntryInTheMcpGuide()
    {
        // A definition that changes, its description included, without its entry here fails, as does a tool without an entry.
        string guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "mcp.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = guide.IndexOf("\n## Tool annotations and definitions (0.9.4)\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "The MCP guide has no section of tool definitions.");
        int end = guide.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var entries = new Dictionary<string, Match>(StringComparer.Ordinal);
        foreach (Match row in EntryRow().Matches(guide[start..end]))
            Assert.True(entries.TryAdd(row.Groups["name"].Value, row), "Two entries name " + row.Groups["name"].Value + ".");
        JsonArray definitions = ToolRegistry.Definitions();
        Assert.Equal(definitions.Select(d => d!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal), entries.Keys.Order(StringComparer.Ordinal));
        var stale = new List<string>();
        foreach (JsonObject definition in definitions.Select(d => d!.AsObject()))
        {
            Match entry = entries[definition["name"]!.GetValue<string>()];
            JsonNode annotations = definition["annotations"]!;
            string expected = $"{Yes(annotations, "readOnlyHint")} {Yes(annotations, "destructiveHint")} {Yes(annotations, "idempotentHint")} {ToolRegistry.DefinitionHash(definition)[7..19]}";
            if ($"{entry.Groups["read"].Value} {entry.Groups["destructive"].Value} {entry.Groups["idempotent"].Value} {entry.Groups["hash"].Value}" != expected)
                stale.Add($"| `{definition["name"]}` | {expected.Replace(" ", " | ", StringComparison.Ordinal)} | <release> |");
        }

        Assert.True(stale.Count == 0, "Record each changed definition in docs/mcp.md with the release that changes it:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void TheReleaseNotesListTheDefinitionsAndTheirHash()
    {
        string package = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int notes = package.IndexOf("} > dist/NOTES.md\n", StringComparison.Ordinal);
        int tools = package.IndexOf("            dotnet artifacts/bin/Vericairn.DocsTool/release/vericairn-docs.dll mcp-tools\n", StringComparison.Ordinal);
        Assert.True(tools > 0 && tools < notes, "The notes list the tool definitions.");
    }

    private static string Yes(JsonNode annotations, string name) => annotations[name]!.GetValue<bool>() ? "yes" : "no";

    private static string Hash(JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return ContentHash.Sha256(CanonicalJson.SerializeUtf8(document.RootElement));
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        return root;
    }

    [GeneratedRegex(@"^\| `(?<name>[a-z_]+)` \| (?<read>yes|no) \| (?<destructive>yes|no) \| (?<idempotent>yes|no) \| `(?<hash>[0-9a-f]{12})` \| (?<release>\d+\.\d+\.\d+) \|$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex EntryRow();
}
