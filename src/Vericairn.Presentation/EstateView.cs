// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Presentation;

/// <summary>A work order as a surface lists it.</summary>
public sealed record WorkOrderRow(string Key, string Kind, string Title, string Status, string Id)
{
    /// <summary>The label of the status.</summary>
    public string StatusLabel => StateLanguage.Label("work-order", Status);
}

/// <summary>An assignment as a surface lists it.</summary>
public sealed record AssignmentRow(string Key, string Id, string Contributor, string? ContributorName, int Attempts, string? LatestResult, string Outcome, string State)
{
    /// <summary>The label of the state.</summary>
    public string StateLabel => StateLanguage.Label("assignment", State);
}

/// <summary>A release as a surface lists it.</summary>
public sealed record ReleaseRowView(string? Version, string? Status, string? Commit, string Id)
{
    /// <summary>The label of the status.</summary>
    public string StatusLabel => StateLanguage.Label("release", Status);
}

/// <summary>An external operation as a surface lists it.</summary>
public sealed record OperationRow(string Identity, string? Provider, string? Action, string? Status, string Id)
{
    /// <summary>The label of the status.</summary>
    public string StatusLabel => StateLanguage.Label("operation", Status);
}

/// <summary>A finding as a surface lists it.</summary>
public sealed record FindingRow(string Severity, string Code, string Location, string Message);

/// <summary>
/// One attention item of the shared attention model: kind, subject, why it matters, state, freshness and the next action.
/// Opening it goes to the subject; acknowledging it changes nothing but attention state.
/// </summary>
public sealed record AttentionItem(string Kind, string Subject, string Why, string State, string AsOf, string NextAction, string? SubjectType = null, string? SubjectId = null)
{
    /// <summary>The label of the kind.</summary>
    public string KindLabel => StateLanguage.Label("attention", Kind);
}

/// <summary>
/// The estate as the surfaces see it, derived from the status document the runtime renders (the same document on every
/// machine) and nothing else: no client computes a state the runtime did not.
/// </summary>
public sealed class EstateView
{
    private EstateView()
    {
    }

    /// <summary>Estate display name.</summary>
    public string Name { get; private set; } = "(no estate)";

    /// <summary>Estate slug.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Estate record id.</summary>
    public string? EstateId { get; private set; }

    /// <summary>Record count.</summary>
    public int Records { get; private set; }

    /// <summary>The pinned Vericairn: mode and version or commit.</summary>
    public string Pin { get; private set; } = "unknown";

    /// <summary>When the status was observed, as the runtime stamped it or as the client received it.</summary>
    public string AsOf { get; private set; } = string.Empty;

    /// <summary>Work orders.</summary>
    public IReadOnlyList<WorkOrderRow> WorkOrders { get; private set; } = [];

    /// <summary>Assignments.</summary>
    public IReadOnlyList<AssignmentRow> Assignments { get; private set; } = [];

    /// <summary>Releases.</summary>
    public IReadOnlyList<ReleaseRowView> Releases { get; private set; } = [];

    /// <summary>External operations.</summary>
    public IReadOnlyList<OperationRow> Operations { get; private set; } = [];

    /// <summary>Findings.</summary>
    public IReadOnlyList<FindingRow> Findings { get; private set; } = [];

    /// <summary>Reconcile tasks.</summary>
    public IReadOnlyList<FindingRow> Reconcile { get; private set; } = [];

    /// <summary>Attention items derived from the above, most urgent first.</summary>
    public IReadOnlyList<AttentionItem> Attention { get; private set; } = [];

    /// <summary>Builds the view from the runtime's status response.</summary>
    public static EstateView From(StatusResponse status, string asOf)
    {
        ArgumentNullException.ThrowIfNull(status);
        return From(status.Json, asOf);
    }

