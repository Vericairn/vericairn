// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

public static partial class SavedPrompts
{
    /// <summary>The additive use field on assignments, run intents and ask-answer decisions.</summary>
    public static FieldDescription UseField { get; } = new("promptUse", "object", false,
        "Exact prompt source, key, revision/version, hash, definition, placeholder values and rendered comment. Authority is unchanged. Kept in run and decision revisions.", 3);

    /// <summary>Validates a self-contained use; installed catalogs are not needed to read historical versions.</summary>
    public static void ValidateUse(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Body.Since(3, "promptUse")) return;
        BodyReader? use = context.Body.Nested("promptUse", false);
        if (context.Record.Type is "operation" or "decision" && context.Find(context.Record.Supersedes) is { } previous
            && !SameField(previous.Body, context.Record.Body, "promptUse"))
            context.Error("prompt.use-kept", "A run or answer keeps its original prompt use unchanged; steering or a new answer requires its own authorized operation.");
        if (use is null) return;
        RequireSigned(context);
        string? source = use.Text("source", true, 80, Patterns.Slug);
        string? key = use.Text("key", true, 80, Patterns.Slug);
        string? version = use.Text("version", true, 200);
        string? hash = use.Hash("hash", true);
        string? rendered = use.Text("rendered", true, 40000);
        if (use.Text("authority", true, 30) != "unchanged")
            context.Error("prompt.authority", "A prompt cannot change outcome, target, budget, data disclosure or permitted effects; request an amendment.");
        BodyReader? definition = use.Nested("definition", true);
        if (definition is not null)
        {
            ValidateDefinition(definition);
            definition.Enum("state", false, ["active", "retired"]);
            if (definition.Nested("origin", false) is { } origin) ValidateOrigin(origin);
            definition.NoOtherKeys();
            if (key != String(definition.Element, "key")) context.Error("prompt.source", "The prompt key must match its pinned definition.");
            if (source == "estate")
            {
                if (context.RequireReference("promptUse.version", version, "saved-prompt") is not { } prompt
                    || prompt.ComputedHash != hash || CanonicalJson.Serialize(prompt.Body) != CanonicalJson.Serialize(definition.Element)
                    || prompt.AtTime > context.Record.AtTime || EstateModel.BodyString(prompt, "state") != "active")
                    context.Error("prompt.source", "An estate prompt use pins the exact saved-prompt revision, hash and definition.");
            }
            else if (ContentHash.Sha256(CanonicalJson.SerializeUtf8(definition.Element)) != hash)
                context.Error("prompt.source", "A built-in use keeps the exact canonical definition hash for its recorded source and version.");
        }
        BodyReader? values = use.Nested("values", true);
        if (values is not null && definition is not null)
        {
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty value in values.Element.EnumerateObject())
            {
                if (values.Text(value.Name, true, 4000) is { } text) arguments[value.Name] = text;
            }
            try
            {
                if (Render(definition.Element, arguments) != rendered) context.Error("prompt.values", "Rendered text must be exactly the literal substitution of the recorded values.");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                context.Error("prompt.values", "The prompt definition or its placeholder values are incomplete or invalid.");
            }
        }
        use.NoOtherKeys();
        if (context.Record.Type == "operation" && EstateModel.BodyString(context.Record, "action") != OperationType.AgentRun)
            context.Error("prompt.target", "An operation uses a prompt only with a new agent run.");
        if (context.Record.Type == "decision")
        {
            if (!context.Record.Body.TryGetProperty("answers", out JsonElement answers) || answers.ValueKind != JsonValueKind.Array || answers.GetArrayLength() != 1
                || EstateModel.BodyString(context.Record, "decidedBy") != context.Record.By
                || EstateModel.BodyString(context.Record, "statement") != rendered)
                context.Error("prompt.answer", "A prompt answer is the actual recipient's signed decision for one ask, with the rendered text as its verbatim statement.");
            if (context.Record.Body.TryGetProperty("affects", out JsonElement affects) && affects.ValueKind == JsonValueKind.Array && affects.GetArrayLength() != 0
                || context.Record.Body.TryGetProperty("policies", out _) || context.Record.Body.TryGetProperty("documents", out _) || context.Record.Body.TryGetProperty("obligations", out _))
                context.Error("prompt.authority", "A prompt answer cannot change work, policies, document scope or obligations; request an amendment or a separate authorized decision.");
        }
    }

    private static string? String(JsonElement value, string field) => value.TryGetProperty(field, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
