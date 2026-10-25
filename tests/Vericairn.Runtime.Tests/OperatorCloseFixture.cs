// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>Finished work in a throwaway estate, using only the test suite's isolated signing home.</summary>
public sealed class OperatorCloseFixture : IDisposable
{
    private static readonly string[] Scope = ["fixture work"];
    private static readonly string[] Outputs = ["result"];
    private static readonly string[] Workspace = ["test repository"];
    private static readonly string[] Criteria = ["The step is finished."];
    public TestEstate Estate { get; } = new(3);
    public EstateService Api { get; }
    public string? Device { get; }

    public OperatorCloseFixture(bool device = true)
    {
        Api = EstateService.Open(Estate.Root);
        if (device)
        {
            WriteOutcome created = Api.DeviceCreate(new(Estate.OperatorId, "Operator test device", "file"));
            Assert.True(created.Ok, created.Problem?.Message);
            Device = created.Written!.Id;
            WriteOutcome admitted = Api.DeviceAdmit(new(Device, Estate.OperatorId, "Admit the test operator's device."));
            Assert.True(admitted.Ok, admitted.Problem?.Message);
        }
    }

    public sealed record StepRecords(RecordEnvelope Order, RecordEnvelope Assignment, RecordEnvelope Attempt, RecordEnvelope Result, RecordEnvelope Review);

    public StepRecords Add(string key, string outcome = "completed", string reviewOutcome = "pass", string kind = "ordinary", bool self = false)
    {
        RecordEnvelope order = Estate.Add("work-order", Estate.OperatorId, JsonSerializer.Serialize(new
        {
            key, kind, title = "Finish " + key, intent = "Produce the requested step.", scope = Scope,
            exitQuestion = "Is " + key + " complete?", inputs = Array.Empty<string>(), deliverables = Outputs,
        }));
        string contributor = self ? Estate.OperatorId : Estate.BuilderId;
        RecordEnvelope assignment = Estate.Add("assignment", Estate.OperatorId, JsonSerializer.Serialize(new
        {
            contributor,
            brief = new
            {
                outcome = "Finish the step.", sources = Array.Empty<string>(), permittedChanges = Scope,
                prohibitedEffects = Array.Empty<string>(), workspace = Workspace, limits = Array.Empty<string>(),
                expectedOutputs = Outputs, reviewCriteria = Criteria, recovery = "Read the result.",
            },
        }), order.Reference);
        RecordEnvelope attempt = Attempt(assignment, contributor);
        RecordEnvelope result = Result(attempt, outcome);
        RecordEnvelope review = Review(result, reviewOutcome);
        return new(order, assignment, attempt, result, review);
    }

    public RecordEnvelope Attempt(RecordEnvelope assignment, string? contributor = null) => Estate.Add("attempt", contributor ?? Estate.BuilderId,
        JsonSerializer.Serialize(new { startedAt = RecordParser.FormatTimestamp(Estate.Clock.AddSeconds(1)), workspace = "test repository", note = "Finish this step." }), assignment.Reference);

    public RecordEnvelope Result(RecordEnvelope attempt, string outcome = "completed", string? supersedes = null) => Estate.Add("result", attempt.By,
        JsonSerializer.Serialize(new { outcome, summary = "Finished work for inspection.", outputs = Array.Empty<string>(), evidence = Array.Empty<string>(), checks = Array.Empty<string>(),
            limitations = Array.Empty<string>(), unresolvedEffects = Array.Empty<string>(), nextStep = "Review and accept the exact result." }), attempt.Reference, supersedes);

    public RecordEnvelope Review(RecordEnvelope result, string outcome = "pass", string? supersedes = null, bool self = false) => Estate.Add("review", self ? result.By : Estate.OperatorId,
        JsonSerializer.Serialize(new { independence = self || result.By == Estate.OperatorId ? "self" : "operator", outcome, summary = "Inspected the exact result.", findings = Array.Empty<string>() }), result.Reference, supersedes);

    public RecordEnvelope Revise(RecordEnvelope record, string field, string value)
    {
        JsonObject body = JsonNode.Parse(record.Body.GetRawText())!.AsObject();
        body[field] = value;
        return Estate.Add(record.Type, record.By, body.ToJsonString(), record.Subject, record.Id);
    }

    public void Accept(RecordEnvelope result) => Estate.Add("acceptance", Estate.OperatorId,
        JsonSerializer.Serialize(new { statement = "Accept the inspected result.", basis = Array.Empty<string>(), self = result.By == Estate.OperatorId }), result.Reference);

    public OperatorCloseRequest Request(bool all = true) => new(Estate.OperatorId, [.. Api.OperatorCloseCandidates().Items.Select(i => i.ToSelection())], all);

    public void Dispose() { Api.Dispose(); Estate.Dispose(); }
}
