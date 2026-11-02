// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// The runtime supplies an answer's basis: the estate repository's head and the design documents changed since a base, read on every
/// surface. A device-signed answer must state the basis the runtime observes when it is prepared and again when it is written.
/// </summary>
[Collection("daemon")]
public sealed class DecisionBasisApiTests
{
    public DecisionBasisApiTests() => DaemonTestHome.Ensure();

    [Fact]
    public void TheContractScopeAndInterfaceDescribeTheBasisRead()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement operation = document.RootElement.GetProperty("paths").GetProperty("/v1/decisions/basis").GetProperty("get");
        Assert.Contains("DecisionBasisResponse", operation.GetProperty("responses").GetProperty("200").GetRawText(), StringComparison.Ordinal);
        Assert.Equal("base", Assert.Single(operation.GetProperty("parameters").EnumerateArray()).GetProperty("name").GetString());
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(Serialized(typeof(DecisionBasisResponse)), schemas.GetProperty("DecisionBasisResponse").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(Serialized(typeof(DecisionBasisDocument)), schemas.GetProperty("DecisionBasisDocument").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.True(schemas.GetProperty("SignedRecordRequest").GetProperty("properties").TryGetProperty("device", out _));
        Assert.True(schemas.GetProperty("PairingConfirm").GetProperty("properties").TryGetProperty("presenceDevice", out _));
        Assert.True(RequestScope.Covers("GET /v1/decisions/basis"));
        Assert.False(typeof(IEstateApi).GetMethod(nameof(IEstateApi.DecisionBasis))!.IsAbstract);
        Assert.False(typeof(IEstateApi).GetMethod(nameof(IEstateApi.DecisionBasisAsync))!.IsAbstract);

        static IEnumerable<string> Serialized(Type type) => type.GetProperties().Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order();
    }

    [Fact]
    public async Task EverySurfaceReadsTheHeadAndTheDesignDocumentsChangedSinceTheBase()
    {
        using var estate = new TestEstate(3);
        await using DaemonHost host = await DaemonTestHome.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        var bound = new CancellationBoundApi(client, TestContext.Current.CancellationToken);

        // Without a repository source the basis is unavailable, never guessed.
        DecisionBasisResponse unavailable = client.DecisionBasis();
        Assert.False(unavailable.Ok);
        Assert.Equal("unavailable", unavailable.Problem?.Code);
        Assert.Null(unavailable.Head);

        string first = EstateGit.Init(estate.Root, host.Service);
        DecisionBasisResponse clean = await client.DecisionBasisAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(clean.Ok, clean.Problem?.Message);
        Assert.Equal((first, first), (clean.BaseCommit, clean.Head));
        Assert.Empty(clean.Documents!);
        Assert.False(clean.DocumentsChanged);

        // An uncommitted design document is a change since the head, with its current bytes' hash.
        estate.WriteFile("design/guide.md", "# Guide\n\nRevised on the desktop.\n");
        DecisionBasisResponse changed = bound.DecisionBasis();
        Assert.True(changed.DocumentsChanged);
        DecisionBasisDocument document = Assert.Single(changed.Documents!);
        Assert.Equal(("design/guide.md", ContentHash.Sha256File(Path.Combine(estate.Root, "design", "guide.md"))), (document.Path, document.Hash));

        // Once committed, the head moves and nothing changed since it; against the earlier base the document still changed.
        string second = EstateGit.Commit(estate.Root, "Revise the guide");
        DecisionBasisResponse committed = client.DecisionBasis();
        Assert.Equal((second, second), (committed.BaseCommit, committed.Head));
        Assert.Empty(committed.Documents!);
        DecisionBasisResponse since = await bound.DecisionBasisAsync(first, TestContext.Current.CancellationToken);
        Assert.Equal((first, second), (since.BaseCommit, since.Head));
        Assert.Equal("design/guide.md", Assert.Single(since.Documents!).Path);
        Assert.False(client.DecisionBasis("HEAD").Ok);
    }

    [Fact]
    public async Task ADeviceSignedAnswerMustStateTheBasisTheRuntimeObservesWhenPreparedAndWritten()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync(recordFormat: 3);
        CancellationToken token = TestContext.Current.CancellationToken;
        string person = harness.Estate.Add("principal", harness.Estate.OperatorId, """{"kind":"human","name":"Phone operator","roles":["operator"]}""").Id;
        RecordEnvelope ask = harness.Estate.Add("ask", harness.Estate.BuilderId, JsonSerializer.Serialize(new
        {
            to = person, question = "Keep the current scope?", recommendation = "Keep the current scope.", consequence = "Work waits for the answer.", state = "open",
        }));
        string head = EstateGit.Init(harness.Estate.Root, harness.Host.Service);
        var presence = new PresenceKeyStandIn(harness.DeviceHome);
        RuntimeConnection phone = harness.Connections.Add(harness.Address, harness.Issue(person).Code, new PairingOptions("Phone") { PresenceKeyStore = presence });
        using VericairnClient client = harness.Connections.Connect(phone);

        // The paired phone reads the same basis the runtime observes.
        DecisionBasisResponse basis = await client.DecisionBasisAsync(cancellationToken: token);
        Assert.Equal((head, head), (basis.BaseCommit, basis.Head));

        // A commit the runtime cannot observe in the estate's repository is no basis.
        RecordPrepareResponse elsewhere = await client.PrepareRecordAsync(Answer(person, ask, new string('a', 40)), token);
        Assert.False(elsewhere.Ok);
        Assert.Contains("full local commit", elsewhere.Problem!.Message, StringComparison.Ordinal);

        harness.Estate.WriteFile("design/guide.md", "# Guide\n");
        RecordPrepareResponse withDocuments = await client.PrepareRecordAsync(Answer(person, ask, head), token);
        Assert.False(withDocuments.Ok);
        Assert.Contains("design documents changed", withDocuments.Problem!.Message, StringComparison.Ordinal);
        File.Delete(Path.Combine(harness.Estate.Root, "design", "guide.md"));

        // Prepared on the observed basis, then the head moves while the person reviews: nothing is written.
        WriteOutcome moved = await DeviceSignedRecords.WriteWithPresenceAsync(client, Answer(person, ask, head), presence, phone.PresenceDevice!,
            _ => { EstateGit.Commit(harness.Estate.Root, "Move the head"); return true; }, cancellationToken: token);
        Assert.False(moved.Ok);
        Assert.Contains("head", moved.Problem!.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Estate.Load().Current("decision"));

        string current = (await client.DecisionBasisAsync(cancellationToken: token)).Head!;
        WriteOutcome answered = await DeviceSignedRecords.WriteWithPresenceAsync(client, Answer(person, ask, current), presence, phone.PresenceDevice!, cancellationToken: token);
        Assert.True(answered.Ok, answered.Problem?.Message);
        RecordEnvelope decision = Assert.Single(harness.Estate.Load().Current("decision"));
        Assert.Equal(current, decision.Body.GetProperty("changeSet").GetProperty("head").GetString());
        Assert.Equal(phone.PresenceDevice, Assert.Single(decision.Signatures).Device);
        Assert.Equal(0, harness.Socket.Check().Errors);
    }

