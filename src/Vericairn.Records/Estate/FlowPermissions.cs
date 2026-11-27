// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Derives a routed step's repository permissions from the run's typed path declarations.</summary>
internal static class FlowPermissions
{
    internal const string Code = "flow.permitted-paths";
    private const string Prefix = "path:";
    private const int MaximumLength = PathIntersection.LengthLimit - 5;

    internal static void Validate(BodyReader body)
    {
        foreach (string entry in body.StringArray("paths", false, allowEmpty: false, maxItemLength: MaximumLength) ?? [])
        {
            if (Pattern(entry) is null)
            {
                body.Error(Code, "paths", $"'{entry}' is not an interpretable repository-relative path pattern.");
            }
        }
    }

    /// <summary>
    /// The patterns a routed step may change: those every typed source permits. The work order's <c>path:</c> entries are read from the
    /// revision the goal's plan names and from every later revision of it, each a source of its own, so a revision written after the
    /// run was proposed can narrow them and never widen them. When the named revision has none, a later revision's grant nothing. To
    /// widen them, or to give them first, the goal names the new revision.
    /// </summary>
    internal static IReadOnlyList<string> For(EstateModel model, RecordEnvelope goal, RecordEnvelope order, FlowStep step)
    {
        var sources = new List<string[]>();
        AddDeclared(step.Element);
        AddDeclared(FlowRuns.Plan(goal));

        // The named revision's path: entries are the work order's grant; a later revision only narrows a grant the named one gives, so
        // entries first added after it grant nothing until the goal names that revision.
        IReadOnlyList<RecordEnvelope> revisions = Revisions(model, goal, order);
        if (revisions.Count > 0 && Scoped(revisions[0]) is { Count: > 0 })
        {
            foreach (RecordEnvelope revision in revisions)
            {
                List<string> scoped = Scoped(revision);
                if (scoped.Count > 0)
                {
                    sources.Add(Patterns(scoped.Select(entry => entry[Prefix.Length..])));
                }
            }
        }

        // A brief can express a list of patterns. Keep a supplied pattern only when every source permits all the paths it matches.
        // An overlap alone is insufficient: it would grant the parts of that pattern beyond the intersection.
        int states = PathIntersection.StateBound;
        long work = PathIntersection.WorkBound;
        var result = new List<string>();
        foreach (string pattern in sources.SelectMany(source => source).Distinct(StringComparer.Ordinal))
        {
            if (sources.All(source => Covers(pattern, source, ref states, ref work)))
            {
                result.Add(Prefix + pattern);
            }
        }

        return result;

        void AddDeclared(JsonElement declaration)
        {
            if (declaration.ValueKind == JsonValueKind.Object && declaration.TryGetProperty("paths", out _))
            {
                sources.Add(Patterns(TJson.Strings(declaration, "paths")));
            }
        }
    }

    // The work order's revisions from the one the goal's plan names to the latest, earliest first; none when the plan names none of its
    // chain, so the work order then grants nothing.
    private static IReadOnlyList<RecordEnvelope> Revisions(EstateModel model, RecordEnvelope goal, RecordEnvelope order)
    {
        string? named = TJson.Strings(TJson.Object(goal.Body, "plan"), "workOrders").FirstOrDefault();
        List<string> chain = [.. model.Chain(order)];
        int from = named is null ? -1 : chain.IndexOf(named);
        return from < 0 ? [] : [.. chain.Skip(from).Select(id => model.ById.GetValueOrDefault(id)).OfType<RecordEnvelope>()];
    }

    private static List<string> Scoped(RecordEnvelope revision) =>
        [.. TJson.Strings(revision.Body, "scope").Where(entry => entry.StartsWith(Prefix, StringComparison.Ordinal))];

    /// <summary>
    /// What a step's brief carries where no typed source gives it a pattern, which only a step no agent carries reaches: the work order's
    /// prose, each entry kept only when it cannot be read as a pattern, or a sentence saying so. Never a pattern.
    /// </summary>
    internal static IReadOnlyList<string> Prose(RecordEnvelope order)
    {
        List<string> prose = [.. TJson.Strings(order.Body, "scope").Where(entry => PathPatterns.Pattern(entry) is null)];
        return prose.Count > 0 ? prose : ["the work the step's work order describes; no typed path declaration covers it"];
    }

