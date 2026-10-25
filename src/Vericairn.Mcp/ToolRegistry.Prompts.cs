// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;

namespace Vericairn.Mcp;

public sealed partial class ToolRegistry
{
    /// <summary>Standard MCP prompt descriptors, ordered by category and filtered for the connected principal.</summary>
    public async Task<JsonObject> PromptsListAsync(CancellationToken cancellationToken = default)
    {
        PromptsResponse response = await api.PromptsAsync(new(actingPrincipal), cancellationToken).ConfigureAwait(false);
        if (response.Problem is { Code: "unsupported" }) return new JsonObject { ["prompts"] = new JsonArray() };
        if (response.Problem is { } problem) throw new InvalidOperationException(problem.Message);
        return new JsonObject { ["prompts"] = new JsonArray(response.Prompts.Where(p => actingPrincipal is not null || p.Definition.GetProperty("roles").GetArrayLength() == 0).Select(p => (JsonNode)DescribePrompt(p)).ToArray()) };
    }

    /// <summary>Records a prompt use through its admitted target before returning standard MCP messages.</summary>
    public async Task<JsonObject> PromptGetAsync(string name, JsonObject? arguments, CancellationToken cancellationToken = default)
    {
        if (!await WritesVisibleAsync(cancellationToken).ConfigureAwait(false)) throw new UnauthorizedAccessException("A recorded prompt use requires an admitted acting principal and signing device.");
        PromptsResponse library = await api.PromptsAsync(new(actingPrincipal), cancellationToken).ConfigureAwait(false);
        if (library.Problem is { } problem) throw new InvalidOperationException(problem.Message);
        PromptRow row = library.Prompts.SingleOrDefault(p => p.Identity == name) ?? throw new ArgumentException("The prompt is not offered to this connection.", nameof(name));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, JsonNode? value) in arguments ?? [])
        {
            if (key is "_target" or "_expected" or "_favor") continue;
            if (value is not JsonValue scalar || !scalar.TryGetValue(out string? text)) throw new ArgumentException("Prompt arguments are strings.", nameof(arguments));
            values.Add(key, text);
        }
        string expected = Arg(arguments, "_expected");
        JsonObject target = JsonNode.Parse(Arg(arguments, "_target")) as JsonObject ?? throw new ArgumentException("_target is an object encoded as a JSON string.", nameof(arguments));
        if (target.Any(p => p.Key is not ("assignment" or "run" or "ask"))) throw new ArgumentException("A prompt target is an assignment, run or ask; authority changes require an amendment.", nameof(arguments));
        target["by"] = actingPrincipal;
        foreach (string field in new[] { "assignment", "run" })
        {
            if (target[field] is JsonObject operation)
            {
                if (operation["by"] is not null && operation["by"]!.GetValue<string>() != actingPrincipal) throw new ArgumentException("A prompt target cannot impersonate another author.", nameof(arguments));
                operation["by"] = actingPrincipal;
            }
        }
        target["selection"] = JsonSerializer.SerializeToNode(new PromptSelection(name, expected, values), ProtocolJson.Options);
        if (OptionalArg(arguments, "_favor") is { } favor)
            target["favor"] = bool.TryParse(favor, out bool selected) ? selected : throw new ArgumentException("_favor is true or false.", nameof(arguments));
        PromptUseRequest request = target.Deserialize<PromptUseRequest>(ProtocolJson.Options)!;
        PromptUseResponse used = await api.PromptUseAsync(request, cancellationToken).ConfigureAwait(false);
        if (!used.Ok)
        {
            string message = used.Problem?.Message ?? used.Run?.Message ?? "The prompt use did not complete.";
            if (used.Written is { } written)
                message = $"Use {written.Id} at {written.Hash} is recorded. Inspect that target before retrying. " + message;
            throw new InvalidOperationException(message);
        }
        return new JsonObject
        {
            ["description"] = row.Definition.GetProperty("title").GetString() + " (recorded use)",
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonObject { ["type"] = "text", ["text"] = used.Text } }),
            ["_meta"] = new JsonObject { ["vericairn/record"] = used.Written?.Id, ["vericairn/hash"] = used.Written?.Hash },
        };
    }

    private static JsonObject DescribePrompt(PromptRow row)
    {
        JsonElement definition = row.Definition;
        var arguments = new JsonArray(definition.GetProperty("placeholders").EnumerateArray().Select(p => (JsonNode)new JsonObject
        {
            ["name"] = p.GetProperty("name").GetString(), ["description"] = p.GetProperty("description").GetString(), ["required"] = true,
        }).ToArray());
        arguments.Add(new JsonObject { ["name"] = "_target", ["description"] = "JSON object naming exactly one new assignment request, new run request, or ask id. The connected principal is the author. This operation records the use.", ["required"] = true });
        arguments.Add(new JsonObject { ["name"] = "_expected", ["description"] = "Exact definition hash from this offer: " + row.Hash, ["required"] = true });
        arguments.Add(new JsonObject { ["name"] = "_favor", ["description"] = "true to also favor this prompt after successful use; false by default.", ["required"] = false });
        return new JsonObject
        {
            ["name"] = row.Identity, ["title"] = definition.GetProperty("category").GetString() + " / " + definition.GetProperty("title").GetString(),
            ["description"] = definition.GetProperty("description").GetString() + " Source: " + row.Source + " @ " + row.Version + (row.Draft ? ". Draft awaiting operator review; not available for use." : "."),
            ["arguments"] = arguments,
            ["_meta"] = new JsonObject { ["vericairn/category"] = definition.GetProperty("category").GetString(), ["vericairn/source"] = row.Source, ["vericairn/version"] = row.Version, ["vericairn/hash"] = row.Hash, ["vericairn/favorite"] = row.Favorite, ["vericairn/draft"] = row.Draft },
        };
    }

    private IEnumerable<ToolDefinition> PromptTools()
    {
        string by = actingPrincipal ?? string.Empty;
        yield return Read("prompts_list", "List the connected principal's prompts by category, with exact source, version and personal preferences. Reads record no use.",
            Arguments(("category", "Optional category, including its nested categories.")), async (service, a, token) =>
            {
                PromptsResponse response = await service.PromptsAsync(new(actingPrincipal, OptionalArg(a, "category")), token).ConfigureAwait(false);
                if (actingPrincipal is null) response = response with { Prompts = response.Prompts.Where(p => p.Definition.GetProperty("roles").GetArrayLength() == 0).ToArray() };
                return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), response.Problem is not null);
            });
        yield return Write("prompt_save", "Add, revise or retire a shared prompt under the operator's authority or an explicit manage-prompts grant.",
            Arguments(("definition", "Complete saved-prompt body."), ("expected", "Expected prior hash for a revision.")), ["definition"],
            async (service, a, token) => Outcome(await service.PromptSaveAsync(new(by, Body(a, "definition"), OptionalArg(a, "expected")), token).ConfigureAwait(false)), ["definition"]);
        yield return Write("prompt_preference", "Change this principal's favorite or hidden choice, or disable estate-wide when the operator acts. Choices are reversible and retain history.",
            Arguments(("body", "PromptPreferenceRequest fields, without by; disabled is an operator-only change.")), ["body"],
            async (service, a, token) => Outcome(await service.PromptPreferenceAsync(PromptRequest<PromptPreferenceRequest>(a, by), token).ConfigureAwait(false)), ["body"]);
        yield return Write("prompt_copy", "Copy a reviewed built-in into the estate with its exact origin; requires shared prompt writing authority.",
            Arguments(("body", "PromptCopyRequest fields: prompt, expected and new key; without by.")), ["body"],
            async (service, a, token) => Outcome(await service.PromptCopyAsync(PromptRequest<PromptCopyRequest>(a, by), token).ConfigureAwait(false)), ["body"]);
        yield return Write("prompt_use", "Record a prompt together with a new run, assignment or ask answer. It cannot change authority; changes require an amendment.",
            Arguments(("body", "PromptUseRequest fields without by: selection and exactly one assignment, run or ask. The nested target must name the connected principal as by.")), ["body"],
            async (service, a, token) =>
            {
                PromptUseResponse response = await service.PromptUseAsync(PromptRequest<PromptUseRequest>(a, by), token).ConfigureAwait(false);
                return new ToolCallResult(JsonSerializer.Serialize(response, ProtocolJson.Options), !response.Ok);
            }, ["body"]);
    }

    private static T PromptRequest<T>(JsonObject? arguments, string by)
    {
        JsonObject body = JsonNode.Parse(Body(arguments, "body").GetRawText())!.AsObject();
        if (body["by"] is not null && body["by"]!.GetValue<string>() != by) throw new ArgumentException("A prompt request cannot impersonate another author.", nameof(arguments));
        body["by"] = by;
        return body.Deserialize<T>(ProtocolJson.Options)!;
    }
}
