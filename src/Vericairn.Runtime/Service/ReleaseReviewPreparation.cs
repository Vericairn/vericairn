// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>Pure record selection and rendering. No provider, clone, clock or launcher participates.</summary>
internal static class ReleaseReviewPreparation
{
    internal static (ReleaseReviewBriefResponse Response, RecordEnvelope? Order) Prepare(
        EstateModel model, string version, string? product, string? decision)
    {
        if (model.Findings.HasErrors) return Failed("refused", "The estate has invalid records; resolve its findings before preparing a release review.");
        List<RecordEnvelope> candidates = model.Current("release").Where(r => EstateModel.BodyString(r, "version") == version &&
            (product is null || SameProduct(model, r.Subject!.Id, product))).ToList();
        if (candidates.Count == 0) return Failed("not-found", "No release matches that version and product.");
        if (candidates.Count != 1) return Failed("ambiguous", "Several products have that version; name the product.");
        RecordEnvelope candidate = candidates[0];
        if (EstateModel.BodyString(candidate, "status") != "candidate") return Failed("refused", "Only a current release candidate can be prepared for review.");

        List<RecordEnvelope> decisions = model.Current("decision").Where(d => d.Body.TryGetProperty("releaseReview", out JsonElement review) &&
            Exact(review.GetProperty("candidate"), candidate)).ToList();
        if (decision is not null) decisions.RemoveAll(d => d.Id != decision);
        if (decisions.Count != 1) return Failed(decisions.Count == 0 ? "not-found" : "ambiguous",
            "Name one current operator decision whose releaseReview pins this exact candidate and records its scope.");
        RecordEnvelope approved = decisions[0];
        JsonElement approval = approved.Body.GetProperty("releaseReview");
        RecordEnvelope order = model.ById[approval.GetProperty("workOrder").GetProperty("id").GetString()!];
        if (!model.IsCurrent(order)) return Failed("conflict", "The approved review work order changed; its decision must pin the current revision.");
        if (!model.HasRole(EstateModel.BodyString(approved, "decidedBy"), Roles.Operator))
            return Failed("refused", "The review scope must be approved by an operator.");
        List<RecordEnvelope> reviewers = model.Current("principal").Where(p => model.HasRole(p.Id, Roles.Reviewer)).ToList();
        if (reviewers.Count != 1) return Failed("ambiguous", "The estate must identify exactly one principal holding the reviewer role.");
        RecordEnvelope reviewer = reviewers[0];
        string reviewerId = model.Chain(reviewer)[0];
        if (reviewerId == candidate.By) return Failed("refused", "The Reviewer authored this candidate; an independent Reviewer is required.");

        string repository = EstateModel.BodyString(candidate, "repository")!;
        bool ReleaseOfProduct(RecordEnvelope r) => SameProduct(model, r.Subject!.Id, candidate.Subject!.Id) &&
            EstateModel.BodyString(r, "repository") == repository;
        RecordEnvelope? previous = Ordered(model.OfType("release").Where(r => ReleaseOfProduct(r) && Before(r, candidate) &&
            EstateModel.BodyString(r, "status") is "candidate" or "published")).LastOrDefault();

        // Freeze operation revisions at the candidate, and count an operation only at its first recorded successful merge.
        List<RecordEnvelope> changes = Ordered(model.OfType("operation").Where(r => AtMost(r, candidate) &&
            (!model.SupersededBy.TryGetValue(r.Id, out string? next) || !AtMost(model.ById[next], candidate)) &&
            EstateModel.BodyString(r, "action") == "change-merge" && EstateModel.BodyString(r, "status") == "observed-success" &&
            r.Body.TryGetProperty("target", out JsonElement target) && Text(target, "destination") == repository &&
            SameProduct(model, Text(target, "product"), candidate.Subject!.Id) && Text(target, "mergeCommit") is not null &&
            (previous is null || Before(previous, FirstMerge(model, r))))).ToList();

        List<RecordEnvelope> reviews = Ordered(model.Current("review").Where(r => r.Subject is { Type: "release" } subject &&
            model.ById.TryGetValue(subject.Id, out RecordEnvelope? release) && subject.Hash == release.ComputedHash &&
            ReleaseOfProduct(release) && AtMost(release, candidate))).ToList();
        RecordEnvelope? reviewed = Ordered(reviews.Select(r => model.ById[r.Subject!.Id])).LastOrDefault();
        reviews.RemoveAll(r => r.Subject!.Id != reviewed?.Id);
        List<RecordEnvelope> dispositions = Ordered(model.Current("decision").Where(d => reviews.Any(r =>
            d.Subject?.Type == "review" && d.Subject.Id == r.Id && d.Subject.Hash == r.ComputedHash))).ToList();

        var sources = new List<RecordEnvelope>();
        var text = new StringBuilder();
        string scope = approval.GetProperty("scope").GetString()!;
        text.Append("# Release review: ").Append(version).Append(" (").Append(scope).Append(")\n\n");
        text.Append("Review the exact candidate under the operator's recorded scope. Report findings and evidence in your result; release acceptance remains with the operator.\n\n");
        Section("Candidate, run evidence and limitations", [candidate]);
        Section("Previous candidate or release", previous is null ? [] : [previous]);
        text.Append("The change window follows record order (timestamp, then id), after the previous candidate or publication through this candidate. Only recorded successful merges for this product and repository are included; unrecorded host facts and commit ancestry are not inferred.\n\n");
        Section("Merged changes in the recorded window", changes);
        Section("Approved scope and probes", [approved]);
        Section("Previously reviewed candidate", reviewed is null ? [] : [reviewed]);
        Section("Previous review findings", reviews);
        Section("Disposition decisions pinned to those reviews", dispositions);
        text.Append("Disposition wording is retained in full. A missing decision is not a resolved finding.\n\n");
        Section("Review work order", [order]);
        Section("Reviewer principal", [reviewer]);
        text.Append("## Worker handoff\n\nUse the recorded Reviewer principal and this assignment with the operator's configured launcher, repository, base commit and run limits. Requested progress: unavailable (WATCH-02).\n");
        string rendered = text.ToString();
        if (rendered.Length > 1_000_000) return Failed("refused", "The complete review brief exceeds the assignment text limit; no content was truncated.");
        var brief = new JsonObject
        {
            ["outcome"] = $"Review release {version} at {candidate.ComputedHash}, with the {scope} scope approved by decision {approved.Id}.",
            ["sources"] = new JsonArray(sources.DistinctBy(r => r.Id).Select(r => (JsonNode)JsonSerializer.SerializeToNode(r.Reference, ProtocolJson.Options)!).ToArray()),
            ["permittedChanges"] = new JsonArray("the estate: the Reviewer's own attempt and result"),
            ["prohibitedEffects"] = new JsonArray("No repository changes, push, merge, publication or release acceptance."),
            ["workspace"] = new JsonArray(repository + " at commit " + EstateModel.BodyString(candidate, "commit")),
            ["limits"] = new JsonArray("Use the operator's configured worker limits and launcher; this assignment starts no run."),
            ["expectedOutputs"] = new JsonArray("A result with findings, evidence, checks, limitations and the next step."),
            ["reviewCriteria"] = new JsonArray("Judge the exact candidate under the recorded scope and repeat the recorded probes."),
            ["recovery"] = "Read this assignment's retained review brief and exact source revisions before resuming.",
            ["reviewBrief"] = rendered,
        };
        return (new(rendered, ContentHash.Sha256(Encoding.UTF8.GetBytes(rendered)), Element(brief), reviewerId), order);

        void Section(string title, IEnumerable<RecordEnvelope> records)
        {
            text.Append("## ").Append(title).Append("\n\n");
            List<RecordEnvelope> selected = records.ToList();
            if (selected.Count == 0) text.Append("None recorded.\n\n");
            foreach (RecordEnvelope record in selected)
            {
                sources.Add(record);
                text.Append("### ").Append(record.Type).Append(' ').Append(record.Id).Append(" at ").Append(record.ComputedHash)
                    .Append("\n\nRecorded ").Append(record.At).Append(" by ").Append(record.By).Append(".\n\n");
                if (record.Subject is { } subject)
                    text.Append("Subject: ").Append(subject.Type).Append(' ').Append(subject.Id).Append(" at ").Append(subject.Hash).Append(".\n\n");
                // Indentation keeps record strings from closing a Markdown fence or becoming instructions outside their source block.
                foreach (string line in CanonicalJson.SerializePretty(record.Body).TrimEnd('\n').Split('\n'))
                    text.Append("    ").Append(line).Append('\n');
                text.Append('\n');
            }
        }
    }

