// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

public sealed partial class ToolRegistry
{
    private static IEnumerable<ToolDefinition> GoalControlTools(string by)
    {
        yield return Write("goal_propose", "Propose a goal or its revision through the local runtime. A flow run is refused if an agent work step has no permitted path pattern. A proposal grants no budget.",
            Arguments(("body", "The complete goal record body, including plan.flow.paths for a flow run when supplied."), ("supersedes", "The current goal record id, when proposing a revision.")), ["body"],
            async (api, arguments, token) => Outcome(await api.GoalProposeAsync(new GoalProposeRequest(by, Body(arguments, "body"), OptionalArg(arguments, "supersedes")), token).ConfigureAwait(false)), ["body"]) with { OperatorOnly = true };
        yield return Write("goal_activate", "Activate a proposed goal under the same authority as the command line. A flow run is refused if an agent work step has no permitted path pattern.",
            Arguments(("goal", "Goal record id (ULID), any revision."), ("note", "Optional activation note.")), ["goal"],
            async (api, arguments, token) => Outcome(await api.GoalActivateAsync(Arg(arguments, "goal"), new GoalActionRequest(by, OptionalArg(arguments, "note")), token).ConfigureAwait(false))) with { OperatorOnly = true };
        yield return Write("goal_step", "Take an active goal's next step under the same authority as the command line. With check true, preview its route and permitted change patterns without signing, recording or dispatching.",
            GoalStepArguments(), ["goal"],
            async (api, arguments, token) =>
            {
                if (!TryStrings(arguments, "search", out IReadOnlyList<string>? search, out string? problem))
                {
                    return new ToolCallResult(problem!, true);
                }

                var request = new GoalStepRequest(by, OptionalArg(arguments, "repository"), arguments?["settings"]?.Deserialize<Dictionary<string, string>>(ProtocolJson.Options),
                    OptionalValue<long>(arguments, "wallMinutes"), OptionalArg(arguments, "branch"), OptionalArg(arguments, "base"), OptionalArg(arguments, "configuration"),
                    arguments?["execution"] is null ? null : Body(arguments, "execution"), arguments?["allocation"] is null ? null : Body(arguments, "allocation"), OptionalArg(arguments, "contextPackage"))
                {
                    Search = search, Occurrence = OptionalArg(arguments, "occurrence"), Now = OptionalArg(arguments, "now"), Check = OptionalValue<bool>(arguments, "check") ?? false,
                };
                GoalStepResponse response = await api.GoalStepAsync(Arg(arguments, "goal"), request, token).ConfigureAwait(false);
                return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), !response.Ok);
            }) with { OperatorOnly = true };
    }

    private static JsonObject GoalStepArguments()
    {
        JsonObject schema = Arguments(("goal", "Goal record id (ULID), any revision."), ("repository", "Repository resource with a clone registered on the runtime machine."),
            ("branch", "Work branch for an agent's run."), ("base", "Starting revision of a new work branch."), ("configuration", "Named local launcher configuration."),
            ("contextPackage", "Expected context package hash."), ("occurrence", "Standing agent occurrence key, or request."), ("now", "UTC time for a standing agent's triggers."));
        JsonNode properties = schema["properties"]!;
        properties["settings"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "string" }, ["description"] = "Settings of the selected launcher." };
        properties["wallMinutes"] = new JsonObject { ["type"] = "integer", ["description"] = "Wall-minute cap for an agent's run." };
        properties["execution"] = new JsonObject { ["type"] = "object", ["description"] = "Explicit run choices." };
        properties["allocation"] = new JsonObject { ["type"] = "object", ["description"] = "Run token reservations and optional context byte allocation." };
        properties["search"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Queries for optional sources in the run's context package." };
        properties["check"] = new JsonObject { ["type"] = "boolean", ["description"] = "Preview the step without recording or dispatching." };
        return schema;
    }

    private static T? OptionalValue<T>(JsonObject? arguments, string name) where T : struct => arguments?[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue<T>(out T parsed) => parsed,
        _ => throw new ArgumentException($"'{name}' has the wrong JSON type."),
    };
}
