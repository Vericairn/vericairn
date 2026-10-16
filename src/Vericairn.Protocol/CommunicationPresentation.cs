// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>Required attention, kept complete regardless of optional delivery frequency.</summary>
public sealed record CommunicationAttention(string Kind, string Text, string? Record = null, string? RecordType = null);

/// <summary>Shared deterministic presentation and delivery policy, usable by every surface without changing a source payload.</summary>
public static class CommunicationPresentation
{
    private static readonly string[] MessageKinds = ["status", "decision", "result", "handoff"];

    /// <summary>Attention that is delivered at every frequency, including none.</summary>
    public static IReadOnlyList<string> MandatoryKinds { get; } = ["required-decision", "failed-check", "unknown-check", "blocker", "uncertain-effect", "incident", "exhausted-budget"];

    /// <summary>Frequency controls only optional delivery. An on-demand read and any required attention always pass.</summary>
    public static bool ShouldDeliver(string? frequency, string boundary, IReadOnlyList<CommunicationAttention> attention, bool onDemand = false)
    {
        ArgumentNullException.ThrowIfNull(attention);
        return onDemand || attention.Count > 0 || frequency switch
        {
            "none" => false,
            "completion-and-need" => boundary == "completion",
            "milestones" => boundary is "completion" or "milestone",
            "verbose" => true,
            _ => true, // An unavailable or conflicted preference cannot suppress information.
        };
    }

    /// <summary>Inspect the winning fields, every contribution and retained revision, with explicit freshness and conflicts.</summary>
    public static string Describe(CommunicationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string scope = profile.Assignment is string assignment ? "assignment " + assignment : profile.Recipient is null ? "estate" : "person";
        var text = new StringBuilder($"Communication for {profile.Recipient ?? "unspecified recipient (estate defaults only)"}; scope {scope}.\n");
        text.Append("Freshness: ").Append(profile.ObservedAt ?? "preview; no runtime observation").Append("; revision set ").Append(profile.RevisionHash ?? "preview").Append(".\n");
        if (profile.Unavailable is not null)
        {
            return text.Append("Unavailable: ").Append(profile.Unavailable).Append(" Full content remains visible; no preference was inferred.\n").ToString();
        }

        foreach ((string field, CommunicationValue value) in profile.Fields)
        {
            text.Append(field).Append(": ").Append(value.Value ?? "unresolved conflict").Append(" ← ").Append(value.Source).Append('\n');
            foreach (CommunicationLayer layer in value.Contributions.Where(c => c.Record is not null))
            {
                text.Append("  ").Append(layer.Scope).Append(" record ").Append(layer.Record).Append(" at ").Append(layer.Hash).Append(" by ").Append(layer.By);
                if (layer.Basis is not null) { text.Append("; basis decision ").Append(layer.Basis); }
                text.Append('\n');
            }
        }

        foreach (CommunicationLayer revision in profile.Revisions)
        {
            text.Append(revision.Current ? "Current " : "Historical ").Append(revision.Scope).Append(" revision ").Append(revision.Record).Append(" at ").Append(revision.Hash).Append(" by ").Append(revision.By).Append('\n');
            if (revision.Authority is not null) { text.Append("  authority ").Append(revision.Authority).Append(" at ").Append(revision.AuthorityHash).Append('\n'); }
            if (revision.Basis is not null) { text.Append("  basis decision ").Append(revision.Basis).Append(" at ").Append(revision.BasisHash).Append('\n'); }
        }

        if (profile.Conflicts.Count > 0)
        {
            text.Append("Conflicting revisions: ").AppendJoin(", ", profile.Conflicts).Append(". Reconcile before relying on their values.\n");
        }

        return text.ToString();
    }

    /// <summary>A human status retains every state, finding and source; short omits only optional principal navigation.</summary>
    public static string Status(StatusResponse response, CommunicationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(response);
        string depth = Depth(profile, "status");
        string content = response.Text;
        if (depth == "short")
        {
            int start = content.IndexOf("\nPrincipals:\n", StringComparison.Ordinal);
            int end = content.IndexOf("\nWork orders:\n", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                content = content.Remove(start, end - start);
            }
        }

        var references = new StringBuilder("Sources and next permitted action: inspect before acting; vericairn status --json gives the complete current view.\n");
        foreach (JsonElement order in Items(response.Json, "workOrders"))
        {
            references.Append("  ").Append(Str(order, "key")).Append(' ').Append(Str(order, "status")).Append("; vericairn records show ").Append(Str(order, "id")).Append('\n');
        }

        return Lead(profile, "status", "estate status", "Current recorded state; unknown observations remain unknown.") + content + references;
    }

    /// <summary>A lead at the selected depth followed by the unchanged complete decision, result, handoff or progress text.</summary>
    public static string Protected(CommunicationProfile profile, string kind, string subject, string summary, string original) =>
        Lead(profile, kind, subject, summary) + original;

    /// <summary>The same four examples used by CLI and Workbench settings previews.</summary>
    public static string Preview(CommunicationProfile profile) => string.Join('\n', MessageKinds.Select(kind =>
        Protected(profile, kind, "example subject at its exact revision", "Example: the check is unknown; review is required.",
            "Complete example: do not publish; the check has not run, no acceptance is recorded, and the next permitted action is to inspect the original evidence.\n")));

