// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Cadence records and an in-memory release provider, entirely within test-owned storage.</summary>
public sealed class ReleaseCadenceFixture : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private readonly CadenceProvider provider;
    private readonly Clock clock = new();
    public PolicyFixture Records { get; } = new();
    public EstateService Api { get; }
    public RecordEnvelope Product { get; private set; }
    public RecordEnvelope Policy { get; }
    public DateTimeOffset DueAt { get; }
    public int Dispatches => provider.Dispatches;
    public int ProviderCalls => provider.Calls;
    public ReleaseSubmitRequest Request => new(Product.Id, "1.0.0", "Example/example", Records.Estate.OperatorId,
        Commit: Commit, Expected: Product.ComputedHash) { ClientBuild = EstateService.Version };

    public ReleaseCadenceFixture(bool declare = true, bool plan = true, bool publicationSchedule = false, int graceDays = 0)
    {
        provider = new(clock);
        JsonObject product = ReleaseLineFixture.ProductBody();
        product["release"] = new JsonObject { ["destination"] = new JsonObject { ["provider"] = provider.Provider,
            ["settings"] = new JsonObject { ["repository"] = "Example/example" } } };
        Product = Records.Add("product", product);
        Records.Add("resource", new JsonObject { ["kind"] = "repository", ["name"] = "Release source", ["role"] = "Fixture release source.",
            ["location"] = new JsonObject { ["github"] = "Example/example" }, ["visibility"] = "public" });
        Api = EstateService.Open(Records.Estate.Root);
        RecordWritten device = Api.DeviceCreate(new(Records.Estate.OperatorId, "Cadence fixture writer", "file")).Written!;
        RecordEnvelope held = Api.Session.Load().ById[device.Id];
        Records.Estate.SetClock(held.AtTime);
        Records.Add("admission", new JsonObject { ["principal"] = Records.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Admit the isolated fixture writer." }, subject: held.Reference);
        DueAt = Records.Estate.Clock.AddMinutes(2);
        JsonObject policy = Records.PolicyBody("release-cadence");
        policy["startsAt"] = RecordParser.FormatTimestamp(Records.Estate.Clock.AddSeconds(2));
        JsonObject parameters = policy["rules"]![0]!["parameters"]!.AsObject();
        parameters["graceDays"] = graceDays;
        parameters["schedule"] = publicationSchedule ? new JsonObject { ["kind"] = "release", ["product"] = Product.Id } :
            new JsonObject { ["kind"] = "interval", ["unit"] = "months", ["every"] = 1, ["firstDueAt"] = RecordParser.FormatTimestamp(DueAt) };
        Policy = Records.Policy(policy);
        if (declare)
        {
            product["cadence"] = new JsonObject { ["policy"] = PolicyFixture.Ref(Policy), ["rule"] = "monthly" };
            Product = Records.Add("product", product, supersedes: Product.Id);
        }
        if (plan) Plan("1.0.0");
        Api.Providers = new ProviderRegistry();
        Api.Providers.Register(provider);
        Api.PolicyClock = clock;
        clock.Now = Records.Estate.Clock;
        ReleaseGateSource.Prepare(Api, "Example/example", Commit);
        Records.AssertValid();
    }

    public RecordEnvelope Plan(string version) => Records.Add("work-order", new JsonObject
    {
        ["key"] = "REL-" + Records.Estate.Load().OfType("work-order").Count(), ["kind"] = "ordinary", ["title"] = "Prepare the release", ["intent"] = "Prepare a release for the operator.",
        ["scope"] = new JsonArray("fixture"), ["exitQuestion"] = "Is the release ready?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("candidate"),
        ["releasePlan"] = new JsonObject { ["product"] = Records.Estate.Load().Chain(Product)[0], ["version"] = version },
    });

    public void At(DateTimeOffset at)
    {
        Records.Estate.SetClock(at.AddSeconds(-1));
        Records.Add("decision", Records.DecisionBody());
        clock.Now = at;
        Records.AssertValid();
    }

    public RecordEnvelope Candidate(string version = "1.0.0", string? line = null, string? channel = null) =>
        Records.Add("release", ReleaseLineFixture.Body(version, line, channel), subject: Product.Reference);

    public RecordEnvelope Publish(string version)
    {
        RecordEnvelope candidate = Candidate(version);
        Records.Add("acceptance", new JsonObject { ["statement"] = "Accept the fixture release.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
        JsonObject body = PolicyFixture.Body(candidate);
        body["status"] = "published";
        body["publishedAt"] = RecordParser.FormatTimestamp(Records.Estate.Clock.AddSeconds(1));
        body["url"] = "https://example.invalid/releases/" + version;
        return Records.Add("release", body, subject: Product.Reference, supersedes: candidate.Id);
    }

    public void RemoveCadence()
    {
        JsonObject product = PolicyFixture.Body(Product);
        product.Remove("cadence");
        Product = Records.Add("product", product, supersedes: Product.Id);
    }

    public string[] Snapshot() => Directory.EnumerateFiles(Records.Estate.Root, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(Records.Estate.Root, file) + " " + ContentHash.Sha256File(file)).Order(StringComparer.Ordinal).ToArray();

    public void Dispose() { Api.Dispose(); Records.Dispose(); }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CadenceProvider(Clock clock) : IReleaseProvider
    {
        internal int Calls { get; private set; }
        internal int Dispatches { get; private set; }
        public string Provider => "github-releases";
        public ProviderDescriptor Descriptor { get; } = new FakeReleaseProvider().Descriptor;
        public string? ResolveCommit(string destination, IReadOnlyDictionary<string, string> settings) { Calls++; throw new InvalidOperationException("A check must not resolve through the provider."); }
        public DispatchReceipt Dispatch(DispatchRequest request)
        {
            Calls++;
            Dispatches++;
            return new("fixture-run", "https://example.invalid/fixture-run", "Fixture dispatch.", RecordParser.FormatTimestamp(clock.Now));
        }
        public RunObservation? ObserveRun(string destination, string runId) { Calls++; return null; }
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) { Calls++; return null; }
        public string ManualPublicationCommand(string destination, string tag) { Calls++; return "Manual fixture publication."; }
    }
}
