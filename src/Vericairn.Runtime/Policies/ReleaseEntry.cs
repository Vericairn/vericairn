// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal static class ReleaseEntry
{
    internal const string Kind = "vericairn.estate/release-entry";
    private sealed record Draft(string Id, JsonElement Data, string Body);
    private sealed record Entry(PolicyFile File, Draft Draft, RecordEnvelope? Pin, RecordEnvelope? First, Draft? Original, RecordEnvelope? Review, string? Problem);

    internal static void Validate(PolicyRuleContext context)
    {
        JsonElement p = context.Parameters;
        Shape(p, ["product", "entryKind", "source", "directory", "fromPublication", "review"]);
        string product = String(p, "product");
        Choice(p, "entryKind", "devlog");
        if (String(p, "source") != "estate-draft/1") throw new PolicyInputException("The exact entry source contract is not installed.");
        Choice(p, "review", "independent", "operator");
        string directory = String(p, "directory");
        if (!Path(directory) || !(directory.StartsWith("docs/", StringComparison.Ordinal) || directory.StartsWith("design/", StringComparison.Ordinal)) || directory.Contains('*', StringComparison.Ordinal))
            throw new PolicyParameterException("The draft directory must be a normalized document directory.");
        JsonElement from = Get(p, "fromPublication");
        if (from.ValueKind == JsonValueKind.String)
        {
            if (from.GetString() != "after-declaration") throw new PolicyParameterException("Use after-declaration or an exact publication reference.");
        }
        else
        {
            Shape(from, ["type", "id", "hash"]);
            if (String(from, "type") != "release" || !ContentHash.IsValid(String(from, "hash"))) throw new PolicyParameterException("The cohort requires an exact release reference.");
            String(from, "id");
        }
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        if (Text(selection, "kind") == "product" && Text(selection, "product") != product) throw new PolicyParameterException("The rule names a different product from its selection.");
        if (Text(selection, "kind") == "repository")
        {
            string[] selectors = Strings(selection, "selectors");
            Selectors(selectors);
            // A release rule cannot hide duplicate entries through a narrower path selection.
            if (!selectors.Contains("**", StringComparer.Ordinal) && !selectors.Contains(directory + "/**", StringComparer.Ordinal))
                throw new PolicyParameterException("The selection must cover the complete draft directory.");
        }
    }

    internal static IReadOnlyList<PolicyFinding> Evaluate(PolicyRuleContext context)
    {
        string product = String(context.Parameters, "product");
        if (!context.Model.ById.TryGetValue(product, out RecordEnvelope? productRecord) || productRecord.Type != "product") throw new PolicyInputException("The release-entry product is unavailable.");
        RecordEnvelope[] publications = Publications(context, product);
        JsonElement from = Get(context.Parameters, "fromPublication");
        RecordEnvelope boundary;
        bool inclusive = from.ValueKind != JsonValueKind.String;
        if (inclusive)
        {
            boundary = Exact(context.Model, from);
            if (!publications.Any(r => r.Reference == boundary.Reference)) throw new PolicyInputException("The cohort does not name this product's first observed publication of a release identity.");
        }
        else boundary = context.Model.ById[context.Identity];
        publications = publications.Where(r => Compare(r, boundary) >= (inclusive ? 0 : 1)).ToArray();
        if (context.Basis.Submission is { } submission)
        {
            // A candidate owes no account of its future publication; a replay of that
            // publication cannot hold itself. Earlier publications still hold this submission.
            publications = submission.Product != product ? [] : publications.Where(r =>
                EstateModel.BodyString(r, "version") != submission.Version && !context.Model.Chain(r).Contains(submission.Release.Id, StringComparer.Ordinal)).ToArray();
        }
        if (publications.Length == 0) return [context.Finding(new("policy", context.Identity, context.Policy.Reference), "", "satisfied",
            "No publications in the applicable cohort.", "An entry for each observed in-range publication.", "No action.", subjects: 0)];

        string estate = context.Model.Current("estate").Select(r => PolicyWork.Identity(context.Model, r)).SingleOrDefault() ?? throw new PolicyInputException("The estate repository identity is unavailable.");
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        if (Text(selection, "kind") == "repository" && Text(selection, "repository") != estate)
            throw new PolicyInputException("estate-draft/1 requires the estate repository and its document pins.");
        PolicyRepository repository = Repository(context.Basis, estate);
        string directory = String(context.Parameters, "directory") + "/";
        PolicyFile[] files = repository.Files.Where(f => f.Path.StartsWith(directory, StringComparison.Ordinal) && f.Path.EndsWith(".md", StringComparison.Ordinal)).ToArray();
        if (files.Length > 10000 || files.Sum(f => (long)f.Bytes.Length) > 32L * 1024 * 1024) throw new PolicyInputException("The draft source bound was exhausted.");

        // Retain the earliest pin of every observed entry identity, including removed paths:
        // copying an old entry to a new path must not fabricate new authorship.
        var originals = new Dictionary<string, (RecordEnvelope Pin, Draft Draft)>(StringComparer.Ordinal);
        RecordEnvelope[] firstPins = context.Model.OfType("document-revision").Where(r => r.Supersedes is null &&
            (EstateModel.BodyString(r, "path") ?? "").StartsWith(directory, StringComparison.Ordinal)).ToArray();
        if (firstPins.Sum(r => Get(r.Body, "bytes").GetInt64()) > 32L * 1024 * 1024) throw new PolicyInputException("The retained draft history byte bound was exhausted.");
        foreach (RecordEnvelope pin in firstPins)
        {
            Draft draft = Parse(Retained(context, pin, files));
            originals.Add(pin.Id, (pin, draft));
        }
        var oldest = originals.Values.GroupBy(o => o.Draft.Id, StringComparer.Ordinal).ToDictionary(g => g.Key,
            g => g.OrderBy(o => o.Pin.At, StringComparer.Ordinal).ThenBy(o => o.Pin.Id, StringComparer.Ordinal).First().Pin, StringComparer.Ordinal);
        var currentPins = context.Model.Current("document-revision").ToLookup(r => EstateModel.BodyString(r, "path"), StringComparer.Ordinal);
        var reviews = context.Model.Current("review").ToLookup(r => r.Subject?.Id, StringComparer.Ordinal);
        List<Entry> entries = [];
        foreach (PolicyFile file in files)
        {
            Draft draft = Parse(Read(file));
            RecordEnvelope[] pins = currentPins[file.Path].ToArray();
            if (pins.Length > 1) throw new PolicyInputException("Conflicting current draft pins: " + file.Path + ".");
            RecordEnvelope? pin = pins.SingleOrDefault();
            if (pin is null || EstateModel.BodyString(pin, "hash") != file.Hash || Get(pin.Body, "bytes").GetInt64() != file.Bytes.Length)
            {
                entries.Add(new(file, draft, pin, null, null, null, "No valid pin of the current bytes."));
                continue;
            }
            string firstId = context.Model.Chain(pin)[0];
            if (!originals.TryGetValue(firstId, out var original)) throw new PolicyInputException("The retained first revision of the draft is unavailable: " + file.Path + ".");
            RecordEnvelope? review = reviews[pin.Id].Where(r => PolicyWork.ReviewQualifies(context.Model, r, pin, String(context.Parameters, "review")))
                .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            string? problem = draft.Id != original.Draft.Id ? "The stable entry identity changed." :
                review is null || EstateModel.BodyString(review, "outcome") != "pass" ? "The latest qualifying review of the current revision does not pass." : null;
            entries.Add(new(file, draft, pin, original.Pin, original.Draft, review, problem));
        }

        var relations = entries.ToLookup(e => Text(Get(Get(e.Draft.Data, "release"), "publication"), "id"), StringComparer.Ordinal);
        List<PolicyFinding> findings = [];
        foreach (RecordEnvelope publication in publications)
        {
            Entry[] related = relations[publication.Id].Where(e => Relation(e.Draft, publication)).ToArray();
            Entry[] qualifying = related.Where(e => e.Problem is null && Complete(e.Draft, publication) && e.Original is { } first && Relation(first, publication) &&
                e.First is { } firstPin && Compare(firstPin, publication) > 0 &&
                Compare(oldest[e.Draft.Id], publication) > 0).ToArray();
            string[] identities = qualifying.Select(e => e.Draft.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            bool duplicatePaths = qualifying.GroupBy(e => e.Draft.Id, StringComparer.Ordinal).Any(g => g.Count() != 1);
            bool satisfied = identities.Length == 1 && !duplicatePaths;
            string version = EstateModel.BodyString(publication, "version")!;
            string actual = satisfied ? "Reviewed entry " + identities[0] + " for " + version + "." :
                qualifying.Length > 0 ? "Duplicate qualifying entries for " + version + ": " + string.Join(", ", qualifying.Select(e => e.Draft.Id + " (" + e.File.Path + ")").Order(StringComparer.Ordinal)) + "." :
                "Missing qualifying devlog for " + version + " (publication " + publication.Id + ")." +
                string.Concat(related.Select(e => " " + e.Draft.Id + " (" + e.File.Path + "): " + (e.Problem ?? "The entry is not new for this publication or its release metadata/content is incomplete.")).Order(StringComparer.Ordinal));
            findings.Add(context.Finding(new("publication", publication.Id, publication.Reference), "entry", satisfied ? "satisfied" : "unmet", actual,
                "Exactly one new Internal devlog, pinned and reviewed at its current revision for this exact publication.",
                "Write or correct the entry and obtain the declared exact-revision review before the product's next submission.", publication,
                related.SelectMany(e => new[] { e.Pin, e.First, e.Review }).OfType<RecordEnvelope>().Select(r => r.Reference).Append(publication.Reference)));
        }
        return findings;
    }

    private static RecordEnvelope[] Publications(PolicyRuleContext context, string product)
    {
        RecordEnvelope[] publications = context.Model.OfType("release").Where(r => r.Subject is { Type: "product" } subject && subject.Id == product &&
            EstateModel.BodyString(r, "status") == "published" && !context.Model.Chain(r).SkipLast(1).Any(id => EstateModel.BodyString(context.Model.ById[id], "status") == "published"))
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        if (publications.GroupBy(r => EstateModel.BodyString(r, "version"), StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new PolicyInputException("The observed publication identity is ambiguous.");
        return publications;
    }

    private static string Retained(PolicyRuleContext context, RecordEnvelope pin, PolicyFile[] files)
    {
        string hash = EstateModel.BodyString(pin, "hash") ?? "";
        PolicyObject[] objects = context.Basis.Objects.Where(o => o.Hash == hash).ToArray();
        if (objects.Length > 1) throw new PolicyInputException("Conflicting retained draft objects.");
        PolicyFile? file = objects.Length == 1 ? new(EstateModel.BodyString(pin, "path")!, hash, objects[0].Bytes, objects[0].Available) : files.FirstOrDefault(f => f.Hash == hash);
        if (file is null || Get(pin.Body, "bytes").GetInt64() != file.Bytes.Length) throw new PolicyInputException("The draft's first revision bytes are unavailable.");
        return Read(file);
    }

    private static Draft Parse(string text)
    {
        (JsonElement data, string body) = DraftData.Read(text);
        try
        {
            Shape(data, ["kind", "id", "title", "summary", "audience", "applicability", "sources", "status"], "release", "sites", "event", "drafted-by", "supersedes", "guidance");
            foreach (string key in new[] { "kind", "id", "title", "summary", "audience", "status", "drafted-by", "supersedes" })
                if (Get(data, key).ValueKind is not (JsonValueKind.String or JsonValueKind.Undefined)) throw new PolicyInputException("Unsupported draft field type: " + key + ".");
            string id = String(data, "id");
            JsonElement applicability = Get(data, "applicability");
            Shape(applicability, ["versions"], "product");
            if (applicability.ValueKind != JsonValueKind.Object || Get(applicability, "versions").ValueKind != JsonValueKind.String) throw new PolicyInputException("Draft applicability requires an exact string versions field.");
            if (Get(applicability, "product").ValueKind is not (JsonValueKind.String or JsonValueKind.Undefined)) throw new PolicyInputException("The draft product context must be a string.");
            if (Get(data, "sources").ValueKind != JsonValueKind.Array) throw new PolicyInputException("Draft sources must be a sequence.");
            foreach (JsonElement source in Items(Get(data, "sources")))
            {
                if (source.ValueKind == JsonValueKind.String) continue;
                Shape(source, ["type", "id", "hash"], "tracking");
                String(source, "type"); String(source, "id");
                if (!ContentHash.IsValid(String(source, "hash"))) throw new PolicyInputException("Invalid draft source hash.");
                if (Get(source, "tracking").ValueKind != JsonValueKind.Undefined) Choice(source, "tracking", "historical", "tracked");
            }
            if (Get(data, "sites").ValueKind is not (JsonValueKind.Array or JsonValueKind.Undefined) || Get(data, "event").ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined) ||
                Get(data, "guidance").ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined)) throw new PolicyInputException("Unsupported draft metadata field type.");
            if (Items(Get(data, "sites")).Any(s => s.ValueKind != JsonValueKind.String)) throw new PolicyInputException("Draft sites must be strings.");
            JsonElement release = Get(data, "release");
            if (release.ValueKind != JsonValueKind.Undefined)
            {
                Shape(release, ["product", "version", "publication"]);
                String(release, "product"); String(release, "version");
                JsonElement publication = Get(release, "publication");
                Shape(publication, ["id", "hash"], "type");
                String(publication, "id");
                if (!ContentHash.IsValid(String(publication, "hash")) || Text(publication, "type") is { } type && type != "release") throw new PolicyInputException("Invalid draft publication reference.");
            }
            return new(id, data, body);
        }
        catch (PolicyParameterException error) { throw new PolicyInputException("estate-draft/1: " + error.Message); }
    }

    private static bool Relation(Draft draft, RecordEnvelope publication)
    {
        JsonElement release = Get(draft.Data, "release");
        JsonElement reference = Get(release, "publication");
        return Text(release, "product") == publication.Subject!.Id && Text(release, "version") == EstateModel.BodyString(publication, "version") &&
            Text(reference, "id") == publication.Id && Text(reference, "hash") == publication.ComputedHash;
    }

    private static bool Complete(Draft draft, RecordEnvelope publication) => Text(draft.Data, "kind") == "devlog" && Text(draft.Data, "audience") == "internal" &&
        Text(draft.Data, "status") == "draft" && !string.IsNullOrWhiteSpace(Text(draft.Data, "title")) && !string.IsNullOrWhiteSpace(Text(draft.Data, "summary")) && !string.IsNullOrWhiteSpace(draft.Body) &&
        Text(Get(draft.Data, "applicability"), "versions") == EstateModel.BodyString(publication, "version") &&
        Items(Get(draft.Data, "sources")).Any(s => PolicyExceptions.ReferenceMatches(s, publication.Reference));

    private static int Compare(RecordEnvelope left, RecordEnvelope right)
    {
        int time = string.CompareOrdinal(left.At, right.At);
        return time == 0 ? string.CompareOrdinal(left.Id, right.Id) : time;
    }
}
