// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Tests;

public sealed class DecisionNewTests
{
    [Fact]
    public void RecordsExactWordsAndAnswerTimeAndSignedNextPinsTogether()
    {
        using var fixture = new DecisionFixture();
        var service = fixture.Service;
        var operatorDevice = service.DeviceCreate(new(fixture.Estate.OperatorId, "Operator", "file"));
        Assert.True(operatorDevice.Ok, operatorDevice.Problem?.Message);
        WriteOutcome admission = service.DeviceAdmit(new(operatorDevice.Written!.Id, fixture.Estate.OperatorId, "First authority."));
        Assert.True(admission.Ok, admission.Problem?.Message);
        var device = service.DeviceCreate(new(fixture.Estate.BuilderId, "Recorder", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        Assert.True(service.DeviceAdmit(new(device.Written!.Id, fixture.Estate.OperatorId, "Admit recorder.")).Ok);
        DecisionNewRequest request = fixture.Request with { By = fixture.Estate.BuilderId };
        DecisionNewResponse result = service.DecisionFromAsk(request);
        Assert.True(result.Ok, Details(result));
        RecordEnvelope decision = service.Session.Load().ById[result.Written!.Id];
        Assert.Equal(request.Statement, EstateModel.BodyString(decision, "statement"));
        Assert.Equal(request.AnsweredAt, EstateModel.BodyString(decision, "decidedAt"));
        Assert.Equal(fixture.Estate.OperatorId, EstateModel.BodyString(decision, "decidedBy"));
        Assert.Equal(fixture.Estate.BuilderId, decision.By);
        Assert.Equal(device.Written.Id, Assert.Single(decision.Signatures).Device);
        Assert.Equal(fixture.Ask.ComputedHash, decision.Body.GetProperty("answer").GetProperty("hash").GetString());
        Assert.Equal(request.AnsweredAt, decision.Body.GetProperty("answer").GetProperty("at").GetString());
        Assert.Equal("answered", Asks.State(service.Session.Load(), fixture.Ask).State);
        RecordWritten receipt = Assert.Single(result.Pins);
        RecordEnvelope pin = service.Session.Load().ById[receipt.Id];
        Assert.Equal(fixture.InitialPin.Id, pin.Supersedes);
        Assert.Equal(2, pin.Body.GetProperty("revision").GetInt64());
        Assert.Equal("Guide", EstateModel.BodyString(pin, "title"));
        Assert.Equal(ContentHash.Sha256File(Path.Combine(fixture.Estate.Root, DecisionFixture.Document)), EstateModel.BodyString(pin, "hash"));
        Assert.Equal(device.Written.Id, Assert.Single(pin.Signatures).Device);
        JsonElement change = decision.Body.GetProperty("changeSet");
        Assert.Equal(fixture.BaseCommit, change.GetProperty("baseCommit").GetString());
        JsonElement saved = Assert.Single(change.GetProperty("pins").EnumerateArray());
        Assert.Equal(receipt.Id, saved.GetProperty("id").GetString());
        Assert.Equal(receipt.Hash, saved.GetProperty("hash").GetString());
        Assert.Equal(DecisionFixture.Document, Assert.Single(decision.Body.GetProperty("documents").EnumerateArray()).GetString());
        Assert.Equal(0, service.Check().Errors);
        Refused(fixture, request, "already answered");
    }

    [Theory]
    [InlineData("future-answer", "later than now")]
    [InlineData("future-decision", "later than now")]
    [InlineData("after-answer", "later than the operator's answer")]
    [InlineData("before-ask", "precede the ask")]
    [InlineData("invalid-time", "UTC record timestamps")]
    [InlineData("missing-document", "equal exactly")]
    [InlineData("extra-document", "equal exactly")]
    [InlineData("duplicate-document", "duplicate")]
    [InlineData("invalid-base", "change set is unavailable")]
    [InlineData("unknown-ask", "existing ask")]
    [InlineData("stale-ask", "expected ask")]
    public void InvalidAnswerTimeAskAndMembershipWriteNothing(string fault, string diagnostic)
    {
        using var fixture = new DecisionFixture();
        DecisionNewRequest request = fixture.Request;
        request = fault switch
        {
            "future-answer" => request with { AnsweredAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1)) },
            "future-decision" => request with { DecidedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1)) },
            "after-answer" => request with { DecidedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddSeconds(-1)) },
            "before-ask" => request with { AnsweredAt = RecordParser.FormatTimestamp(fixture.Ask.AtTime.AddSeconds(-1)) },
            "invalid-time" => request with { AnsweredAt = "tomorrow" },
            "missing-document" => request with { Documents = [] },
            "extra-document" => request with { Documents = [DecisionFixture.Document, "design/extra.md"] },
            "duplicate-document" => request with { Documents = [DecisionFixture.Document, DecisionFixture.Document] },
            "invalid-base" => request with { BaseCommit = "HEAD" },
            "unknown-ask" => request with { Ask = Ulid.NewUlid().Value },
            "stale-ask" => request with { ExpectedAsk = ContentHash.Sha256("stale"u8) },
            _ => throw new InvalidOperationException(),
        };
        Refused(fixture, request, diagnostic);
    }

    [Theory]
    [InlineData("committed")]
    [InlineData("staged")]
    [InlineData("unstaged")]
    [InlineData("untracked")]
    public void CompleteChangeSetIncludesEveryLocalDesignChange(string state)
    {
        using var fixture = new DecisionFixture();
        const string other = "design/second.md";
        if (state == "unstaged")
        {
            fixture.Estate.WriteFile(other, "# Before\n");
            fixture.Git("add", other);
            fixture.Git("commit", "-qm", "Add second fixture document");
        }
        fixture.Estate.WriteFile(other, "# After\n");
        if (state is "staged" or "committed") fixture.Git("add", other);
        if (state == "committed") fixture.Git("commit", "-qm", "Revise fixture document");
        Refused(fixture, fixture.Request, "equal exactly");
        DecisionNewResponse result = fixture.Service.DecisionFromAsk(fixture.Request with { Documents = [other, DecisionFixture.Document] });
        Assert.True(result.Ok, Details(result));
        Assert.Equal(2, result.Pins.Count);
        Assert.Equal(0, fixture.Service.Check().Errors);
    }

    [Fact]
    public void NoDesignChangesNeedsAnEmptyDocumentListAndNoPins()
    {
        using var fixture = new DecisionFixture();
        fixture.Git("checkout", "--", DecisionFixture.Document);
        fixture.Estate.WriteFile("docs/note.md", "An unrelated document.\n");
        Refused(fixture, fixture.Request, "equal exactly");
        DecisionNewResponse result = fixture.Service.DecisionFromAsk(fixture.Request with { Documents = [] });
        Assert.True(result.Ok, Details(result));
        Assert.Empty(result.Pins);
        Assert.Equal(0, fixture.Service.Check().Errors);
    }

    [Theory]
    [InlineData("withdrawn", "withdrawn")]
    [InlineData("non-operator", "operator")]
    [InlineData("deleted", "unavailable")]
    [InlineData("already-pinned", "unchanged bytes")]
    [InlineData("no-adapter", "unavailable")]
    public void UnusableAsksFilesAndPinsRefuseTheWholeBatch(string fault, string diagnostic)
    {
        using var fixture = new DecisionFixture();
        DecisionNewRequest request = fixture.Request with { ExpectedAsk = null };
        if (fault == "withdrawn")
        {
            JsonObject body = JsonNode.Parse(fixture.Ask.Body.GetRawText())!.AsObject();
            body["state"] = "withdrawn";
            fixture.Estate.Add("ask", fixture.Estate.BuilderId, body.ToJsonString(), supersedes: fixture.Ask.Id);
        }
        if (fault == "non-operator")
        {
            JsonObject body = JsonNode.Parse(fixture.Ask.Body.GetRawText())!.AsObject();
            body["to"] = fixture.Estate.BuilderId;
            request = request with { Ask = fixture.Estate.Add("ask", fixture.Estate.BuilderId, body.ToJsonString()).Id };
        }
        if (fault == "deleted") File.Delete(Path.Combine(fixture.Estate.Root, DecisionFixture.Document));
        if (fault == "already-pinned") Assert.True(fixture.Service.DocumentPin(new(DecisionFixture.Document, fixture.Estate.OperatorId, "Already pinned.")).Ok);
        if (fault == "no-adapter") fixture.Service.Providers = new ProviderRegistry();
        Refused(fixture, request, diagnostic);
    }

    [Fact]
    public void InvalidDecisionIsJudgedBeforeAnyPreparedPinIsStored()
    {
        using var fixture = new DecisionFixture();
        Refused(fixture, fixture.Request with { Effects = [] }, "invalid");
    }

    [Fact]
    public void StagedChangesCannotDisappearFromTheDocumentListWhenWorkingBytesAreRestored()
    {
        using var fixture = new DecisionFixture();
        fixture.Git("add", DecisionFixture.Document);
        fixture.Estate.WriteFile(DecisionFixture.Document, "# Guide\n\nInitial.\n");
        Refused(fixture, fixture.Request with { Documents = [] }, "equal exactly");
        Refused(fixture, fixture.Request, "unchanged bytes");
    }

    [Theory]
    [InlineData("--skip-worktree")]
    [InlineData("--assume-unchanged")]
    public void HiddenTrackedEditsCannotProduceAnEmptyChangeSet(string flag)
    {
        using var fixture = new DecisionFixture();
        fixture.Git("update-index", flag, DecisionFixture.Document);
        Refused(fixture, fixture.Request with { Documents = [] }, "unavailable");
    }

    [Fact]
    public void ForkedDocumentPinsRefuseBeforeAnyDecisionIsStored()
    {
        using var fixture = new DecisionFixture();
        JsonObject pin = JsonNode.Parse(fixture.Service.Record(fixture.InitialPin.Id)!.Envelope.GetProperty("body").GetRawText())!.AsObject();
        pin["revision"] = 2;
        pin["hash"] = ContentHash.Sha256File(Path.Combine(fixture.Estate.Root, DecisionFixture.Document));
        pin["bytes"] = new FileInfo(Path.Combine(fixture.Estate.Root, DecisionFixture.Document)).Length;
        fixture.Estate.SetClock(DateTimeOffset.UtcNow);
        fixture.Estate.Add("document-revision", fixture.Estate.OperatorId, pin.ToJsonString(), supersedes: fixture.InitialPin.Id);
        fixture.Estate.Add("document-revision", fixture.Estate.OperatorId, pin.ToJsonString(), supersedes: fixture.InitialPin.Id);
        Refused(fixture, fixture.Request, "forked pins");
    }

    [Fact]
    public void FilesChangedWhileSigningRefuseWithoutPublishingAnyRecord()
    {
        using var fixture = new DecisionFixture();
        var device = fixture.Service.DeviceCreate(new(fixture.Estate.OperatorId, "Signer", "file"));
        Assert.True(device.Ok);
        Assert.True(fixture.Service.DeviceAdmit(new(device.Written!.Id, fixture.Estate.OperatorId, "Admit signer.")).Ok);
        fixture.Service.BeforeSigningRecord = () => fixture.Estate.WriteFile("design/late.md", "# Changed during preparation\n");
        Refused(fixture, fixture.Request, "changed during preparation");
    }

    [Fact]
    public void InterruptedBatchReturnsItsStoredPinsAndRequiresReconciliation()
    {
        using var fixture = new DecisionFixture();
        fixture.Service.BeforeStoringTogether = record =>
        {
            if (record.Envelope.Type == "decision") throw new IOException("Simulated interruption.");
        };
        DecisionNewResponse result = fixture.Service.DecisionFromAsk(fixture.Request);
        Assert.False(result.Ok);
        Assert.Null(result.Written);
        Assert.Single(result.Pins);
        Assert.Equal("reconcile-required", result.Problem!.Code);
        Assert.Empty(fixture.Service.Session.Load().OfType("decision"));
    }

    [Theory]
    [InlineData("answer-hash", "decision.answer")]
    [InlineData("late-time", "decision.time")]
    [InlineData("missing-pin", "decision.documents")]
    [InlineData("pin-hash", "decision.pin")]
    [InlineData("extra-document", "decision.documents")]
    public void SavedAnswerAndChangePinsRemainValidatedOnRevision(string fault, string code)
    {
        using var fixture = new DecisionFixture();
        DecisionNewResponse result = fixture.Service.DecisionFromAsk(fixture.Request);
        Assert.True(result.Ok, Details(result));
        RecordEnvelope original = fixture.Service.Session.Load().ById[result.Written!.Id];
        JsonObject body = JsonNode.Parse(original.Body.GetRawText())!.AsObject();
        switch (fault)
        {
            case "answer-hash": body["answer"]!["hash"] = ContentHash.Sha256("wrong"u8); break;
            case "late-time": body["decidedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1)); break;
            case "missing-pin": body["changeSet"]!["pins"] = new JsonArray(); break;
            case "pin-hash": body["changeSet"]!["pins"]![0]!["hash"] = ContentHash.Sha256("wrong"u8); break;
            case "extra-document": body["documents"]!.AsArray().Add("design/unrelated.md"); break;
        }
        var before = fixture.Records();
        WriteOutcome refused = fixture.Service.NewRecord(new("decision", fixture.Estate.OperatorId,
            JsonSerializer.SerializeToElement(body), Supersedes: original.Id));
        Assert.False(refused.Ok);
        Assert.Contains(refused.Problem!.Findings, finding => finding.Code == code);
        Assert.Equal(before, fixture.Records());
    }

    private static void Refused(DecisionFixture fixture, DecisionNewRequest request, string diagnostic)
    {
        var before = fixture.Records();
        DecisionNewResponse result = fixture.Service.DecisionFromAsk(request);
        Assert.False(result.Ok);
        Assert.Null(result.Written);
        Assert.Empty(result.Pins);
        Assert.Contains(diagnostic, result.Problem!.Message, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Records());
    }

    private static string Details(DecisionNewResponse result) => JsonSerializer.Serialize(result.Problem);
}
