// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed partial class WorkOperations
{
    public int WorkProgress(string root, string attempt, string by, string phase, string note, bool completed, string? progressPath, string? statusText, bool json)
    {
        int Submit(JsonElement? progress) => context.WithApi(root, api =>
        {
            WorkProgressResponse response = api.WorkProgress(new WorkProgressRequest(attempt, by, phase, note, completed)
                { Progress = progress, StatusText = statusText });
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                return response.Ok ? Clean : Failed;
            }
            if (!response.Ok) return context.ReportOutcome(WriteOutcome.Fail(response.Problem!), false);
            WorkProgressClaim claim = response.Claim!;
            context.Output.WriteLine($"Agent claim for phase {claim.Phase}: {claim.Note}");
            if (claim.Progress is JsonElement value) context.Output.WriteLine(Describe(value));
            if (claim.StatusText is not null) context.Output.WriteLine(claim.StatusText);
            context.Output.WriteLine(response.Written is { } written ? $"{written.Path}  {written.Id}  {written.Hash}" : "Live state; no record written.");
            return Clean;
        });
        return progressPath is null ? Submit(null) : context.WithBody(progressPath, value => Submit(value));
    }

    private static string Describe(JsonElement value)
    {
        string Number(string name) => value.GetProperty(name).GetInt64().ToString(CultureInfo.InvariantCulture);
        return value.GetProperty("type").GetString() switch
        {
            "accurate" => Number("done") + (value.TryGetProperty("total", out _) ? " / " + Number("total") : string.Empty)
                + " " + value.GetProperty("unit").GetString()
                + (value.TryGetProperty("estimatePercent", out _) ? "; best-effort estimate " + Number("estimatePercent") + "%" : string.Empty),
            "best-effort" => "Best-effort estimate " + Number("percent") + "%",
            _ => "In progress (not measurable)",
        };
    }
}
