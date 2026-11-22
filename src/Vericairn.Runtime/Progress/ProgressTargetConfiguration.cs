// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Progress;

/// <summary>
/// From 0.9.4 (WATCH-06): one progress target as the operator configured it on this machine: its name, the adapter that places it,
/// chosen by id as products choose their providers, the adapter's settings, its form, its title, and the events that refresh it and
/// those that add a comment (decision 01M784PSWP4N4J9EKZQ3PN6X8G).
/// </summary>
/// <param name="Name">A lower-case kebab-case name.</param>
/// <param name="Adapter">The adapter id, such as github-discussions.</param>
/// <param name="Settings">The adapter's settings, by name.</param>
/// <param name="Form">private or public; a target whose destination is readable beyond its members is always given the public form.</param>
/// <param name="Title">The place's title; null for "Progress of" and the estate's name.</param>
/// <param name="Refresh">The triggers that refresh it, in their text form.</param>
/// <param name="Comment">The triggers that add a comment, in their text form.</param>
internal sealed record ProgressTargetConfiguration(string Name, string Adapter, ImmutableSortedDictionary<string, string> Settings, string Form, string? Title,
    ImmutableArray<ProgressTrigger> Refresh, ImmutableArray<ProgressTrigger> Comment)
{
    /// <summary>The longest title a target takes.</summary>
    internal const int MaxTitle = 200;

    /// <summary>The most triggers one list holds.</summary>
    internal const int MaxTriggers = 32;

    /// <summary>
    /// Why a configuration cannot be used with an adapter, or null when it can: its name, form and title, and every setting the adapter
    /// declares as required present, none it does not declare, and each a single line.
    /// </summary>
    internal string? Problem(IProgressTargetAdapter? adapter)
    {
        if (!Patterns.Slug.IsMatch(Name) || Name.Length > 64) return $"A target's name is lower-case kebab-case, at most 64 characters, not '{Name}'.";
        if (!ProgressReport.TargetForms.Contains(Form, StringComparer.Ordinal)) return $"A target's form is private or public, not '{Form}'.";
        if (Title is not null && (Title.Trim().Length == 0 || Title.Length > MaxTitle || Title.Any(char.IsControl)))
            return $"A target's title is one line of at most {MaxTitle} characters.";
        if (Refresh.Length > MaxTriggers || Comment.Length > MaxTriggers) return $"A target names at most {MaxTriggers} triggers of each kind.";
        if (adapter is null) return $"No progress-target adapter '{Adapter}' is available in this build; nothing selects another.";
        IReadOnlyList<ProviderSetting> declared = adapter.Descriptor.DeclaredSettings;
        foreach (string name in Settings.Keys.Where(n => !declared.Any(d => d.Name == n)))
            return $"The adapter {Adapter} takes no setting '{name}'; it takes {string.Join(", ", declared.Select(d => d.Name))}.";
        foreach (ProviderSetting setting in declared.Where(d => d.Required && !Settings.ContainsKey(d.Name) && d.Default is null))
            return $"The adapter {Adapter} requires the setting '{setting.Name}': {setting.Summary}";
        foreach (KeyValuePair<string, string> setting in Settings.Where(s => s.Value.Trim().Length == 0 || s.Value.Length > 200 || s.Value.Any(char.IsControl)))
            return $"The setting '{setting.Key}' is one line of at most 200 characters.";
        return null;
    }

    /// <summary>The settings given to the adapter: the configured ones, with each declared default for a setting not configured.</summary>
    internal ImmutableSortedDictionary<string, string> Effective(IProgressTargetAdapter adapter)
    {
        ImmutableSortedDictionary<string, string>.Builder settings = Settings.ToBuilder();
        foreach (ProviderSetting setting in adapter.Descriptor.DeclaredSettings.Where(s => s.Default is not null && !settings.ContainsKey(s.Name)))
            settings[setting.Name] = setting.Default!;
        return settings.ToImmutable();
    }

    internal JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["adapter"] = Adapter,
        ["settings"] = new JsonObject([.. Settings.Select(s => KeyValuePair.Create(s.Key, (JsonNode?)JsonValue.Create(s.Value)))]),
        ["form"] = Form,
        ["title"] = Title,
        ["refresh"] = new JsonArray([.. Refresh.Select(t => (JsonNode)JsonValue.Create(t.ToString()))]),
        ["comment"] = new JsonArray([.. Comment.Select(t => (JsonNode)JsonValue.Create(t.ToString()))]),
    };

    internal static ProgressTargetConfiguration FromJson(JsonNode node)
    {
        string Text(string name) => node[name]?.GetValue<string>() ?? throw new InvalidOperationException($"A target has no {name}.");
        ImmutableArray<ProgressTrigger> Triggers(string name) => node[name] is JsonArray list
            ? [.. list.Select(t => ProgressTrigger.Parse(t!.GetValue<string>()))]
            : throw new InvalidOperationException($"A target has no {name} list.");
        var settings = node["settings"] is JsonObject map
            ? map.ToImmutableSortedDictionary(s => s.Key, s => s.Value!.GetValue<string>(), StringComparer.Ordinal)
            : throw new InvalidOperationException("A target has no settings.");
        return new(Text("name"), Text("adapter"), settings, Text("form"), node["title"]?.GetValue<string>(), Triggers("refresh"), Triggers("comment"));
    }
}

