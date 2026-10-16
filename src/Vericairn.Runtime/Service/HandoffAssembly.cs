// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>The full-record selection of the hand-over context contract. It never asks an actor to write a recap.</summary>
internal static class HandoffAssembly
{
    internal const string Format = "vericairn.handoff/1";

    internal static HandoffResponse Build(EstateModel model, IReadOnlyList<RecordFile> files, long? maxBytes)
    {
        if (maxBytes is <= 0 or > CanonicalJson.MaxSafeInteger)
        {
            return Refuse("invalid", "The payload byte limit must be positive.", []);
        }

        List<FindingDto> errors = model.Findings.Where(f => f.Severity == Severity.Error)
            .OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal)
            .Select(f => new FindingDto("error", f.Code, f.Location, f.Message)).ToList();
        if (errors.Count > 0)
        {
            return Refuse("incomplete-handoff", "Hand-over refused: restore or reconcile every missing or invalid source listed below, then prepare again.", errors);
        }

        Dictionary<string, RecordFile> byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        List<ContextAssembly.Source> sources = model.Records.Select(record => ContextAssembly.Record(record, byPath[record.ExpectedPath], true, "Review before continuing from this hand-over.")).ToList();

        var lead = new StringBuilder(ProgressReport.Render(model, null));
        foreach (RecordEnvelope goal in model.Current("goal").OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            lead.Append('\n').Append(GoalView.RenderText(model, goal));
        }

        foreach (RecordEnvelope order in model.Current("work-order").OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            lead.Append('\n').Append(WorkView.RenderBrief(model, order));
        }

        lead.Append('\n').Append(Asks.RenderText(model));
        const string scope = "All records held by this estate copy, including superseded originals; for an authorized estate reader continuing on another surface or copy.";
        ContextPackage context = ContextAssembly.Build(ContextSelection.Basis(model, scope), sources, lead.ToString(), maxBytes);
        JsonObject payload = JsonNode.Parse(context.Payload.GetRawText())!.AsObject();
        // Preserve the API v1 discriminator and resume fields as a compatibility view of the shared content contract.
        payload["format"] = Format;
        payload["contextFormat"] = ContextAssembly.Format;
        payload["generator"] = ContextAssembly.Format;
        payload["estate"] = model.Manifest!.Estate;
        payload["recordFormat"] = model.Manifest.RecordFormat;
        payload["pin"] = JsonSerializer.SerializeToNode(model.Manifest, ProtocolJson.Options);
        payload["latestRecordedAt"] = model.Records.OrderBy(r => r.At, StringComparer.Ordinal).LastOrDefault()?.At;
        payload["scope"] = scope;
        payload["exclusions"] = new JsonArray("Source-file contents, output objects, unrecorded provider state and conversation are outside this record-only selection.");
        payload["limitations"] = new JsonArray("The world may have moved. Recorded observations are not live health.", "This package grants no authority; current authority and required external originals must be checked before acting.", "No model input allocation, tokenizer, personalized communication profile or compaction recovery is qualified by this standalone hand-over.", "Remote is not built.");
        payload["observedState"] = State(model);
        foreach (JsonObject original in payload["originals"]!.AsArray().Cast<JsonObject>())
        {
            RecordEnvelope record = model.ById[original["id"]!.GetValue<string>()];
            original["current"] = model.IsCurrent(record);
            original["supersedes"] = record.Supersedes;
            original["retrieve"] = "vericairn records show " + record.Id;
            original["api"] = "/v1/records/" + record.Id;
            original["originalRetrieve"] = ContextAssembly.Retrieval(payload["manifest"]!["hash"]!.GetValue<string>(), original["key"]!.GetValue<string>());
        }

        payload["originals"] = new JsonArray(payload["originals"]!.AsArray().OrderBy(s => s!["id"]!.GetValue<string>(), StringComparer.Ordinal).Select(s => s!.DeepClone()).ToArray());

        context = ContextAccounting.Measure(payload, maxBytes);
        if (maxBytes is long limit && context.Bytes > limit)
        {
            return Refuse("handoff-over-budget", $"Hand-over requires {context.Bytes.ToString(CultureInfo.InvariantCulture)} payload bytes; the limit is {limit.ToString(CultureInfo.InvariantCulture)} bytes. Required originals and framing cannot be dropped. Increase the limit or continue with staged original reads; no complete package was produced.", []);
        }

        var package = new HandoffPackage(context.Hash, context.Bytes, context.Payload);
        var text = new StringBuilder("Hand-over from the records\n");
        text.Append("Package ").Append(package.Hash).Append(" · ").Append(package.Bytes.ToString(CultureInfo.InvariantCulture)).Append(" payload bytes · ")
            .Append(maxBytes is null ? "Limit not set" : $"limit {maxBytes.Value.ToString(CultureInfo.InvariantCulture)} bytes").Append(" · tokens unknown; model fit not claimed\n");
        text.Append("The world may have moved. This package grants no authority. Remote is not built.\n")
            .Append("All records held by this copy, including history. No selected source withheld. Source files and output objects are outside this selection.\n\n")
            .Append("Derived presentation (deterministic; synthesis off; not accepted)\n").Append(lead)
            .Append("\nObserved state (records only)\n").Append(payload["observedState"]!.ToJsonString(ProtocolJson.Options));
        (string rendered, ContextTextMeasurement measurement) = ContextRendering.Render(text.ToString(), context);
        return new HandoffResponse(rendered, package, null, measurement);
    }

    internal static JsonObject State(EstateModel model) => new()
    {
        ["status"] = JsonNode.Parse(EstateStatus.RenderJson(model, new FindingList())),
        ["goals"] = new JsonArray(model.Current("goal").OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => JsonNode.Parse(GoalView.RenderJson(model, r))).ToArray()),
        ["work"] = new JsonArray(model.Current("work-order").OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => JsonNode.Parse(WorkView.RenderBriefJson(model, r))).ToArray()),
        ["asks"] = JsonNode.Parse(Asks.RenderJson(model)),
        ["decisions"] = Current(model, "decision"),
        ["operations"] = Current(model, "operation"),
        ["observations"] = Current(model, "observation"),
    };

    private static JsonArray Current(EstateModel model, string type) => new(model.Current(type).OrderBy(r => r.Id, StringComparer.Ordinal)
        .Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["hash"] = r.ComputedHash, ["body"] = JsonNode.Parse(r.Body.GetRawText()) }).ToArray());

    internal static HandoffResponse Refuse(string code, string message, IReadOnlyList<FindingDto> findings) =>
        new(message + "\n" + string.Concat(findings.Select(f => $"{f.Location}: {f.Code}: {f.Message}\n")), null, new Problem(code, message, findings));
}
