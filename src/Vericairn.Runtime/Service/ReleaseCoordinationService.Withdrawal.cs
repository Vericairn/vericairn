// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

internal sealed partial class ReleaseCoordinationService
{
    private ReleaseResponse ObserveWithdrawal(ReleaseObserveRequest request)
    {
        RecordEnvelope operation;
        string identity;
        string status;
        string providerId;
        JsonElement target;
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? found = Latest(model, request.Withdrawal!, "operation");
            if (found is null || EstateModel.BodyString(found, "action") != ReleaseFlow.DeleteDraftAction)
                return ReleaseProblem(request.Withdrawal!, "unknown", "not-found", "No draft-deletion intent has that id.");
            operation = found;
            identity = EstateModel.BodyString(operation, "identity")!;
            status = EstateModel.BodyString(operation, "status")!;
            providerId = EstateModel.BodyString(operation, "provider")!;
            target = operation.Body.GetProperty("target");
            string? version = Text(target, "version");
            string? repository = Text(target, "repository");
            string? product = Text(target, "product");
            RecordEnvelope? withdrawal = operation.Subject is { Type: "release" } subject ? model.ById.GetValueOrDefault(subject.Id) : null;
            if (withdrawal is null || operation.Subject != withdrawal.Reference || EstateModel.BodyString(withdrawal, "status") != "withdrawn" ||
                version != EstateModel.BodyString(withdrawal, "version") || repository != EstateModel.BodyString(withdrawal, "repository") || product != withdrawal.Subject?.Id)
                return ReleaseProblem(identity, status, "refused", "The deletion intent must pin the withdrawn release and retain its repository, product and version.");
            if (request.Version != version ||
                request.Repository is not null && request.Repository != repository ||
                request.Provider is not null && request.Provider != providerId ||
                request.Product is not null && request.Product != product || request.Cancel is not null)
                return ReleaseProblem(identity, status, "refused", "Observe the deletion intent's recorded version, destination and provider; cancellation is a separate revision of that intent.");
            if (status == "observed-success") return new(identity, status, "The draft's absence is already recorded under this deletion intent.", true);
            if (status is not ("dispatched" or "uncertain"))
                return ReleaseProblem(identity, status, "refused", "The operator deletes the draft and records dispatch or uncertainty under this intent before its outcome is observed. Nothing was deleted by the runtime.");
        }

        IReleaseProvider? provider = Providers.Release(providerId);
        if (provider is null) return ReleaseProblem(identity, status, "refused", "The recorded deletion provider is unavailable; no other provider may observe it.");
        var settings = target.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name is not ("version" or "product"))
            .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        string? destinationKey = provider.Descriptor.Destination;
        if (destinationKey is not null && !settings.ContainsKey(destinationKey))
            return ReleaseProblem(identity, status, "refused", "The deletion intent lacks its provider's destination setting; reconcile that original intent before observing it.");
        string destination = destinationKey is null ? providerId : settings[destinationKey];
        ReleaseObservation? observed;
        try { observed = provider.ObserveRelease(destination, ReleaseFlow.Tag(request.Version), settings); }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
        { return ReleaseProblem(identity, status, "provider", exception.Message); }

        lock (gate)
        {
            RecordEnvelope? current = CurrentOperation(identity);
            if (current?.ComputedHash != operation.ComputedHash)
                return ReleaseProblem(identity, status, "conflict", "The deletion intent changed during observation; observe its current revision again.");
            if (observed is not null)
                return new(identity, status, observed.Draft ? "The draft is still present; deletion has not been observed." : "The release is published at the recorded destination; draft deletion has not been observed.", false, Url: observed.Url);
            JsonObject body = JsonNode.Parse(operation.Body.GetRawText())!.AsObject();
            body["status"] = "observed-success";
            body["observed"] = new JsonObject
            {
                ["summary"] = "The provider observed no release at the withdrawn candidate's tag after the operator's recorded deletion dispatch.",
                ["at"] = RecordParser.FormatTimestamp(now()),
            };
            string? subject = operation.Subject is { } reference ? $"{reference.Type}:{reference.Id}:{reference.Hash}" : null;
            WriteOutcome result = WriteRecord(new("operation", request.By, Element(body), subject, operation.Id, Expected: operation.ComputedHash));
            return result.Ok ? new(identity, "observed-success", "The withdrawn draft's absence is recorded; the runtime performed no deletion.", true, Operation: result.Written)
                : ReleaseProblem(identity, status, result.Problem!);
        }

        static string? Text(JsonElement body, string key) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