/// <summary>
/// From 0.9.4 (WATCH-06): what this machine keeps of one target between runs: the place its adapter created, the event cursor its
/// follower reached, and the stops it last saw waiting on someone, so a comment is added once for each. Null waiting means the
/// follower has not yet looked, and its first look comments on nothing that already waited.
/// </summary>
internal sealed record ProgressTargetState(string? PlaceId = null, string? PlaceUrl = null, string? Cursor = null, ImmutableArray<string>? Waiting = null)
{
    internal JsonObject ToJson() => new()
    {
        ["place"] = PlaceId is null ? null : new JsonObject { ["id"] = PlaceId, ["url"] = PlaceUrl },
        ["cursor"] = Cursor,
        ["waiting"] = Waiting is { } waiting ? new JsonArray([.. waiting.Select(w => (JsonNode)JsonValue.Create(w))]) : null,
    };

    internal static ProgressTargetState FromJson(JsonNode? node) => node is null ? new() : new(
        node["place"]?["id"]?.GetValue<string>(), node["place"]?["url"]?.GetValue<string>(), node["cursor"]?.GetValue<string>(),
        node["waiting"] is JsonArray waiting ? [.. waiting.Select(w => w!.GetValue<string>())] : null);
}

/// <summary>
/// From 0.9.4 (WATCH-06): the progress targets of one estate on this machine, kept owner-only beside its live store: machine
/// configuration, never a record, as the runtime's own settings are. The configuration and what the follower keeps are two files, so
/// a follower's cursor never rewrites what the operator configured.
/// </summary>
internal sealed class ProgressTargetStore(string directory)
{
    internal const string FileName = "progress-targets.json";
    internal const string StateFileName = "progress-targets-state.json";
    private const string Format = "vericairn.progress-targets/1";
    private const string StateFormat = "vericairn.progress-targets-state/1";

    /// <summary>The store of an estate on this machine: the directory of its live store.</summary>
    internal static ProgressTargetStore Of(string root) => new(Path.GetDirectoryName(EstateLocations.EndpointFilePath(root))!);

    internal string Directory => directory;

    /// <summary>Every configured target, by name.</summary>
    internal IReadOnlyList<ProgressTargetConfiguration> Load()
    {
        JsonNode? node = Read(FileName, Format);
        if (node?["targets"] is not JsonArray targets) return [];
        try
        {
            return [.. targets.Select(t => ProgressTargetConfiguration.FromJson(t!)).OrderBy(t => t.Name, StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"The progress targets in {Path.Combine(directory, FileName)} cannot be read: {exception.Message} Set them again with 'vericairn progress target set'.", exception);
        }
    }

    /// <summary>The target of a name, or null.</summary>
    internal ProgressTargetConfiguration? Find(string name) => Load().FirstOrDefault(t => t.Name == name);

    /// <summary>Adds or replaces a target.</summary>
    internal void Save(ProgressTargetConfiguration target)
    {
        List<ProgressTargetConfiguration> targets = [.. Load().Where(t => t.Name != target.Name), target];
        Write(FileName, new JsonObject { ["format"] = Format, ["targets"] = new JsonArray([.. targets.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (JsonNode)t.ToJson())]) });
    }

    /// <summary>Removes a target and what this machine kept of it; false when none has the name.</summary>
    internal bool Remove(string name)
    {
        IReadOnlyList<ProgressTargetConfiguration> targets = Load();
        if (targets.All(t => t.Name != name)) return false;
        Write(FileName, new JsonObject { ["format"] = Format, ["targets"] = new JsonArray([.. targets.Where(t => t.Name != name).Select(t => (JsonNode)t.ToJson())]) });
        JsonObject states = States();
        states.Remove(name);
        Write(StateFileName, new JsonObject { ["format"] = StateFormat, ["targets"] = states });
        return true;
    }

    /// <summary>What this machine kept of a target.</summary>
    internal ProgressTargetState State(string name) => ProgressTargetState.FromJson(States()[name]);

    /// <summary>Keeps what this machine knows of a target.</summary>
    internal void Save(string name, ProgressTargetState state)
    {
        JsonObject states = States();
        states[name] = state.ToJson();
        Write(StateFileName, new JsonObject { ["format"] = StateFormat, ["targets"] = states });
    }

    private JsonObject States()
    {
        try
        {
            return Read(StateFileName, StateFormat)?["targets"] is JsonObject targets ? (JsonObject)targets.DeepClone() : [];
        }
        catch (InvalidOperationException)
        {
            // What a follower kept is a convenience: an unreadable copy is started again, and the next refresh locates the place.
            return [];
        }
    }

    private JsonNode? Read(string name, string format)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) return null;
        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            return node?["format"]?.GetValue<string>() == format ? node : throw new InvalidOperationException($"{path} is not of format {format}.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{path} cannot be read: {exception.Message}", exception);
        }
    }

    private void Write(string name, JsonObject node)
    {
        OwnerOnlyState.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        string temporary = path + ".tmp";
        OwnerOnlyState.WriteAllBytes(temporary, Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        File.Move(temporary, path, overwrite: true);
    }
}
