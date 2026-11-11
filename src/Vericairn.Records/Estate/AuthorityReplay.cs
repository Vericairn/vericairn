// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>
/// Which principal and authority records give roles and scopes: the records that count, read in time order, ids breaking ties, from the
/// root operator, each accepted only when its author may write it at that point, judged by the records accepted before it. A principal
/// record is written by an operator or a principal holding administer, and one that gives the operator role only by an operator; an
/// administrator amends only a line whose revision it wrote; no principal writes its own record unless it is the root operator; an
/// agent's record gives no operator role; a grant is written by an operator. A removal keeps counting: a person who held the operator
/// role earlier in the reading may still take it away from a line that gives it, and narrow a grant, after losing the role, as the
/// strongest acts read such a revision. So no record is what authorizes itself, and authority rests on the root operator.
/// </summary>
internal sealed class AuthorityReplay
{
    private readonly Dictionary<string, List<RecordEnvelope>> timelines;
    private readonly HashSet<string> grantLines;

    private AuthorityReplay(HashSet<string> accepted, Dictionary<string, List<RecordEnvelope>> timelines, HashSet<string> grantLines)
    {
        Accepted = accepted;
        this.timelines = timelines;
        this.grantLines = grantLines;
    }

    /// <summary>The ids of the accepted records.</summary>
    internal IReadOnlySet<string> Accepted { get; }

    /// <summary>The ids of the lines of accepted grants.</summary>
    internal IEnumerable<string> GrantLines => grantLines;

    /// <summary>The latest accepted record of a line, or null.</summary>
    internal RecordEnvelope? Current(string line) => timelines.TryGetValue(line, out List<RecordEnvelope>? list) && list.Count > 0 ? list[^1] : null;

    /// <summary>The latest accepted record of a line dated at or before a time, or null.</summary>
    internal RecordEnvelope? At(string line, string at)
    {
        if (!timelines.TryGetValue(line, out List<RecordEnvelope>? list)) return null;
        RecordEnvelope? found = null;
        foreach (RecordEnvelope record in list)
        {
            if (string.CompareOrdinal(record.At, at) > 0) break;
            found = record;
        }

        return found;
    }

    /// <summary>The latest accepted record of a line read before a record, in time order with ids breaking ties, or null.</summary>
    internal RecordEnvelope? Before(string line, RecordEnvelope record)
    {
        if (!timelines.TryGetValue(line, out List<RecordEnvelope>? list)) return null;
        RecordEnvelope? found = null;
        foreach (RecordEnvelope accepted in list)
        {
            if (!GoalRules.Before(accepted, record)) break;
            found = accepted;
        }

        return found;
    }

    /// <summary>Reads the records in time order, ids breaking ties.</summary>
    internal static AuthorityReplay Run(IEnumerable<RecordEnvelope> principals, IEnumerable<RecordEnvelope> grants, string? root, Func<RecordEnvelope, string> lineOf,
        Func<string, RecordEnvelope?> find)
    {
        List<RecordEnvelope> ordered = [.. principals.Concat(grants).DistinctBy(r => r.Id).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
        var principalState = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        var grantState = new Dictionary<string, RecordEnvelope>(StringComparer.Ordinal);
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        var timelines = new Dictionary<string, List<RecordEnvelope>>(StringComparer.Ordinal);
        var formerOperators = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecordEnvelope record in ordered)
        {
            string line = lineOf(record);
            if (!(record.Type == "principal" ? MayWritePrincipal(record, line) : MayWriteGrant(record, line))) continue;
            accepted.Add(record.Id);
            (record.Type == "principal" ? principalState : grantState)[line] = record;
            if (record.Type == "principal" && IsOperator(record)) formerOperators.Add(line);
            if (!timelines.TryGetValue(line, out List<RecordEnvelope>? list)) timelines[line] = list = [];
            list.Add(record);
        }

        return new AuthorityReplay(accepted, timelines, [.. grantState.Keys]);

        bool Operator(string author) => author == root && root is not null || principalState.TryGetValue(author, out RecordEnvelope? current) && IsOperator(current);

        bool Administers(string author, DateTimeOffset at) => grantState.Values.Any(g => EstateModel.BodyString(g, "grantee") == author &&
            g.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array &&
            scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == Scopes.Administer) && !EstateModel.Expired(g, at));

        // A grant is written by an operator; one who held the operator role earlier may still narrow it.
        bool MayWriteGrant(RecordEnvelope record, string line) => Operator(record.By) ||
            formerOperators.Contains(record.By) && grantState.TryGetValue(line, out RecordEnvelope? previous) && Narrows(record, previous);

        bool MayWritePrincipal(RecordEnvelope record, string line)
        {
            if (record.Id == root && record.By == root && record.Supersedes is null) return true;
            if (line == record.By && record.By != root) return false;
            bool givesOperator = Roles(record).Contains(Types.Roles.Operator);
            if (givesOperator && EstateModel.BodyString(record, "kind") != "human") return false;
            if (Operator(record.By)) return true;
            // A removal keeps counting: one who held the operator role earlier takes it away from a line that gives it.
            if (!givesOperator && formerOperators.Contains(record.By) && principalState.TryGetValue(line, out RecordEnvelope? held) && IsOperator(held)) return true;
            if (givesOperator || !Administers(record.By, record.AtTime)) return false;
            // An administrator amends only a line whose revision it wrote: the line as accepted, or, when none of it is, the record named.
            RecordEnvelope? amended = principalState.GetValueOrDefault(line) ?? (record.Supersedes is not null && find(record.Supersedes) is { Type: "principal" } named ? named : null);
            return amended is null || amended.By == record.By;
        }
    }

    /// <summary>
    /// Whether a grant revision only narrows the grant it revises: the same grantee, no scope it did not give, an expiry no later, and the
    /// same bounds, with every reserved action and condition it stated.
    /// </summary>
    internal static bool Narrows(RecordEnvelope grant, RecordEnvelope previous) =>
        EstateModel.BodyString(grant, "grantee") == EstateModel.BodyString(previous, "grantee") &&
        Strings(grant, "scope").All(Strings(previous, "scope").Contains) &&
        (EstateModel.BodyString(previous, "expires") is not { } before || EstateModel.BodyString(grant, "expires") is { } after && string.CompareOrdinal(after, before) <= 0) &&
        Strings(previous, "reserved").All(Strings(grant, "reserved").Contains) && Strings(previous, "conditions").All(Strings(grant, "conditions").Contains) &&
        Canonical(grant, "bounds") == Canonical(previous, "bounds");

    private static HashSet<string> Strings(RecordEnvelope record, string field) =>
        record.Body.TryGetProperty(field, out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!)]
            : [];

    private static string? Canonical(RecordEnvelope record, string field) =>
        record.Body.TryGetProperty(field, out JsonElement value) ? Vericairn.Records.Canonical.CanonicalJson.Serialize(value) : null;

    /// <summary>Whether a principal record describes a person holding the operator role.</summary>
    internal static bool IsOperator(RecordEnvelope? principal) =>
        principal is not null && EstateModel.BodyString(principal, "kind") == "human" && Roles(principal).Contains(Types.Roles.Operator);

    /// <summary>The roles a principal record states.</summary>
    internal static List<string> Roles(RecordEnvelope principal) =>
        principal.Body.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array
            ? [.. roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!)]
            : [];
}
