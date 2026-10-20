// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal static partial class FoundationPolicyKinds
{
    internal const string Placement = "vericairn.estate/placement";
    internal const string Staleness = "vericairn.estate/staleness";
    internal const string KeyFamily = "vericairn.estate/work-key-family";
    internal const string RevisionLine = "vericairn.estate/revision-line";
    internal const string Consistency = "vericairn.estate/work-state-consistency";

    internal static void Validate(PolicyRuleContext context)
    {
        if (context.Parameters.ValueKind != JsonValueKind.Object) throw new PolicyParameterException("Rule parameters must be an object.");
        // Run the same closed readers used by evaluation, without selecting subjects or reading source
        // bytes. Declarations can be checked before activation without a second schema drifting apart.
        IEnumerable<PolicyFinding> validation = Text(context.Rule, "kind") switch
        {
            Placement => Placements(context, parametersOnly: true),
            Staleness => Stale(context, parametersOnly: true),
            KeyFamily => Families(context, parametersOnly: true),
            RevisionLine => Revisions(context, parametersOnly: true),
            Consistency => Consistencies(context, parametersOnly: true),
            _ => throw new PolicyParameterException("This foundation parameter reader does not recognize the kind."),
        };
        _ = validation.ToArray();
    }

    internal static IReadOnlyList<PolicyFinding> Evaluate(PolicyRuleContext context)
    {
        if (!Get(context.Rule, "version").TryGetInt64Safe(out long version) || version != 1) return [context.Unavailable("The exact rule kind version is not installed.")];
        IEnumerable<PolicyFinding> results = Text(context.Rule, "kind") switch
        {
            Placement => Placements(context),
            Staleness => Stale(context),
            KeyFamily => Families(context),
            RevisionLine => Revisions(context),
            Consistency => Consistencies(context),
            _ => [context.Unavailable("The exact rule kind and version are not installed.")],
        };
        PolicyFinding[] findings = results.ToArray();
        return findings.Length > 0 ? findings : [context.Finding(new("policy", context.Identity, context.Policy.Reference), "", "satisfied", "No subjects selected.", "All selected subjects satisfy the rule.", "No action.", subjects: 0)];
    }

    private static IEnumerable<PolicyFinding> Placements(PolicyRuleContext context, bool parametersOnly = false)
    {
        Shape(context.Parameters, ["backlogs"]);
        JsonElement[] backlogs = Array(context.Parameters, "backlogs", empty: true);
        foreach (JsonElement backlog in backlogs)
        {
            Shape(backlog, ["key", "title", "owner", "reason"], "product");
            if (!Patterns.Slug.IsMatch(String(backlog, "key"))) throw new PolicyParameterException("Backlog keys are lower-case names.");
            String(backlog, "title"); String(backlog, "reason");
            if (!context.Model.ById.TryGetValue(String(backlog, "owner"), out RecordEnvelope? owner) || owner.Type != "principal") throw new PolicyInputException("A backlog owner is missing.");
            if (Text(backlog, "product") is { } product && (!context.Model.ById.TryGetValue(product, out RecordEnvelope? found) || found.Type != "product")) throw new PolicyInputException("A backlog product is missing.");
        }
        if (backlogs.GroupBy(b => Text(b, "key"), StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new PolicyParameterException("Backlog keys must be unique.");
        if (parametersOnly) yield break;
        if (PolicyWork.PlacementRegistries(context).Count() != 1) throw new PolicyInputException("Conflicting active placement registries.");
        foreach (RecordEnvelope order in PolicyWork.Orders(context).Where(o => PolicyWork.Planned(context.Model, o, context.Basis.Time!.Value)))
        {
            JsonElement placement = Get(order.Body, "placement");
            (string verdict, string reason, RecordEnvelope? evidence) = PolicyWork.ReleasePlacement(context.Model, order, context.Basis.Time!.Value);
            if (Text(placement, "kind") == "backlog")
            {
                JsonElement backlog = backlogs.FirstOrDefault(b => Text(b, "key") == Text(placement, "key"));
                bool valid = backlog.ValueKind == JsonValueKind.Object && (Text(backlog, "product") is null || Text(Get(context.Policy.Body, "appliesTo"), "kind") == "estate" || Text(backlog, "product") == Text(Get(context.Policy.Body, "appliesTo"), "product"));
                (verdict, reason) = valid ? ("satisfied", "The named backlog is valid.") : ("unmet", "The backlog is unknown or contradicts the selected product.");
            }
            yield return context.Finding(PolicyWork.Target(context.Model, order), "placement", verdict, reason, "A valid release placement or declared backlog.", "Revise the work's explicit placement.", order, evidence is null ? [] : [evidence.Reference]);
        }
    }

    private static IEnumerable<PolicyFinding> Families(PolicyRuleContext context, bool parametersOnly = false)
    {
        Shape(context.Parameters, ["families", "createdFrom"]);
        DateTimeOffset from = Timestamp(context.Parameters, "createdFrom");
        JsonElement[] families = Array(context.Parameters, "families");
        foreach (JsonElement family in families)
        {
            Shape(family, ["prefix", "separator", "digits", "suffixes"]);
            if (String(family, "prefix").Any(c => c is not (>= 'A' and <= 'Z') && c != '-')) throw new PolicyParameterException("Family prefixes use ASCII uppercase letters and hyphens.");
            if (Text(family, "separator") is not ("" or "-")) throw new PolicyParameterException("The family separator is empty or a hyphen.");
            Integer(family, "digits", 1);
            if (Strings(family, "suffixes").Any(s => s.Any(c => c is not (>= 'A' and <= 'Z')))) throw new PolicyParameterException("Family suffixes are literal ASCII uppercase words, or empty.");
        }
        if (parametersOnly) yield break;
        foreach (RecordEnvelope order in PolicyWork.Orders(context).Where(o => context.Model.ById[PolicyWork.Identity(context.Model, o)].AtTime >= from))
        {
            string key = EstateModel.BodyString(order, "key") ?? "";
            int count = families.Count(f => FamilyMatches(f, key));
            yield return context.Finding(PolicyWork.Target(context.Model, order), "key", count == 1 ? "satisfied" : "unmet", key + ": " + Number(count) + " matching families.", "Exactly one declared family.", "Declare the intended family or correct the key attributably.", order);
        }
    }

    private static bool FamilyMatches(JsonElement family, string key)
    {
        string prefix = Text(family, "prefix") + Text(family, "separator");
        int digits = (int)Integer(family, "digits", 1);
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Length - prefix.Length < digits || key.AsSpan(prefix.Length, digits).ContainsAnyExceptInRange('0', '9')) return false;
        string ending = key[(prefix.Length + digits)..];
        return Strings(family, "suffixes").Any(s => ending == (s.Length == 0 ? "" : "-" + s));
    }

    private static IEnumerable<PolicyFinding> Revisions(PolicyRuleContext context, bool parametersOnly = false)
    {
        Shape(context.Parameters, ["selectors", "style"], "repository");
        Choice(context.Parameters, "style", "estate-design/1");
        string[] selectors = Strings(context.Parameters, "selectors");
        Selectors(selectors);
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        string selectionKind = Choice(selection, "kind", "repository", "product");
        string repositoryId = selectionKind == "repository" ? String(selection, "repository") : String(context.Parameters, "repository");
        if (Text(context.Parameters, "repository") is { } named && named != repositoryId) throw new PolicyParameterException("The source repository differs from the policy selection.");
        if (selectionKind == "product" && (!context.Model.ById.TryGetValue(repositoryId, out RecordEnvelope? resource) || resource.Type != "resource" || EstateModel.BodyString(resource, "kind") != "repository" || resource.Subject?.Type != "product" || resource.Subject.Id != String(selection, "product")))
            throw new PolicyInputException("The source repository must be explicitly related to the selected product.");
        string[] coverage = selectionKind == "repository" ? Strings(selection, "selectors") : ["**"];
        Selectors(coverage);
        if (parametersOnly) yield break;
        PolicyRepository repository = Repository(context.Basis, repositoryId);
        foreach (PolicyFile file in repository.Files.Where(f => f.Path.EndsWith(".md", StringComparison.Ordinal) && selectors.Any(s => Matches(s, f.Path)) && coverage.Any(s => Matches(s, f.Path))))
        {
            string text = Read(file);
            string? line = WritingScanner.LinesOutsideFences(text).Select(l => l.Text).Where(l => !l.StartsWith("    ", StringComparison.Ordinal) && !l.StartsWith('\t'))
                .SkipWhile(l => !l.TrimStart(' ').StartsWith("# ", StringComparison.Ordinal)).Skip(1).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            Match match = RevisionPattern().Match(line ?? "");
            bool valid = match.Success && DateOnly.TryParseExact(match.Groups["date"].Value, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) &&
                date <= DateOnly.FromDateTime(context.Basis.Time!.Value.UtcDateTime) && match.Groups["explanation"].Value.Any(char.IsLetterOrDigit);
            yield return context.Finding(new("file", repository.Identity + ":" + file.Path, Repository: repository.Identity, Path: file.Path, Hash: file.Hash), "revision-line", valid ? "satisfied" : "unmet",
                line ?? "No prose line after the first level-one title.", "Revision N, Month D, YYYY followed by an explanation, dated no later than the UTC basis date.", "Write a valid authored revision line.");
        }
    }

    [GeneratedRegex(@"\ARevision [1-9][0-9]*, (?<date>(?:January|February|March|April|May|June|July|August|September|October|November|December) [0-9]{1,2}, [0-9]{4})(?<explanation>\D.*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    private static IEnumerable<PolicyFinding> Consistencies(PolicyRuleContext context, bool parametersOnly = false)
    {
        Shape(context.Parameters, ["cases"]);
        string[] cases = Strings(context.Parameters, "cases");
        if (cases.Any(c => c is not ("accepted-still-open" or "published-plan-still-open"))) throw new PolicyParameterException("Unknown work-state consistency case.");
        if (parametersOnly) yield break;
        DateTimeOffset from = context.Model.ById[context.Identity].AtTime;
        foreach (RecordEnvelope order in PolicyWork.Orders(context))
        {
            RecordEnvelope? status = PolicyWork.Status(context.Model, order, context.Basis.Time!.Value);
            foreach (string item in cases)
            {
                RecordEnvelope? fact = item == "accepted-still-open"
                    ? PolicyWork.Acceptance(context.Model, order, context.Basis.Time!.Value)
                    : context.Model.OfType("release").FirstOrDefault(r => ReleaseMatchesProduct(context.Model, r, Text(Get(order.Body, "releasePlan"), "product")) && EstateModel.BodyString(r, "version") == Text(Get(order.Body, "releasePlan"), "version") && EstateModel.BodyString(r, "status") == "published");
                bool inconsistent = status is not null && EstateModel.BodyString(status, "status") is "planned" or "active" or "paused" or "submitted" && fact is not null;
                // A contradiction begins when both its explicit status and exact fact exist.
                // Policy revisions preserve the first declaration's prospective boundary.
                if (inconsistent && status!.AtTime < from && fact!.AtTime < from) continue;
                yield return context.Finding(PolicyWork.Target(context.Model, order), item, inconsistent ? "unmet" : "satisfied",
                    inconsistent ? "An explicit open status contradicts the exact acceptance or publication." : "No contradictory explicit status.", "An explicit status consistent with the exact accepted work or published plan.", "Record the appropriate status disposition or correct the subject relation.", status, fact is null ? [] : [fact.Reference]);
            }
        }
    }
}
