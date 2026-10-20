// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using System.Net;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

public sealed class PackageObservationTests
{
    [Theory]
    [InlineData("listed", "versions", "published", "listed", "not-deprecated")]
    [InlineData("unlisted", "versions", "published", "unlisted", "not-deprecated")]
    [InlineData("deprecated", "versions", "published", "listed", "deprecated")]
    [InlineData("not-found", "not-found", "not-found", "unknown", "unknown")]
    public void ObservationsPinTheReleaseAndRetainRegistryFactsAndEvidence(string registration, string versions, string publication, string listing, string deprecation)
    {
        using var f = new PackageTestEstate();
        f.Handler.Registration = registration; f.Handler.Versions = versions;
        PackagesResponse response = f.Service.PackagesObserve(f.Observe);
        Assert.True(response.Ok, response.Problem?.Message + response.Problem?.Findings);
        RecordEnvelope record = f.Service.Session.Load().ById[Assert.Single(response.Packages).Observation.Id];
        Assert.Equal(f.Release.Reference, record.Subject);
        Assert.Equal("package", EstateModel.BodyString(record, "kind"));
        Assert.Equal(publication, record.Body.GetProperty("facts").GetProperty("publication").GetString());
        Assert.Equal(listing, record.Body.GetProperty("facts").GetProperty("listing").GetString());
        Assert.Equal(deprecation, record.Body.GetProperty("facts").GetProperty("deprecation").GetString());
        Assert.Equal(3, record.Body.GetProperty("evidence").GetArrayLength());
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntentPrecedesTheAccountHolderAndOutcomeIsObservedUnderOneIdentity(bool deprecate)
    {
        using var f = new PackageTestEstate();
        PackagesResponse first = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.True(first.Ok, first.Message);
        Assert.Empty(f.Handler.Requests);
        Assert.Equal("not-started", first.Status);
        Assert.Contains("https://www.nuget.org/packages/example.widget/1.0.0/", first.Instruction, StringComparison.Ordinal);
        Assert.Contains(first.Intent!.Id, first.Instruction, StringComparison.Ordinal);
        f.Reopen();
        PackagesResponse rerun = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.Equal(first.Intent, rerun.Intent);
        Assert.Single(f.Service.Session.Load().OfType("operation"));
        f.Handler.BeforeRequest = _ => Assert.Equal("not-started", EstateModel.BodyString(f.Service.Session.Load().ById[first.Intent.Id], "status"));
        PackagesResponse pending = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: first.Intent.Id));
        Assert.False(pending.Ok);
        Assert.Equal("unconfirmed", pending.Status);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Service.Session.Load().ById[pending.Operation!.Id], "status"));
        // Replay the registry state after the account holder's act; no provider mutation exists.
        f.Handler.Registration = deprecate ? "deprecated" : "unlisted";
        PackagesResponse observed = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: first.Intent.Id));
        Assert.True(observed.Ok, observed.Problem?.Message);
        Assert.Equal(first.Intent, observed.Intent);
        EstateModel model = f.Service.Session.Load();
        RecordEnvelope outcome = model.ById[observed.Operation!.Id];
        Assert.Equal("observed-success", EstateModel.BodyString(outcome, "status"));
        Assert.False(outcome.Body.TryGetProperty("dispatchedAt", out _));
        Assert.Equal(first.Intent.Id, Assert.Single(observed.Packages).Observation is { } written ? model.ById[written.Id].Subject!.Id : null);
        Assert.Single(model.OfType("operation"), o => o.Supersedes is null);
        Assert.Equal(0, f.Service.Validate().Errors);
        PackagesResponse settled = deprecate ? f.Service.PackagesDeprecate(f.Deprecate) : f.Service.PackagesUnlist(f.Unlist);
        Assert.Equal(first.Intent, settled.Intent);
        Assert.Equal(observed.Operation, settled.Operation);
    }

    [Fact]
    public void MissingVersionCannotConfirmAnUnlistingAndFailedReadLeavesTheIntentUntouched()
    {
        using var f = new PackageTestEstate();
        PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
        Assert.True(intent.Ok, intent.Message);
        f.Handler.Registration = "timeout";
        int before = f.Service.Session.Load().Records.Count;
        PackagesResponse timeout = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        Assert.False(timeout.Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        f.Handler.Registration = "not-found"; f.Handler.Versions = "not-found";
        PackagesResponse absent = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent.Id));
        Assert.False(absent.Ok);
        Assert.Equal("unknown", Assert.Single(absent.Packages).State.Listing);
        Assert.Equal("uncertain", EstateModel.BodyString(f.Service.Session.Load().ById[absent.Operation!.Id], "status"));
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("package")]
    [InlineData("version")]
    [InlineData("provider")]
    [InlineData("reasons")]
    public void RefusedIntentsRecordNothingAndNeverContactTheRegistry(string refusal)
    {
        using var f = new PackageTestEstate();
        int before = f.Service.Session.Load().Records.Count;
        if (refusal == "provider") f.Service.Providers = new();
        PackagesResponse response = refusal switch
        {
            "actor" => f.Service.PackagesUnlist(f.Unlist with { By = f.Estate.BuilderId }),
            "package" => f.Service.PackagesUnlist(f.Unlist with { Package = "Example.Other" }),
            "version" => f.Service.PackagesUnlist(f.Unlist with { Version = "2.0.0" }),
            "reasons" => f.Service.PackagesDeprecate(f.Deprecate with { Reasons = ["made-up"] }),
            _ => f.Service.PackagesUnlist(f.Unlist),
        };
        Assert.False(response.Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        Assert.Empty(f.Handler.Requests);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("oversized")]
    [InlineData("timeout")]
    public void UnavailableObservationsRecordNothing(string failure)
    {
        using var f = new PackageTestEstate();
        f.Handler.Registration = failure;
        int before = f.Service.Session.Load().Records.Count;
        PackagesResponse response = f.Service.PackagesObserve(f.Observe);
        Assert.False(response.Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
    }

    [Fact]
    public void ChangedDeprecationMetadataCannotReplaceTheIntentOrClaimAMatchingOutcome()
    {
        using var f = new PackageTestEstate();
        PackagesResponse intent = f.Service.PackagesDeprecate(f.Deprecate with { Message = "A different intended message." });
        Assert.True(intent.Ok, intent.Message);
        PackagesResponse changed = f.Service.PackagesDeprecate(f.Deprecate);
        Assert.False(changed.Ok);
        Assert.Equal(intent.Intent, changed.Intent);
        f.Handler.Registration = "deprecated";
        PackagesResponse observed = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        Assert.False(observed.Ok);
        Assert.Equal("unconfirmed", observed.Status);
        Assert.Single(f.Service.Session.Load().OfType("operation"), o => o.Supersedes is null);
    }

    [Fact]
    public void RecordValidationRejectsClaimedSuccessWithoutAMatchingObservation()
    {
        using var f = new PackageTestEstate();
        PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
        PackagesResponse pending = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        RecordEnvelope operation = f.Service.Session.Load().ById[pending.Operation!.Id];
        JsonNode forged = JsonNode.Parse(operation.Body.GetRawText())!;
        forged["status"] = "observed-success";
        f.Estate.Add("operation", f.Estate.OperatorId, forged.ToJsonString(), operation.Subject, operation.Id);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == "operation.package-outcome");
    }

    [Fact]
    public void EveryDeclaredPackageIsObservedAndABadSecondResponseStoresNoPartialSet()
    {
        using var f = new PackageTestEstate();
        JsonNode product = JsonNode.Parse(f.Product.Body.GetRawText())!;
        product["packages"]!["ids"]!.AsArray().Add("Example.Other");
        RecordEnvelope revised = f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product.Id);
        bool failSecond = true;
        f.Handler.Override = url => !url.Contains("example.other", StringComparison.Ordinal) ? null
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Encoding.UTF8.GetString(RecordedNuGetHandler.Fixture(
                url.Contains("/v3-flatcontainer/", StringComparison.Ordinal) ? "versions" : failSecond ? "malformed" : "listed"))
                .Replace("example.widget", "example.other", StringComparison.Ordinal).Replace("Example.Widget", "Example.Other", StringComparison.Ordinal)) };
        int before = f.Service.Session.Load().Records.Count;
        PackagesResponse refused = f.Service.PackagesObserve(f.Observe with { Product = revised.Id });
        Assert.False(refused.Ok);
        Assert.Equal(before, f.Service.Session.Load().Records.Count);
        failSecond = false;
        PackagesResponse result = f.Service.PackagesObserve(f.Observe with { Product = revised.Id });
        Assert.True(result.Ok, result.Problem?.Message);
        Assert.Equal(["example.widget", "example.other"], result.Packages.Select(p => p.State.Package));
        Assert.Equal(2, f.Service.Session.Load().OfType("observation").Count());
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void AnOriginalIntentKeepsItsPinsAfterTheProductDeclarationChanges()
    {
        using var f = new PackageTestEstate();
        PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
        JsonNode product = JsonNode.Parse(f.Product.Body.GetRawText())!;
        product["packages"]!["ids"] = new JsonArray("Example.Other");
        f.Estate.Add("product", f.Estate.OperatorId, product.ToJsonString(), supersedes: f.Product.Id);
        f.Reopen();
        f.Handler.Registration = "unlisted";
        PackagesResponse observed = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        Assert.True(observed.Ok, observed.Problem?.Message);
        RecordEnvelope observation = f.Service.Session.Load().ById[Assert.Single(observed.Packages).Observation.Id];
        Assert.Equal(f.Product.Id, observation.Body.GetProperty("facts").GetProperty("product").GetString());
        Assert.Equal(0, f.Service.Validate().Errors);
    }

    [Fact]
    public void AFailedLaterObservationDoesNotEraseAHistoricalSuccessOrRepeatItsInstruction()
    {
        using var f = new PackageTestEstate();
        PackagesResponse intent = f.Service.PackagesUnlist(f.Unlist);
        f.Handler.Registration = "unlisted";
        PackagesResponse success = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: intent.Intent!.Id));
        Assert.True(success.Ok, success.Message);
        f.Handler.Registration = "listed";
        PackagesResponse changed = f.Service.PackagesObserve(new(f.Estate.OperatorId, Operation: success.Operation!.Id));
        Assert.False(changed.Ok);
        Assert.Equal("listed", Assert.Single(changed.Packages).State.Listing);
        Assert.Equal(success.Operation, changed.Operation);
        Assert.Contains("earlier observed success", changed.Message, StringComparison.Ordinal);
        PackagesResponse rerun = f.Service.PackagesUnlist(f.Unlist);
        Assert.StartsWith("vericairn packages observe", rerun.Instruction, StringComparison.Ordinal);
        Assert.Equal(intent.Intent, rerun.Intent);
    }

    [Fact]
    public async Task ConcurrentRerunsThroughOneWriterRetainOneIntent()
    {
        using var f = new PackageTestEstate();
        PackagesResponse[] responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => f.Service.PackagesUnlist(f.Unlist), TestContext.Current.CancellationToken)));
        Assert.All(responses, r => Assert.True(r.Ok, r.Message));
        Assert.Single(responses.Select(r => r.Intent!.Id).Distinct());
        Assert.Single(f.Service.Session.Load().OfType("operation"));
        Assert.Empty(f.Handler.Requests);
    }

    [Fact]
    public void ForgedAbsenceCannotBeRecordedAsAnUnlisting()
    {
        using var f = new PackageTestEstate();
        f.Handler.Registration = "not-found"; f.Handler.Versions = "not-found";
        PackagesResponse absent = f.Service.PackagesObserve(f.Observe);
        RecordEnvelope original = f.Service.Session.Load().ById[Assert.Single(absent.Packages).Observation.Id];
        JsonNode body = JsonNode.Parse(original.Body.GetRawText())!;
        body["facts"]!["listing"] = "unlisted";
        f.Estate.Add("observation", f.Estate.OperatorId, body.ToJsonString(), original.Subject);
        Assert.Contains(f.Estate.Load().Findings, finding => finding.Code == "observation.package-state");
    }
}
