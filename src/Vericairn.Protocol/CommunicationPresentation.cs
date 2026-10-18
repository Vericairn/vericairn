// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;

namespace Vericairn.Protocol;

/// <summary>Required attention, kept complete regardless of optional delivery frequency.</summary>
public sealed record CommunicationAttention(string Kind, string Text, string? Record = null, string? RecordType = null)
{
    /// <summary>The complete source hash, never a shortened display hash.</summary>
    public string? Hash { get; init; }
    /// <summary>The source record's subject, at its recorded pin.</summary>
    public AttentionReference? Subject { get; init; }
    /// <summary>The current state, or unknown when its resolution cannot be established.</summary>
    public string? State { get; init; }
    /// <summary>The source's recorded timestamp; not a claim of live external health.</summary>
    public string? AsOf { get; init; }
    /// <summary>The command that opens the source or the subject that needs action.</summary>
    public string? Command { get; init; }
    /// <summary>The exact record to open in an interactive surface.</summary>
    public AttentionReference? Target { get; init; }
}

/// <summary>An exact record reference. A missing hash is explicit, never inferred.</summary>
public sealed record AttentionReference(string Type, string Id, string? Hash);

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
        return Attention(api, recipient, api.Progress);
    }

    // Use the runtime's compact projection when present, or derive it through the same rules for an older runtime.
    // This observation is shared only within this refresh, including its failure; no retained client cache selects attention.
    internal static IReadOnlyList<CommunicationAttention> Attention(IEstateApi api, string? recipient, Func<ProgressResponse> readProgress)
    {
        ArgumentNullException.ThrowIfNull(api);
        ProgressResponse? progress = null;
        Exception? unavailable = null;
        try { progress = readProgress(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Http.HttpRequestException or JsonException or NotSupportedException) { unavailable = exception; }
        var derivation = new AttentionDerivation(api.Records, api.Record, () => progress ?? throw new InvalidOperationException("Progress is unavailable.", unavailable), api.Asks, api.Goals, api.Runs);
        if (progress?.Json.ValueKind == JsonValueKind.Object && progress.Json.TryGetProperty("attention", out JsonElement observed))
        {
            try
            {
                IReadOnlyList<CommunicationAttention>? attention = observed.Deserialize<List<CommunicationAttention>>(ProtocolJson.Options);
                if (attention is not null && attention.All(a => a is not null && !string.IsNullOrWhiteSpace(a.Kind) && !string.IsNullOrWhiteSpace(a.Text) && a.State is not null && a.Command is not null))
                {
                    return recipient is null ? attention : [.. derivation.AskItems(recipient), .. attention.Where(a => a.RecordType != "ask")];
                }
            }
            catch (JsonException) { }
            return [new CommunicationAttention("unknown-check", "Required attention projection unavailable; current state unknown.\n  vericairn status\n") { State = "unknown", Command = "vericairn status" }, .. derivation.Derive(recipient)];
        }
        return derivation.Derive(recipient);
    }

    /// <summary>Reads required attention without blocking the caller, reusing one progress observation for this refresh.</summary>
    public static Task<IReadOnlyList<CommunicationAttention>> AttentionAsync(IEstateApi api, string? recipient = null,
        ProgressResponse? progress = null, bool progressObserved = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        return ClientOperation.RunAsync("Attention", token => Task.Run(() =>
        {
            var bound = new CancellationBoundApi(api, token);
            return Attention(bound, recipient, () => progress ?? (progressObserved ? throw new InvalidOperationException("Progress was unavailable in this observation.") : bound.Progress()));
        }, token), false, cancellationToken: cancellationToken);
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
