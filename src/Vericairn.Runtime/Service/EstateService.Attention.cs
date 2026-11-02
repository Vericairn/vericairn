// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public AttentionResponse AttentionGet(string? principal = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            principal ??= RequestPrincipal(model) ?? (model.CurrentEstate is { } charter ? EstateModel.BodyString(charter, "operator") : null);
            if (AttentionPrincipalRefusal(model, principal) is { } problem) throw new UnauthorizedAccessException(problem.Message);
            return AttentionLocked(model, principal!);
        }
    }

    /// <inheritdoc />
    public AttentionAcknowledgeResponse AttentionAcknowledge(AttentionAcknowledgeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (session.IsReadOnly) return new(null, new Problem("denied", "This session is read-only; acknowledgment was not stored.", []));
            EstateModel model = session.Load();
            if (AttentionPrincipalRefusal(model, request.By) is { } problem) return new(null, problem);
            AttentionNotice? item = AttentionLocked(model, request.By).Items.FirstOrDefault(i =>
                i.Kind == request.Kind && i.SubjectType == request.SubjectType && i.SubjectId == request.SubjectId);
            if (item is null || item.Revision != request.Expected)
                return new(null, new Problem("conflict", "This notice changed or is no longer present. Refresh attention before acknowledging it.", []));

            // No authoring path or signing key is involved. A repeated tap returns the first acknowledgment.
            Dictionary<string, AttentionAcknowledgment> kept = ReadAcknowledgments(request.By);
            string at = item.AcknowledgedAt ?? RecordParser.FormatTimestamp(WorkerOptions.Clock.GetUtcNow());
            kept[item.Key] = new(item.Revision, at);
            session.Live.SetMeta("attention:" + request.By, JsonSerializer.Serialize(kept, ProtocolJson.Options));
            return new(item with { AcknowledgedAt = at });
        }
    }

    private string? RequestPrincipal(EstateModel model) => requestDevice.Value is { Closed: false } request ? Standing(model, request.Device)?.Principal : null;

    private Problem? AttentionPrincipalRefusal(EstateModel model, string? principal)
    {
        if (RequestDeviceRefusal(model) is { } denied) return denied;
        if (principal is null || model.CurrentPrincipal(principal) is null)
            return new("denied", "Attention requires a known principal.", []);
        return RequestPrincipal(model) is string requester && requester != principal
            ? new("denied", "A device can acknowledge and read attention only for its own principal.", []) : null;
    }

    private AttentionResponse AttentionLocked(EstateModel model, string principal)
    {
        string at = RecordParser.FormatTimestamp(WorkerOptions.Clock.GetUtcNow());
        FindingList checks = RepositoryChecks(model);
        ProgressResponse progress = ProgressCore(model, checks);
        var notices = new List<AttentionNotice>();
        using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, checks));
        JsonElement document = status.RootElement;

        void Add(string kind, string subject, string why, string state, string next, string type, string id, string? hash = null, string? sourceAt = null, AttentionReference? target = null, string? basis = null)
        {
            if (model.ById.TryGetValue(id, out RecordEnvelope? record)) { hash ??= record.ComputedHash; sourceAt ??= record.At; }
            string revision = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind, subject, why = basis ?? why, state, next, type, id, hash }, ProtocolJson.Options)));
            notices.Add(new(kind, subject, why, state, sourceAt ?? at, next, type, id, revision) { Target = target });
        }

        // Acceptance notices are directed to the operator. Shared checks remain visible to every estate reader.
        if (model.HasRole(principal, Roles.Operator))
        {
            foreach (JsonElement order in AttentionRows(document, "workOrders").Where(o => AttentionString(o, "status") is "submitted" or "accepted (earlier revision)"))
                Add("decision needed", AttentionString(order, "key") + " " + AttentionString(order, "title"),
                    "The current work revision awaits acceptance under recorded authority.", AttentionString(order, "status"),
                    "Review the exact work revision from Workbench or the command line.", "work-order", AttentionString(order, "id"));
            foreach (JsonElement assignment in AttentionRows(document, "assignments").Where(a => AttentionString(a, "state") == "awaiting review"
                && !AttentionRows(document, "workOrders").Any(o => AttentionString(o, "key") == AttentionString(a, "key") && AttentionString(o, "status") == "accepted")))
                Add("decision needed", AttentionString(assignment, "key") + ": result " + AttentionString(assignment, "latestResult"),
                    "The contributor submitted a result (" + AttentionString(assignment, "outcome") + "); it is a claim to inspect.", "awaiting review",
                    "Review the exact result from Workbench or the command line.", "result", AttentionString(assignment, "latestResult"));
            foreach (JsonElement release in AttentionRows(document, "releases").Where(r => AttentionString(r, "status") == "candidate"))
                Add("decision needed", "release " + AttentionString(release, "version"), "A release candidate awaits acceptance of its exact hash.", "candidate",
                    "Inspect the release in Workbench or the command line.", "release", AttentionString(release, "id"));
        }
        foreach (JsonElement finding in AttentionRows(document, "findings").Where(f => AttentionString(f, "severity") is "error" or "warning"))
            Add(AttentionString(finding, "severity") == "error" ? "failed check" : "information", AttentionString(finding, "location"), AttentionString(finding, "message"), AttentionString(finding, "code"),
                "Inspect the estate check in Workbench or run vericairn check.", "finding", AttentionString(finding, "location") + ":" + AttentionString(finding, "code"));
        foreach (JsonElement finding in AttentionRows(document, "reconcile"))
            Add("incident", AttentionString(finding, "location"), AttentionString(finding, "message"), AttentionString(finding, "code"),
                "Inspect reconciliation in Workbench or the command line.", "reconciliation", AttentionString(finding, "location") + ":" + AttentionString(finding, "code"));
        RunsResponse? observedRuns = null;
        RunsResponse ReadRuns() => observedRuns ??= Runs(model);
        foreach (CommunicationAttention item in RequiredAttention(model, () => progress, recipient: principal, runs: ReadRuns))
        {
            // Elapsed quiet time advances on every read; only a changed claim, request or worker observation renews this notice.
            RequestedProgress? reporting = item.Kind == "progress-overdue" ? AttentionRows(progress.Json, "requestedProgress")
                .Select(p => p.Deserialize<RequestedProgress>(ProtocolJson.Options)).FirstOrDefault(p => p is not null && (p.Attempt ?? p.Run ?? p.Assignment) == item.Record) : null;
            string? basis = reporting is null ? null : JsonSerializer.Serialize(new
            {
                reporting.Claim, reporting.Request, reporting.Source, reporting.Freshness, reporting.Closed, reporting.WorkerObserved, reporting.Liveness,
            }, ProtocolJson.Options);
            if (item is { Kind: "incident", RecordType: "operation", Record: { } operation } && AgentRuns.Find(model, operation) is { } run &&
                ReadRuns().Runs.FirstOrDefault(r => r.Run == run.Id) is { } observation)
                basis = JsonSerializer.Serialize(new { item.Text, observation.Signals, observation.Scope, observation.Stop }, ProtocolJson.Options);
            Add(item.Kind, item.Record ?? "Required attention", item.Text, item.State ?? "unknown", item.Command ?? "vericairn status",
                item.RecordType ?? "attention", item.Record ?? item.Command ?? item.Kind, item.Hash, item.AsOf, item.Target, basis);
        }

        notices.AddRange(PolicyNotices(progress, principal));
        Dictionary<string, AttentionAcknowledgment> kept = ReadAcknowledgments(principal);
        // Several findings can concern the same kind and subject. Keep every reason in that notice.
        var current = notices.GroupBy(n => n.Key, StringComparer.Ordinal).Select(group =>
        {
            AttentionNotice first = group.First();
            string[] revisions = group.Select(n => n.Revision).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return revisions.Length == 1 ? first : first with
            {
                Why = string.Join("\n", group.Select(n => n.Why).Distinct(StringComparer.Ordinal)),
                Revision = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(revisions, ProtocolJson.Options))),
            };
        }).ToDictionary(n => n.Key, StringComparer.Ordinal);
        bool changed = false;
        foreach (string key in kept.Keys.ToArray())
            if (!current.TryGetValue(key, out AttentionNotice? item) || kept[key] is null || item.Revision != kept[key].Revision) { kept.Remove(key); changed = true; }
        // Expire obsolete presentation state so an item that disappears and returns is never silently acknowledged again.
        if (changed && !session.IsReadOnly) session.Live.SetMeta("attention:" + principal, JsonSerializer.Serialize(kept, ProtocolJson.Options));
        return new(principal, at, current.Values.Select(i => kept.TryGetValue(i.Key, out AttentionAcknowledgment? ack) ? i with { AcknowledgedAt = ack.At } : i)
            .OrderBy(i => AttentionPriority(i.Kind)).ThenBy(i => i.AsOf, StringComparer.Ordinal).ThenBy(i => i.Key, StringComparer.Ordinal).ToList());
    }

    private Dictionary<string, AttentionAcknowledgment> ReadAcknowledgments(string principal)
    {
        try { return session.Live.Meta("attention:" + principal) is string json ? JsonSerializer.Deserialize<Dictionary<string, AttentionAcknowledgment>>(json, ProtocolJson.Options) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private static int AttentionPriority(string kind) => kind switch
    {
        "uncertain-effect" or "uncertain effect" or "incident" => 0,
        "failed-check" or "failed check" or "unknown-check" or "exhausted-budget" => 1,
        "blocker" or "blocked" or "progress-overdue" => 2,
        "decision needed" or "required-decision" or "operator-request" => 3,
        _ => 4,
    };

    private static JsonElement.ArrayEnumerator AttentionRows(JsonElement json, string name) => json.TryGetProperty(name, out JsonElement items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : default;
    private static string AttentionString(JsonElement json, string name) => json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "unknown";
    private sealed record AttentionAcknowledgment(string Revision, string At);
}
