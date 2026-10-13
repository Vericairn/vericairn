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
            .Append(" · record format ").Append((model.Manifest?.RecordFormat ?? RecordFormats.Current).ToString(CultureInfo.InvariantCulture)).Append(" · ").Append(model.Records.Count.ToString(CultureInfo.InvariantCulture)).Append(" records\n");
        text.Append("Vericairn: ").Append(model.Manifest?.Describe() ?? "(no manifest)").Append('\n');
        text.Append("Principals:\n");
        foreach (RecordEnvelope principal in model.OfType("principal").Where(p => !model.SupersededBy.ContainsKey(p.Id) || true).Where(p => model.Records.All(r => r.Supersedes != p.Id)).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            RecordEnvelope first = model.OfType("principal").First(p => model.Latest(p).Id == principal.Id);
            text.Append("  ").Append(EstateModel.BodyString(principal, "name")).Append(" [").Append(string.Join(", ", model.RolesOf(first.Id))).Append("] ")
                .Append(EstateModel.BodyString(principal, "kind")).Append(' ').Append(first.Id).Append('\n');
        }

        text.Append("Work orders:\n");
        IReadOnlyList<WorkItem> items = WorkItems(model);
        int keyWidth = items.Count == 0 ? 0 : items.Max(i => i.Key.Length) + 2;
        int statusWidth = items.Count == 0 ? 0 : items.Max(i => i.Status.Length) + 2;
        foreach (WorkItem item in items)
        {
            text.Append("  ").Append(item.Key.PadRight(keyWidth)).Append(item.Status.PadRight(statusWidth)).Append(item.Kind.PadRight(11)).Append(item.Title).Append('\n');
        }

        List<RecordEnvelope> releases = model.Current("release").OrderBy(r => EstateModel.BodyString(r, "version"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        if (releases.Count > 0)
        {
            text.Append("Releases:\n");
            foreach (RecordEnvelope release in releases)
            {
                text.Append("  ").Append(EstateModel.BodyString(release, "version")).Append("  ").Append(EstateModel.BodyString(release, "status")).Append("  ").Append(EstateModel.BodyString(release, "commit")?[..12]).Append("  ").Append(release.Id).Append('\n');
            }
        }

        IReadOnlyList<WorkView.AssignmentItem> assignments = WorkView.AssignmentItems(model);
        if (assignments.Count > 0)
        {
            text.Append("Assignments:\n");
            int assignmentKeyWidth = assignments.Max(a => a.Key.Length) + 2;
            foreach (WorkView.AssignmentItem assignment in assignments)
            {
                text.Append("  ").Append(assignment.Key.PadRight(assignmentKeyWidth)).Append(assignment.AssignmentId).Append("  ").Append(assignment.ContributorName)
                    .Append("  attempts ").Append(assignment.Attempts.ToString(CultureInfo.InvariantCulture)).Append("  ").Append(assignment.Outcome).Append(", ").Append(assignment.State).Append('\n');
            }
        }

        IReadOnlyList<OperationItem> operations = OperationItems(model);
        if (operations.Count > 0)
        {
            text.Append("Operations:\n");
            int identityWidth = operations.Max(o => o.Identity.Length) + 2;
            foreach (OperationItem operation in operations)
            {
                text.Append("  ").Append(operation.Identity.PadRight(identityWidth)).Append(operation.Status).Append("  ").Append(operation.Provider).Append('/').Append(operation.Action).Append("  ").Append(operation.Id).Append('\n');
            }
        }

        text.Append("Decisions: ").Append(model.Current("decision").Count().ToString(CultureInfo.InvariantCulture))
            .Append(" · Documents: ").Append(model.Current("document-revision").Count().ToString(CultureInfo.InvariantCulture))
            .Append(" · Resources: ").Append(model.Current("resource").Count().ToString(CultureInfo.InvariantCulture))
            .Append(" · Verifications: ").Append(model.Current("verification").Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
        var all = new FindingList();
        foreach (Finding finding in model.Findings)
        {
            all.Add(finding);
        }

        foreach (Finding finding in checks)
        {
            all.Add(finding);
        }

        IReadOnlyList<Finding> reconcile = ReconcileTasks(all);
        if (reconcile.Count > 0)
        {
            text.Append("Reconcile:\n");
            foreach (Finding task in reconcile)
            {
                text.Append("  ").Append(task.Location).Append(": ").Append(task.Message).Append('\n');
            }
        }

        text.Append("Findings: ").Append(all.Errors.ToString(CultureInfo.InvariantCulture)).Append(" errors, ").Append(all.Warnings.ToString(CultureInfo.InvariantCulture)).Append(" warnings\n");
        foreach (Finding finding in all.Sorted().Where(f => f.Severity != Severity.Info))
        {
            text.Append("  ").Append(finding).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>One current external operation.</summary>
    /// <param name="Identity">Original identity.</param>
    /// <param name="Provider">Provider id.</param>
    /// <param name="Action">Action.</param>
    /// <param name="Status">Current status.</param>
    /// <param name="Id">Record id of the current revision.</param>
    public sealed record OperationItem(string Identity, string Provider, string Action, string Status, string Id);

    /// <summary>Every current external operation, ordered by identity; a status other than observed-success or canceled needs attention.</summary>
    public static IReadOnlyList<OperationItem> OperationItems(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Current("operation")
            .Select(o => new OperationItem(EstateModel.BodyString(o, "identity") ?? "?", EstateModel.BodyString(o, "provider") ?? "?", EstateModel.BodyString(o, "action") ?? "?", EstateModel.BodyString(o, "status") ?? "?", o.Id))
            .OrderBy(o => o.Identity, StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Codes that mean two clones diverged and a person or an authorized agent must settle it; never merged silently.</summary>
    public static readonly string[] ReconcileCodes = ["reconcile.successors", "work.duplicate-key", "result.duplicate", "estate.multiple", "record.duplicate-id"];

    /// <summary>The findings that are reconcile tasks, sorted.</summary>
    public static IReadOnlyList<Finding> ReconcileTasks(FindingList findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings.Sorted().Where(f => Array.IndexOf(ReconcileCodes, f.Code) >= 0).ToList();
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
            ["releases"] = new JsonArray(model.Current("release").OrderBy(r => EstateModel.BodyString(r, "version"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
                .Select(r => (JsonNode)new JsonObject { ["version"] = EstateModel.BodyString(r, "version"), ["status"] = EstateModel.BodyString(r, "status"), ["commit"] = EstateModel.BodyString(r, "commit"), ["id"] = r.Id }).ToArray()),
            ["assignments"] = new JsonArray(WorkView.AssignmentItems(model).Select(a => (JsonNode)new JsonObject
            {
                ["key"] = a.Key,
                ["id"] = a.AssignmentId,
                ["contributor"] = a.ContributorId,
                ["contributorName"] = a.ContributorName,
                ["attempts"] = a.Attempts,
                ["latestResult"] = a.LatestResultId,
                ["outcome"] = a.Outcome,
                ["state"] = a.State,
            }).ToArray()),
            ["operations"] = new JsonArray(OperationItems(model).Select(o => (JsonNode)new JsonObject { ["identity"] = o.Identity, ["provider"] = o.Provider, ["action"] = o.Action, ["status"] = o.Status, ["id"] = o.Id }).ToArray()),
            ["findings"] = new JsonArray(model.Findings.Concat(checks).OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal)
                .Select(f => (JsonNode)new JsonObject { ["severity"] = f.Severity.ToString().ToLowerInvariant(), ["code"] = f.Code, ["location"] = f.Location, ["message"] = f.Message }).ToArray()),
            ["reconcile"] = new JsonArray(model.Findings.Concat(checks).Where(f => Array.IndexOf(ReconcileCodes, f.Code) >= 0).OrderBy(f => f.Location, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal)
                .Select(f => (JsonNode)new JsonObject { ["code"] = f.Code, ["location"] = f.Location, ["message"] = f.Message }).ToArray()),
        };
        using JsonDocument document = JsonDocument.Parse(status.ToJsonString());
        return CanonicalJson.SerializePretty(document.RootElement);
    }
}
