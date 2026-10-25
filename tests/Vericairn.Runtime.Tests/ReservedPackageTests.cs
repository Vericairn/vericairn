// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

public sealed class ReservedPackageTests
{
    [Theory]
    [InlineData("listed", "versions", "published", "listed", "not-deprecated")]
    [InlineData("unlisted", "versions", "published", "unlisted", "not-deprecated")]
    [InlineData("deprecated", "versions", "published", "listed", "deprecated")]
    [InlineData("not-found", "not-found", "not-found", "unknown", "unknown")]
    public void ReservedPublicationPinsTheProductAndRetainsTheSameRegistryEvidence(string registration, string versions, string publication, string listing, string deprecation)
    {
        using var f = new PackageTestEstate(reserved: true);
        f.Handler.Registration = registration; f.Handler.Versions = versions;
        PackagesResponse response = f.Service.PackagesObserve(f.Observe);
        Assert.True(response.Ok, response.Message);
        PackageObservationResult result = Assert.Single(response.Packages);
        RecordEnvelope record = f.Service.Session.Load().ById[result.Observation.Id];
        Assert.Equal(f.Product.Reference, record.Subject);
        AssertReservedPins(f, record.Body.GetProperty("facts"));
        Assert.Equal(publication, result.State.Publication);
        Assert.Equal(listing, result.State.Listing);
        Assert.Equal(deprecation, result.State.Deprecation);
        Assert.Equal(3, result.State.Evidence.Count);
        Assert.Equal(result.State.Evidence, record.Body.GetProperty("evidence").Deserialize<RegistryEvidence[]>(ProtocolJson.Options));
        AssertNoRelease(f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedIntentsPrecedeEffectsAndConfirmOnlyTheRecordedOutcome(bool deprecate)
    {
        using var f = new PackageTestEstate(reserved: true);
        PackagesResponse first = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.True(first.Ok, first.Message);
        Assert.Equal("not-started", first.Status);
        Assert.Empty(f.Handler.Requests);
        Assert.Contains($"https://www.nuget.org/packages/example.widget/{f.Version}/", first.Instruction, StringComparison.Ordinal);
        RecordEnvelope intent = f.Service.Session.Load().ById[first.Intent!.Id];
        Assert.Equal(f.Product.Reference, intent.Subject);
        AssertReservedPins(f, intent.Body.GetProperty("target"));
        f.Reopen();
        PackagesResponse rerun = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.Equal(first.Intent, rerun.Intent);
        Assert.Single(f.Service.Session.Load().OfType("operation"));
        f.Handler.BeforeRequest = _ => Assert.Equal("not-started", EstateModel.BodyString(f.Service.Session.Load().ById[intent.Id], "status"));
        PackagesResponse pending = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Id));
        Assert.False(pending.Ok);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Service.Session.Load().ById[pending.Operation!.Id], "status"));
        f.Handler.Registration = deprecate ? "deprecated" : "unlisted";
        PackagesResponse confirmed = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: pending.Operation.Id));
        Assert.True(confirmed.Ok, confirmed.Message);
        EstateModel model = f.Service.Session.Load();
        RecordEnvelope outcome = model.ById[confirmed.Operation!.Id];
        RecordEnvelope observation = model.ById[Assert.Single(confirmed.Packages).Observation.Id];
        Assert.Equal("observed-success", EstateModel.BodyString(outcome, "status"));
        Assert.Equal(intent.Subject, outcome.Subject);
        Assert.Equal(intent.Reference, observation.Subject);
        AssertReservedPins(f, observation.Body.GetProperty("facts"));
        Assert.Equal(intent.Body.GetProperty("target").GetRawText(), outcome.Body.GetProperty("target").GetRawText());
        Assert.Contains(outcome.Body.GetProperty("evidence").EnumerateArray(), e =>
            e.GetProperty("hash").GetString() == observation.ComputedHash && e.GetProperty("location").GetString() == observation.ExpectedPath);
        Assert.False(outcome.Body.TryGetProperty("dispatchedAt", out _));
        Assert.Single(model.OfType("operation"), o => o.Supersedes is null);
        PackagesResponse settled = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.Equal(first.Intent, settled.Intent);
        Assert.Equal(confirmed.Operation, settled.Operation);
        AssertNoRelease(f);
    }

    [Theory]
    [InlineData("observe", "implicit")]
    [InlineData("unlist", "implicit")]
    [InlineData("deprecate", "implicit")]
    [InlineData("observe", "undeclared")]
    [InlineData("unlist", "undeclared")]
    [InlineData("deprecate", "undeclared")]
    [InlineData("observe", "unrecorded")]
    [InlineData("unlist", "unrecorded")]
    [InlineData("deprecate", "unrecorded")]
    [InlineData("observe", "release")]
    [InlineData("unlist", "release")]
    [InlineData("deprecate", "release")]
    [InlineData("observe", "removed")]
    [InlineData("unlist", "removed")]
    [InlineData("deprecate", "removed")]
    [InlineData("observe", "stale")]
    [InlineData("unlist", "stale")]
    [InlineData("deprecate", "stale")]
    public void SelectionRefusalsWriteNothingAndNeverReadTheRegistry(string action, string selection)
    {
        using var f = new PackageTestEstate(reserved: selection != "release");
        string version = selection is "undeclared" or "unrecorded" ? "2.0.0" : f.Version;
        bool reserved = selection is not ("implicit" or "unrecorded");
        string product = f.Product.Id;
        if (selection is "removed" or "stale")
        {
            JsonNode body = JsonNode.Parse(f.Product.Body.GetRawText())!;
            body["packages"]!.AsObject().Remove("reserved");
            RecordEnvelope revised = f.Estate.Add("product", f.Estate.OperatorId, body.ToJsonString(), supersedes: f.Product.Id);
            if (selection == "removed") product = revised.Id;
        }
        int before = f.Service.Session.Load().Records.Count;
        PackagesResponse response = action switch
        {
            "observe" => f.Service.PackagesObserve(f.Observe with { Product = product, Version = version, Reserved = reserved }),
            "unlist" => f.Service.PackagesUnlist(f.Unlist with { Product = product, Version = version, Reserved = reserved }),
            _ => f.Service.PackagesDeprecate(f.Deprecate with { Product = product, Version = version, Reserved = reserved }),
        };
        Assert.False(response.Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        Assert.Empty(f.Handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedIntentsKeepTheirOriginalDeclarationAfterItsReservationIsRemoved(bool deprecate)
    {
        using var f = new PackageTestEstate(reserved: true);
        PackagesResponse intent = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.True(intent.Ok, intent.Message);
        JsonNode product = JsonNode.Parse(f.Product.Body.GetRawText())!;
        product["packages"]!.AsObject().Remove("reserved");
        product["packages"]!["ids"] = new JsonArray("Example.Other");
        f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product.Id);
        f.Reopen();
        f.Handler.Registration = deprecate ? "deprecated" : "unlisted";
        PackagesResponse result = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        Assert.True(result.Ok, result.Message);
        AssertReservedPins(f, f.Service.Session.Load().ById[Assert.Single(result.Packages).Observation.Id].Body.GetProperty("facts"));
        AssertNoRelease(f);
    }

    [Fact]
    public void AProductRevisionLeavesReservedPackageRecordsCurrent()
    {
        // The product declaration is revised far more often than a release, so its reserved versions' records must not turn stale.
        using var f = new PackageTestEstate(reserved: true);
        PackagesResponse observed = f.Service.PackagesObserve(f.Observe);
        Assert.True(observed.Ok, observed.Message);
        PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
        Assert.True(intent.Ok, intent.Message);
        JsonNode product = JsonNode.Parse(f.Product.Body.GetRawText())!;
        product["summary"] = "A newer declaration.";
        f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product.Id);
        f.Reopen();
        var model = f.Service.Session.Load();
        string[] paths = [.. model.OfType("observation").Concat(model.OfType("operation")).Select(record => record.ExpectedPath)];
        Assert.Equal(2, paths.Length);
        Assert.DoesNotContain(model.Findings, finding => finding.Code == "record.subject-stale" && paths.Contains(finding.Location));
    }

    [Fact]
    public void ADeclarationChangedDuringAReservedReadRequiresANewSelection()
    {
        using var f = new PackageTestEstate(reserved: true);
        bool revised = false;
        f.Handler.BeforeRequest = _ =>
        {
            if (revised) return;
            revised = true;
            JsonNode product = JsonNode.Parse(f.Product.Body.GetRawText())!;
            product["summary"] = "A newer declaration.";
            f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product.Id);
        };
        PackagesResponse response = f.Service.PackagesObserve(f.Observe);
        Assert.False(response.Ok);
        Assert.Contains("changed during observation", response.Message, StringComparison.Ordinal);
        Assert.Empty(f.Service.Session.Load().OfType("observation"));
        AssertNoRelease(f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingVersionsAndFailedReadsCannotConfirmReservedActions(bool deprecate)
    {
        using var f = new PackageTestEstate(reserved: true);
        PackagesResponse intent = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        var request = new PackagesObserveRequest(f.Estate.OperatorId, Operation: intent.Intent!.Id);
        f.Handler.Registration = "malformed";
        int before = f.Service.Session.Load().Records.Count;
        Assert.False(f.Service.PackagesObserve(request).Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        f.Handler.Registration = "not-found"; f.Handler.Versions = "not-found";
        PackagesResponse absent = f.Service.PackagesObserve(request);
        Assert.False(absent.Ok);
        Assert.Equal("unknown", Assert.Single(absent.Packages).State.Listing);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Service.Session.Load().ById[absent.Operation!.Id], "status"));
        RecordEnvelope operation = f.Service.Session.Load().ById[absent.Operation.Id];
        JsonNode forged = JsonNode.Parse(operation.Body.GetRawText())!;
        forged["status"] = "observed-success";
        f.Estate.Add("operation", f.Estate.OperatorId, forged.ToJsonString(), operation.Subject, operation.Id);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == "operation.package-outcome");
        Assert.Empty(f.Estate.Load().OfType("release"));
    }

    [Fact]
    public void ReservedActionsKeepOperatorAuthorityAndOperationSelectionIsExclusive()
    {
        using var f = new PackageTestEstate(reserved: true);
        int before = f.Service.Session.Load().Records.Count;
        Assert.False(f.Service.PackagesUnlist(f.Unlist with { By = f.Estate.BuilderId }).Ok);
        Assert.False(f.Service.PackagesDeprecate(f.Deprecate with { By = f.Estate.BuilderId }).Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
        var request = new PackagesObserveRequest(f.Estate.OperatorId, Operation: intent.Intent!.Id);
        before = f.Service.Session.Load().Records.Count;
        Assert.False(f.Service.PackagesObserve(request with { By = f.Estate.BuilderId }).Ok);
        Assert.False(f.Service.PackagesObserve(request with { Reserved = true }).Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        Assert.Empty(f.Handler.Requests);
        AssertNoRelease(f);
    }

    [Fact]
    public void AReservedDeprecationCanNameTheFirstRealVersionAsAnOrdinaryAlternate()
    {
        using var f = new PackageTestEstate(reserved: true);
        PackagesDeprecateRequest request = f.Deprecate with { Message = "Use the first release, 1.0.0.", AlternatePackage = "Example.Widget", AlternateRange = "[1.0.0,)" };
        PackagesResponse intent = f.Service.PackagesDeprecate(request);
        Assert.True(intent.Ok, intent.Message);
        f.Handler.Registration = "deprecated";
        Assert.False(f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id)).Ok);
        Assert.False(f.Service.PackagesDeprecate(f.Deprecate).Ok);
        f.Handler.Override = url => !url.Contains("/registration", StringComparison.Ordinal) ? null : new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Encoding.UTF8.GetString(RecordedNuGetHandler.Fixture("deprecated"))
                .Replace("1.0.0", f.Version, StringComparison.Ordinal)
                .Replace("Use the replacement package.", request.Message, StringComparison.Ordinal)
                .Replace("Example.Replacement", request.AlternatePackage, StringComparison.Ordinal)
                .Replace("[2.0.0,)", request.AlternateRange, StringComparison.Ordinal)),
        };
        PackagesResponse result = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent.Id));
        Assert.True(result.Ok, result.Message);
        Assert.Equal("[1.0.0,)", Assert.Single(result.Packages).State.Details!.AlternateRange);
        AssertNoRelease(f);
    }

    private static void AssertReservedPins(PackageTestEstate fixture, JsonElement pins)
    {
        Assert.Equal(fixture.Product.Id, pins.GetProperty("product").GetString());
        Assert.Equal(fixture.Product.ComputedHash, pins.GetProperty("productHash").GetString());
        Assert.Equal(fixture.Version, pins.GetProperty("version").GetString());
        Assert.Equal(fixture.Version, pins.GetProperty("reserved").GetString());
        Assert.False(pins.TryGetProperty("release", out _));
        Assert.False(pins.TryGetProperty("releaseHash", out _));
    }

    private static void AssertNoRelease(PackageTestEstate fixture)
    {
        Assert.Empty(fixture.Service.Session.Load().OfType("release"));
        Assert.Equal(0, fixture.Service.Validate().Errors);
        Assert.Equal(3, fixture.Service.Session.Manifest.RecordFormat);
    }
}
