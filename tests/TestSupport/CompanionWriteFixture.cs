// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;

namespace Vericairn.TestSupport;

/// <summary>Real record writes with an interruption after storage and before companion publication.</summary>
internal sealed class CompanionWriteFixture : IAsyncDisposable
{
    private static readonly string[] Inputs = ["format=1", "audience=Internal", "selection=all-revisions"];
    private static readonly string[] CompanionTypes = Register();
    private DaemonHost? host;
    private VericairnClient? client;

    public TestEstate Estate { get; } = new(recordFormat: 3);
    public EstateService Service { get; private set; } = null!;
    public IEstateApi Api { get; private set; } = null!;
    public DaemonEndpoint Endpoint => host!.Endpoint;
    public RecordWritten Order { get; private set; } = null!;
    public string Author => Estate.OperatorId;
    public int Publications { get; private set; }

    public static async Task<CompanionWriteFixture> StartAsync(bool daemon)
    {
        var fixture = new CompanionWriteFixture();
        try
        {
            if (daemon)
            {
                fixture.host = await DaemonHost.StartAsync(fixture.Estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
                fixture.Service = fixture.host.Service;
                fixture.Api = fixture.client = new VericairnClient(fixture.host.Endpoint);
            }
            else fixture.Api = fixture.Service = EstateService.Open(fixture.Estate.Root);

            RecordWritten device = Stored(fixture.Service.DeviceCreate(new(fixture.Author, "Test device", "file")));
            Stored(fixture.Service.DeviceAdmit(new(device.Id, fixture.Author, "Test authority.")));
            fixture.Order = Stored(fixture.Service.NewRecord(new("work-order", fixture.Author, Json("""{"key":"COMPANION-1","kind":"ordinary","title":"Exercise companion recovery","intent":"Preserve the stored receipt.","scope":["test"],"exitQuestion":"Is the receipt retained?","inputs":[],"deliverables":["test result"]}"""))));
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public RecordWritten Assign() => Stored(Service.Assign(new("COMPANION-1", Author, Author,
        Json("""{"outcome":"A stored record.","sources":[],"permittedChanges":["test"],"prohibitedEffects":["publication"],"workspace":["test repository"],"limits":[],"expectedOutputs":["result"],"reviewCriteria":["receipt retained"],"recovery":"Reconcile the original companion outputs."}"""))));

    public void DeclareCompanions(string type)
    {
        Assert.Contains(type, CompanionTypes);
        Stored(Service.NewRecord(new("living-document", Author, JsonSerializer.SerializeToElement(new
        {
            pathPattern = "docs/generated/" + type + "/{month}/{date}-{slug}-{id}.md",
            title = "Test companions", kind = "generated", source = "estate-file",
            generator = new { id = "example.stored/" + type, inputs = Inputs },
            triggers = Array.Empty<object>(), summary = "Retain each exact test record.",
        }))));
    }

    public void InterruptPublication() => Service.BeforePublishingDocuments = _ =>
    {
        Publications++;
        throw new IOException("Injected companion publication interruption.");
    };

    public RecordWritten AssertInterrupted(WriteOutcome outcome)
    {
        Assert.False(outcome.Ok);
        RecordWritten written = Assert.IsType<RecordWritten>(outcome.Written);
        AssertRecovery(outcome.Problem!, written.Id);
        RecordResponse record = Assert.IsType<RecordResponse>(Service.Record(written.Id));
        Assert.Equal(written.Hash, record.Hash);
        Assert.True(File.Exists(Path.Combine(Estate.Root, written.Path)));
        Assert.Single(BatchJournal.All(Service.Session).Journals);
        Assert.Equal(1, Publications);
        return written;
    }

    public static void AssertRecovery(Problem problem, string id)
    {
        Assert.NotNull(problem);
        Assert.Equal("reconcile-required", problem.Code);
        Assert.Contains("Record " + id + " was stored.", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Companion publication stopped", problem.Message, StringComparison.Ordinal);
        Assert.Contains("batch journal", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Reopen the estate or run docs regenerate", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(problem.Findings);
    }

    public static RecordWritten Stored(WriteOutcome outcome)
    {
        Assert.True(outcome.Ok, outcome.Problem?.Message);
        return outcome.Written!;
    }

    public static JsonElement Json(string json) => JsonNode.Parse(json)!.Deserialize<JsonElement>();

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (host is not null) await host.DisposeAsync();
        else Service?.Dispose();
        Estate.Dispose();
    }

    private static string[] Register()
    {
        string[] types = ["assignment", "attempt", "review", "acceptance", "communication-setting"];
        foreach (string type in types) DocumentGenerators.Register(new Companion(type));
        return types;
    }

    private sealed class Companion(string type) : IDocumentGenerator
    {
        public string Id => "example.stored/" + type;
        public DocumentGeneratorDescriptor Descriptor { get; } = new("example.stored/" + type, 1, "example.stored/1", "sha256:" + new string('c', 64),
            new Dictionary<string, ImmutableArray<string>> { ["audience"] = ["Internal"], ["selection"] = ["all-revisions"] }.ToImmutableDictionary(StringComparer.Ordinal),
            [type], ["record"], ["item", "all-revisions"], "text/markdown", "UTC time then ordinal identity", "Internal", "record-revision");

        public DocumentRenderResult Generate(DocumentGenerationContext context) =>
            DocumentRenderResult.Success("# Stored record\n\n" + Assert.Single(context.Sources).Identity + "\n");
    }
}
