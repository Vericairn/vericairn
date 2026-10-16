// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class ContextJourneyTests
{
    [Fact]
    public void DefaultAgentBriefWithholdsNamedOptionalTextAndListsTheUnselectedCatalogue()
    {
        using var estate = new HandoffEstate();
        byte[] optional = Encoding.UTF8.GetBytes("OPTIONAL SOURCE BODY " + new string('o', 100_000));
        Document(estate, "docs/named.md", optional);
        Document(estate, "docs/unrelated.md", Encoding.UTF8.GetBytes(new string('u', 1_000_000)));
        estate.Add("work-order", estate.OperatorId, """{"key":"C-1","kind":"ordinary","title":"Context","intent":"Read docs/named.md for background.","scope":["work"],"exitQuestion":"Complete?","inputs":[],"deliverables":["result"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        Assert.True(api.CommunicationSet(new(estate.OperatorId, "person", new(Handoff: "short"), estate.OperatorId)).Ok);
        var registry = new ToolRegistry(api, null);
        ToolCallResult agent = registry.Call("work_brief", new JsonObject { ["workOrder"] = "C-1", ["recipient"] = estate.OperatorId });
        Assert.Contains("handoff (short)", agent.Text, StringComparison.Ordinal);
        Assert.False(agent.IsError);
        Assert.DoesNotContain("OPTIONAL SOURCE BODY", agent.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("docs/unrelated.md", agent.Text, StringComparison.Ordinal);
        Assert.Contains("docs/named.md", agent.Text, StringComparison.Ordinal);
        Assert.Contains("vericairn context catalogue", agent.Text, StringComparison.Ordinal);
        Assert.Contains("OPTIONAL SOURCE BODY", CliHarness.Ok("--estate", estate.Root, "work", "brief", "C-1").Out, StringComparison.Ordinal);
        Assert.Contains("OPTIONAL SOURCE BODY", registry.Call("work_brief", new JsonObject { ["workOrder"] = "C-1", ["maxBytes"] = "300000" }).Text, StringComparison.Ordinal);
        Assert.True(registry.Call("work_brief", new JsonObject { ["workOrder"] = "C-1", ["maxBytes"] = "1" }).IsError);
        string catalogue = CliHarness.Ok("--estate", estate.Root, "context", "catalogue").Out;
        Assert.Contains("docs/unrelated.md", catalogue, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('u', 100), catalogue, StringComparison.Ordinal);
        Assert.Empty(api.Objects());
    }

    [Fact]
    public async Task OriginalStdoutPreservesArbitraryBytesWithoutTextEncodingOrFraming()
    {
        using var estate = new HandoffEstate();
        byte[] original = [0, 255, 254, 13, 10, 128, 65];
        ContextAssembly.Source source = Document(estate, "docs/binary.dat", original);
        ContextPackage package = ContextAssembly.Build(new JsonObject { ["estate"] = estate.Load().Manifest!.Estate }, [source], "lead");
        string command = package.Payload.GetProperty("withheld")[0].GetProperty("retrieve").GetString()!;
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vericairn.exe" : "vericairn"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--estate", estate.Root }.Concat(command.Split(' ').Skip(1)))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        using var output = new MemoryStream();
        Task copied = process.StandardOutput.BaseStream.CopyToAsync(output, TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await copied;
        Assert.Equal(string.Empty, await error);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void PrepareUsesTheSameMeasuredPayloadThroughCliAndMcpAndRejectsOverflow()
    {
        using var estate = new HandoffEstate();
        estate.Add("work-order", estate.OperatorId, """{"key":"C-1","kind":"ordinary","title":"Context","intent":"Continue.","scope":["work"],"exitQuestion":"Complete?","inputs":[],"deliverables":["result"]}""");
        using EstateService api = EstateService.Open(estate.Root);
        var registry = new ToolRegistry(api, null);
        string text = CliHarness.Ok("--estate", estate.Root, "context", "prepare", "C-1", "--max-bytes", "100000").Out;
        ToolCallResult mcp = registry.Call("context_prepare", new JsonObject { ["workOrder"] = "C-1", ["maxBytes"] = "100000" });
        Assert.Equal(text, mcp.Text);
        using JsonDocument payload = JsonDocument.Parse(text);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), payload.RootElement.GetProperty("accounting").GetProperty("bytes").GetInt64());
        Assert.False(mcp.IsError);
        Assert.False(registry.Call("work_brief", new JsonObject { ["workOrder"] = "C-1" }).IsError);
        CliHarness.Ok("--estate", estate.Root, "work", "brief", "C-1");
        Assert.Empty(api.Objects());
        Assert.Equal(2, CliHarness.Run("--estate", estate.Root, "context", "prepare", "C-1", "--max-bytes", "1").Code);
        Assert.True(registry.Call("context_prepare", new JsonObject { ["workOrder"] = "C-1", ["maxBytes"] = "1" }).IsError);
        Assert.True(registry.Call("context_prepare", new JsonObject { ["workOrder"] = "C-1", ["maxBytes"] = "not a cap" }).IsError);
    }

    [Fact]
    public void EveryGeneratedWithheldCommandAndMcpReadReturnsThePinnedBytesOrItsUnavailableState()
    {
        using var estate = new HandoffEstate();
        using EstateService api = EstateService.Open(estate.Root);
        byte[] full = Encoding.UTF8.GetBytes("Original bytes: café\n");
        byte[] partial = Encoding.UTF8.GetBytes("A separate excerpt's original.\n");
        var record = estate.Load().CurrentEstate!;
        ContextAssembly.Source recordSource = ContextAssembly.Record(record, new RecordFile(record.ExpectedPath, File.ReadAllBytes(Path.Combine(estate.Root, record.ExpectedPath))), false, "optional for this step") with { Inline = false };
        ContextPackage package = ContextAssembly.Build(new JsonObject { ["estate"] = estate.Load().Manifest!.Estate },
            [recordSource, Document(estate, "docs/quoted 'name; ignored.md", full), Document(estate, "docs/excerpt.md", partial, 7), Document(estate, "docs/missing.md", null)], "Derived lead.");
        var registry = new ToolRegistry(api, null);
        string scratch = CliHarness.TempDirectory("context-original");
        try
        {
            foreach (JsonElement entry in package.Payload.GetProperty("withheld").EnumerateArray())
            {
                string command = entry.GetProperty("retrieve").GetString()!;
                Assert.Matches("^vericairn context read vc1_[A-Za-z0-9_-]+ --original$", command);
                Assert.Equal(ContextRetrieval.Command(entry.GetProperty("pin").GetString()!), command);
                string[] args = ["--estate", estate.Root, "--json", .. command.Split(' ').Skip(1)];
                CliHarness.Outcome cli = CliHarness.Run(args);
                ToolCallResult mcp = registry.Call("context_read", new JsonObject { ["pin"] = entry.GetProperty("pin").GetString() });
                Assert.Equal(cli.Out, mcp.Text);
                ContextReadResponse read = JsonSerializer.Deserialize<ContextReadResponse>(cli.Out, ProtocolJson.Options)!;
                if (entry.GetProperty("state").GetString() == "missing")
                {
                    Assert.Equal(2, cli.Code);
                    Assert.Equal("original-unavailable", read.Problem!.Code);
                    Assert.True(mcp.IsError);
                }
                else
                {
                    Assert.Equal(0, cli.Code);
                    Assert.Equal(entry.GetProperty("byteHash").GetString(), ContentHash.Sha256(read.Content!));
                    string output = Path.Combine(scratch, entry.GetProperty("key").GetString()!);
                    CliHarness.Ok(["--estate", estate.Root, .. command.Split(' ').Skip(1), "--out", output]);
                    Assert.Equal(read.Content, File.ReadAllBytes(output));
                    Assert.Equal(2, CliHarness.Run(["--estate", estate.Root, .. command.Split(' ').Skip(1), "--out", output]).Code);
                }
            }
        }
        finally
        {
            CliHarness.Delete(scratch);
        }
    }
    private static ContextAssembly.Source Document(HandoffEstate estate, string path, byte[]? bytes, int? excerpt = null)
    {
        byte[] declared = bytes ?? Encoding.UTF8.GetBytes("Unavailable original.");
        var body = new JsonObject
        {
            ["path"] = path, ["title"] = "Context source", ["hash"] = ContentHash.Sha256(declared),
            ["bytes"] = declared.Length, ["mediaType"] = "application/octet-stream", ["revision"] = 1, ["summary"] = "The original.",
        };
        var revision = estate.Add("document-revision", estate.OperatorId, body.ToJsonString());
        if (bytes is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(estate.Root, path))!);
            File.WriteAllBytes(Path.Combine(estate.Root, path), bytes);
        }

        return new ContextAssembly.Source(new JsonObject
        {
            ["kind"] = "document", ["path"] = path, ["revision"] = revision.Id, ["revisionHash"] = revision.ComputedHash,
            ["byteHash"] = ContentHash.Sha256(declared), ["bytes"] = bytes?.LongLength,
        }, bytes, false, "optional for this step", Inline: false, ExcerptBytes: excerpt);
    }

}
