// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-109 (decision 01M6WZSSDJDMMTJ6QF6KT45P92, issue #338): every revision of an operation keeps its predecessor's intent and the target
/// facts it recorded, so a different effect or destination takes its own identity. The rule is prospective: a revision already in the
/// estate, in any record format, keeps its reading and is reported as information; the runtime refuses a new one. The issue's synthetic
/// operation is a manual effect at a fake destination, and nothing is dispatched anywhere.
/// </summary>
public sealed class OperationEffectKeptTests
{
    public const string IntentCode = "operation.intent-kept";
    public const string TargetCode = "operation.target-kept";

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnExistingRevisionThatChangesOnlyItsIntentIsInformationAndKeepsItsBytes(long format)
    {
        using var estate = new TestEstate(format);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        JsonObject body = Body("uncertain", intent.At);
        body["intent"] = "Publish synthetic media item 0001 and its companion to the example destination.";
        RecordEnvelope changed = estate.Add("operation", estate.OperatorId, body.ToJsonString(), supersedes: dispatched.Id);
        string path = Path.Combine(estate.Root, changed.ExpectedPath);
        byte[] before = File.ReadAllBytes(path);

        EstateModel model = estate.Load();

        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == IntentCode);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(changed.ExpectedPath + "#body.intent", finding.Location);
        Assert.Contains(dispatched.Id, finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Findings, f => f.Code == TargetCode);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(changed.ComputedHash, model.ById[changed.Id].ComputedHash);
        Assert.True(model.IsCurrent(model.ById[changed.Id]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnExistingRevisionThatChangesItsTargetAndIdempotencyKeyIsInformation(long format)
    {
        using var estate = new TestEstate(format);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        JsonObject body = Body("uncertain", intent.At);
        body["target"]!["destination"] = "https://example.invalid/media/0002";
        body["target"]!["idempotencyKey"] = "synthetic-key-0002";
        RecordEnvelope changed = estate.Add("operation", estate.OperatorId, body.ToJsonString(), supersedes: dispatched.Id);
        byte[] before = File.ReadAllBytes(Path.Combine(estate.Root, changed.ExpectedPath));

        EstateModel model = estate.Load();

        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Finding finding = Assert.Single(model.Findings, f => f.Code == TargetCode);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(changed.ExpectedPath + "#body.target", finding.Location);
        Assert.Contains("facts 'destination', 'idempotencyKey'", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Findings, f => f.Code == IntentCode);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(estate.Root, changed.ExpectedPath)));
    }

