// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Cli;

internal sealed class RecordsNewCommand : Command
{
    public RecordsNewCommand(CommandContext context)
        : base("new", "Create a record in canonical form, validate the estate with it, and write it.")
    {
        var typeArgument = new Argument<string>("type") { Description = "Record type, for example work-order." };
        var byOption = new Option<string>("--by") { Description = "Authoring principal id (ULID).", Required = true };
        var bodyOption = new Option<string>("--body") { Description = "Path to a JSON file holding the body, or '-' for standard input.", Required = true };
        var subjectOption = new Option<string?>("--subject") { Description = "Subject as type:id or type:id:hash." };
        var supersedesOption = new Option<string?>("--supersedes") { Description = "Id of the record this one replaces." };
        var atOption = new Option<string?>("--at") { Description = "Authoring time, for example 2026-10-13T14:00:00Z (default: now)." };
        var idOption = new Option<string?>("--id") { Description = "Explicit record id; only the root operator principal needs this." };
        var expectedOption = new Option<string?>("--expected") { Description = "Required for policy or placement authoring: expected predecessor or policy hash, or the canonical body hash for a creation." };
        Arguments.Add(typeArgument);
        Options.Add(byOption);
        Options.Add(bodyOption);
        Options.Add(subjectOption);
        Options.Add(supersedesOption);
        Options.Add(atOption);
        Options.Add(idOption);
        Options.Add(expectedOption);
        SetAction(parseResult => Execute(context,
            parseResult.GetValue(context.EstateOption)!,
            parseResult.GetValue(typeArgument)!,
            parseResult.GetValue(byOption)!,
            parseResult.GetValue(bodyOption)!,
            parseResult.GetValue(subjectOption),
            parseResult.GetValue(supersedesOption),
            parseResult.GetValue(atOption),
            parseResult.GetValue(idOption),
            parseResult.GetValue(expectedOption),
            parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string type, string by, string bodyPath, string? subject, string? supersedes, string? at, string? id, string? expected, bool json) =>
        context.WithBody(bodyPath, body => context.WithApi(root, api =>
        {
            WriteOutcome outcome = api.NewRecord(new NewRecordRequest(type, by, body, subject, supersedes, at, id, expected));
            int code = context.ReportOutcome(outcome, json);
            if (outcome.Ok && type == "principal" && AgentProductWarning(body, ProviderRegistry.Current.Launchers) is { } warning)
                context.Error.WriteLine("warning: " + warning);
            return code;
        }));

    // Routing matches an agent's product to a launcher's harness name or id, ignoring case. A product no installed launcher
    // matches is still recorded, but goal next would report the agent unavailable until a signed revision names one.
    internal static string? AgentProductWarning(JsonElement body, IReadOnlyList<IAgentLauncher> launchers)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "agent")
            return null;
        string? product = body.TryGetProperty("agent", out JsonElement agent) && agent.ValueKind == JsonValueKind.Object
            && agent.TryGetProperty("product", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        List<IAgentLauncher> routed = launchers.Where(l => l.Harness is not null).ToList();
        if (routed.Count == 0 || product is not null && routed.Any(l => string.Equals(l.Harness, product, StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Launcher, product, StringComparison.OrdinalIgnoreCase)))
            return null;
        string accepted = string.Join("; ", routed.Select(l => $"{l.Harness} or {l.Launcher}"));
        return $"the agent's product {(product is null ? "is missing" : $"'{product}' matches no installed launcher")}, so goal next will report it unavailable; routing accepts {accepted}.";
    }
}
