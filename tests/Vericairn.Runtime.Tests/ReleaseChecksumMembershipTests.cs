// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// FIX-140: 0.9.3's SHA256SUMS named its companions archive, which the release did not attach, so a reader could not download every
/// listed file from the release. From 0.9.4 the observation refuses such a draft as a known failure naming each missing file, records no
/// candidate, and the submission check names the files until a reconciliation settles the attempt; a complete draft becomes the candidate.
/// </summary>
public class ReleaseChecksumMembershipTests
{
    private const string Companions = "vericairn-1.1.0-companions.zip";

    [Fact]
    public void ADraftWhoseChecksumFileNamesAnUnattachedFileIsAKnownFailureAndNoCandidate()
    {
        using var f = new Fixture();
        ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
        Assert.True(submitted.Ok, submitted.Message);
        f.Provider.CompleteRun("Example/widget", submitted.Run!, true, f.Request.Version, unattached: [Companions, "vericairn-1.1.0-evidence.zip"]);
        ReleaseObservation draft = f.Provider.ObserveRelease("Example/widget", "v1.1.0", Assert.Single(f.Provider.Dispatches).Settings)!;
        Assert.Equal([Companions, "vericairn-1.1.0-evidence.zip"], ReleaseProfile.Unattached(draft, "SHA256SUMS"));

        ReleaseResponse observed = f.Api.ReleaseObserve(f.Observe);
        Assert.False(observed.Ok);
        Assert.Equal("known-failure", observed.Status);
        Assert.Null(observed.Candidate);
        Assert.Contains($"does not attach the 2 files {Companions}, vericairn-1.1.0-evidence.zip that SHA256SUMS names", observed.Message, StringComparison.Ordinal);
        EstateModel model = f.Api.Session.Load();
        Assert.DoesNotContain(model.Current("release"), r => EstateModel.BodyString(r, "version") == "1.1.0");
        RecordEnvelope operation = Assert.Single(model.Current("operation"), o => EstateModel.BodyString(o, "identity") == submitted.Identity);
        Assert.Equal("known-failure", EstateModel.BodyString(operation, "status"));
        Assert.Contains(Companions, operation.Body.GetProperty("observed").GetProperty("summary").GetString(), StringComparison.Ordinal);

        // The submission check names each missing file until a reconciliation settles the attempt; nothing is dispatched.
        ReleaseResponse check = f.Api.ReleaseCheck(f.Request);
        Assert.False(check.Ok);
        ReleaseObjection identity = Assert.Single(check.Check!.Objections, o => o.Code == "release.identity");
        Assert.Contains("vericairn-1.1.0-evidence.zip", identity.Message, StringComparison.Ordinal);
        Assert.Contains(Companions, identity.Message, StringComparison.Ordinal);
        Assert.Single(f.Provider.Dispatches);
        ReleaseResponse reconciled = f.Api.ReleaseCheck(f.Request with { Reconciliation = "The draft lacked its companions archive; the fixed workflow replaces it." });
        Assert.DoesNotContain(reconciled.Check!.Objections, o => o.Code == "release.identity");
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void ADraftThatAttachesEveryListedFileBecomesTheCandidate()
    {
        using var f = new Fixture();
        ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
        Assert.True(submitted.Ok, submitted.Message);
        f.Provider.CompleteRun("Example/widget", submitted.Run!, true, f.Request.Version, ["widget-1.1.0-linux-x64.zip", Companions]);
        ReleaseObservation draft = f.Provider.ObserveRelease("Example/widget", "v1.1.0", Assert.Single(f.Provider.Dispatches).Settings)!;
        Assert.Equal(["widget-1.1.0-linux-x64.zip", Companions], draft.ChecksumNames);
        Assert.Empty(ReleaseProfile.Unattached(draft, "SHA256SUMS"));

        ReleaseResponse observed = f.Api.ReleaseObserve(f.Observe);
        Assert.True(observed.Ok, observed.Message);
        Assert.Equal("observed-success", observed.Status);
        RecordEnvelope candidate = f.Api.Session.Load().ById[observed.Candidate!.Id];
        Assert.Contains(candidate.Body.GetProperty("artifacts").EnumerateArray(), a => a.GetProperty("name").GetString() == Companions);
        Assert.Contains(candidate.Body.GetProperty("evidence").EnumerateArray(),
            e => e.GetProperty("summary").GetString()!.EndsWith("checksums for every asset, attaching every file SHA256SUMS names.", StringComparison.Ordinal));
        Assert.Equal(0, f.Api.Validate().Errors);
    }

    [Fact]
    public void AnUnattachedListedFileWaitsWhileTheRunIsStillInProgress()
    {
        using var f = new Fixture();
        ReleaseResponse submitted = f.Api.ReleaseSubmit(f.Request);
        Assert.True(submitted.Ok, submitted.Message);
        DispatchRequest dispatch = Assert.Single(f.Provider.Dispatches);
        f.Provider.CompleteRun("Example/widget", submitted.Run!, true, f.Request.Version, unattached: [Companions]);
        ReleaseObservation draft = f.Provider.ObserveRelease("Example/widget", "v1.1.0", dispatch.Settings)!;
        RunObservation done = f.Provider.ObserveRun("Example/widget", submitted.Run!)!;

        // Assets may still be uploading while the run runs, and with no run identified nothing says the upload ended: both wait.
        JsonObject intent = ReleaseFlow.AfterDispatch(PolicyFixture.Element(ReleaseFlow.IntentBody("github-releases", "Example/widget", dispatch.Settings, "1.1.0", f.Product.Id)),
            new DispatchReceipt(submitted.Run, null, "dispatched", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow)));
        Assert.Throws<InvalidOperationException>(() => ReleaseFlow.AfterObservation(PolicyFixture.Element(intent), done with { Status = "in_progress", Conclusion = null }, draft, "1.1.0", ReleaseProfile.None, "SHA256SUMS"));
        Assert.Throws<InvalidOperationException>(() => ReleaseFlow.AfterObservation(PolicyFixture.Element(intent), null, draft, "1.1.0", ReleaseProfile.None, "SHA256SUMS"));
        JsonObject failed = ReleaseFlow.AfterObservation(PolicyFixture.Element(intent), done, draft, "1.1.0", ReleaseProfile.None, "SHA256SUMS");
        Assert.Equal("known-failure", failed["status"]!.GetValue<string>());
        Assert.Equal($"Run {done.RunId} succeeded, but draft release v1.1.0 does not attach the file {Companions} that SHA256SUMS names. No candidate was recorded: a release attaches every file its checksum file names.",
            failed["observed"]!["summary"]!.GetValue<string>());

        // A provider that reports no checksum names (one written before 0.9.4) leaves membership unjudged.
        Assert.Empty(ReleaseProfile.Unattached(draft with { ChecksumNames = null }, "SHA256SUMS"));
        Assert.Equal("observed-success", ReleaseFlow.AfterObservation(PolicyFixture.Element(intent), done, draft with { ChecksumNames = null }, "1.1.0", ReleaseProfile.None, "SHA256SUMS")["status"]!.GetValue<string>());
    }

