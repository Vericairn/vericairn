// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Records.Types;

/// <summary>Who fills each role of the estate's work, in order of preference, from record format 2 (the estate's design/goals.md § 6).</summary>
public sealed class RosterType : IRecordType
{
    /// <summary>The roles the goals design names; an estate may name others.</summary>
    public static readonly ImmutableArray<string> KnownRoles = ["planner", "builder", "reviewer", "evaluator", "release-manager", "publisher", "decider"];

    /// <inheritdoc />
    public string Name => "roster";

    /// <inheritdoc />
    public string Summary => "Who fills each role of the estate's work, in order of preference: planner, builder, reviewer, evaluator, release manager, publisher and decider, or a role the estate names. Agents where available, people where needed; a role with no available principal becomes an ask, never a silent fallback.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("execution", "object", false, "Explicit execution choices and cumulative restrictions; launcher, roster, goal, assignment, then run precedence. Role restrictions remain binding.", 2),
        new("roles", "object[]", true, "Each role (a lower-case name such as builder or decider), its principals (ulid[]) in order of preference, and an optional note; the decider role is held by operators.", 2),
        new("limits", "object", false, "stallSeconds (event-stream silence limit, 1–86400 seconds) and watch (bounded rate and loop thresholds), inherited by goals using this roster. Goal fields override these; omitted fields keep the launcher defaults.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        IReadOnlyList<BodyReader>? roles = body.NestedArray("roles", true, allowEmpty: false);
        BodyReader? limits = body.Nested("limits", false);
        ExecutionPolicy.Read(body);
        body.NoOtherKeys();
        if (limits is not null)
        {
            limits.Number("stallSeconds", false, 1, 86400);
            WatchLimits.Read(limits);
            limits.NoOtherKeys();
        }

        if (roles is not null)
        {
            Roles(context, roles, "roles");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("roster.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
        else if (!context.HasRole(context.Record.By, Types.Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer))
        {
            context.Error("roster.author", "The roster is recorded by the operator or under the 'administer' scope.");
        }

        if (context.Model.IsCurrent(context.Record) && context.Model.Current(Name).Any(other => other.Id != context.Record.Id))
        {
            context.Error("roster.multiple", "An estate has one current roster; a change supersedes it.");
        }
    }

    /// <summary>Reads role assignments, shared by the roster and a goal's overrides.</summary>
    public static void Roles(ValidationContext context, IReadOnlyList<BodyReader> roles, string field)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(roles);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader role in roles)
        {
            string? name = role.Text("role", true, 60, Patterns.Slug, "a lower-case role name such as builder");
            IReadOnlyList<string>? principals = role.StringArray("principals", true, allowEmpty: false, maxItemLength: Identity.Ulid.Length);
            role.Text("note", false, 2000);
            ExecutionPolicy.Read(role);
            role.NoOtherKeys();
            if (name is not null && !seen.Add(name))
            {
                role.Error("roster.duplicate-role", "role", $"Role '{name}' is listed twice.");
            }

            foreach (string id in principals ?? [])
            {
                if (context.RequireReference(field + ".principals", id, "principal") is not null && name == "decider" && !context.HasRole(id, Types.Roles.Operator))
                {
                    role.Error("roster.decider", "principals", "The decider role is held by principals with the operator role: decisions and acceptances stay theirs.");
                }
            }
        }
    }
}
