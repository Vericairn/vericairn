// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Policies.PolicyInputs;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public WorkMovePlanResponse WorkMovePlan(string key, WorkMovePlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? source = WorkView.FindOrder(model, key);
            if (source is null) return Failed("not-found", "No source carrier has that key or id.");
            if (source.ComputedHash != request.Expected) return Conflict();
            if (Get(source.Body, "releasePlan").ValueKind != JsonValueKind.Object)
                return Failed("refused", "The source carrier has no typed release plan.");
            if ((request.To is null) == (request.Version is null))
                return Failed("refused", "Select exactly one destination carrier or new version.");

            RecordEnvelope target = source;
            if (request.To is not null)
            {
                RecordEnvelope? selected = WorkView.FindOrder(model, request.To);
                if (selected is null) return Failed("not-found", "No destination carrier has that key or id.");
                if (selected.ComputedHash != request.ExpectedTarget) return Conflict();
                if (Get(selected.Body, "releasePlan").ValueKind != JsonValueKind.Object)
                    return Failed("refused", "The destination carrier has no typed release plan.");
                if (selected.Id == source.Id) return Failed("refused", "The destination is already the source carrier.");
                target = selected;
            }
            else if (request.ExpectedTarget is not null)
                return Failed("refused", "An expected destination hash accompanies a destination carrier, not a version.");

            DateTimeOffset at = PolicyClock.GetUtcNow();
            string identity = PolicyWork.Identity(model, source);
            RecordEnvelope[] members;
            try
            {
                members = model.Current("work-order").Where(o => Text(Get(o.Body, "placement"), "kind") == "release" &&
                    Text(Get(o.Body, "placement"), "carrier") == identity && PolicyWork.Planned(model, o, at))
                    .OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
            }
            catch (PolicyInputException)
            {
                return Failed("refused", "The planned membership is ambiguous; reconcile the work history before moving it.");
            }
            if (request.ExpectedWork is null || request.ExpectedWork.Count != members.Length ||
                !members.Select(o => KeyValuePair.Create(o.Id, o.ComputedHash))
                    .SequenceEqual(request.ExpectedWork.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
                return Conflict();

            JsonElement plan = Get(target.Body, "releasePlan");
            var placement = new JsonObject
            {
                ["kind"] = "release", ["carrier"] = PolicyWork.Identity(model, target),
                ["product"] = Text(plan, "product"), ["version"] = request.Version ?? Text(plan, "version"),
            };
            // A carrier may itself be a planned member. Combine its plan and placement into a single successor.
            var revisions = new Dictionary<string, (RecordEnvelope Original, JsonObject Body)>(StringComparer.Ordinal);
            if (request.Version is not null)
            {
                JsonObject body = JsonNode.Parse(source.Body.GetRawText())!.AsObject();
                body["releasePlan"]!["version"] = request.Version;
                revisions.Add(source.Id, (source, body));
            }
            foreach (RecordEnvelope member in members)
            {
                JsonObject body = revisions.TryGetValue(member.Id, out var revision) ? revision.Body : JsonNode.Parse(member.Body.GetRawText())!.AsObject();
                body["placement"] = placement.DeepClone();
                revisions[member.Id] = (member, body);
            }

            List<AuthoredRecord> records = [];
            foreach (var (original, body) in revisions.Values)
            {
                var draft = new RecordDraft("work-order", request.By, Element(body), original.Subject, original.Id, at);
                string? refusal = ResolveSigner(model, draft, out IRecordSigner? signer);
                if (refusal is not null || signer is null)
                    return Failed("refused", refusal ?? "The acting principal needs an admitted device whose key this runtime holds.");
                (AuthoredRecord? record, refusal) = AuthorLocked(draft, model, [signer]);
                if (record is null) return Failed("refused", refusal!);
                records.Add(record);
            }
            if (records.Count == 0) return new([]);
            var stored = StoreTogetherLocked(records, "Move the complete planned work of a release carrier.", identity);
            return new(stored.Written, stored.Problem);
        }

        static WorkMovePlanResponse Failed(string code, string message) => new([], new(code, message, []));
        static WorkMovePlanResponse Conflict() => Failed("conflict", "The expected carrier, destination or complete planned-work set changed. Read the current records and prepare again; nothing was written.");
    }
}