    [Fact]
    public void ARevisionMayAddAnObservedTargetFactButNeverDropsOne()
    {
        using var estate = new TestEstate(3);
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, Body("not-started").ToJsonString());
        JsonObject observed = Body("dispatched", intent.At);
        observed["target"]!["run"] = "1000";
        RecordEnvelope dispatched = estate.Add("operation", estate.OperatorId, observed.ToJsonString(), supersedes: intent.Id);
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code is IntentCode or TargetCode);

        JsonObject dropped = Body("uncertain", intent.At);
        dropped["target"]!["run"] = "1000";
        dropped["target"]!.AsObject().Remove("idempotencyKey");
        RecordEnvelope uncertain = estate.Add("operation", estate.OperatorId, dropped.ToJsonString(), supersedes: dispatched.Id);
        Finding finding = Assert.Single(estate.Load().Findings, f => f.Code == TargetCode);
        Assert.Equal(uncertain.ExpectedPath + "#body.target", finding.Location);
        Assert.Contains("fact 'idempotencyKey'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRetryReconciliationPathIsUnchanged()
    {
        // The issue's control: a retry after an uncertain outcome without a reconciliation is refused; with one, it is accepted.
        using var estate = new TestEstate(3);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        RecordEnvelope uncertain = estate.Add("operation", estate.OperatorId, Body("uncertain", intent.At).ToJsonString(), supersedes: dispatched.Id);
        RecordEnvelope unreconciled = estate.Add("operation", estate.OperatorId, Body("dispatched", intent.At).ToJsonString(), supersedes: uncertain.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "operation.reconciliation" && f.Severity == Severity.Error && f.Location == unreconciled.ExpectedPath);
        File.Delete(Path.Combine(estate.Root, unreconciled.ExpectedPath));

        JsonObject retry = Body("dispatched", intent.At);
        retry["reconciliation"] = "Nothing reached the example destination; retried under the same identity.";
        RecordEnvelope retried = estate.Add("operation", estate.OperatorId, retry.ToJsonString(), supersedes: uncertain.Id);
        JsonObject success = Body("observed-success", intent.At);
        success["reconciliation"] = retry["reconciliation"]!.GetValue<string>();
        success["observed"] = new JsonObject { ["summary"] = "The synthetic item is at the example destination.", ["at"] = retried.At };
        estate.Add("operation", estate.OperatorId, success.ToJsonString(), supersedes: retried.Id);

        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code is IntentCode or TargetCode);
    }

    [Theory]
    [InlineData("known-failure", "dispatched")]
    [InlineData("known-failure", "uncertain")]
    [InlineData("canceled", "dispatched")]
    [InlineData("canceled", "uncertain")]
    public void AReconciledRetryAfterASettledOutcomeReadsAsBefore(string settled, string retried)
    {
        // The retry path is unchanged (decision 01M6WZSSDJDMMTJ6QF6KT45P92): a retry after a known failure or a cancellation that names
        // its reconciliation pins the settings it actually runs with, as a release retry that repairs its workflow does.
        using var estate = new TestEstate(3);
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, Body("not-started").ToJsonString());
        JsonObject outcome = Body(settled);
        if (settled == "known-failure") outcome["observed"] = new JsonObject { ["summary"] = "Refused before any dispatch.", ["at"] = intent.At };
        RecordEnvelope earlier = estate.Add("operation", estate.OperatorId, outcome.ToJsonString(), supersedes: intent.Id);
        JsonObject retry = Body(retried, intent.At);
        retry["intent"] = "Publish synthetic media item 0001 to the example destination through the repaired route.";
        retry["target"]!["route"] = "repaired";
        retry["target"]!["idempotencyKey"] = "synthetic-key-0001-retry";
        retry["reconciliation"] = "The earlier attempt was refused before any dispatch; nothing reached the destination.";
        estate.Add("operation", estate.OperatorId, retry.ToJsonString(), supersedes: earlier.Id);

        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code is IntentCode or TargetCode);
    }

    [Theory]
    [InlineData("format-1-estate")]
    [InlineData("format-3-policy-estate")]
    public void TheGoldenFixturesReadAsBefore(string fixture)
    {
        // Operations written by earlier releases, repository changes and their observations included, keep their reading.
        EstateModel model = EstateLoader.Load(Path.Combine(RecordCompanionTests.FindRepository(), "tests", "fixtures", fixture));
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.DoesNotContain(model.Findings, f => f.Code is IntentCode or TargetCode);
    }

    /// <summary>The issue's synthetic operation: a manual effect at a fake destination, in a state.</summary>
    public static JsonObject Body(string status, string? dispatchedAt = null)
    {
        var body = new JsonObject
        {
            ["provider"] = "manual",
            ["action"] = "publish-media",
            ["identity"] = "synthetic:media-0001",
            ["intent"] = "Publish synthetic media item 0001 to the example destination.",
            ["target"] = new JsonObject { ["destination"] = "https://example.invalid/media/0001", ["idempotencyKey"] = "synthetic-key-0001" },
            ["status"] = status,
            ["evidence"] = new JsonArray(),
        };
        if (dispatchedAt is not null) body["dispatchedAt"] = dispatchedAt;
        return body;
    }

    /// <summary>The intent, recorded before anything is dispatched, and its dispatched revision.</summary>
    public static (RecordEnvelope Intent, RecordEnvelope Dispatched) Dispatch(TestEstate estate)
    {
        RecordEnvelope intent = estate.Add("operation", estate.OperatorId, Body("not-started").ToJsonString());
        RecordEnvelope dispatched = estate.Add("operation", estate.OperatorId, Body("dispatched", intent.At).ToJsonString(), supersedes: intent.Id);
        return (intent, dispatched);
    }
}