    /// <summary>Builds the view from the canonical status document.</summary>
    public static EstateView From(JsonElement document, string asOf)
    {
        var view = new EstateView { AsOf = asOf };
        if (document.ValueKind != JsonValueKind.Object)
        {
            return view;
        }

        if (document.TryGetProperty("estate", out JsonElement estate) && estate.ValueKind == JsonValueKind.Object)
        {
            view.Name = Str(estate, "name") ?? view.Name;
            view.Slug = Str(estate, "slug") ?? string.Empty;
            view.EstateId = Str(estate, "id");
        }

        if (document.TryGetProperty("records", out JsonElement records) && records.ValueKind == JsonValueKind.Number)
        {
            view.Records = records.GetInt32();
        }

        if (document.TryGetProperty("vericairn", out JsonElement pin) && pin.ValueKind == JsonValueKind.Object)
        {
            view.Pin = $"{Str(pin, "mode")} {Str(pin, "version") ?? Str(pin, "commit")}".Trim();
        }

        view.WorkOrders = Rows(document, "workOrders", e => new WorkOrderRow(Str(e, "key") ?? "?", Str(e, "kind") ?? "?", Str(e, "title") ?? "?", Str(e, "status") ?? "?", Str(e, "id") ?? string.Empty));
        view.Assignments = Rows(document, "assignments", e => new AssignmentRow(Str(e, "key") ?? "?", Str(e, "id") ?? string.Empty, Str(e, "contributor") ?? "?", Str(e, "contributorName"), Int(e, "attempts"), Str(e, "latestResult"), Str(e, "outcome") ?? "none", Str(e, "state") ?? "?"));
        view.Releases = Rows(document, "releases", e => new ReleaseRowView(Str(e, "version"), Str(e, "status"), Str(e, "commit"), Str(e, "id") ?? string.Empty));
        view.Operations = Rows(document, "operations", e => new OperationRow(Str(e, "identity") ?? "?", Str(e, "provider"), Str(e, "action"), Str(e, "status"), Str(e, "id") ?? string.Empty));
        view.Findings = Rows(document, "findings", e => new FindingRow(Str(e, "severity") ?? "info", Str(e, "code") ?? string.Empty, Str(e, "location") ?? string.Empty, Str(e, "message") ?? string.Empty));
        view.Reconcile = Rows(document, "reconcile", e => new FindingRow("error", Str(e, "code") ?? string.Empty, Str(e, "location") ?? string.Empty, Str(e, "message") ?? string.Empty));
        view.Attention = DeriveAttention(view);
        return view;
    }

    private static List<AttentionItem> DeriveAttention(EstateView view)
    {
        var items = new List<AttentionItem>();
        foreach (WorkOrderRow order in view.WorkOrders.Where(o => o.Status == "submitted"))
        {
            items.Add(new AttentionItem("decision needed", $"{order.Key} {order.Title}", order.Kind == "milestone" ? "A milestone is submitted; its acceptance is the operator's." : "Submitted; acceptance under recorded authority is pending.", order.StatusLabel, view.AsOf, "Review the result and accept or refuse the exact revision.", "work-order", order.Id));
        }

        foreach (WorkOrderRow order in view.WorkOrders.Where(o => o.Status == "accepted (earlier revision)"))
        {
            items.Add(new AttentionItem("decision needed", $"{order.Key} {order.Title}", "The work order was revised after its acceptance; the current revision has no acceptance.", order.StatusLabel, view.AsOf, "Reconsider the current revision.", "work-order", order.Id));
        }

        foreach (AssignmentRow assignment in view.Assignments.Where(a => a.State == "awaiting review" && !view.WorkOrders.Any(o => o.Key == a.Key && o.Status == "accepted")))
        {
            items.Add(new AttentionItem("decision needed", $"{assignment.Key}: result {Short(assignment.LatestResult)}", $"{assignment.ContributorName ?? assignment.Contributor} submitted a result ({assignment.Outcome}); it is a claim to inspect.", assignment.StateLabel, view.AsOf, "Review the exact result; accept it or request rework.", "result", assignment.LatestResult));
        }

        foreach (ReleaseRowView release in view.Releases.Where(r => r.Status == "candidate"))
        {
            items.Add(new AttentionItem("decision needed", $"release {release.Version}", "A release candidate awaits the operator's acceptance of its exact hash.", release.StatusLabel, view.AsOf, "Accept the candidate from the recorded evidence, then publish by hand.", "release", release.Id));
        }

        foreach (FindingRow finding in view.Findings.Where(f => f.Severity == "error"))
        {
            items.Add(new AttentionItem("failed check", finding.Location, finding.Message, finding.Code, view.AsOf, "Fix the cause; the estate check names it.", null, null));
        }

        foreach (FindingRow task in view.Reconcile)
        {
            items.Add(new AttentionItem("incident", task.Location, task.Message, task.Code, view.AsOf, "Settle the divergence with a reconciliation record.", null, null));
        }

        foreach (FindingRow finding in view.Findings.Where(f => f.Severity == "warning"))
        {
            items.Add(new AttentionItem("information", finding.Location, finding.Message, finding.Code, view.AsOf, "Consider the warning; it blocks nothing.", null, null));
        }

        return items;
    }

    private static List<T> Rows<T>(JsonElement document, string name, Func<JsonElement, T> map) =>
        document.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Select(map).ToList() : [];

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static string Short(string? id) => id is null ? "?" : id.Length > 12 ? id[..12] : id;
}
