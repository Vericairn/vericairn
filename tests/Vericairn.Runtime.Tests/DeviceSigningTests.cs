// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.ResultOutputSchemeTests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// MCP-03's signed mode in the service: under a request's device signing no key on the runtime's machine signs, even one it
/// holds for the requesting principal, and no record is written unsigned. A write prepares its one record for the device and
/// writes nothing, and then writes only that record, re-authored from the same request, with the device's signature.
/// </summary>
public sealed class DeviceSigningTests
{
    private const string Brief = """{"outcome":"A validated widget.","sources":[],"permittedChanges":["the widget sources"],"prohibitedEffects":["no publication"],"workspace":["example repository, main branch"],"limits":[],"expectedOutputs":["widget.txt"],"reviewCriteria":["output hash verified"],"recovery":"Resume from the last recorded result."}""";

    [Fact]
    public void NothingHereSignsAndOnlyTheDevicesOwnSignatureOverItsPlanWrites()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, OrderBody);
        RecordEnvelope assignment = estate.Add("assignment", estate.OperatorId, $$"""{"contributor":"{{estate.BuilderId}}","brief":{{Brief}}}""", order.Reference);
        using EstateService service = EstateService.Open(estate.Root);
        string operatorDevice = service.DeviceCreate(new(estate.OperatorId, "Operator device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(operatorDevice, estate.OperatorId, "Admit the operator's own device.")).Ok);
        string device = service.DeviceCreate(new(estate.BuilderId, "Builder device", "file")).Written!.Id;
        Assert.True(service.DeviceAdmit(new(device, estate.OperatorId, "Admit the builder's device.")).Ok);
        string key = EstateModel.BodyString(service.Session.Load().ById[device], "publicKey")!;
        IDeviceKeyStore store = DeviceKeyStores.All().First(s => s.Has(device));
        int before = service.Session.Load().Records.Count;
        var attempt = new AttemptRequest(assignment.Id, estate.BuilderId, "widget repository", "Build it.");

        // A read writes no record, though this machine holds the builder's key and would sign for it on any other request.
        using (service.ForDeviceRequest(device, key, null, DeviceSigning.Read()))
        {
            WriteOutcome refused = service.Attempt(attempt);
            Assert.False(refused.Ok);
            Assert.Contains("prepared for the requesting device", refused.Problem!.Message, StringComparison.Ordinal);
            using JsonDocument other = JsonDocument.Parse(OrderBody.Replace("W-1", "W-2", StringComparison.Ordinal));
            Assert.False(service.NewRecord(new("work-order", estate.BuilderId, other.RootElement.Clone())).Ok);
        }

        // Preparing writes nothing and authors one record; a second write in the same request is refused.
        DeviceSigning preparing = DeviceSigning.Prepare();
        using (service.ForDeviceRequest(device, key, null, preparing))
        {
            WriteOutcome prepared = service.Attempt(attempt);
            Assert.Equal("prepared", prepared.Problem?.Code);
            Assert.NotNull(preparing.Prepared);
            using JsonDocument amendment = JsonDocument.Parse("""{"reason":"More time."}""");
            Assert.Contains("one record", service.Amend(new AmendRequest(assignment.Id, estate.BuilderId, amendment.RootElement.Clone())).Problem!.Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, service.Session.Load().Records.Count);
        RecordPrepared plan = preparing.Prepared!;
        Assert.Equal(estate.BuilderId, plan.Record.GetProperty("by").GetString());
        Assert.False(plan.Record.TryGetProperty("signatures", out JsonElement unsigned) && unsigned.GetArrayLength() > 0);

        // Another principal's device cannot sign the builder's plan; the device's own signature writes exactly that record.
        string signature = Base64Url.EncodeToString(store.Sign(device, RecordSigning.Digest(plan.Hash)));
        using (service.ForDeviceRequest(operatorDevice, EstateModel.BodyString(service.Session.Load().ById[operatorDevice], "publicKey")!, null, DeviceSigning.Sign(plan.Plan, signature)))
        {
            Assert.Equal("denied", service.Attempt(attempt).Problem?.Code);
        }

        using (service.ForDeviceRequest(device, key, null, DeviceSigning.Sign(plan.Plan, signature)))
        {
            WriteOutcome written = service.Attempt(attempt);
            Assert.True(written.Ok, written.Problem?.Message);
            Assert.Equal((plan.Id, plan.Hash), (written.Written!.Id, written.Written.Hash));
        }

        RecordEnvelope record = service.Session.Load().ById[plan.Id];
        Assert.Equal(device, Assert.Single(record.Signatures).Device);
        Assert.Equal(plan.At, record.Body.GetProperty("startedAt").GetString());
        Assert.Equal(before + 1, service.Session.Load().Records.Count);
        Assert.Equal(0, service.Validate().Errors);
    }
}
