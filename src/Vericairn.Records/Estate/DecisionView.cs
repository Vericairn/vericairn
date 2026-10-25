// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Renders decisions for clean display: the synthesis first, the verbatim statement quoted, then effects and affected work.</summary>
public static class DecisionView
{
    /// <summary>Current decisions, newest first.</summary>
    public static IReadOnlyList<RecordEnvelope> Current(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Current("decision").OrderByDescending(d => EstateModel.BodyString(d, "decidedAt"), StringComparer.Ordinal).ThenByDescending(d => d.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Finds a current decision by id, or by the id of any earlier revision of it.</summary>
    public static RecordEnvelope? Find(EstateModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Type == "decision" ? model.Latest(record) : null;
    }

    /// <summary>A one-line-per-decision list.</summary>
    public static string RenderList(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var text = new StringBuilder();
        foreach (RecordEnvelope decision in Current(model))
        {
            text.Append(EstateModel.BodyString(decision, "decidedAt")?[..10]).Append("  ").Append(decision.Id).Append("  ").Append(EstateModel.BodyString(decision, "title")).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>One decision as Markdown.</summary>
    public static string Render(EstateModel model, RecordEnvelope decision)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(decision);
        var text = new StringBuilder();
        string decidedBy = EstateModel.BodyString(decision, "decidedBy") ?? string.Empty;
        string recorder = decision.By;
        text.Append("# ").Append(EstateModel.BodyString(decision, "title")).Append("\n\n");
        text.Append("Decided by ").Append(PrincipalName(model, decidedBy)).Append(" on ").Append(EstateModel.BodyString(decision, "decidedAt"))
            .Append("; recorded by ").Append(PrincipalName(model, recorder)).Append(" at ").Append(decision.At)
            .Append("; record ").Append(decision.Id).Append(" (revision ").Append(model.Chain(decision).Count).Append(")\n\n");
        if (EstateModel.BodyString(decision, "stake") is { } stake)
        {
            text.Append("Stake: ").Append(stake).Append("\n\n");
        }

        string? synthesis = EstateModel.BodyString(decision, "synthesis");
        text.Append("## Synthesis\n\n").Append(synthesis ?? "_No synthesis recorded yet._").Append("\n\n");
        text.Append("## Verbatim\n\n");
        foreach (string line in (EstateModel.BodyString(decision, "statement") ?? string.Empty).Split('\n'))
        {
            text.Append("> ").Append(line).Append('\n');
        }

        text.Append("\n## Summary\n\n").Append(EstateModel.BodyString(decision, "summary")).Append("\n\n## Effects\n\n");
        if (decision.Body.TryGetProperty("effects", out JsonElement effects))
        {
            foreach (JsonElement effect in effects.EnumerateArray())
            {
                text.Append("- ").Append(effect.GetString()).Append('\n');
            }
        }

        if (decision.Body.TryGetProperty("notAuthorized", out JsonElement notAuthorized) && notAuthorized.GetArrayLength() > 0)
        {
            text.Append("\n## Not authorized by this answer\n\n");
            foreach (JsonElement item in notAuthorized.EnumerateArray())
            {
                text.Append("- ").Append(item.GetString()).Append('\n');
            }
        }

        if (decision.Body.TryGetProperty("answer", out JsonElement answer) && answer.ValueKind == JsonValueKind.Object)
        {
            text.Append("\n## Recorded answer\n\nAsk ").Append(answer.GetProperty("ask").GetString())
                .Append(" at ").Append(answer.GetProperty("hash").GetString())
                .Append("; answered at ").Append(answer.GetProperty("at").GetString()).Append(".\n");
        }
        if (decision.Body.TryGetProperty("changeSet", out JsonElement change) && change.ValueKind == JsonValueKind.Object)
        {
            text.Append("\n## Design change set\n\nBase commit: ").Append(change.GetProperty("baseCommit").GetString())
                .Append(". Observed HEAD: ").Append(change.GetProperty("head").GetString()).Append(".\n\n");
            JsonElement pins = change.GetProperty("pins");
            if (pins.GetArrayLength() == 0) text.Append("No design files changed.\n");
            foreach (JsonElement pin in pins.EnumerateArray())
                text.Append("- ").Append(pin.GetProperty("path").GetString()).Append(": pin ").Append(pin.GetProperty("id").GetString())
                    .Append(" at ").Append(pin.GetProperty("hash").GetString()).Append('\n');
        }

        if (decision.Body.TryGetProperty("affects", out JsonElement affects) && affects.GetArrayLength() > 0)
        {
            text.Append("\n## Affects\n\n");
            foreach (JsonElement affected in affects.EnumerateArray())
            {
                RecordEnvelope? order = affected.GetString() is { } id && model.ById.TryGetValue(id, out RecordEnvelope? o) ? model.Latest(o) : null;
                text.Append("- ").Append(order is null ? affected.GetString() : $"{EstateModel.BodyString(order, "key")} {EstateModel.BodyString(order, "title")} ({order.Id})").Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>All current decisions as canonical pretty JSON for machines.</summary>
    public static string RenderJson(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var list = new JsonArray(Current(model).Select(d =>
        {
            var row = new JsonObject
            {
                ["id"] = d.Id,
                ["title"] = EstateModel.BodyString(d, "title"),
                ["decidedAt"] = EstateModel.BodyString(d, "decidedAt"),
                ["decidedBy"] = PrincipalName(model, EstateModel.BodyString(d, "decidedBy") ?? string.Empty),
                ["summary"] = EstateModel.BodyString(d, "summary"),
                ["synthesis"] = EstateModel.BodyString(d, "synthesis"),
                ["statement"] = EstateModel.BodyString(d, "statement"),
                ["revision"] = model.Chain(d).Count,
            };
            if (EstateModel.BodyString(d, "stake") is { } stake)
            {
                row["stake"] = stake;
            }

            return (JsonNode)row;
        }).ToArray());
        using JsonDocument document = JsonDocument.Parse(list.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }

    private static string PrincipalName(EstateModel model, string id) => EstateModel.BodyString(model.CurrentPrincipal(id)!, "name") ?? id;
}
