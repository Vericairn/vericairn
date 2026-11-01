// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class ReleaseChannelTests
{
    private static readonly string[] Channels = ["stable", "lts", "prerelease", "nightly"];
    private static readonly string[] BackportKeys = ["id", "hash"];

    [Theory]
    [InlineData("stable", "--latest")]
    [InlineData("lts", "--latest=false")]
    [InlineData("prerelease", "--prerelease")]
    [InlineData("nightly", "--prerelease")]
    public void SubmissionKeepsTheChannelAndBackportThroughCandidateAndPublication(string channel, string flags)
    {
        using var f = new Fixture();
        ReleaseSubmitRequest request = f.Request with
        {
            Line = "maintenance", Channel = channel, EndOfSupport = channel == "lts" ? f.EndOfSupport : null,
            BackportOf = new(f.Source.Id, f.Source.ComputedHash),
        };
        ReleaseResponse submitted = f.Api.ReleaseSubmit(request);
        Assert.True(submitted.Ok, submitted.Message);
        DispatchRequest dispatch = Assert.Single(f.Provider.Dispatches);
        Assert.False(dispatch.Settings.ContainsKey("releaseChannel"));
        Assert.Equal("1.1.0", dispatch.Inputs["version"]);
        f.Provider.CompleteRun("Example/widget", submitted.Run!, true, request.Version);
        var observe = new ReleaseObserveRequest(request.Version, "Example/widget", f.Records.Estate.OperatorId);
        ReleaseResponse candidate = f.Api.ReleaseObserve(observe);
        Assert.True(candidate.Ok, candidate.Message);
        string command = $"gh release edit v1.1.0 -R Example/widget --draft=false {flags}";
        Assert.Equal(command, candidate.Publish);
        RecordEnvelope recorded = f.Api.Session.Load().ById[candidate.Candidate!.Id];
        Assert.Equal("maintenance", EstateModel.BodyString(recorded, "line"));
        Assert.Equal(channel, EstateModel.BodyString(recorded, "channel"));
        Assert.Equal(f.Source.Id, recorded.Body.GetProperty("backportOf").GetProperty("id").GetString());
        Assert.Equal(f.Source.ComputedHash, recorded.Body.GetProperty("backportOf").GetProperty("hash").GetString());
        Assert.Equal(channel == "lts", recorded.Body.TryGetProperty("endOfSupport", out JsonElement end));
        if (request.EndOfSupport is not null) Assert.Equal(request.EndOfSupport, end.GetString());
        Assert.Equal(command, f.Api.ReleaseObserve(observe).Publish);

        // An observation cannot accept or publish the candidate on any channel.
        Assert.DoesNotContain(f.Api.Session.Load().Current("acceptance"), a => a.Subject?.Id == recorded.Id);
        Assert.True(f.Provider.ObserveRelease("Example/widget", "v1.1.0", dispatch.Settings)!.Draft);
        WriteOutcome accepted = f.Api.NewRecord(new("acceptance", request.By,
            PolicyFixture.Element(new JsonObject { ["statement"] = "Accept the channel fixture.", ["basis"] = new JsonArray(), ["self"] = true }),
            $"release:{recorded.Id}:{recorded.ComputedHash}"));
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        f.Provider.Publish("Example/widget", "v1.1.0");
        ReleaseResponse published = f.Api.ReleaseObserve(observe);
        Assert.True(published.Ok, published.Message);
        RecordEnvelope publication = f.Api.Session.Load().ById[published.Release!.Id];
        Assert.True(Vericairn.Records.Types.ReleaseType.IsPublicationOf(recorded, publication));
        Assert.Equal(channel, EstateModel.BodyString(publication, "channel"));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Theory]
    [InlineData("name", "body.pattern")]
    [InlineData("channel", "body.enum")]
    [InlineData("support", "body.missing")]
    [InlineData("backport", "release.backport-hash")]
    [InlineData("line-channel", "release.line-channel")]
    public void InvalidMetadataIsRefusedBeforeAnyIntentOrDispatch(string mutation, string code)
    {
        using var f = new Fixture();
        ReleaseSubmitRequest request = mutation switch
        {
            "name" => f.Request with { Line = "Bad" },
            "channel" => f.Request with { Channel = "preview" },
            "support" => f.Request with { Line = "1.x", Channel = "lts" },
            "backport" => f.Request with { Line = "1.x", BackportOf = new(f.Source.Id, "sha256:" + new string('0', 64)) },
            _ => f.Request with { Channel = "nightly" },
        };
        ReleaseResponse checkedResult = f.Api.ReleaseCheck(request);
        Assert.False(checkedResult.Ok);
        Assert.Contains(checkedResult.Check!.Objections, o => o.Code == code);
        ReleaseResponse submitted = f.Api.ReleaseSubmit(request);
        Assert.False(submitted.Ok);
        Assert.Contains(submitted.Problem!.Findings, o => o.Code == code);
        Assert.Empty(f.Provider.Dispatches);
        Assert.Empty(f.Api.Session.Load().OfType("operation"));
    }

    [Fact]
    public void AProviderWrittenBeforeChannelsSupportsStableAndRefusesOtherChannels()
    {
        IReleaseProvider provider = new LegacyProvider();
        Assert.Equal("Publish the stable fixture.", provider.ManualPublicationCommand("Example/widget", "v1.1.0", "stable"));
        Assert.Throws<NotSupportedException>(() => provider.ManualPublicationCommand("Example/widget", "v1.1.0", "lts"));
        Assert.Throws<NotSupportedException>(() => provider.ManualPublicationCommand("Example/widget", "v1.1.0", "prerelease"));
        Assert.Throws<NotSupportedException>(() => provider.ManualPublicationCommand("Example/widget", "v1.1.0", "nightly"));
    }

    [Theory]
    [InlineData("lts")]
    [InlineData("prerelease")]
    [InlineData("nightly")]
    public void AnOlderProviderLeavesPublicationExplicitlyUnsupported(string channel)
    {
        using var f = new Fixture();
        f.Api.Providers.Register(new LegacyProvider(f.Provider));
        ReleaseSubmitRequest request = f.Request with { Line = "maintenance", Channel = channel, EndOfSupport = channel == "lts" ? f.EndOfSupport : null };
        ReleaseResponse submitted = f.Api.ReleaseSubmit(request);
        Assert.True(submitted.Ok, submitted.Message);
        f.Provider.CompleteRun("Example/widget", submitted.Run!, true, request.Version);
        var observe = new ReleaseObserveRequest(request.Version, "Example/widget", f.Records.Estate.OperatorId);
        for (int i = 0; i < 2; i++)
        {
            ReleaseResponse response = f.Api.ReleaseObserve(observe);
            Assert.True(response.Ok, response.Message);
            Assert.Null(response.Publish);
            Assert.Contains($"does not support a publication command for channel '{channel}'", response.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("--latest", response.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OmittedFieldsStillCheckTheDefaultAgainstTheExistingLine()
    {
        using var f = new Fixture(sourceChannel: "nightly");
        ReleaseResponse result = f.Api.ReleaseCheck(f.Request);
        Assert.False(result.Ok);
        Assert.Contains(result.Check!.Objections, o => o.Code == "release.line-channel");
        Assert.False(f.Api.ReleaseSubmit(f.Request).Ok);
        Assert.Empty(f.Provider.Dispatches);
        Assert.Empty(f.Api.Session.Load().OfType("operation"));
    }

    [Theory]
    [InlineData("releaseLine")]
    [InlineData("releaseChannel")]
    [InlineData("releaseEndOfSupport")]
    [InlineData("releaseBackport")]
    [InlineData("releaseBackportHash")]
    public void ProviderSettingsCannotSupplyReleaseMetadata(string key)
    {
        using var f = new Fixture();
        ReleaseSubmitRequest request = f.Request with { Settings = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = "nightly" } };
        ReleaseResponse result = f.Api.ReleaseCheck(request);
        Assert.False(result.Ok);
        Assert.Contains(result.Check!.Objections, o => o.Code == "release.metadata-setting");
        Assert.False(f.Api.ReleaseSubmit(request).Ok);
        Assert.Empty(f.Provider.Dispatches);
        Assert.Empty(f.Api.Session.Load().OfType("operation"));
    }

    [Fact]
    public async Task ProtocolAndMcpKeepAllFourFieldsOnAReadOnlyCheck()
    {
        using var f = new Fixture();
        ReleaseSubmitRequest request = f.Request with
        {
            Line = "1.x", Channel = "lts", EndOfSupport = f.EndOfSupport, BackportOf = new(f.Source.Id, f.Source.ComputedHash),
        };
        string json = JsonSerializer.Serialize(request, ProtocolJson.Options);
        Assert.Equal(request, JsonSerializer.Deserialize<ReleaseSubmitRequest>(json, ProtocolJson.Options));
        int before = f.Api.Session.Load().Records.Count;
        var registry = new ToolRegistry(f.Api, null);
        ToolCallResult result = await registry.CallAsync("release_submit_check", JsonNode.Parse(json)!.AsObject(), TestContext.Current.CancellationToken);
        Assert.False(result.IsError, result.Text);
        Assert.Equal(before, f.Api.Session.Load().Records.Count);
        Assert.Empty(f.Provider.Dispatches);
        request = request with { Channel = "nightly" };
        result = await registry.CallAsync("release_submit_check", JsonSerializer.SerializeToNode(request, ProtocolJson.Options)!.AsObject(), TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Contains("release.end-of-support", result.Text, StringComparison.Ordinal);
        JsonObject extra = JsonNode.Parse(json)!.AsObject();
        extra["backportOf"]!["type"] = "release";
        result = await registry.CallAsync("release_submit_check", extra, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(before, f.Api.Session.Load().Records.Count);

        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        JsonElement fields = contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ReleaseSubmitRequest").GetProperty("properties");
        Assert.Equal("^[a-z0-9][a-z0-9.-]{0,39}$", fields.GetProperty("line").GetProperty("pattern").GetString());
        Assert.Equal(Channels, fields.GetProperty("channel").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("date-time", fields.GetProperty("endOfSupport").GetProperty("format").GetString());
        Assert.Equal(BackportKeys, fields.GetProperty("backportOf").GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.False(fields.GetProperty("backportOf").GetProperty("additionalProperties").GetBoolean());
    }

    private sealed class LegacyProvider(FakeReleaseProvider? inner = null) : IReleaseProvider
    {
        public string Provider => inner?.Provider ?? "fixture-releases";
        public ProviderDescriptor Descriptor => inner?.Descriptor ?? new FakeReleaseProvider(Provider).Descriptor;
        public DispatchReceipt Dispatch(DispatchRequest request) => inner?.Dispatch(request) ?? throw new InvalidOperationException("No dispatch expected.");
        public RunObservation? ObserveRun(string destination, string runId) => inner?.ObserveRun(destination, runId);
        public ReleaseObservation? ObserveRelease(string destination, string tag, IReadOnlyDictionary<string, string> settings) => inner?.ObserveRelease(destination, tag, settings);
        public string ManualPublicationCommand(string destination, string tag) => inner?.ManualPublicationCommand(destination, tag) ?? "Publish the stable fixture.";
        public byte[]? ReadAsset(string destination, string tag, string name) => inner?.ReadAsset(destination, tag, name);
    }

    private sealed class Fixture : IDisposable
    {
        public PolicyFixture Records { get; } = new();
        public FakeReleaseProvider Provider { get; } = new();
        public EstateService Api { get; }
        public RecordEnvelope Product { get; }
        public RecordEnvelope Source { get; }
        public string EndOfSupport { get; } = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddYears(2));
        public ReleaseSubmitRequest Request => new(Product.Id, "1.1.0", "Example/widget", Records.Estate.OperatorId,
            Provider: Provider.Provider, Commit: Provider.HeadSha, Expected: Product.ComputedHash) { ClientBuild = EstateService.Version };

        public Fixture(string? sourceChannel = null)
        {
            Product = Records.Add("product", ReleaseLineFixture.ProductBody());
            JsonObject source = ReleaseLineFixture.Body("2.1.0", channel: sourceChannel);
            source["repository"] = "Example/widget";
            RecordEnvelope candidate = Records.Add("release", source, subject: Product.Reference);
            Records.Add("acceptance", new() { ["statement"] = "Accept the source fixture.", ["basis"] = new JsonArray(), ["self"] = true }, subject: candidate.Reference);
            source["status"] = "published";
            source["publishedAt"] = RecordParser.FormatTimestamp(Records.Estate.Clock.AddSeconds(1));
            source["url"] = "https://example.invalid/releases/2.1.0";
            Source = Records.Add("release", source, subject: Product.Reference, supersedes: candidate.Id);
            Api = EstateService.Open(Records.Estate.Root);
            WriteOutcome device = Api.DeviceCreate(new(Records.Estate.OperatorId, "Channel fixture writer", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            RecordEnvelope held = Api.Session.Load().ById[device.Written!.Id];
            // Admission may share the device's second; it must not be put ahead of subsequent runtime writes.
            Records.Estate.SetClock(held.AtTime.AddSeconds(-1));
            Records.Add("admission", new() { ["principal"] = Records.Estate.OperatorId, ["status"] = "admitted", ["note"] = "Admit the isolated fixture writer." }, subject: held.Reference);
            Api.Providers = new ProviderRegistry();
            Api.Providers.Register(Provider);
            ReleaseGateSource.Prepare(Api, "Example/widget", Provider.HeadSha);
        }

        public void Dispose() { Api.Dispose(); Records.Dispose(); }
    }
}
