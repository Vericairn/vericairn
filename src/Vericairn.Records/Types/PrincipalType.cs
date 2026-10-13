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
        new("agent", "object", false, "For agents: vendor, product and optional notes. Never a model name as proof of anything."),
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
            foreach (string role in roles.Where(r => Array.IndexOf(Roles.All, r) < 0))
            {
                body.Error("body.enum", "roles", $"Unknown role '{role}'. Known roles: {string.Join(", ", Roles.All)}.");
            }

            if (roles.Contains(Roles.Operator, StringComparer.Ordinal) && kind != "human")
            {
                body.Error("principal.operator", "roles", "Only a human can hold the operator role.");
            }
        }

        bool selfAuthored = context.Record.By == context.Record.Id;
        if (selfAuthored)
        {
            Model.RecordEnvelope? estate = context.Model.CurrentEstate;
            string? rootOperator = estate?.Body.TryGetProperty("operator", out System.Text.Json.JsonElement op) == true ? op.GetString() : null;
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
            else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.Model.HasScope(context.Record.By, Scopes.Administer))
            {
                context.Error("principal.author", "Principals are admitted by the operator or by a principal granted the 'administer' scope.");
            }
        }

        if (context.Record.Supersedes is not null && context.Record.Supersedes != context.Record.Id)
        {
            Model.RecordEnvelope? previous = context.Find(context.Record.Supersedes);
            if (previous is not null && previous.Type == Name && !selfAuthored && context.Record.By != previous.By && !context.HasRole(context.Record.By, Roles.Operator))
            {
                context.Error("principal.amendment", "A principal record is amended by the operator or by the principal that admitted it.");
            }
        }
    }
}
