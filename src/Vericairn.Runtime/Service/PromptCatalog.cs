// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

/// <summary>Installed immutable prompt contributions. Registering a catalog grants no estate authority.</summary>
public sealed class PromptCatalog
{
    private readonly Dictionary<string, PromptRow> entries = new(StringComparer.Ordinal);

    /// <summary>Installs the foundation's review drafts.</summary>
    public PromptCatalog() => Register(Foundation());

    /// <summary>The installed definitions in stable source/key order.</summary>
    public IReadOnlyList<PromptRow> All { get { lock (entries) return entries.Values.OrderBy(p => p.Identity, StringComparer.Ordinal).ToArray(); } }

    /// <summary>Register an exact pack contribution atomically; conflicting identities are refused.</summary>
    public void Register(IEnumerable<PromptRow> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        PromptRow[] proposed = prompts.ToArray();
        foreach (PromptRow prompt in proposed)
        {
            var findings = new FindingList();
            var body = new BodyReader(prompt.Definition, "prompt", findings, 3);
            SavedPrompts.ValidateDefinition(body);
            body.NoOtherKeys();
            if (findings.HasErrors || prompt.Source == "estate" || prompt.Identity != prompt.Source + ":" + prompt.Key
                || !SavedPrompts.IdentityPattern.IsMatch(prompt.Identity) || string.IsNullOrWhiteSpace(prompt.Version)
                || prompt.Hash != ContentHash.Sha256(CanonicalJson.SerializeUtf8(prompt.Definition))
                || prompt.Key != prompt.Definition.GetProperty("key").GetString())
                throw new ArgumentException("A catalog contribution needs a valid definition and exact source, key, version and hash.", nameof(prompts));
        }
        lock (entries)
        {
            if (proposed.Select(p => p.Identity).Distinct(StringComparer.Ordinal).Count() != proposed.Length
                || proposed.Any(p => entries.TryGetValue(p.Identity, out PromptRow? prior) && prior != p))
                throw new ArgumentException("A prompt identity already has a different installed definition.", nameof(prompts));
            foreach (PromptRow prompt in proposed) entries[prompt.Identity] = prompt;
        }
    }

    /// <summary>Create a content-hashed built-in from a complete definition; drafts cannot be used.</summary>
    public static PromptRow BuiltIn(string source, string version, JsonElement definition, bool draft = true)
    {
        string key = definition.GetProperty("key").GetString()!;
        return new(source + ":" + key, source, key, version, ContentHash.Sha256(CanonicalJson.SerializeUtf8(definition)), definition.Clone(), Draft: draft);
    }

    /// <summary>Construct a one-placeholder definition for a shipped contribution.</summary>
    public static JsonElement Definition(string key, string title, string description, string category, string text, string argument, string argumentDescription, params string[] roles) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["key"] = key, ["title"] = title, ["description"] = description, ["category"] = category, ["text"] = text,
            ["placeholders"] = new JsonArray(new JsonObject { ["name"] = argument, ["description"] = argumentDescription }),
            ["tags"] = new JsonArray(), ["roles"] = new JsonArray(roles.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
        });

    private static IEnumerable<PromptRow> Foundation()
    {
        string version = typeof(PromptCatalog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        yield return BuiltIn("foundation", version, Definition("clarify-next-step", "Clarify the next step", "Identify the next useful action within the assignment.", "Work/Planning", "For {topic}, identify the next action within the existing assignment and explain any missing evidence. Request an amendment for changed terms.", "topic", "The part of the assigned work to consider."));
        yield return BuiltIn("foundation", version, Definition("summarize-evidence", "Summarize the evidence", "Separate observations, claims and unknowns.", "Work/Review", "Summarize the already permitted evidence about {topic}, separating observations, claims and unknowns. Keep the existing disclosure limits.", "topic", "The evidence topic."));
        yield return BuiltIn("foundation", version, Definition("review-within-scope", "Review within scope", "Assess work against its existing criteria.", "Work/Review", "Review {topic} against the assignment's existing review criteria. Cite evidence and unresolved findings; this review does not accept the work.", "topic", "The assigned work to review.", "reviewer", "operator"));
        yield return BuiltIn("foundation", version, Definition("prepare-handoff", "Prepare a handoff", "Explain the current state for the next contributor.", "Work/Handoff", "Prepare a handoff for {topic}: completed work, exact evidence, limitations and next action, within the existing disclosure and output rules.", "topic", "The assigned work to hand off."));
    }
}
