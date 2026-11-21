// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using RequestData = Vericairn.Records.Tests.OperatorRequestTests;

namespace Vericairn.Runtime.Tests;

/// <summary>
/// OPS-22 (0.9.4): recording a request refuses a pinned file the estate's objects do not hold, so its content is retrievable from any copy
/// of the estate; an optional request is a possible todo that raises no required attention and closes whenever the operator chooses.
/// </summary>
[Collection("home")]
public sealed class OptionalRequestServiceTests
{
    [Fact]
    public void RecordingARequestRefusesAPinnedFileTheEstatesObjectsDoNotHold()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope work = RequestData.Work(f.Estate);
        byte[] kit = Encoding.UTF8.GetBytes("The post's exact text.\n");
        JsonObject body = RequestData.Body(f.Estate);
        body["optional"] = true;
        body["content"]!.AsArray().Add(new JsonObject { ["kind"] = "file", ["name"] = "post.md", ["hash"] = ContentHash.Sha256(kit) });
        int before = f.Estate.Load().Records.Count;

        WriteOutcome refused = f.Api.OperatorRequestWrite(new(f.Estate.BuilderId, JsonSerializer.SerializeToElement(body), OperatorRequestTests.Subject(work)));
        WriteOutcome around = f.Api.NewRecord(new("operator-request", f.Estate.BuilderId, JsonSerializer.SerializeToElement(body), OperatorRequestTests.Subject(work)));

        Assert.False(refused.Ok);
        Assert.Contains("post.md (" + ContentHash.Sha256(kit) + ")", refused.Problem!.Message, StringComparison.Ordinal);
        Assert.Contains("vericairn objects put", refused.Problem.Message, StringComparison.Ordinal);
        Assert.False(around.Ok);
        Assert.Equal(before, f.Estate.Load().Records.Count);
        Assert.NotNull(f.Api.ObjectPut(kit).Stored);
        WriteOutcome written = f.Api.OperatorRequestWrite(new(f.Estate.BuilderId, JsonSerializer.SerializeToElement(body), OperatorRequestTests.Subject(work)));
        Assert.True(written.Ok, written.Problem?.Message);
    }

    [Fact]
    public void AnOptionalRequestRaisesNoRequiredAttentionAndClosesWheneverTheOperatorChooses()
    {
        using var f = new OperatorCloseFixture();
        RecordEnvelope work = RequestData.Work(f.Estate);
        JsonObject body = RequestData.Body(f.Estate);
        body["optional"] = true;
        WriteOutcome written = f.Api.OperatorRequestWrite(new(f.Estate.BuilderId, JsonSerializer.SerializeToElement(body), OperatorRequestTests.Subject(work)));
        Assert.True(written.Ok, written.Problem?.Message);

        Assert.DoesNotContain(f.Api.AttentionGet(f.Estate.OperatorId).Items, i => i.SubjectId == written.Written!.Id);
        Assert.Equal(0, f.Api.OperatorRequests().Json.GetProperty("waiting").GetInt32());
        Assert.Equal(written.Written!.Id, f.Api.OperatorRequests().Json.GetProperty("optional")[0].GetProperty("revision").GetString());
        Assert.Contains("Possible todos: 1 optional request", f.Api.Status().Text, StringComparison.Ordinal);
        // An optional request is not among the items an operator act signs; it closes on its own.
        Assert.DoesNotContain(f.Api.OperatorActItems(f.Estate.OperatorId).Items, i => i.Subject.Id == written.Written!.Id);
        WriteOutcome done = f.Api.OperatorRequestDone(written.Written.Id, new(f.Estate.OperatorId, written.Written.Hash));
        Assert.True(done.Ok, done.Problem?.Message);
        Assert.False(f.Api.OperatorRequests().Json.TryGetProperty("optional", out _));
        Assert.Equal(0, f.Api.Validate().Errors);
    }
}
