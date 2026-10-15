// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The state status derives for an ask (record format 2, the estate's design/goals.md § 8): answered once a current decision names
/// any revision of it in its answers, unless its asker withdrew it; otherwise the state its asker recorded, open or notified or
/// withdrawn. Waiting never answers an ask. The goal predicates, the progress report and every surface read this one derivation.
/// </summary>
public static class Asks
{
    /// <summary>The states that still wait on the principal the ask is put to: open, and notified, whose delivery stays unknown.</summary>
    public static readonly string[] Waiting = ["open", "notified"];

    /// <summary>An ask's derived state and the current decisions that answer it, oldest first.</summary>
    public static (string State, IReadOnlyList<RecordEnvelope> Answers) State(EstateModel model, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);
        RecordEnvelope latest = model.Latest(ask);
        string recorded = EstateModel.BodyString(latest, "state") ?? "open";
        if (recorded == "withdrawn")
        {
            return (recorded, []);
        }

        IReadOnlyList<string> chain = model.Chain(latest);
        List<RecordEnvelope> answers = model.Current("decision")
            .Where(d => d.Body.TryGetProperty("answers", out JsonElement ids) && ids.ValueKind == JsonValueKind.Array && ids.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.String && chain.Contains(i.GetString()!)))
            .OrderBy(d => d.At, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        return (answers.Count > 0 ? "answered" : recorded, answers);
    }

    /// <summary>True when the state still waits on the principal the ask is put to.</summary>
    public static bool IsWaiting(string state) => Array.IndexOf(Waiting, state) >= 0;
}
