// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>A human, agent or service that can author records.</summary>
public sealed class PrincipalType : IRecordType
{
    /// <inheritdoc />
    public string Name => "principal";

    /// <inheritdoc />
    public string Summary => "A human, agent or service that authors records. Only the root operator may author its own principal record.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kind", "human|agent|service", true, "What the principal is."),
        new("name", "string", true, "Display name."),
        new("roles", "string[]", true, "operator, builder, reviewer, contributor, service."),
        new("agent", "object", false, "For agents: vendor, product and optional notes. Routing matches product to a launcher's harness name or id, ignoring case: Codex or codex for the codex launcher, Claude Code or claude-code for claude-code. Never a model name as proof of anything."),
        new("notes", "string", false, "Free text."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? kind = body.Enum("kind", true, "human", "agent", "service");
        body.Text("name", true, 200);
        IReadOnlyList<string>? roles = body.StringArray("roles", true, allowEmpty: false, maxItemLength: 50);
        BodyReader? agent = body.Nested("agent", kind == "agent");
        if (agent is not null)
        {
            agent.Text("vendor", true, 200);
            agent.Text("product", true, 200);
            agent.Text("notes", false, 2000);
            agent.NoOtherKeys();
        }

        body.Text("notes", false, 4000);
        body.NoOtherKeys();
        if (roles is not null)
        {
            foreach (string role in roles.Where(r => Roles.All.IndexOf(r) < 0))
            {
                body.Error("body.enum", "roles", $"Unknown role '{role}'. Known roles: {string.Join(", ", Roles.All)}.");
            }

            if (roles.Contains(Roles.Operator, StringComparer.Ordinal) && kind != "human")
            {
                body.Error("principal.operator", "roles", "Only a human can hold the operator role.");
            }
        }

        bool selfAuthored = context.Record.By == context.Record.Id;
        string line = context.Model.Chain(context.Record)[0];
        bool operatorAuthor = context.HasRole(context.Record.By, Roles.Operator);
        if (selfAuthored)
        {
            string? rootOperator = context.Model.RootOperator;
            if (rootOperator != context.Record.Id)
            {
                context.Error("principal.self-authored", "Only the estate's root operator principal may author its own record.");
            }
        }
        else
        {
            if (context.Principal(context.Record.By) is null)
            {
                context.Error("principal.author", $"Authoring principal {context.Record.By} has no principal record.");
            }
            else if (!operatorAuthor && !context.HasScope(context.Record.By, Scopes.Administer))
            {
                context.Error("principal.author", "Principals are admitted by the operator or by a principal granted the 'administer' scope.");
            }
            else if (!operatorAuthor && roles is not null && roles.Contains(Roles.Operator, StringComparer.Ordinal))
            {
                context.Error("principal.author", "Only an operator gives the operator role; a principal granted the 'administer' scope admits principals with other roles.");
            }
        }

        // Only the root operator authors its own record, at any revision: another principal's revision of its own record counts for
        // nothing. One that would give nothing the line did not give, such as a rename, is reported as a warning, so an estate holding
        // one stays valid; one that would give a role, or change what kind of principal the line describes, is an error.
        if (!selfAuthored && context.Record.By == line && context.Record.By != context.Model.RootOperator)
        {
            Model.RecordEnvelope? before = context.Model.PrincipalBefore(line, context.Record);
            bool gives = before is null || kind != Estate.EstateModel.BodyString(before, "kind") ||
                (roles ?? []).Except(Estate.AuthorityReplay.Roles(before), StringComparer.Ordinal).Any();
            string message = "Only the estate's root operator principal may author its own record, at any revision; this revision of a principal's own record counts for nothing.";
            if (gives) context.Error("principal.self-authored", message);
            else context.Warning("principal.self-authored", message + " It would give nothing the record before it did not; the operator may write the change instead.");
        }
        else if (!selfAuthored && !operatorAuthor && context.Record.Supersedes is not null && context.Record.Supersedes != context.Record.Id)
        {
            // An administrator amends only a line whose revision it wrote: the revision its author may write that is read before this one,
            // or, when none is, the record named.
            Model.RecordEnvelope? amended = context.Model.PrincipalBefore(line, context.Record) ?? context.Find(context.Record.Supersedes);
            if (amended is not null && amended.Type == Name && context.Record.By != amended.By)
            {
                context.Error("principal.amendment", "A principal record is amended by the operator or by the principal that admitted it.");
            }
        }
    }
}