    [Fact]
    public void TheListingNamesEveryFileWithinTheOperationSummaryLimit()
    {
        Assert.Equal("the file a.zip that SHA256SUMS names", ReleaseFlow.Unattached(["a.zip"], "SHA256SUMS"));
        Assert.Equal("the 2 files a.zip, b.zip that CHECKSUMS names", ReleaseFlow.Unattached(["a.zip", "b.zip"], "CHECKSUMS"));
        string[] many = Enumerable.Range(0, 40).Select(i => new string('n', 200) + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string listing = ReleaseFlow.Unattached(many, "SHA256SUMS");
        Assert.StartsWith("the 40 files ", listing, StringComparison.Ordinal);
        Assert.Matches(@" and \d+ more that SHA256SUMS names$", listing);
        Assert.True(listing.Length < 3500, listing.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class Fixture : IDisposable
    {
        public PolicyFixture Records { get; } = new();
        public FakeReleaseProvider Provider { get; } = new();
        public EstateService Api { get; }
        public RecordEnvelope Product { get; }
        public ReleaseSubmitRequest Request => new(Product.Id, "1.1.0", "Example/widget", Records.Estate.OperatorId,
            Provider: Provider.Provider, Commit: Provider.HeadSha, Expected: Product.ComputedHash) { ClientBuild = EstateService.Version };
        public ReleaseObserveRequest Observe => new("1.1.0", "Example/widget", Records.Estate.OperatorId);

        public Fixture()
        {
            Product = Records.Add("product", ReleaseLineFixture.ProductBody());
            Api = EstateService.Open(Records.Estate.Root);
            WriteOutcome device = Api.DeviceCreate(new(Records.Estate.OperatorId, "Membership fixture writer", "file"));
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
