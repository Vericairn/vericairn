// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>All records of an estate, indexed, with the findings produced while loading them.</summary>
public sealed class EstateModel
{
    /// <summary>Creates a model.</summary>
    public EstateModel(string root, IReadOnlyList<RecordEnvelope> records, IReadOnlyDictionary<string, RecordEnvelope> byId, IReadOnlyDictionary<string, string> supersededBy, FindingList findings)
    {
        Root = root;
        Records = records;
        ById = byId;
        SupersededBy = supersededBy;
        Findings = findings;
    }

    /// <summary>Repository root.</summary>
    public string Root { get; }

    /// <summary>All parsed records in path order.</summary>
    public IReadOnlyList<RecordEnvelope> Records { get; }

    /// <summary>Records by id.</summary>
    public IReadOnlyDictionary<string, RecordEnvelope> ById { get; }

    /// <summary>Superseded record id to the id of its successor.</summary>
    public IReadOnlyDictionary<string, string> SupersededBy { get; }

    /// <summary>Findings from loading and validating records.</summary>
    public FindingList Findings { get; }

    /// <summary>The manifest, when it loaded.</summary>
    public EstateManifest? Manifest { get; set; }

    /// <summary>True when no later record supersedes this one.</summary>
    public bool IsCurrent(RecordEnvelope record) => record is not null && !SupersededBy.ContainsKey(record.Id);

    /// <summary>All records of a type.</summary>
    public IEnumerable<RecordEnvelope> OfType(string type) => Records.Where(r => r.Type == type);

    /// <summary>Current records of a type.</summary>
    public IEnumerable<RecordEnvelope> Current(string type) => OfType(type).Where(IsCurrent);

    /// <summary>The current estate charter, if exactly one exists (the lowest id wins when several do; validators flag that).</summary>
    public RecordEnvelope? CurrentEstate => Current("estate").OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Follows the supersedes chain from a record to its latest successor.</summary>
    public RecordEnvelope Latest(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        RecordEnvelope current = record;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.Id };
        while (SupersededBy.TryGetValue(current.Id, out string? next) && ById.TryGetValue(next, out RecordEnvelope? successor) && seen.Add(successor.Id))
        {
            current = successor;
        }

        return current;
    }

    /// <summary>All record ids in this record's supersedes chain, earliest first, ending with the record itself.</summary>
    public IReadOnlyList<string> Chain(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var ids = new List<string> { record.Id };
        var seen = new HashSet<string>(StringComparer.Ordinal) { record.Id };
        RecordEnvelope current = record;
        while (current.Supersedes is not null && ById.TryGetValue(current.Supersedes, out RecordEnvelope? previous) && seen.Add(previous.Id))
        {
            ids.Insert(0, previous.Id);
            current = previous;
        }

        return ids;
    }

    /// <summary>The latest principal record describing a principal (principals are referenced by their first record's id).</summary>
    public RecordEnvelope? CurrentPrincipal(string? id)
    {
        if (id is null || !ById.TryGetValue(id, out RecordEnvelope? record) || record.Type != "principal")
        {
            return null;
        }

        return Latest(record);
    }

    /// <summary>Roles the principal currently holds.</summary>
    public IReadOnlyList<string> RolesOf(string? id)
    {
        RecordEnvelope? principal = CurrentPrincipal(id);
        if (principal is null || !principal.Body.TryGetProperty("roles", out JsonElement roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToList();
    }

    /// <summary>True when the principal currently holds the role.</summary>
    public bool HasRole(string? id, string role) => RolesOf(id).Contains(role, StringComparer.Ordinal);

    /// <summary>True when a current authority record grants the principal the scope.</summary>
    public bool HasScope(string? id, string scope) =>
        id is not null && Current("authority").Any(a =>
            a.Body.TryGetProperty("grantee", out JsonElement grantee) && grantee.GetString() == id &&
            a.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
            scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == scope));

    /// <summary>Records of a type whose subject is the given record id.</summary>
    public IEnumerable<RecordEnvelope> About(string subjectId, string type) => OfType(type).Where(r => r.Subject?.Id == subjectId);

    /// <summary>Reads a string field from a body, or null.</summary>
    public static string? BodyString(RecordEnvelope record, string name) =>
        record is not null && record.Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
