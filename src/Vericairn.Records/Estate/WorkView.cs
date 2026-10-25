// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>Derived views over work orders, assignments, attempts and results: the brief a contributor takes, and what an observer sees.</summary>
public static class WorkView
{
    /// <summary>One current assignment with its derived state.</summary>
    /// <param name="Key">Work-order key.</param>
    /// <param name="AssignmentId">Assignment record id.</param>
    /// <param name="ContributorId">Contributor principal id.</param>
    /// <param name="ContributorName">Contributor display name.</param>
    /// <param name="Attempts">Number of current attempts.</param>
    /// <param name="LatestResultId">The current result of the latest attempt, if any.</param>
    /// <param name="Outcome">That result's outcome, or "none".</param>
    /// <param name="State">no attempt, in progress, awaiting review, accepted, accepted (earlier revision).</param>
    public sealed record AssignmentItem(string Key, string AssignmentId, string ContributorId, string ContributorName, int Attempts, string? LatestResultId, string Outcome, string State);

    /// <summary>Finds the current work order with a key, or the latest revision of the work order with an id.</summary>
    public static RecordEnvelope? FindOrder(EstateModel model, string keyOrId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyOrId);
        if (model.ById.TryGetValue(keyOrId, out RecordEnvelope? record) && record.Type == "work-order")
        {
            return model.Latest(record);
        }

