// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Requests = Vericairn.Records.Estate.OperatorRequests;
using AskQueue = Vericairn.Records.Estate.Asks;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public OperatorRequestsResponse OperatorRequests(string? principal = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return new(Requests.RenderText(model, principal), Element(Requests.Derive(model, principal)), model.Findings.HasErrors);
        }
    }

    /// <inheritdoc />
    public WriteOutcome OperatorRequestWrite(OperatorRequestWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!TryParseSubject(request.Subject, out RecordReference? subject)) return Refuse("Supply the work or release subject as type:id:hash.");
            if (request.Supersedes is string prior && (!model.ById.TryGetValue(prior, out RecordEnvelope? previous) ||
                previous.Type != "operator-request" || !model.IsCurrent(previous) || previous.ComputedHash != request.Expected))
                return RequestConflict();
            if (request.Supersedes is null && request.Expected is not null) return RequestConflict();
            return WriteLocked(new("operator-request", request.By, request.Body, subject, request.Supersedes), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome OperatorRequestCarry(string ask, OperatorRequestCarryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(ask, out RecordEnvelope? source) || source.Type != "ask" || !model.IsCurrent(source) ||
                source.ComputedHash != request.Expected || !AskQueue.IsWaiting(AskQueue.State(model, source).State) || Requests.Carried(model, source) is not null)
                return RequestConflict();
            if (request.Body.ValueKind != JsonValueKind.Object || !TryParseSubject(request.Subject, out RecordReference? subject))
                return Refuse("Supply the request details as an object and the work or release subject as type:id:hash.");
            var body = JsonNode.Parse(request.Body.GetRawText())!.AsObject();
            body["to"] = EstateModel.BodyString(source, "to");
            body["askedBy"] = source.By;
            body["action"] = EstateModel.BodyString(source, "question");
            body["recommendation"] = EstateModel.BodyString(source, "recommendation");
            body["consequence"] = EstateModel.BodyString(source, "consequence");
            body["why"] ??= EstateModel.BodyString(source, "consequence");
            body["sourceAsk"] = source.Id;
            body["sourceAskHash"] = source.ComputedHash;
            return WriteLocked(new("operator-request", request.By, Element(body), subject), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome OperatorRequestDone(string id, OperatorRequestCloseRequest request) => CloseRequest(id, request, "done");

    /// <inheritdoc />
    public WriteOutcome OperatorRequestDismiss(string id, OperatorRequestCloseRequest request) => CloseRequest(id, request, "dismissed");

    private WriteOutcome CloseRequest(string id, OperatorRequestCloseRequest request, string outcome)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(id, out RecordEnvelope? target) || target.Type != "operator-request" || !model.IsCurrent(target) ||
                request.Expected is not null && target.ComputedHash != request.Expected || Requests.Closures(model, target).Any())
                return RequestConflict();
            var body = new JsonObject { ["outcome"] = outcome };
            if (request.Reason is not null) body["reason"] = request.Reason;
            if (request.Link is not null) body["link"] = request.Link;
            if (request.Evidence is JsonElement evidence) body["evidence"] = JsonNode.Parse(evidence.GetRawText());
            if (request.OperatorWords is not null) body["operatorWords"] = JsonSerializer.SerializeToNode(request.OperatorWords, ProtocolJson.Options);
            return WriteLocked(new("operator-request-close", request.By, Element(body), target.Reference), model);
        }
    }

    private static WriteOutcome RequestConflict() => WriteOutcome.Fail(new("conflict", "The request or ask changed, is already carried or closed, or does not match the expected hash. Read the current queue and exact record before acting.", []));
}
