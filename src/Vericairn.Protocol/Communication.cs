// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Presentation values; null inherits. None is an explicit progress value, never inheritance.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record CommunicationValues(string? Progress = null, string? Status = null, string? Decision = null, string? Result = null, string? Handoff = null)
{
    /// <summary>The five independently resolved fields.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string?> Fields => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["progress"] = Progress, ["status"] = Status, ["decision"] = Decision, ["result"] = Result, ["handoff"] = Handoff,
    };
}

/// <summary>An attributable revision at a scope; historical revisions remain inspectable but do not select values.</summary>
public sealed record CommunicationLayer(string Scope, CommunicationValues Values, string? Record = null, string? Hash = null, string? By = null, string? Basis = null, bool Current = true, string? Supersedes = null, string? Authority = null, string? AuthorityHash = null, string? BasisHash = null);

/// <summary>One field's effective value and every current contribution. A conflict has no winning value at its scope.</summary>
public sealed record CommunicationValue(string? Value, string? Source, bool Conflict, IReadOnlyList<CommunicationLayer> Contributions);

/// <summary>A read-only profile for a recipient and optional assignment. Freshness identifies the settings read, not authority.</summary>
public sealed record CommunicationProfile(string? Recipient, string? Assignment, IReadOnlyDictionary<string, CommunicationValue> Fields, IReadOnlyList<CommunicationLayer> Revisions, IReadOnlyList<string> Conflicts, string? ObservedAt = null, string? RevisionHash = null, string? Unavailable = null)
{
    /// <summary>Absent until the estate explicitly adopts an exact communication template.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PresentationTemplateUse? Template { get; init; }
}

/// <summary>Deterministic, field-by-field precedence shared by runtime and client previews.</summary>
public static class CommunicationResolution
{
    /// <summary>Defaults adopted by DESIGN-08; these are not a person's recorded choices.</summary>
    public static CommunicationValues Defaults { get; } = new("completion-and-need", "short", "comprehensive", "comprehensive", "comprehensive");

    /// <summary>Permitted optional-progress frequencies.</summary>
    public static IReadOnlyList<string> Frequencies { get; } = ["none", "completion-and-need", "milestones", "verbose"];

    /// <summary>Permitted presentation depths.</summary>
    public static IReadOnlyList<string> Depths { get; } = ["short", "standard", "comprehensive"];

    /// <summary>Resolve shipped, estate, personal, assignment and ephemeral view values without changing any layer.</summary>
    public static CommunicationProfile Resolve(string? recipient, string? assignment, IReadOnlyList<CommunicationLayer> revisions, CommunicationValues? view = null)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        foreach (CommunicationLayer layer in revisions)
        {
            if (layer.Scope is not ("estate" or "person" or "assignment"))
            {
                throw new ArgumentException("A stored communication scope is estate, person or assignment.", nameof(revisions));
            }

            Validate(layer.Values);
        }

        if (view is not null)
        {
            Validate(view);
        }

        var fields = new Dictionary<string, CommunicationValue>(StringComparer.Ordinal);
        List<string> conflicts = [];
        foreach ((string field, string? initial) in Defaults.Fields)
        {
            string? value = initial;
            string? source = "shipped";
            bool conflict = false;
            List<CommunicationLayer> contributions = [new("shipped", Defaults)];
            foreach (string scope in new[] { "estate", "person", "assignment" })
            {
                List<CommunicationLayer> current = revisions.Where(r => r.Current && r.Scope == scope).OrderBy(r => r.Record, StringComparer.Ordinal).ToList();
                contributions.AddRange(current.Where(r => r.Values.Fields[field] is not null));
                if (current.Count > 1)
                {
                    // Divergent profiles conflict even if one branch resets this field to inheritance.
                    conflicts.AddRange(current.Select(r => r.Record ?? scope));
                    value = null;
                    source = scope;
                    conflict = true;
                }
                else if (current.Count == 1 && current[0].Values.Fields[field] is string selected)
                {
                    value = selected;
                    source = scope;
                    conflict = false;
                }
            }

            if (view?.Fields[field] is string requested)
            {
                contributions.Add(new CommunicationLayer("view", view));
                value = requested;
                source = "view";
                conflict = false;
            }

            fields[field] = new CommunicationValue(value, source, conflict, contributions);
        }

        return new CommunicationProfile(recipient, assignment, fields, revisions, conflicts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>Reject unsupported values rather than silently applying a partial setting.</summary>
    public static void Validate(CommunicationValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach ((string field, string? value) in values.Fields)
        {
            if (value is not null && !(field == "progress" ? Frequencies : Depths).Contains(value, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unsupported communication {field} '{value}'.", nameof(values));
            }
        }
    }
}

/// <summary>Replace one complete scoped profile. Expected is the previous record's hash; null means no previous profile.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record CommunicationSetRequest(string By, string Scope, CommunicationValues Values, string? Recipient = null, string? Assignment = null, string? Expected = null, string? Authority = null, string? Basis = null);

/// <summary>A person grants or revokes another principal's permission to change exactly one personal scope.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record CommunicationDelegateRequest(string By, string Recipient, string Grantee, string Scope, string? Assignment = null, string Status = "active", string? Expected = null, string? Expires = null);