    /// <summary>Communication instructions in a brief are additional input, separate from its complete original obligations.</summary>
    public static string Brief(string original, CommunicationProfile profile) =>
        Protected(profile, "handoff", "work brief", "The assignment and its original return contract follow in full.", original) +
        "\n## Communication\n\n" + Describe(profile) +
        "Frequency governs optional progress only. Required decisions, failed or unknown checks, blockers, uncertain effects, incidents and exhausted budgets are always reported. Read communication_get again at the next settled boundary.\n";

    /// <summary>Complete required attention from the API. Missing checks or unavailable sources remain visible as unknown.</summary>
    public static IReadOnlyList<CommunicationAttention> Attention(IEstateApi api, string? recipient = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var attention = new List<CommunicationAttention>();
        Read(() =>
        {
            AsksResponse asks = api.Asks(recipient);
            if (Items(asks.Json, "principals").Any(p => p.TryGetProperty("waiting", out JsonElement n) && n.GetInt32() > 0))
            {
                attention.Add(new("required-decision", asks.Text));
            }
        });
        foreach (string type in new[] { "verification", "result", "review", "operation" })
        {
            Read(() =>
            {
                foreach (RecordResponse record in api.Records(type))
                {
                    JsonElement body = record.Envelope.GetProperty("body");
                    string? kind = type switch
                    {
                        "verification" when Str(body, "outcome") == "failed" => "failed-check",
                        "verification" when Str(body, "outcome") != "passed" => "unknown-check",
                        "result" when Str(body, "outcome") == "blocked" => "blocker",
                        "result" when Items(body, "unresolvedEffects").Count > 0 => "uncertain-effect",
                        "result" when Items(body, "checks").Any(c => Str(c, "outcome") == "failed") => "failed-check",
                        "result" when Items(body, "checks").Any(c => Str(c, "outcome") is not ("pass" or "passed")) => "unknown-check",
                        "review" when Str(body, "outcome") == "fail" => "failed-check",
                        "review" when Str(body, "outcome") is not ("pass" or "passed") => "required-decision",
                        "operation" when Str(body, "status") == "known-failure" => "incident",
                        "operation" when Str(body, "status") is not ("observed-success" or "canceled" or "not-started") => "uncertain-effect",
                        _ => null,
                    };
                    if (kind is not null)
                    {
                        attention.Add(new(kind, $"{type} {record.Id} at {record.Hash}\n" + record.Envelope.GetRawText() + "\n", record.Id, type));
                    }
                }
            });
        }

        Read(() =>
        {
            ProgressResponse progress = api.Progress();
            if (progress.Json.TryGetProperty("eligible", out JsonElement eligible))
            {
                foreach (JsonElement held in Items(eligible, "held"))
                {
                    attention.Add(new("blocker", held.GetRawText() + "\n", Str(held, "id"), "work-order"));
                }
            }
        });
        Read(() =>
        {
            foreach (GoalRow goal in api.Goals().Where(g => g.Status is "blocked" or "budget-limited"))
            {
                GoalResponse? details = api.Goal(goal.Id);
                attention.Add(details is null ? new("unknown-check", "The current goal details are unavailable.\n", goal.Id, "goal") :
                    new(goal.Status == "blocked" ? "blocker" : "exhausted-budget", details.Text + details.Json.GetRawText() + "\n", goal.Id, "goal"));
            }
        });

        Read(() =>
        {
            RunsResponse runs = api.Runs();
            foreach (RunRow run in runs.Runs.Where(r => r.Signals is { Count: > 0 } || r.Scope?.Problem is not null || r.Scope?.OutsideCount > 0))
            {
                attention.Add(new("incident", JsonSerializer.Serialize(run, ProtocolJson.Options) + "\n", run.Run, "operation"));
            }

            foreach (LedgerRow ledger in runs.Goals.Where(g => g.Status == "budget-limited" || g.Units.Any(u => u.Remaining <= 0)))
            {
                attention.Add(new("exhausted-budget", JsonSerializer.Serialize(ledger, ProtocolJson.Options) + "\n", ledger.Goal, "goal"));
            }
        });
        return attention;

        void Read(Action read)
        {
            try { read(); }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Http.HttpRequestException or JsonException or NotSupportedException)
            {
                attention.Add(new("unknown-check", "Required attention source unavailable: " + exception.Message + "\n"));
            }
        }
    }

    /// <summary>Render every attention item, independent of any frequency or depth.</summary>
    public static string RenderAttention(IReadOnlyList<CommunicationAttention> attention) => attention.Count == 0 ? string.Empty :
        "\nRequired attention (every frequency):\n" + string.Join('\n', attention.Select(a => a.Kind + ": " + a.Text));

    /// <summary>A conflict or unavailable setting uses full presentation and is labeled by the lead.</summary>
    public static string Depth(CommunicationProfile profile, string kind) =>
        profile.Unavailable is null && profile.Fields.TryGetValue(kind, out CommunicationValue? value) && value.Value is string depth ? depth : "comprehensive";

    private static string Lead(CommunicationProfile profile, string kind, string subject, string summary)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string depth = Depth(profile, kind);
        var text = new StringBuilder($"{kind} ({depth}) · {subject} · recipient {profile.Recipient ?? "unspecified; defaults"}\n{summary}\n");
        if (depth is "standard" or "comprehensive")
        {
            text.Append("Full source content follows. This presentation changes no permission, evidence, check outcome or acceptance.\n");
        }

        if (depth == "comprehensive" || profile.Conflicts.Count > 0 || profile.Unavailable is not null)
        {
            text.Append(Describe(profile));
        }

        return text.Append('\n').ToString();
    }

    private static List<JsonElement> Items(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().ToList() : [];

    private static string? Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
