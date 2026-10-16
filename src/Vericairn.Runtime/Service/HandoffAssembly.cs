// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

/// <summary>The full-record selection of the hand-over context contract. It never asks an actor to write a recap.</summary>
internal static class HandoffAssembly
{
    internal const string Format = "vericairn.handoff/1";

    internal static HandoffResponse Build(EstateModel model, IReadOnlyList<RecordFile> files, long? maxBytes)
    {
        if (maxBytes is <= 0)
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
        var originals = new JsonArray();
        foreach (RecordEnvelope record in model.Records.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            RecordFile file = byPath[record.ExpectedPath];
            originals.Add(new JsonObject
            {
                ["id"] = record.Id,
                ["type"] = record.Type,
                ["hash"] = record.ComputedHash,
                ["path"] = record.ExpectedPath,
                ["current"] = model.IsCurrent(record),
                ["supersedes"] = record.Supersedes,
                ["bytes"] = file.Bytes.LongLength,
                ["byteHash"] = ContentHash.Sha256(file.Bytes),
                ["encoding"] = "base64",
                ["content"] = Convert.ToBase64String(file.Bytes),
                ["retrieve"] = "vericairn records show " + record.Id,
                ["api"] = "/v1/records/" + record.Id,
            });
        }

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
        var payload = new JsonObject
        {
            ["format"] = Format,
            ["generator"] = "vericairn.handoff/1",
            ["estate"] = model.Manifest!.Estate,
            ["recordFormat"] = model.Manifest.RecordFormat,
            ["pin"] = JsonSerializer.SerializeToNode(model.Manifest, ProtocolJson.Options),
            ["latestRecordedAt"] = model.Records.OrderBy(r => r.At, StringComparer.Ordinal).LastOrDefault()?.At,
            ["scope"] = "All records held by this estate copy, including superseded originals; for an authorized estate reader continuing on another surface or copy.",
            ["exclusions"] = new JsonArray("Source-file contents, output objects, unrecorded provider state and conversation are outside this record-only selection."),
            ["limitations"] = new JsonArray("The world may have moved. Recorded observations are not live health.", "This package grants no authority; current authority and required external originals must be checked before acting.", "No model input allocation, tokenizer, personalized communication profile or compaction recovery is qualified by this standalone hand-over.", "Remote is not built."),
            ["accounting"] = new JsonObject
            {
                ["method"] = "Canonical pretty UTF-8 payload, including originals, presentation, manifest and accounting; hash and byte count are in the sidecar.",
                ["maxBytes"] = maxBytes,
                ["capSource"] = maxBytes is null ? "limit not set" : "explicit request",
                ["tokens"] = null,
                ["tokenizer"] = "unknown",
                ["modelFit"] = "not claimed",
            },
            ["observedState"] = State(model),
            ["originals"] = originals,
            ["withheld"] = new JsonArray(),
            ["completeness"] = "Complete for the declared all-records selection; no selected source withheld and no irreversible reduction.",
            ["presentation"] = new JsonObject
            {
                ["generator"] = "vericairn.handoff/1",
                ["method"] = "deterministic record views",
                ["synthesis"] = "off",
                ["review"] = "derived, not accepted",
                ["basis"] = new JsonArray(model.Records.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["hash"] = r.ComputedHash }).ToArray()),
                ["text"] = lead.ToString(),
            },
        };
        JsonElement element = JsonSerializer.SerializeToElement(payload, ProtocolJson.Options);
        byte[] bytes = CanonicalJson.SerializePrettyUtf8(element);
        if (maxBytes is long limit && bytes.LongLength > limit)
        {
            return Refuse("handoff-over-budget", $"Hand-over requires {bytes.LongLength.ToString(CultureInfo.InvariantCulture)} payload bytes; the limit is {limit.ToString(CultureInfo.InvariantCulture)} bytes. Required originals and framing cannot be dropped. Increase the limit or continue with staged original reads; no complete package was produced.", []);
        }

        var package = new HandoffPackage(ContentHash.Sha256(bytes), bytes.LongLength, element);
        var text = new StringBuilder("Hand-over from the records\n");
        text.Append("Package ").Append(package.Hash).Append(" · ").Append(package.Bytes.ToString(CultureInfo.InvariantCulture)).Append(" payload bytes · ")
            .Append(maxBytes is null ? "Limit not set" : $"limit {maxBytes.Value.ToString(CultureInfo.InvariantCulture)} bytes").Append(" · tokens unknown; model fit not claimed\n");
        text.Append("The world may have moved. This package grants no authority. Remote is not built.\n")
            .Append("All records held by this copy, including history. No selected source withheld. Source files and output objects are outside this selection.\n\n")
            .Append("Derived presentation (deterministic; synthesis off; not accepted)\n").Append(lead)
            .Append("\nObserved state (records only)\n").Append(payload["observedState"]!.ToJsonString(ProtocolJson.Options))
            .Append("\n\nOriginals (complete)\n");
        foreach (JsonNode? original in originals)
        {
            text.Append('\n').Append(original!["type"]!.GetValue<string>()).Append(' ').Append(original["id"]!.GetValue<string>())
                .Append(" at ").Append(original["hash"]!.GetValue<string>()).Append(" · ").Append(original["bytes"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture)).Append(" bytes\n")
                .Append(Encoding.UTF8.GetString(Convert.FromBase64String(original["content"]!.GetValue<string>())));
        }

        return new HandoffResponse(text.ToString(), package, null);
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
