// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.ResultOutputSchemeTests;

namespace Vericairn.Runtime.Tests;

public sealed class ResultOutputSchemeWriteTests
{
    [Theory]
    [InlineData(1, "record")]
    [InlineData(3, "record")]
    [InlineData(3, "result")]
    public void ANewResultWithASchemeLocationIsRefusedAndWritesNothing(long format, string surface)
    {
        using var estate = new TestEstate(format);
        RecordEnvelope attempt = StartAttempt(estate);
        using EstateService service = EstateService.Open(estate.Root);
        int before = service.Session.Load().Records.Count;

        WriteOutcome refused = Write(service, surface, attempt, Json(Body(GitHubLocation)));

        Assert.False(refused.Ok);
        Assert.Null(refused.Written);
        FindingDto finding = Assert.Single(refused.Problem!.Findings, f => f.Code == Code);
        Assert.Equal("error", finding.Severity);
        Assert.EndsWith("#body.outputs", finding.Location, StringComparison.Ordinal);
        Assert.Contains("outputs[0] (https:)", finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, service.Session.Load().Records.Count);
        Assert.Empty(service.Session.Load().OfType("result"));

        WriteOutcome accepted = Write(service, surface, attempt, Json(Body("widget.txt")));
        Assert.True(accepted.Ok, accepted.Problem?.Message);
        Assert.Equal(0, service.Validate().Errors);
    }

    [Fact]
    public void AnExistingSchemeLocationWarnsAndACorrectionMustDropIt()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        RecordEnvelope earlier = estate.Add("result", estate.BuilderId, Body(GitHubLocation), attempt.Reference);
        using EstateService service = EstateService.Open(estate.Root);

        foreach (FindingsResponse check in new[] { service.Validate(), service.Check() })
        {
            Assert.Equal(0, check.Errors);
            FindingDto warning = Assert.Single(check.Findings, f => f.Code == Code);
            Assert.Equal("warning", warning.Severity);
            Assert.StartsWith(earlier.ExpectedPath + "#", warning.Location, StringComparison.Ordinal);
        }

        // An unrelated record is written as before.
        WriteOutcome unrelated = service.NewRecord(new("work-order", estate.BuilderId, Json(OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal))));
        Assert.True(unrelated.Ok, unrelated.Problem?.Message);

        // A correction is a new record: it may not carry the scheme forward.
        WriteOutcome kept = service.NewRecord(new("result", estate.BuilderId, Json(Body(GitHubLocation)), $"attempt:{attempt.Id}:{attempt.ComputedHash}", earlier.Id));
        Assert.False(kept.Ok);
        Assert.Equal("error", Assert.Single(kept.Problem!.Findings, f => f.Code == Code).Severity);

        WriteOutcome corrected = service.NewRecord(new("result", estate.BuilderId, Json(Body("widget.txt")), $"attempt:{attempt.Id}:{attempt.ComputedHash}", earlier.Id));
        Assert.True(corrected.Ok, corrected.Problem?.Message);
        Assert.Equal(earlier.ComputedHash, service.Session.Load().ById[earlier.Id].ComputedHash);
        FindingsResponse after = service.Validate();
        Assert.Equal(0, after.Errors);
        Assert.Equal("warning", Assert.Single(after.Findings, f => f.Code == Code).Severity);
    }

    [Fact]
    public void ADevicePreparedResultWithASchemeLocationIsRefusedBeforeSigning()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope attempt = StartAttempt(estate);
        using EstateService service = EstateService.Open(estate.Root);
        string operatorDevice = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(operatorDevice, estate.OperatorId, "Admit the operator's own device.")).Ok);
        WriteOutcome created = service.DeviceCreate(new(estate.BuilderId, "Builder device", "file"));
        Assert.True(created.Ok, created.Problem?.Message);
        string device = created.Written!.Id;
        WriteOutcome admitted = service.DeviceAdmit(new(device, estate.OperatorId, "Admit the builder's device."));
        Assert.True(admitted.Ok, admitted.Problem?.Message);
        string publicKey = EstateModel.BodyString(service.Session.Load().ById[device], "publicKey")!;
        using EstateService.RequestDeviceScope scope = service.ForDeviceRequest(device, publicKey);

        RecordPrepareResponse refused = service.PrepareRecord(new("result", estate.BuilderId, Json(Body(GitHubLocation)), $"attempt:{attempt.Id}:{attempt.ComputedHash}"));
        Assert.False(refused.Ok);
        Assert.Null(refused.Prepared);
        Assert.Equal("error", Assert.Single(refused.Problem!.Findings, f => f.Code == Code).Severity);

        RecordPrepareResponse prepared = service.PrepareRecord(new("result", estate.BuilderId, Json(Body("widget.txt")), $"attempt:{attempt.Id}:{attempt.ComputedHash}"));
        Assert.True(prepared.Ok, prepared.Problem?.Message);
        Assert.Empty(service.Session.Load().OfType("result"));
    }

    private static WriteOutcome Write(EstateService service, string surface, RecordEnvelope attempt, JsonElement body) => surface == "result"
        ? service.Result(new(attempt.Id, attempt.By, body))
        : service.NewRecord(new("result", attempt.By, body, $"attempt:{attempt.Id}:{attempt.ComputedHash}"));

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
