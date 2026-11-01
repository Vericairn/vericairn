// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

public sealed partial class ToolRegistry
{
    private static WorkProgressRequest ReadProgress(JsonObject? arguments, string by)
    {
        if (arguments is not null && arguments.Any(p => p.Key is not ("attempt" or "phase" or "note" or "state" or "progress" or "statusText")))
            throw new ArgumentException("Progress arguments are attempt, phase, note, state, and optional progress and statusText.");
        bool completed = Arg(arguments, "state") switch { "active" => false, "completed" => true, _ => throw new ArgumentException("state is active or completed.") };
        return new WorkProgressRequest(Arg(arguments, "attempt"), by, Arg(arguments, "phase"), Arg(arguments, "note"), completed)
        {
            Progress = arguments?.ContainsKey("progress") == true ? Body(arguments, "progress") : null,
            StatusText = ProgressStatus(arguments),
        };
    }

    private static JsonObject ProgressArguments()
    {
        JsonObject schema = Arguments(("attempt", "Attempt record id."), ("phase", "Stable lower-case phase name."),
            ("note", "The contributor's progress claim, without secrets or machine paths."), ("state", "active or completed."));
        schema["properties"]!["progress"] = ProgressSchemas.Value();
        schema["properties"]!["statusText"] = ProgressSchemas.StatusText();
        return schema;
    }

    private static string? ProgressStatus(JsonObject? arguments)
    {
        if (arguments is null || !arguments.ContainsKey("statusText")) return null;
        // OptionalArg intentionally treats empty strings as absent for older tools. A supplied status is validated, never dropped.
        if (arguments["statusText"] is JsonValue value && value.TryGetValue(out string? text) && text is not null) return text;
        throw new ArgumentException("'statusText' must be a string when supplied.");
    }
}
