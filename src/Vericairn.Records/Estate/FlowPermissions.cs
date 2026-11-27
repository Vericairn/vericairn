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

    internal static IReadOnlyList<string> For(RecordEnvelope goal, RecordEnvelope order, FlowStep step)
    {
        var sources = new List<string[]>();
        AddDeclared(step.Element);
        AddDeclared(FlowRuns.Plan(goal));

        List<string> scoped = TJson.Strings(order.Body, "scope").Where(entry => entry.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        if (scoped.Count > 0)
        {
            sources.Add(Patterns(scoped.Select(entry => entry[Prefix.Length..])));
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
            && For(goal, order, step).Count == 0)];

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
        $"Flow step {step} would receive no interpretable permitted path. Give it a repository-relative pattern in the step, the goal's flow paths, or a path: entry in the work order. The supplied sources must permit a common pattern.";

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
