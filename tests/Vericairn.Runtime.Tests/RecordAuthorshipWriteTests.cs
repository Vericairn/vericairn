// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.RecordAuthorshipTests;

namespace Vericairn.Runtime.Tests;

public sealed class RecordAuthorshipWriteTests
{
    [Theory]
    [InlineData(1, "nobody")]
    [InlineData(3, "nobody")]
    [InlineData(3, "charter")]
    [InlineData(3, "work-order")]
    public void ANewRecordWhoseAuthorIsNoPrincipalIsRefusedAndWritesNothing(long format, string author)
    {
        using var estate = new TestEstate(format);
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Exercise authors.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        string by = author switch { "charter" => estate.Estate.Id, "work-order" => order.Id, _ => Ulid.NewUlid().Value };
        using EstateService service = EstateService.Open(estate.Root);
        int before = service.Session.Load().Records.Count;

        WriteOutcome refused = service.NewRecord(new("resource", by, Json(ResourceBody)));

        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        FindingDto finding = AssertAuthorError(refused.Problem!, by);
        Assert.Contains(author == "nobody" ? "names no record" : "type " + (author == "charter" ? "estate" : "work-order"), finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Empty(service.Session.Load().OfType("resource"));

        WriteOutcome accepted = service.NewRecord(new("resource", estate.BuilderId, Json(ResourceBody)));
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void ANewRecordDatedBeforeItsPrincipalIsWrittenBecauseTheAuthorIsKnownWhenItIsWritten()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string early = RecordParser.FormatTimestamp(estate.Builder.AtTime.AddSeconds(-1));

        WriteOutcome written = service.NewRecord(new("resource", estate.BuilderId, Json(ResourceBody), At: early));

        Assert.True(written.Ok, written.Problem?.Message);
        Assert.Equal(early, service.Session.Load().ById[written.Written!.Id].At);
        Assert.DoesNotContain(service.Validate().Findings, f => f.Code == Code);
    }

    [Fact]
    public void ATypedOperationRefusesAnUnknownAuthorWithTheSameFinding()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = estate.Add("work-order", estate.BuilderId, """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Exercise authors.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$$"""{"contributor":"{{{estate.BuilderId}}}","brief":{"outcome":"A widget.","sources":[],"permittedChanges":["x"],"prohibitedEffects":["y"],"workspace":["z"],"limits":[],"expectedOutputs":["w"],"reviewCriteria":["v"],"recovery":"Resume."}}""", order.Reference);
        using EstateService service = EstateService.Open(estate.Root);
        string nobody = Ulid.NewUlid().Value;

        WriteOutcome refused = service.Amend(new(assignment.Id, nobody, Json("""{"kind":"scope","rationale":"Scope changed.","proposedChanges":["Add a second widget."]}""")));

        Assert.False(refused.Ok);
        AssertAuthorError(refused.Problem!, nobody);
        Assert.Empty(service.Session.Load().OfType("amendment"));
    }

    [Fact]
    public void AnExistingUnresolvedAuthorIsInformationAndDoesNotBlockLaterWrites()
    {
        using var estate = new TestEstate(3);
        string nobody = Ulid.NewUlid().Value;
        RecordEnvelope earlier = estate.Add("resource", nobody, ResourceBody);
        using EstateService service = EstateService.Open(estate.Root);

        foreach (FindingsResponse check in new[] { service.Validate(), service.Check() })
        {
            Assert.Equal(0, check.Errors);
            FindingDto info = Assert.Single(check.Findings, f => f.Code == Code);
            Assert.Equal("info", info.Severity);
            Assert.Equal(earlier.ExpectedPath + "#by", info.Location);
        }

        // A revision by the same unknown author is a new record and is refused; one by a known principal is written.
        WriteOutcome same = service.NewRecord(new("resource", nobody, Json(ResourceBody.Replace("Site", "Website", StringComparison.Ordinal)), Supersedes: earlier.Id));
        Assert.False(same.Ok);
        AssertAuthorError(same.Problem!, nobody);
        WriteOutcome revised = service.NewRecord(new("resource", estate.OperatorId, Json(ResourceBody.Replace("Site", "Website", StringComparison.Ordinal)), Supersedes: earlier.Id));
        Assert.True(revised.Ok, revised.Problem?.Message);
        Assert.Equal(earlier.ComputedHash, service.Session.Load().ById[earlier.Id].ComputedHash);
        FindingsResponse after = service.Validate();
        Assert.Equal(0, after.Errors);
        Assert.Equal("info", Assert.Single(after.Findings, f => f.Code == Code).Severity);
    }

    [Fact]
    public void SignedWritesNeedAKnownAuthorToo()
    {
        using var estate = new TestEstate(3);
        using EstateService service = EstateService.Open(estate.Root);
        string operatorDevice = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(operatorDevice, estate.OperatorId, "Admit the operator's own device.")).Ok);
        string builderDevice = service.DeviceCreate(new(estate.BuilderId, "Builder device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(builderDevice, estate.OperatorId, "Admit the builder's device.")).Ok);

        WriteOutcome signed = service.NewRecord(new("resource", estate.OperatorId, Json(ResourceBody)));
        Assert.True(signed.Ok, signed.Problem?.Message);
        Assert.Single(service.Session.Load().ById[signed.Written!.Id].Signatures);

        // A device-signed request prepares only its own principal's records, and that principal is known.
        string publicKey = EstateModel.BodyString(service.Session.Load().ById[builderDevice], "publicKey")!;
        using (EstateService.RequestDeviceScope scope = service.ForDeviceRequest(builderDevice, publicKey))
        {
            RecordPrepareResponse foreign = service.PrepareRecord(new("resource", Ulid.NewUlid().Value, Json(ResourceBody)));
            Assert.False(foreign.Ok);
            Assert.Null(foreign.Prepared);
            RecordPrepareResponse own = service.PrepareRecord(new("resource", estate.BuilderId, Json(ResourceBody)));
            Assert.True(own.Ok, own.Problem?.Message);
            string signature = new StoreSigner(new FileKeyStore(VericairnHome.Path()), builderDevice).Sign(RecordSigning.Digest(own.Prepared!.Hash)).Signature;
            WriteOutcome written = service.NewSignedRecord(new(own.Prepared.Plan, signature));
            Assert.True(written.Ok, written.Problem?.Message);
        }

        // An unknown author has no admitted device here, so in optional mode its write would be unsigned; it is refused all the same.
        WriteOutcome unknown = service.NewRecord(new("resource", Ulid.NewUlid().Value, Json(ResourceBody)));
        Assert.False(unknown.Ok);
        Assert.Equal(2, service.Session.Load().OfType("resource").Count());
        Assert.Equal(0, service.Validate().Errors);
    }

    private static FindingDto AssertAuthorError(Problem problem, string by)
    {
        FindingDto finding = Assert.Single(problem.Findings, f => f.Code == Code);
        Assert.Equal("error", finding.Severity);
        Assert.EndsWith("#by", finding.Location, StringComparison.Ordinal);
        Assert.Contains(by, finding.Message, StringComparison.Ordinal);
        return finding;
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
