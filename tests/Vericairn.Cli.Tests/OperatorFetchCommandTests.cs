// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Tests;
using RequestData = Vericairn.Records.Tests.OperatorRequestTests;

namespace Vericairn.Cli.Tests;

/// <summary>OPS-22 (0.9.4): vericairn operator fetch writes a request's pinned files, under their names, and its guide into a folder.</summary>
[Collection("cli")]
public sealed class OperatorFetchCommandTests
{
    public OperatorFetchCommandTests() => _ = CliHarness.BuiltExecutable;

    [Fact]
    public void ACompleteKitIsWrittenUnderItsNamesBesideItsGuide()
    {
        using var f = new OperatorCloseFixture();
        byte[] post = Encoding.UTF8.GetBytes("The post's exact text.\n");
        byte[] image = [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF];
        Assert.NotNull(f.Api.ObjectPut(post).Stored);
        Assert.NotNull(f.Api.ObjectPut(image).Stored);
        RecordEnvelope request = Kit(f, ("post.md", post), ("card.png", image));
        string folder = Path.Combine(f.Estate.Root, "kit");

        CliHarness.Outcome outcome = Run(f, request.Id, "--to", folder);

        Assert.True(outcome.Code == 0, outcome.Err + outcome.Out);
        Assert.Equal(post, File.ReadAllBytes(Path.Combine(folder, "post.md")));
        Assert.Equal(image, File.ReadAllBytes(Path.Combine(folder, "card.png")));
        string guide = File.ReadAllText(Path.Combine(folder, "guide.md"));
        Assert.StartsWith("# Perform action 1\n", guide, StringComparison.Ordinal);
        Assert.Contains("Exact draft.", guide, StringComparison.Ordinal);
        Assert.Contains("the file post.md, beside this guide", guide, StringComparison.Ordinal);
        Assert.Contains($"vericairn operator dismiss {request.Id} --by {f.Estate.OperatorId} --expected {request.ComputedHash} --reason <your-reason>", guide, StringComparison.Ordinal);
        Assert.Equal(["card.png", "guide.md", "post.md"], Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains("Wrote post.md at " + ContentHash.Sha256(post), outcome.Out, StringComparison.Ordinal);
        // A second fetch into the same folder replaces nothing.
        Assert.Equal(1, Run(f, request.Id, "--to", folder).Code);
        Assert.Equal(post, File.ReadAllBytes(Path.Combine(folder, "post.md")));
    }

    [Fact]
    public void AMissingObjectWritesNothing()
    {
        using var f = new OperatorCloseFixture();
        byte[] post = Encoding.UTF8.GetBytes("The post's exact text.\n");
        byte[] lost = Encoding.UTF8.GetBytes("Never stored.\n");
        Assert.NotNull(f.Api.ObjectPut(post).Stored);
        // A request recorded before 0.9.4 could pin a file only one machine held; write it as such a record was written.
        RecordEnvelope work = RequestData.Work(f.Estate);
        JsonObject body = RequestData.Body(f.Estate);
        body["content"]!.AsArray().Add(new JsonObject { ["kind"] = "file", ["name"] = "post.md", ["hash"] = ContentHash.Sha256(post) });
        body["content"]!.AsArray().Add(new JsonObject { ["kind"] = "file", ["name"] = "lost.md", ["hash"] = ContentHash.Sha256(lost) });
        RecordEnvelope request = f.Estate.Add("operator-request", f.Estate.BuilderId, body.ToJsonString(), work.Reference);
        string folder = Path.Combine(f.Estate.Root, "kit");

        CliHarness.Outcome outcome = Run(f, request.Id, "--to", folder, "--json");

        Assert.Equal(1, outcome.Code);
        Assert.Contains("the estate's objects do not hold lost.md (" + ContentHash.Sha256(lost) + ")", outcome.Json.GetProperty("problem").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void ANameThatIsNotAPlainFileNameIsRefused()
    {
        using var f = new OperatorCloseFixture();
        byte[] post = Encoding.UTF8.GetBytes("The post's exact text.\n");
        Assert.NotNull(f.Api.ObjectPut(post).Stored);
        RecordEnvelope request = Kit(f, ("../post.md", post));
        string folder = Path.Combine(f.Estate.Root, "kit");

        Assert.Equal(1, Run(f, request.Id, "--to", folder).Code);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(1, Run(f, Records.Identity.Ulid.NewUlid().Value, "--to", folder).Code);
    }

    private static RecordEnvelope Kit(OperatorCloseFixture f, params (string Name, byte[] Bytes)[] files)
    {
        RecordEnvelope work = RequestData.Work(f.Estate);
        JsonObject body = RequestData.Body(f.Estate);
        body["optional"] = true;
        foreach ((string name, byte[] bytes) in files)
            body["content"]!.AsArray().Add(new JsonObject { ["kind"] = "file", ["name"] = name, ["hash"] = ContentHash.Sha256(bytes) });
        WriteOutcome written = f.Api.OperatorRequestWrite(new(f.Estate.BuilderId, JsonSerializer.SerializeToElement(body), work.Type + ":" + work.Id + ":" + work.ComputedHash));
        Assert.True(written.Ok, written.Problem?.Message);
        return f.Estate.Load().ById[written.Written!.Id];
    }

    private static CliHarness.Outcome Run(OperatorCloseFixture fixture, params string[] arguments) =>
        CliHarness.RunWithApi(fixture.Api, null, ["--estate", fixture.Estate.Root, "operator", "fetch", .. arguments]);
}
