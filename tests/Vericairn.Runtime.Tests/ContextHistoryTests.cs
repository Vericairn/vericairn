// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

public sealed class ContextHistoryTests
{
    private const string DocumentPath = "design/pinned-original.md";
    private static readonly byte[] Original = [0, 255, 254, 13, 10, 128, 65, 195, 169];
    private static readonly string[] GitSettings = ["-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredEarlierRevisionIsReadFromHistoryWithoutChangingTheEstate(bool deleted)
    {
        using var fixture = new Fixture();
        TestEstate estate = fixture.Estate;
        RecordEnvelope revision = SeedHistory(estate);
        using EstateService service = Open(estate.Root);
        if (deleted)
        {
            File.Delete(Path.Combine(estate.Root, DocumentPath));
            Git(estate.Root, "add", "-u");
            Git(estate.Root, "commit", "-qm", "Remove the working original");
        }
        string before = Git(estate.Root, "status", "--porcelain=v1");
        string head = Git(estate.Root, "rev-parse", "HEAD");
        string index = ContentHash.Sha256File(Path.Combine(estate.Root, ".git", "index"));
        int records = estate.Load().Records.Count;

        ContextResponse response = service.Context("H-1");
        Assert.Null(response.Problem);
        JsonElement original = Document(response.Package!);
        Assert.Equal(revision.Id, original.GetProperty("revision").GetString());
        Assert.Equal(revision.ComputedHash, original.GetProperty("revisionHash").GetString());
        Assert.Equal(ContentHash.Sha256(Original), original.GetProperty("byteHash").GetString());
        Assert.Equal(Original, Convert.FromBase64String(original.GetProperty("content").GetString()!));
        ContextReadResponse read = service.ContextRead(original.GetProperty("pin").GetString()!);
        Assert.Null(read.Problem);
        Assert.Equal(Original, read.Content);
        Assert.Null(service.Brief("H-1")!.Problem);
        Assert.Equal(records, estate.Load().Records.Count);
        Assert.Empty(service.Objects());
        Assert.Equal(head, Git(estate.Root, "rev-parse", "HEAD"));
        Assert.Equal(index, ContentHash.Sha256File(Path.Combine(estate.Root, ".git", "index")));
        Assert.Equal(before, Git(estate.Root, "status", "--porcelain=v1"));
        if (!deleted)
        {
            Assert.Equal("Newest revision.\n", File.ReadAllText(Path.Combine(estate.Root, DocumentPath)));
        }
    }

    [Theory]
    [InlineData("no-history")]
    [InlineData("shallow")]
    [InlineData("shallow-with-original")]
    [InlineData("hash-mismatch")]
    public void UnavailableHistoryRefusesBothPreparationAndThePreviouslyIssuedPin(string missing)
    {
        using var fixture = new Fixture();
        TestEstate estate = fixture.Estate;
        SeedHistory(estate);
        using EstateService original = Open(estate.Root);
        ContextResponse available = original.Context("H-1");
        Assert.Null(available.Problem);
        string pin = Document(available.Package!).GetProperty("pin").GetString()!;
        string copy = Path.Combine(estate.Root, "copy");
        bool shallow = missing.StartsWith("shallow", StringComparison.Ordinal);
        Git(estate.Root, "clone", "--no-local", "--depth", missing == "shallow" ? "1" : missing == "shallow-with-original" ? "2" : "10", ".", "copy");
        try
        {
            if (shallow)
            {
                Assert.Equal("true", Git(copy, "rev-parse", "--is-shallow-repository").Trim());
            }
            else
            {
                WorkspaceCleanup.Delete(Path.Combine(copy, ".git"));
                if (missing == "hash-mismatch")
                {
                    // The record pin survives, but this repository has only another revision's bytes.
                    Git(copy, "init", "-q");
                    // Exact bytes under a different path cannot satisfy this pin either.
                    File.WriteAllBytes(Path.Combine(copy, "design", "different-path.md"), Original);
                    Git(copy, "add", ".");
                    Git(copy, "commit", "-qm", "A copy with only the latest bytes");
                }
            }
            using EstateService service = Open(copy);
            ContextResponse refused = service.Context("H-1");
            Assert.Null(refused.Package);
            Assert.Equal("incomplete-context", refused.Problem!.Code);
            Assert.Contains(refused.Problem.Findings, f => f.Code == "context.original-unavailable" && f.Location == DocumentPath);
            Assert.Contains("local history", refused.Problem.Message, StringComparison.Ordinal);
            ContextReadResponse read = service.ContextRead(pin);
            Assert.Null(read.Content);
            Assert.Equal("original-unavailable", read.Problem!.Code);
            Assert.Contains("local history", read.Problem.Message, StringComparison.Ordinal);

            // Even a shallow or history-less copy can use exact bytes already in its working tree.
            File.WriteAllBytes(Path.Combine(copy, DocumentPath), Original);
            Assert.Null(service.Context("H-1").Problem);
            Assert.Equal(Original, service.ContextRead(pin).Content);
        }
        finally
        {
            WorkspaceCleanup.Delete(copy);
        }
    }

    [Fact]
    public void RuntimeVerifiesAdapterBytesAndPinnedLengthInsteadOfTrustingTheAdapter()
    {
        using var fixture = new Fixture();
        TestEstate estate = fixture.Estate;
        SeedHistory(estate);
        using EstateService service = Open(estate.Root);
        string pin = Document(service.Context("H-1").Package!).GetProperty("pin").GetString()!;
        JsonObject identity = JsonNode.Parse(ContextRetrieval.Decode(pin, estate.Estate.Id).GetRawText())!.AsObject();
        identity["bytes"] = Original.Length + 1;
        Assert.Equal("original-unavailable", service.ContextRead(ContextRetrieval.Pin(identity, estate.Estate.Id)).Problem!.Code);

        service.Providers.Register(new IncorrectHistory());
        ContextResponse refused = service.Context("H-1");
        Assert.Null(refused.Package);
        Assert.Contains(refused.Problem!.Findings, f => f.Code == "context.original-unavailable");
        ContextReadResponse read = service.ContextRead(pin);
        Assert.Null(read.Content);
        Assert.Equal("original-unavailable", read.Problem!.Code);
    }

    [Fact]
    public void ARequiredPinStillRefusesWhenTheHostHasNoHistoryAdapter()
    {
        using var fixture = new Fixture();
        TestEstate estate = fixture.Estate;
        SeedHistory(estate);
        using EstateService service = Open(estate.Root);
        string pin = Document(service.Context("H-1").Package!).GetProperty("pin").GetString()!;
        service.Providers = new ProviderRegistry();
        Assert.Contains(service.Context("H-1").Problem!.Findings, f => f.Code == "context.original-unavailable");
        Assert.Null(service.ContextRead(pin).Content);
    }

    private sealed class IncorrectHistory : ISourceAdapter
    {
        public ProviderDescriptor Descriptor { get; } = new("incorrect-history", ISourceAdapter.Contract, "implemented", ["read-file-at-hash"], []);
        public bool IsRepository(string path) => throw new NotSupportedException();
        public RepositoryObservation Inspect(string path) => throw new NotSupportedException();
        public byte[]? ReadFileAtHash(string root, string relativePath, string hash) => Encoding.UTF8.GetBytes("Another revision.");
    }

    [Fact]
    public void OptionalDocumentsRemainWorkingTreeOnlyEvenWhenHistoryHasThePinnedBytes()
    {
        using var fixture = new Fixture();
        TestEstate estate = fixture.Estate;
        ContextTests.AddDocument(estate, DocumentPath, Original);
        estate.Add("work-order", estate.OperatorId, HandoffTests.Order.Replace("Continue from records.", "Read " + DocumentPath + " if useful.", StringComparison.Ordinal));
        Git(estate.Root, "init", "-q");
        Git(estate.Root, "add", ".");
        Git(estate.Root, "commit", "-qm", "Keep the optional original");
        using EstateService service = Open(estate.Root);
        JsonElement source = service.Context("H-1").Package!.Payload.GetProperty("withheld").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
        Assert.Equal("withheld-but-retrievable", source.GetProperty("state").GetString());
        string pin = source.GetProperty("pin").GetString()!;
        File.WriteAllText(Path.Combine(estate.Root, DocumentPath), "Changed optional document.");
        foreach (long? limit in new long?[] { null, 1_000_000 })
        {
            ContextResponse response = service.Context("H-1", limit);
            Assert.Null(response.Problem);
            JsonElement missing = response.Package!.Payload.GetProperty("withheld").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "document");
            Assert.Equal("missing", missing.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, missing.GetProperty("originalBytes").ValueKind);
        }
        Assert.Equal("original-unavailable", service.ContextRead(pin).Problem!.Code);
    }

