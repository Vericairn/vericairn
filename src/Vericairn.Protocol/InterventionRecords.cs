// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vericairn.Protocol;

/// <summary>Exact drafts for reviewed interventions prepared for the acting device's signature.</summary>
public static class InterventionRecords
{
    /// <summary>Prepare a pause of the reviewed goal and active status, with the original request's note.</summary>
    public static NewRecordRequest Pause(string by, string goal, string goalHash, string activeStatusHash, string note) =>
        new("goal-status", by, JsonSerializer.SerializeToElement(new { status = "paused", note, activeStatusHash }),
            $"goal:{goal}:{goalHash}");

    /// <summary>Prepare a cancel or containment of the reviewed run operation.</summary>
    public static NewRecordRequest Stop(string by, string run, string operationRevision, string operationHash, string requestId, bool contain,
        string reason, string? expectedStopReason = null)
    {
        var body = new JsonObject
        {
            ["run"] = run,
            ["operation"] = contain ? "contain" : "cancel",
            ["requestId"] = requestId,
            ["reason"] = reason,
        };
        if (expectedStopReason is not null) body["expectedStopReason"] = expectedStopReason;
        return new("run-stop-request", by, JsonSerializer.SerializeToElement(body), $"operation:{operationRevision}:{operationHash}");
    }
}