    [Fact]
    public void TheDesktopRouteCanUseTheSameBasisWithoutATypedCommit()
    {
        using var fixture = new Runtime.Tests.DecisionFixture();
        DecisionBasisResponse basis = fixture.Service.DecisionBasis();
        Assert.Equal((fixture.BaseCommit, fixture.BaseCommit), (basis.BaseCommit, basis.Head));
        Assert.Equal(Runtime.Tests.DecisionFixture.Document, Assert.Single(basis.Documents!).Path);
        DecisionNewResponse answered = fixture.Service.DecisionFromAsk(fixture.Request with { BaseCommit = "" });
        Assert.True(answered.Ok, answered.Problem?.Message);
        JsonElement change = fixture.Service.Record(answered.Written!.Id)!.Envelope.GetProperty("body").GetProperty("changeSet");
        Assert.Equal(fixture.BaseCommit, change.GetProperty("baseCommit").GetString());
        Assert.Equal(fixture.BaseCommit, change.GetProperty("head").GetString());
    }

    private static NewRecordRequest Answer(string person, RecordEnvelope ask, string head)
    {
        string at = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddSeconds(-2));
        const string words = "Keep the current scope.";
        return new("decision", person, JsonSerializer.SerializeToElement(new
        {
            title = "Answer to " + ask.Id, statement = words, summary = words, synthesis = words, effects = new[] { words }, affects = Array.Empty<string>(),
            decidedBy = person, decidedAt = at, stake = "low", notAuthorized = Array.Empty<string>(), answers = new[] { ask.Id },
            answer = new { ask = ask.Id, hash = ask.ComputedHash, at }, changeSet = new { baseCommit = head, head, pins = Array.Empty<object>() },
        }));
    }
}