    private sealed class Fixture : IDisposable
    {
        internal TestEstate Estate { get; } = new(recordFormat: 2);

        public void Dispose()
        {
            WorkspaceCleanup.Delete(Estate.Root);
            Estate.Dispose();
        }
    }

    private static RecordEnvelope SeedHistory(TestEstate estate)
    {
        RecordEnvelope revision = ContextTests.AddDocument(estate, DocumentPath, Original);
        JsonObject order = JsonNode.Parse(HandoffTests.Order)!.AsObject();
        order["inputs"] = new JsonArray(new JsonObject { ["type"] = revision.Type, ["id"] = revision.Id, ["hash"] = revision.ComputedHash });
        estate.Add("work-order", estate.OperatorId, order.ToJsonString());
        Git(estate.Root, "init", "-q");
        Git(estate.Root, "add", ".");
        Git(estate.Root, "commit", "-qm", "Keep the pinned original");
        byte[] newer = Encoding.UTF8.GetBytes("Newest revision.\n");
        File.WriteAllBytes(Path.Combine(estate.Root, DocumentPath), newer);
        JsonObject body = JsonNode.Parse(revision.Body.GetRawText())!.AsObject();
        body["revision"] = 2;
        body["hash"] = ContentHash.Sha256(newer);
        body["bytes"] = newer.Length;
        estate.Add("document-revision", estate.OperatorId, body.ToJsonString(), supersedes: revision.Id);
        Git(estate.Root, "add", ".");
        Git(estate.Root, "commit", "-qm", "Revise the document");
        return revision;
    }

    private static EstateService Open(string root)
    {
        EstateService service = EstateService.Open(root);
        service.Providers = new ProviderRegistry();
        service.Providers.Register(new GitCommandAdapter());
        return service;
    }

    private static JsonElement Document(ContextPackage package) =>
        Assert.Single(package.Payload.GetProperty("originals").EnumerateArray(), s => s.GetProperty("kind").GetString() == "document");

    private static string Git(string root, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in GitSettings.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000));
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }
}