        return model.Current("work-order").FirstOrDefault(o => EstateModel.BodyString(o, "key") == keyOrId);
    }

    /// <summary>The work order an assignment delegates (any revision of it, as pinned).</summary>
    public static RecordEnvelope? OrderOfAssignment(EstateModel model, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        return assignment.Subject is not null && model.ById.TryGetValue(assignment.Subject.Id, out RecordEnvelope? order) && order.Type == "work-order" ? order : null;
    }

    /// <summary>The assignment an attempt belongs to.</summary>
    public static RecordEnvelope? AssignmentOfAttempt(EstateModel model, RecordEnvelope attempt)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(attempt);
        return attempt.Subject is not null && model.ById.TryGetValue(attempt.Subject.Id, out RecordEnvelope? assignment) && assignment.Type == "assignment" ? assignment : null;
    }

    /// <summary>The attempt a result closes.</summary>
    public static RecordEnvelope? AttemptOfResult(EstateModel model, RecordEnvelope result)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(result);
        return result.Subject is not null && model.ById.TryGetValue(result.Subject.Id, out RecordEnvelope? attempt) && attempt.Type == "attempt" ? attempt : null;
    }

    /// <summary>The work order a result is toward, following result, attempt, assignment, work order.</summary>
    public static RecordEnvelope? OrderOfResult(EstateModel model, RecordEnvelope result)
    {
        RecordEnvelope? attempt = AttemptOfResult(model, result);
        RecordEnvelope? assignment = attempt is null ? null : AssignmentOfAttempt(model, attempt);
        return assignment is null ? null : OrderOfAssignment(model, assignment);
    }

    /// <summary>Current attempts of an assignment, oldest first.</summary>
    public static IReadOnlyList<RecordEnvelope> AttemptsOf(EstateModel model, RecordEnvelope assignment)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        return model.About(assignment.Id, "attempt").Where(model.IsCurrent).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The current result of an attempt, if any.</summary>
    public static RecordEnvelope? ResultOf(EstateModel model, RecordEnvelope attempt)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(attempt);
        return model.About(attempt.Id, "result").Where(model.IsCurrent).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
    }

    /// <summary>The acceptance state of a result: accepted, accepted (earlier revision), or awaiting review.</summary>
    public static string ResultState(EstateModel model, RecordEnvelope result)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(result);
        IReadOnlyList<string> chain = model.Chain(result);
        RecordEnvelope? acceptance = chain.SelectMany(id => model.About(id, "acceptance")).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).LastOrDefault();
        if (acceptance is null)
        {
            return "awaiting review";
        }

        return acceptance.Subject?.Id == result.Id && acceptance.Subject.Hash == result.ComputedHash ? "accepted" : "accepted (earlier revision)";
    }

    /// <summary>Every current assignment with its derived state, ordered by work-order key then assignment id.</summary>
    public static IReadOnlyList<AssignmentItem> AssignmentItems(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var items = new List<AssignmentItem>();
        foreach (RecordEnvelope assignment in model.Current("assignment"))
        {
            RecordEnvelope? order = OrderOfAssignment(model, assignment);
            string key = order is null ? "?" : EstateModel.BodyString(model.Latest(order), "key") ?? "?";
            string contributor = EstateModel.BodyString(assignment, "contributor") ?? "?";
            IReadOnlyList<RecordEnvelope> attempts = AttemptsOf(model, assignment);
            RecordEnvelope? latestAttempt = attempts.Count == 0 ? null : attempts[attempts.Count - 1];
            RecordEnvelope? result = latestAttempt is null ? null : ResultOf(model, latestAttempt);
            string state = latestAttempt is null ? "no attempt" : result is null ? "in progress" : ResultState(model, result);
            items.Add(new AssignmentItem(key, assignment.Id, contributor, PrincipalName(model, contributor), attempts.Count, result?.Id, result is null ? "none" : EstateModel.BodyString(result, "outcome") ?? "?", state));
        }

        return items.OrderBy(i => i.Key, StringComparer.Ordinal).ThenBy(i => i.AssignmentId, StringComparer.Ordinal).ToList();
    }

    /// <summary>The brief for a work order as Markdown: everything a contributor needs, and nothing from outside the records.</summary>
    public static string RenderBrief(EstateModel model, RecordEnvelope order) => RenderBrief(model, order, includeDocumentCatalogue: true);

    /// <summary>The same obligations with a catalogue listing or a link to it; selection belongs to the context package.</summary>
    public static string RenderBrief(EstateModel model, RecordEnvelope order, bool includeDocumentCatalogue)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        var text = new StringBuilder();
        string key = EstateModel.BodyString(order, "key") ?? "?";
        text.Append("# Brief: ").Append(key).Append(' ').Append(EstateModel.BodyString(order, "title")).Append("\n\n");
        RecordEnvelope? estate = model.CurrentEstate;
        text.Append("Estate: ").Append(estate is null ? "(no charter)" : $"{EstateModel.BodyString(estate, "name")} ({EstateModel.BodyString(estate, "slug")})")
            .Append(" · Vericairn: ").Append(model.Manifest?.Describe() ?? "(no manifest)")
            .Append(" · work order ").Append(order.Id).Append(" at ").Append(order.ComputedHash).Append(" (").Append(EstateModel.BodyString(order, "kind")).Append(")\n\n");
        text.Append("## Intent\n\n").Append(EstateModel.BodyString(order, "intent")).Append("\n\n");
        AppendList(text, "## Scope", Strings(order.Body, "scope"));
        text.Append("## Exit question\n\n").Append(EstateModel.BodyString(order, "exitQuestion")).Append("\n\n");
        AppendList(text, "## Deliverables", Strings(order.Body, "deliverables"));
        text.Append("## Inputs\n\n");
        bool anyInput = false;
        if (order.Body.TryGetProperty("inputs", out JsonElement inputs) && inputs.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement input in inputs.EnumerateArray())
            {
                string? id = input.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
                if (id is not null && model.ById.TryGetValue(id, out RecordEnvelope? record))
                {
                    anyInput = true;
                    text.Append("- ").Append(Describe(model, record)).Append('\n');
                }
            }
        }

        text.Append(anyInput ? "\n" : "(none)\n\n");
        text.Append("## Decisions affecting this work\n\n");
        bool anyDecision = false;
        foreach (RecordEnvelope decision in model.Current("decision").Where(d => Affects(d, model.Chain(order))).OrderBy(d => EstateModel.BodyString(d, "decidedAt"), StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            anyDecision = true;
            text.Append("- ").Append(EstateModel.BodyString(decision, "title")).Append(": ").Append(EstateModel.BodyString(decision, "summary")).Append(" (").Append(decision.Id).Append(")\n");
        }

        text.Append(anyDecision ? "\n" : "(none)\n\n");
        foreach (RecordEnvelope assignment in model.Current("assignment").Where(a => a.Subject is not null && model.Chain(order).Contains(a.Subject.Id)).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            AppendAssignment(model, text, order, assignment);
        }

        if (includeDocumentCatalogue)
        {
            text.Append("## Design documents (current revisions)\n\n");
            foreach (RecordEnvelope revision in model.Current("document-revision").OrderBy(r => EstateModel.BodyString(r, "path"), StringComparer.Ordinal))
            {
                text.Append("- ").Append(EstateModel.BodyString(revision, "path")).Append(" (revision ").Append(revision.Body.TryGetProperty("revision", out JsonElement rev) ? rev.GetRawText() : "?").Append(", ").Append(EstateModel.BodyString(revision, "hash")).Append(")\n");
            }
        }
        else
        {
            text.Append("## Document selection\n\nThe context manifest names this step's selected originals. The rest of the document catalogue is outside the selection; list it with `vericairn context catalogue`.\n");
        }

        text.Append('\n');
        text.Append("## Rules\n\n");
        text.Append("- Preparation is separate from submission; an attempt is recorded before work starts and a result when it stops.\n");
        text.Append("- A result is a claim to inspect. Acceptance is a separate record pinned to the exact result hash; nothing is accepted because a contributor says it is done.\n");
        text.Append("- Work beyond the permitted changes becomes an amendment request, never an implicit expansion.\n");
        text.Append("- An unresolved external effect is reconciled under its original identity before any retry.\n");
        text.Append("- Nothing from outside these records (a conversation, a provider memory) is the custody of this work; what a successor needs is in the records.\n");
        return text.ToString();
    }

    /// <summary>The brief as JSON, for machine consumption.</summary>
    public static string RenderBriefJson(EstateModel model, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        IReadOnlyList<string> chain = model.Chain(order);
        var assignments = new JsonArray();
        foreach (RecordEnvelope assignment in model.Current("assignment").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id)).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var attempts = new JsonArray();
            foreach (RecordEnvelope attempt in AttemptsOf(model, assignment))
            {
                RecordEnvelope? result = ResultOf(model, attempt);
                attempts.Add(new JsonObject
                {
                    ["id"] = attempt.Id,
                    ["hash"] = attempt.ComputedHash,
                    ["by"] = attempt.By,
                    ["startedAt"] = EstateModel.BodyString(attempt, "startedAt"),
                    ["workspace"] = EstateModel.BodyString(attempt, "workspace"),
                    ["note"] = EstateModel.BodyString(attempt, "note"),
                    ["result"] = result is null ? null : new JsonObject { ["id"] = result.Id, ["hash"] = result.ComputedHash, ["state"] = ResultState(model, result), ["body"] = JsonNode.Parse(result.Body.GetRawText()) },
                });
            }

            assignments.Add(new JsonObject
            {
                ["id"] = assignment.Id,
                ["hash"] = assignment.ComputedHash,
                ["workOrderHash"] = assignment.Subject?.Hash,
                ["contributor"] = EstateModel.BodyString(assignment, "contributor"),
                ["brief"] = assignment.Body.TryGetProperty("brief", out JsonElement brief) ? JsonNode.Parse(brief.GetRawText()) : null,
                ["attempts"] = attempts,
            });
        }

        var decisions = new JsonArray(model.Current("decision").Where(d => Affects(d, chain)).OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => (JsonNode)new JsonObject { ["id"] = d.Id, ["title"] = EstateModel.BodyString(d, "title"), ["summary"] = EstateModel.BodyString(d, "summary") }).ToArray());
        var documents = new JsonArray(model.Current("document-revision").OrderBy(r => EstateModel.BodyString(r, "path"), StringComparer.Ordinal)
            .Select(r => (JsonNode)new JsonObject { ["path"] = EstateModel.BodyString(r, "path"), ["hash"] = EstateModel.BodyString(r, "hash") }).ToArray());
        var json = new JsonObject
        {
            ["estate"] = model.CurrentEstate?.Id,
            ["vericairn"] = model.Manifest?.Describe(),
            ["workOrder"] = new JsonObject { ["id"] = order.Id, ["hash"] = order.ComputedHash, ["body"] = JsonNode.Parse(order.Body.GetRawText()) },
            ["decisions"] = decisions,
            ["assignments"] = assignments,
            ["documents"] = documents,
        };
        using JsonDocument document = JsonDocument.Parse(json.ToJsonString());
        return Canonical.CanonicalJson.SerializePretty(document.RootElement);
    }

    /// <summary>What an observer sees of a work order: assignments, attempts, results, amendments, reviews and acceptances.</summary>
    public static string RenderObserve(EstateModel model, RecordEnvelope order)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(order);
        var text = new StringBuilder();
        IReadOnlyList<string> chain = model.Chain(order);
        string status = EstateStatus.WorkItems(model).FirstOrDefault(i => i.Id == order.Id)?.Status ?? "?";
        text.Append(EstateModel.BodyString(order, "key")).Append("  ").Append(status).Append("  ").Append(EstateModel.BodyString(order, "kind")).Append("  ").Append(EstateModel.BodyString(order, "title")).Append('\n');
        text.Append("  record ").Append(order.Id).Append(" at ").Append(order.ComputedHash).Append(" (revision ").Append(chain.Count.ToString(CultureInfo.InvariantCulture)).Append(")\n");
        foreach (RecordEnvelope assignment in model.Current("assignment").Where(a => a.Subject is not null && chain.Contains(a.Subject.Id)).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            string contributor = EstateModel.BodyString(assignment, "contributor") ?? "?";
            text.Append("  assignment ").Append(assignment.Id).Append(" to ").Append(PrincipalName(model, contributor)).Append(" (").Append(contributor).Append(')')
                .Append(WorkOrderRevisions.IsCurrentSubject(model, assignment.Subject, order) ? string.Empty : "  [pinned to an earlier work-order revision]").Append('\n');
            foreach (RecordEnvelope amendment in model.About(assignment.Id, "amendment").Where(model.IsCurrent).OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                text.Append("    amendment ").Append(amendment.Id).Append(" (").Append(EstateModel.BodyString(amendment, "kind")).Append("): ").Append(EstateModel.BodyString(amendment, "rationale")).Append('\n');
            }

            foreach (RecordEnvelope attempt in AttemptsOf(model, assignment))
            {
                text.Append("    attempt ").Append(attempt.Id).Append(" started ").Append(EstateModel.BodyString(attempt, "startedAt")).Append(" in ").Append(EstateModel.BodyString(attempt, "workspace")).Append(": ").Append(EstateModel.BodyString(attempt, "note")).Append('\n');
                RecordEnvelope? result = ResultOf(model, attempt);
                if (result is null)
                {
                    text.Append("      no result yet\n");
                    continue;
                }

                text.Append("      result ").Append(result.Id).Append(' ').Append(EstateModel.BodyString(result, "outcome")).Append(", ").Append(ResultState(model, result)).Append(": ").Append(EstateModel.BodyString(result, "summary")).Append('\n');
                AppendResultDetails(text, result, "        ");
                foreach (RecordEnvelope review in model.About(result.Id, "review").Where(model.IsCurrent).OrderBy(r => r.Id, StringComparer.Ordinal))
                {
                    text.Append("        review ").Append(review.Id).Append(" (").Append(EstateModel.BodyString(review, "independence")).Append(", ").Append(EstateModel.BodyString(review, "outcome")).Append(") by ").Append(PrincipalName(model, review.By)).Append('\n');
                    text.Append("          ").Append(EstateModel.BodyString(review, "summary")).Append('\n');
                    if (review.Body.TryGetProperty("findings", out JsonElement findings) && findings.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement finding in findings.EnumerateArray())
                        {
                            text.Append("          ").Append(Field(finding, "severity") ?? "unknown").Append(": ").Append(Field(finding, "summary"));
                            if (Field(finding, "location") is string location)
                            {
                                text.Append(" (").Append(location).Append(')');
                            }

                            text.Append('\n');
                        }
                    }
                }

                foreach (RecordEnvelope acceptance in model.About(result.Id, "acceptance").Where(model.IsCurrent).OrderBy(a => a.Id, StringComparer.Ordinal))
                {
                    text.Append("        acceptance ").Append(acceptance.Id).Append(" by ").Append(PrincipalName(model, acceptance.By)).Append(acceptance.Subject?.Hash == result.ComputedHash ? string.Empty : " [earlier revision]").Append(": ").Append(EstateModel.BodyString(acceptance, "statement")).Append('\n');
                }
            }
        }

        foreach (RecordEnvelope review in chain.SelectMany(id => model.About(id, "review")).Where(model.IsCurrent).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            text.Append("  review ").Append(review.Id).Append(" (").Append(EstateModel.BodyString(review, "independence")).Append(", ").Append(EstateModel.BodyString(review, "outcome")).Append(") by ").Append(PrincipalName(model, review.By)).Append('\n');
        }

        foreach (RecordEnvelope acceptance in chain.SelectMany(id => model.About(id, "acceptance")).Where(model.IsCurrent).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            text.Append("  acceptance ").Append(acceptance.Id).Append(" by ").Append(PrincipalName(model, acceptance.By)).Append(WorkOrderRevisions.IsCurrentSubject(model, acceptance.Subject, order) ? string.Empty : " [earlier revision]").Append(": ").Append(EstateModel.BodyString(acceptance, "statement")).Append('\n');
        }

        return text.ToString();
    }

    private static void AppendAssignment(EstateModel model, StringBuilder text, RecordEnvelope order, RecordEnvelope assignment)
    {
        string contributor = EstateModel.BodyString(assignment, "contributor") ?? "?";
        text.Append("## Assignment ").Append(assignment.Id).Append(" to ").Append(PrincipalName(model, contributor)).Append(" (").Append(contributor).Append(")\n\n");
        text.Append("Pinned to work-order revision ").Append(assignment.Subject?.Hash).Append(WorkOrderRevisions.IsCurrentSubject(model, assignment.Subject, order) ? " (current)" : " (an earlier revision; review before continuing)").Append("; assignment hash ").Append(assignment.ComputedHash).Append(".\n\n");
        if (assignment.Body.TryGetProperty("brief", out JsonElement brief) && brief.ValueKind == JsonValueKind.Object)
        {
            if (brief.TryGetProperty("reviewBrief", out JsonElement reviewBrief) && reviewBrief.ValueKind == JsonValueKind.String)
                text.Append(reviewBrief.GetString()).Append('\n');
            text.Append("### Outcome\n\n").Append(brief.TryGetProperty("outcome", out JsonElement outcome) ? outcome.GetString() : string.Empty).Append("\n\n");
            text.Append("### Sources\n\n");
            bool anySource = false;
            if (brief.TryGetProperty("sources", out JsonElement sources) && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement source in sources.EnumerateArray())
                {
                    string? id = source.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
                    if (id is not null && model.ById.TryGetValue(id, out RecordEnvelope? record))
                    {
                        anySource = true;
                        text.Append("- ").Append(Describe(model, record)).Append('\n');
                    }
                }
            }

            text.Append(anySource ? "\n" : "(none)\n\n");
            AppendList(text, "### Permitted changes", Strings(brief, "permittedChanges"));
            AppendList(text, "### Prohibited effects", Strings(brief, "prohibitedEffects"));
            AppendList(text, "### Workspace", Strings(brief, "workspace"));
            AppendList(text, "### Limits", Strings(brief, "limits"));
            AppendList(text, "### Expected outputs", Strings(brief, "expectedOutputs"));
            AppendList(text, "### Review criteria", Strings(brief, "reviewCriteria"));
            text.Append("### Recovery\n\n").Append(brief.TryGetProperty("recovery", out JsonElement recovery) ? recovery.GetString() : string.Empty).Append("\n\n");
        }

        if (assignment.Body.TryGetProperty("promptUse", out JsonElement promptUse)) text.Append(SavedPrompts.Addendum(promptUse));
        text.Append("### Attempts and results\n\n");
        IReadOnlyList<RecordEnvelope> attempts = AttemptsOf(model, assignment);
        if (attempts.Count == 0)
        {
            text.Append("(none yet)\n\n");
        }

        foreach (RecordEnvelope attempt in attempts)
        {
            text.Append("- attempt ").Append(attempt.Id).Append(" (hash ").Append(attempt.ComputedHash).Append(") started ").Append(EstateModel.BodyString(attempt, "startedAt")).Append(" in ").Append(EstateModel.BodyString(attempt, "workspace")).Append(": ").Append(EstateModel.BodyString(attempt, "note")).Append('\n');
            RecordEnvelope? result = ResultOf(model, attempt);
            if (result is null)
            {
                text.Append("  - no result yet; whoever continues records a result for this attempt or starts a new attempt\n");
                continue;
            }

            text.Append("  - result ").Append(result.Id).Append(" (hash ").Append(result.ComputedHash).Append("): ").Append(EstateModel.BodyString(result, "outcome")).Append(", ").Append(ResultState(model, result)).Append(". ").Append(EstateModel.BodyString(result, "summary")).Append('\n');
            AppendResultDetails(text, result, "    - ");
        }

        text.Append('\n');
    }

    private static void AppendResultDetails(StringBuilder text, RecordEnvelope result, string indent)
    {
        if (result.Body.TryGetProperty("outputs", out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement output in outputs.EnumerateArray())
            {
                text.Append(indent).Append("output ").Append(output.TryGetProperty("name", out JsonElement name) ? name.GetString() : "?").Append(' ').Append(output.TryGetProperty("hash", out JsonElement hash) ? hash.GetString() : "?")
                    .Append(output.TryGetProperty("location", out JsonElement location) ? " at " + location.GetString() : string.Empty).Append('\n');
            }
        }

        if (result.Body.TryGetProperty("checks", out JsonElement checks) && checks.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement check in checks.EnumerateArray())
            {
                text.Append(indent).Append("check ").Append(Field(check, "name") ?? "?").Append(": ").Append(Field(check, "outcome") ?? "?").Append('\n');
                if (Field(check, "summary") is string summary)
                {
                    text.Append(indent).Append("  ").Append(summary).Append('\n');
                }
            }
        }

        foreach (string limitation in Strings(result.Body, "limitations"))
        {
            text.Append(indent).Append("limitation: ").Append(limitation).Append('\n');
        }

        foreach (string effect in Strings(result.Body, "unresolvedEffects"))
        {
            text.Append(indent).Append("unresolved effect: ").Append(effect).Append('\n');
        }

        text.Append(indent).Append("next step: ").Append(EstateModel.BodyString(result, "nextStep")).Append('\n');
    }

    private static bool Affects(RecordEnvelope decision, IReadOnlyList<string> orderChain) =>
        decision.Body.TryGetProperty("affects", out JsonElement affects) && affects.ValueKind == JsonValueKind.Array && affects.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String && orderChain.Contains(a.GetString()!));

    private static string Describe(EstateModel model, RecordEnvelope record) => record.Type switch
    {
        "decision" => $"decision {record.Id}: {EstateModel.BodyString(record, "title")}",
        "document-revision" => $"document {EstateModel.BodyString(record, "path")} ({EstateModel.BodyString(record, "hash")}): {EstateModel.BodyString(record, "title")}",
        "product" => $"product {record.Id}: {EstateModel.BodyString(record, "name")}",
        "work-order" => $"work order {EstateModel.BodyString(model.Latest(record), "key")} {record.Id}: {EstateModel.BodyString(record, "title")}",
        "resource" => $"resource {record.Id}: {EstateModel.BodyString(record, "name") ?? EstateModel.BodyString(record, "kind")}",
        _ => $"{record.Type} {record.Id}",
    };

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    private static string? Field(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AppendList(StringBuilder text, string heading, List<string> items)
    {
        text.Append(heading).Append("\n\n");
        if (items.Count == 0)
        {
            text.Append("(none)\n\n");
            return;
        }

        foreach (string item in items)
        {
            text.Append("- ").Append(item).Append('\n');
        }

        text.Append('\n');
    }

    private static string PrincipalName(EstateModel model, string id) =>
        model.CurrentPrincipal(id) is RecordEnvelope principal ? EstateModel.BodyString(principal, "name") ?? id : id;
}
