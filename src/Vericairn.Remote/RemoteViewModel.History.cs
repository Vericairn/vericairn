// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    // The runtime's work projection contains current assignments. A notice can still refer to an earlier assignment:
    // retain its exact brief and attempt/result records instead of substituting the new assignment's scope.
    private static async Task<ObserveResponse> IncludePinnedAssignmentAsync(IEstateApi api, ObserveResponse work, RecordResponse assignment)
    {
        if (RemoteWorkDetail.Rows(work.Json, "assignments").Any(a => RemoteWorkDetail.Text(a, "id") == assignment.Id)) return work;
        IReadOnlyList<RecordResponse> attempts = await api.RecordsAsync("attempt").ConfigureAwait(true);
        IReadOnlyList<RecordResponse> results = await api.RecordsAsync("result").ConfigureAwait(true);
        var history = new JsonArray();
        foreach (RecordResponse attempt in attempts.Where(r => r.Current && Subject(r) == assignment.Id).OrderBy(r => RemoteWorkDetail.Text(r.Envelope, "at"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            JsonElement body = RemoteWorkDetail.Property(attempt.Envelope, "body");
            RecordResponse? result = results.Where(r => r.Current && Subject(r) == attempt.Id).OrderBy(r => RemoteWorkDetail.Text(r.Envelope, "at"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            history.Add(new JsonObject
            {
                ["id"] = attempt.Id, ["hash"] = attempt.Hash, ["by"] = RemoteWorkDetail.Text(attempt.Envelope, "by"),
                ["startedAt"] = RemoteWorkDetail.Text(body, "startedAt"), ["note"] = RemoteWorkDetail.Text(body, "note"),
                ["result"] = result is null ? null : new JsonObject
                {
                    ["id"] = result.Id, ["hash"] = result.Hash, ["at"] = RemoteWorkDetail.Text(result.Envelope, "at"),
                    ["state"] = "historical context; current acceptance not observed in this detail",
                    ["body"] = JsonNode.Parse(RemoteWorkDetail.Property(result.Envelope, "body").GetRawText()),
                },
            });
        }
        JsonObject projection = JsonNode.Parse(work.Json.GetRawText())!.AsObject();
        var assigned = projection["assignments"]!.AsArray();
        assigned.Add(new JsonObject
        {
            ["id"] = assignment.Id, ["hash"] = assignment.Hash, ["historical"] = true,
            ["workOrderHash"] = RemoteWorkDetail.Text(RemoteWorkDetail.Property(assignment.Envelope, "subject"), "hash"),
            ["contributor"] = RemoteWorkDetail.Text(RemoteWorkDetail.Property(assignment.Envelope, "body"), "contributor"),
            ["brief"] = JsonNode.Parse(RemoteWorkDetail.Property(RemoteWorkDetail.Property(assignment.Envelope, "body"), "brief").GetRawText()),
            ["attempts"] = history,
        });
        return work with { Json = JsonSerializer.SerializeToElement(projection, ProtocolJson.Options) };
    }

    private static string Subject(RecordResponse record) => RemoteWorkDetail.Text(RemoteWorkDetail.Property(record.Envelope, "subject"), "id");
}