    private static (ReleaseReviewBriefResponse, RecordEnvelope?) Failed(string code, string message) => (new("", Problem: new(code, message, [])), null);
    private static bool Exact(JsonElement reference, RecordEnvelope record) => Text(reference, "id") == record.Id && Text(reference, "hash") == record.ComputedHash;
    private static string? Text(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool SameProduct(EstateModel model, string? left, string right) => left is not null &&
        model.ById.TryGetValue(left, out RecordEnvelope? product) && product.Type == "product" && model.Chain(model.Latest(product)).Contains(right, StringComparer.Ordinal);
    private static bool Before(RecordEnvelope left, RecordEnvelope right) => string.CompareOrdinal(left.At, right.At) is var at && (at < 0 || at == 0 && string.CompareOrdinal(left.Id, right.Id) < 0);
    private static bool AtMost(RecordEnvelope left, RecordEnvelope right) => left.Id == right.Id || Before(left, right);
    private static IOrderedEnumerable<RecordEnvelope> Ordered(IEnumerable<RecordEnvelope> records) => records.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);
    private static RecordEnvelope FirstMerge(EstateModel model, RecordEnvelope operation) => model.Chain(operation).Select(id => model.ById[id])
        .First(r => EstateModel.BodyString(r, "status") == "observed-success");
}
