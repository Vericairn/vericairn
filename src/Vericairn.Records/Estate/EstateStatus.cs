// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Renders the current state of an estate from its records: the only status surface.</summary>
public static class EstateStatus
{
    /// <summary>One work order with its derived status.</summary>
    /// <param name="Key">Work-order key.</param>
    /// <param name="Kind">milestone or ordinary.</param>
    /// <param name="Title">Title.</param>
    /// <param name="Status">Derived status.</param>
    /// <param name="Id">Record id.</param>
    public sealed record WorkItem(string Key, string Kind, string Title, string Status, string Id);

    /// <summary>Derives the status of every current work order.</summary>
    public static IReadOnlyList<WorkItem> WorkItems(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var items = new List<WorkItem>();
        foreach (RecordEnvelope order in model.Current("work-order"))
        {
            IReadOnlyList<string> chain = model.Chain(order);
            string status = "planned";
            RecordEnvelope? latest = chain.SelectMany(id => model.About(id, "work-status")).OrderBy(s => s.At, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).LastOrDefault();
            if (latest is not null)
            {
                status = EstateModel.BodyString(latest, "status") ?? status;
            }

            RecordEnvelope? acceptance = chain.SelectMany(id => model.About(id, "acceptance")).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
            if (acceptance is not null)
            {
                status = acceptance.Subject?.Id == order.Id ? "accepted" : "accepted (earlier revision)";
            }

            items.Add(new WorkItem(EstateModel.BodyString(order, "key") ?? "?", EstateModel.BodyString(order, "kind") ?? "?", EstateModel.BodyString(order, "title") ?? "?", status, order.Id));
        }

        return items.OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Human-readable status.</summary>
    public static string Render(EstateModel model, FindingList checks)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(checks);
        var text = new StringBuilder();
        RecordEnvelope? estate = model.CurrentEstate;
        text.Append("Estate: ").Append(estate is null ? "(no charter)" : $"{EstateModel.BodyString(estate, "name")} ({EstateModel.BodyString(estate, "slug")})")
            .Append(" · record format 1 · ").Append(model.Records.Count.ToString(CultureInfo.InvariantCulture)).Append(" records\n");
        text.Append("Vericairn: ").Append(model.Manifest?.Describe() ?? "(no manifest)").Append('\n');
        text.Append("Principals:\n");
        foreach (RecordEnvelope principal in model.OfType("principal").Where(p => !model.SupersededBy.ContainsKey(p.Id) || true).Where(p => model.Records.All(r => r.Supersedes != p.Id)).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            RecordEnvelope first = model.OfType("principal").First(p => model.Latest(p).Id == principal.Id);
            text.Append("  ").Append(EstateModel.BodyString(principal, "name")).Append(" [").Append(string.Join(", ", model.RolesOf(first.Id))).Append("] ")
                .Append(EstateModel.BodyString(principal, "kind")).Append(' ').Append(first.Id).Append('\n');
        }

        text.Append("Work orders:\n");
        foreach (WorkItem item in WorkItems(model))
        {
            text.Append("  ").Append(item.Key.PadRight(12)).Append(item.Status.PadRight(26)).Append(item.Kind.PadRight(10)).Append(item.Title).Append('\n');
        }

        text.Append("Decisions: ").Append(model.Current("decision").Count().ToString(CultureInfo.InvariantCulture))
            .Append(" · Documents: ").Append(model.Current("document-revision").Count().ToString(CultureInfo.InvariantCulture))
            .Append(" · Resources: ").Append(model.Current("resource").Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
        var all = new FindingList();
        foreach (Finding finding in model.Findings)
        {
            all.Add(finding);
        }

        foreach (Finding finding in checks)
        {
            all.Add(finding);
        }

        text.Append("Findings: ").Append(all.Errors.ToString(CultureInfo.InvariantCulture)).Append(" errors, ").Append(all.Warnings.ToString(CultureInfo.InvariantCulture)).Append(" warnings\n");
        foreach (Finding finding in all.Sorted().Where(f => f.Severity != Severity.Info))
        {
            text.Append("  ").Append(finding).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Machine-readable status in canonical pretty JSON (byte-identical across machines for the same estate).</summary>
    public static string RenderJson(EstateModel model, FindingList checks)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(checks);
        RecordEnvelope? estate = model.CurrentEstate;
        var status = new JsonObject
        {
            ["estate"] = estate is null ? null : new JsonObject { ["id"] = estate.Id, ["name"] = EstateModel.BodyString(estate, "name"), ["slug"] = EstateModel.BodyString(estate, "slug") },
            ["records"] = model.Records.Count,
            ["vericairn"] = model.Manifest is null ? null : new JsonObject { ["mode"] = model.Manifest.Mode, ["repository"] = model.Manifest.Repository, ["commit"] = model.Manifest.Commit, ["version"] = model.Manifest.Version },
            ["workOrders"] = new JsonArray(WorkItems(model).Select(i => (JsonNode)new JsonObject { ["key"] = i.Key, ["kind"] = i.Kind, ["title"] = i.Title, ["status"] = i.Status, ["id"] = i.Id }).ToArray()),
            ["findings"] = new JsonArray(model.Findings.Concat(checks).OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal)
                .Select(f => (JsonNode)new JsonObject { ["severity"] = f.Severity.ToString().ToLowerInvariant(), ["code"] = f.Code, ["location"] = f.Location, ["message"] = f.Message }).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(status.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }
}
