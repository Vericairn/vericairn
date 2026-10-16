// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class ContextJourneyTests
{
    [Fact]
    public async Task OriginalStdoutPreservesArbitraryBytesWithoutTextEncodingOrFraming()
    {
        using var estate = new HandoffEstate();
        byte[] original = [0, 255, 254, 13, 10, 128, 65];
        var source = new ContextAssembly.Source(new JsonObject
        {
            ["kind"] = "document", ["path"] = "docs/binary.dat", ["byteHash"] = ContentHash.Sha256(original), ["bytes"] = original.LongLength,
        }, original, false, "optional for this step", Inline: false);
        ContextPackage package = ContextAssembly.Build(new JsonObject { ["estate"] = estate.Load().Manifest!.Estate }, [source], "lead");
        using (EstateService api = EstateService.Open(estate.Root))
        {
            api.ObjectPut(original);
            api.ObjectPut(CanonicalJson.SerializePrettyUtf8(package.Payload.GetProperty("manifest").GetProperty("content")));
        }

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
        ContextAssembly.Source Source(string path, byte[]? bytes, int? excerpt = null) => new(new JsonObject
        {
            ["kind"] = "document", ["path"] = path, ["byteHash"] = bytes is null ? "sha256:" + new string('b', 64) : ContentHash.Sha256(bytes), ["bytes"] = bytes?.LongLength,
        }, bytes, false, "optional for this step", Inline: false, ExcerptBytes: excerpt);
        ContextPackage package = ContextAssembly.Build(new JsonObject { ["estate"] = estate.Load().Manifest!.Estate },
            [Source("docs/quoted 'name; ignored.md", full), Source("docs/excerpt.md", partial, 7), Source("docs/missing.md", null)], "Derived lead.");
        api.ObjectPut(full);
        api.ObjectPut(partial);
        string manifest = api.ObjectPut(CanonicalJson.SerializePrettyUtf8(package.Payload.GetProperty("manifest").GetProperty("content"))).Stored!.Hash;
        var registry = new ToolRegistry(api, null);
        string scratch = CliHarness.TempDirectory("context-original");
        try
        {
            foreach (JsonElement entry in package.Payload.GetProperty("withheld").EnumerateArray())
            {
                string command = entry.GetProperty("retrieve").GetString()!;
                Assert.Matches("^vericairn context read sha256:[0-9a-f]{64} --source s[0-9]{6} --original$", command);
                Assert.Equal(ContextAssembly.Retrieval(manifest, entry.GetProperty("key").GetString()!), command);
                string[] args = ["--estate", estate.Root, "--json", .. command.Split(' ').Skip(1)];
                CliHarness.Outcome cli = CliHarness.Run(args);
                ToolCallResult mcp = registry.Call("context_read", new JsonObject { ["manifest"] = manifest, ["source"] = entry.GetProperty("key").GetString() });
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
}
