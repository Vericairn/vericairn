// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (OPS-24, decision 01M7GYC4BKATR834HV9KBQPBAD): one reviewed act over every operator item whose command is ready. Each kind
/// of item, what its signature records, and the exact body each surface authors for it, so the command line, Workbench and Remote sign the
/// same records: an acceptance of a reviewed result, an answer with the ask's recommendation (<see cref="OperatorAnswers"/>), an
/// acceptance of a goal, a release candidate, a milestone or a milestone's result, and a request's close as done, or dismissed with the
/// operator's reason.
/// </summary>
public static class OperatorActs
{
    /// <summary>The kinds of ready item, in the order an act lists them.</summary>
    public static readonly ImmutableArray<string> Kinds =
        ["close", "answer", "goal-acceptance", "release-acceptance", "milestone-acceptance", "milestone-result-acceptance", "request"];

    /// <summary>What an operator chooses for each listed item: sign its record, leave it waiting, or dismiss it with a reason.</summary>
    public static readonly ImmutableArray<string> Choices = ["sign", "wait", "dismiss"];

    /// <summary>The statement an acceptance of an item of this kind records, the operator's words as the act gives them.</summary>
    public static string AcceptanceStatement(string kind) => kind switch
    {
        "close" => "Accepted with operator act after inspecting the exact result and its passing reviews.",
        "goal-acceptance" => "Accepted with operator act after inspecting the goal and its current evaluation.",
        "release-acceptance" => "Accepted with operator act after inspecting the exact release candidate.",
        "milestone-acceptance" => "Accepted with operator act after inspecting the exact milestone revision.",
        "milestone-result-acceptance" => "Accepted with operator act after inspecting the exact result toward the milestone.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an acceptance an operator act signs."),
    };

    /// <summary>The body of an act's acceptance of an item: its statement, the exact basis shown and whether the operator authored the subject.</summary>
    public static JsonObject Acceptance(string kind, IEnumerable<string> basis, bool self)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return new JsonObject
        {
            ["statement"] = AcceptanceStatement(kind),
            ["basis"] = new JsonArray([.. basis.Order(StringComparer.Ordinal).Select(b => (JsonNode)JsonValue.Create(b)!)]),
            ["self"] = self,
        };
    }

    /// <summary>The body of an act's close of a request: done, or dismissed with the operator's reason.</summary>
    public static JsonObject RequestClose(string? dismissReason) => dismissReason is null
        ? new JsonObject { ["outcome"] = "done" }
        : new JsonObject { ["outcome"] = "dismissed", ["reason"] = dismissReason };
}
