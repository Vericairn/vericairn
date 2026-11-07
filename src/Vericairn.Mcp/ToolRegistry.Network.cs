// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

/// <summary>What a network session's call may do with a record (MCP-03).</summary>
public enum McpSigning
{
    /// <summary>Write no record: a read, or a write that keeps live state only.</summary>
    Read,

    /// <summary>Prepare the call's one record for the requesting device's signature, writing nothing.</summary>
    Prepare,

    /// <summary>Write the call's one record with the requesting device's signature over the plan it prepared.</summary>
    Sign,
}

/// <summary>
/// A network MCP session in signed mode (MCP-03): an admitted agent or service device on another machine, whose every request it
/// signed. The transport runs each call through the runtime as its API operation for that device, under the signing rule the call
/// asks for, and says what record a preparing call prepared. It adds no permission: the runtime judges every operation.
/// </summary>
public interface IMcpNetworkSession
{
    /// <summary>The principal of the requesting device: the only one its calls act as.</summary>
    string Principal { get; }

    /// <summary>
    /// Runs one call's <paramref name="operation"/> under a signing rule. For <see cref="McpSigning.Sign"/>, <paramref name="plan"/> and <paramref name="signature"/>
    /// name the prepared record and the device's signature of it. Returns the call's result and, for a preparing call that reached
    /// its record, the prepared record; null otherwise.
    /// </summary>
    Task<(T Result, RecordPrepared? Prepared)> RunAsync<T>(McpSigning signing, string? plan, string? signature, Func<Task<T>> operation);
}

public sealed partial class ToolRegistry
{
    /// <summary>The writes section 8 of the network design offers a contributor; every other write is absent over the network.</summary>
    public static readonly IReadOnlySet<string> NetworkWrites = new HashSet<string>(StringComparer.Ordinal)
    {
        "work_attempt", "work_progress", "work_result", "work_amend", "document_review",
    };

    /// <summary>The _meta key a client sets true to have a write prepared for its device's signature.</summary>
    public const string PrepareMeta = "vericairn/prepare";

    /// <summary>The _meta key of the prepared record: in a preparing call's result, and in the signing call that repeats it.</summary>
    public const string PlanMeta = "vericairn/plan";

    /// <summary>The _meta key of the device's signature of the prepared record's digest, in the signing call.</summary>
    public const string SignatureMeta = "vericairn/signature";

    private readonly IMcpNetworkSession? network;

    /// <summary>
    /// From 0.9.2 (MCP-03): the registry of a network session in signed mode, acting as the requesting device's principal. It offers
    /// section 8's set: every read, and the contributor writes, each prepared for the device and written with its signature.
    /// </summary>
    public static ToolRegistry ForNetwork(IEstateApi api, IMcpNetworkSession network)
    {
        ArgumentNullException.ThrowIfNull(network);
        return new ToolRegistry(api, network.Principal, network);
    }

    private void OfferOverNetwork()
    {
        for (int i = 0; i < tools.Count; i++)
        {
            if (tools[i].Name == "work_result")
            {
                // Duty evidence cannot be prepared for a device's signature, so its expected hash is not offered over the network.
                JsonObject schema = (JsonObject)tools[i].InputSchema.DeepClone();
                ((JsonObject)schema["properties"]!).Remove("expected");
                ((JsonObject)schema["properties"]!["body"]!)["description"] = "The complete result body: outcome, summary, outputs, evidence, checks, limitations, unresolvedEffects and nextStep. Duty evidence is not offered over the network.";
                tools[i] = tools[i] with { InputSchema = schema };
            }
        }
    }

    /// <summary>Whether this registry serves a network session.</summary>
    public bool Network => network is not null;

    /// <summary>Whether a tool is in section 8's network set: every read, and the contributor writes.</summary>
    public static bool OfferedOverNetwork(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return !tool.Writes || NetworkWrites.Contains(tool.Name);
    }

    /// <summary>A tool as tools/list presents it to this connection: over the network a write says it is prepared and signed.</summary>
    public JsonObject Describe(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        JsonObject described = tool.Describe();
        if (network is not null && tool.Writes)
        {
            described["_meta"] = new JsonObject { ["vericairn/prepared"] = true };
        }

        return described;
    }

    private async Task<ToolCallResult> CallOverNetworkAsync(IMcpNetworkSession session, ToolDefinition? tool, JsonObject? arguments, JsonObject? meta, CancellationToken cancellationToken)
    {
        if (tool is null || !OfferedOverNetwork(tool))
        {
            return new ToolCallResult(string.Empty, true, Unknown: true);
        }

        if (!tool.Writes || tool.Name == "work_progress" && OptionalArg(arguments, "state") == "active")
        {
            // A read, or an active progress note, which keeps live state and no record: nothing to prepare or sign.
            (ToolCallResult read, _) = await session.RunAsync(McpSigning.Read, null, null, () => tool.InvokeAsync(api, arguments, cancellationToken)).ConfigureAwait(false);
            return read;
        }

        if (tool.Name == "work_result" && (arguments?.ContainsKey("expected") == true || arguments?["body"] is JsonObject body && body.ContainsKey("duties")))
        {
            return new ToolCallResult("Duty evidence and its expected hash are not offered over network MCP: they cannot be prepared for a device's signature. Submit the result without duties.", true);
        }

        bool prepare = meta?[PrepareMeta] is JsonValue flag && flag.TryGetValue(out bool asked) && asked;
        string? plan = PlanOf(meta?[PlanMeta]);
        string? signature = meta?[SignatureMeta] is JsonValue signed && signed.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;
        if (prepare && (plan is not null || signature is not null))
        {
            return new ToolCallResult($"A call either prepares ({PrepareMeta}) or signs ({PlanMeta} with {SignatureMeta}), not both.", true);
        }

        if (prepare)
        {
            (ToolCallResult outcome, RecordPrepared? prepared) = await session.RunAsync(McpSigning.Prepare, null, null, () => tool.InvokeAsync(api, arguments, cancellationToken)).ConfigureAwait(false);
            if (prepared is null)
            {
                return outcome;
            }

            return new ToolCallResult($"Prepared record {prepared.Id} ({prepared.Hash}) for this device's signature; nothing was written. Repeat this call with the same arguments, {PlanMeta} {prepared.Plan} and {SignatureMeta}, the device's signature of the record's signing digest, before {prepared.ExpiresAt}.", false)
            {
                Meta = new JsonObject { [PlanMeta] = JsonSerializer.SerializeToNode(new RecordPrepareResponse(true, prepared), ProtocolJson.Options) },
            };
        }

        if (plan is null || signature is null)
        {
            return new ToolCallResult($"A network write takes two calls: with _meta {PrepareMeta} true the runtime prepares its record and writes nothing; the same call repeated with {PlanMeta} and {SignatureMeta}, this device's signature of that record, writes it. Nothing was written.", true);
        }

        (ToolCallResult written, _) = await session.RunAsync(McpSigning.Sign, plan, signature, () => tool.InvokeAsync(api, arguments, cancellationToken)).ConfigureAwait(false);
        return written;
    }

    /// <summary>The plan a signing call names: the plan's id, or the prepared record or preparation the preparing call returned.</summary>
    private static string? PlanOf(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) => text,
        JsonObject response when response["prepared"] is JsonObject prepared => PlanOf(prepared["plan"]),
        JsonObject prepared when prepared["plan"] is JsonNode inner => PlanOf(inner),
        _ => null,
    };
}