    internal static IReadOnlyList<string>? Carried(IReadOnlyList<string> permitted, RecordEnvelope assignment)
    {
        string[] bounds = Patterns(permitted.Select(entry => entry[Prefix.Length..]));
        string[] held = [.. TJson.Strings(TJson.Object(assignment.Body, "brief"), "permittedChanges")
            .Select(PathPatterns.Pattern).OfType<string>().Select(pattern => pattern.TrimEnd('/')).Distinct(StringComparer.Ordinal)];
        int states = PathIntersection.StateBound;
        long work = PathIntersection.WorkBound;
        return held.Length > 0 && held.All(pattern => Covers(pattern, bounds, ref states, ref work))
            ? [.. held.Select(pattern => Prefix + pattern)] : null;
    }

    internal static IReadOnlyList<FlowStep> Missing(EstateModel model, RecordEnvelope goal, FlowDefinition flow, FlowRuns.Configuration configuration, RecordEnvelope order)
    {
        return [.. flow.Steps.Where(step => step.IsRouted && step.Reserved is null && Routing.ReservedAction(step.Role) is null
            && !configuration.Off.ContainsKey(step.Key) && (step.Each is null || FlowRuns.Items(goal, step.Each).Count > 0)
            && (MayRouteToAgent(step) || HasAgentReview(step))
            && For(model, goal, order, step).Count == 0)];

        bool MayRouteToAgent(FlowStep step) =>
            (!step.Decides || (step.Transition is { PersonOnly: false } && configuration.Decider(step) != "rule"))
            && AgentRole(step.Role);

        bool AgentRole(string role) => flow.Role(role)?.Holder switch
        {
            "person" => false,
            "agent" => true,
            _ => Routing.Principals(model, goal, role).Principals.Any(principal =>
                model.CurrentPrincipal(principal) is RecordEnvelope record && EstateModel.BodyString(record, "kind") == "agent"),
        };

        bool HasAgentReview(FlowStep step)
        {
            if (step.Decides)
            {
                return AgentReview(step.Transition!.Review);
            }

            return step.Outputs.Count == 0 ? AgentReview(FlowRuns.ReviewRequirement(flow, configuration, step, null))
                : step.Outputs.Where(item => !configuration.ItemsOff.ContainsKey(step.Key + "." + item.Name))
                    .Any(item => AgentReview(FlowRuns.ReviewRequirement(flow, configuration, step, item)));
        }

        bool AgentReview(FlowReview? review) =>
            review is { Independence: not ("none" or "person" or "self") } && AgentRole(review.Role ?? Roles.Reviewer);
    }

    internal static string Message(string step) =>
        $"Flow step {step} would receive no interpretable permitted path. Give it a repository-relative pattern in the step, the goal's flow paths, or a path: entry in the work order revision the goal names. The supplied sources must permit a common pattern.";

    private static string? Pattern(string entry)
    {
        string? pattern = PathPatterns.Pattern(Prefix + entry)?.TrimEnd('/');
        return pattern is not null && pattern.Length <= MaximumLength && PathPatterns.Valid(pattern) ? pattern : null;
    }

    private static string[] Patterns(IEnumerable<string> entries) =>
        [.. entries.Select(Pattern).OfType<string>().Distinct(StringComparer.Ordinal)];

    private static bool Covers(string pattern, string[] source, ref int states, ref long work)
    {
        if (source.Contains(pattern, StringComparer.Ordinal))
        {
            return true;
        }

        if (source.Length == 0 || states <= 0 || work <= 0)
        {
            return false;
        }

        // Search for a counterexample: a valid path in the candidate but outside the source's union.
        PathIntersection.Outcome outside = PathIntersection.Search(new PathIntersection.Party([pattern], []),
            new PathIntersection.Party(["**"], source), bound: states, work: work);
        states -= outside.States;
        work -= outside.Work;
        return outside.Determined && !outside.Overlap;
    }
}
