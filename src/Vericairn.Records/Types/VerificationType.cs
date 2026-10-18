// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;

namespace Vericairn.Records.Types;

/// <summary>A check of an exact subject at an exact commit: method, environment, role, observed result and limitations.</summary>
public sealed class VerificationType : IRecordType
{
    /// <summary>Observed outcomes; unknown stays unknown.</summary>
    public static readonly ImmutableArray<string> Outcomes = ["passed", "failed", "unknown"];

    /// <summary>Who or what performed the check.</summary>
    public static readonly ImmutableArray<string> Roles = ["self", "independent-agent", "operator", "automation"];

    /// <summary>How the evidence was obtained, from record format 2.</summary>
    public static readonly ImmutableArray<string> EvidenceClasses = ["observation", "report", "attestation", "inference"];

    /// <summary>What kind of observer made the claim, from record format 2.</summary>
    public static readonly ImmutableArray<string> Observers = ["person", "agent", "automation", "provider"];

    /// <inheritdoc />
    public string Name => "verification";

    /// <inheritdoc />
    public string Summary => "A check of an exact subject (a repository resource, a result or a release) at an exact commit: the method, the command, the environment, who performed it, the observed result, evidence and limitations. Evidence, never permission; historical once the subject changes.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("method", "string", true, "How the check was performed: command, github-actions, manual, or a named tool."),
        new("commit", "hex", true, "The exact commit checked (40 hex digits)."),
        new("command", "string", false, "The command run, when the method is a command."),
        new("environment", "object", true, "Where it ran: platform, tool versions, runner; string values only, never machine paths."),
        new("role", string.Join('|', Roles), true, "self, independent-agent, operator or automation."),
        new("outcome", string.Join('|', Outcomes), true, "passed, failed or unknown."),
        new("summary", "string", true, "What was observed."),
        new("exitCode", "integer", false, "Process exit code, when a command ran."),
        new("durationMs", "integer", false, "How long the check took."),
        new("startedAt", "timestamp", false, "When it started."),
        new("finishedAt", "timestamp", false, "When it finished."),
        new("evidence", "object[]", true, "summary, optional hash and location; may be empty."),
        new("limitations", "string[]", true, "What the check does not establish; may be empty."),
        new("evidenceClass", string.Join('|', EvidenceClasses), false, "How the evidence was obtained: observed where it was recorded, reported by a host or provider, attested by a principal, or inferred from other evidence.", 2),
        new("observer", string.Join('|', Observers), false, "What kind of observer made the claim.", 2),
        new("expires", "timestamp", false, "When the claim stops counting as current and needs observing again.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("method", true, 200);
        body.Text("commit", true, 40, Patterns.Commit, "a 40-character lower-case hex commit id");
        body.Text("command", false, 4000);
        BodyReader? environment = body.Nested("environment", true);
        if (environment is not null)
        {
            foreach (JsonProperty property in environment.Element.EnumerateObject())
            {
                environment.Allow(property.Name);
                if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString()!.Length > 500)
                {
                    environment.Error("body.type", property.Name, "Environment values are strings of at most 500 characters.");
                }
            }

            environment.NoOtherKeys();
        }

        string? role = body.Enum("role", true, Roles);
        body.Enum("outcome", true, Outcomes);
        body.Text("summary", true, 8000);
        body.Number("exitCode", false, int.MinValue, int.MaxValue);
        body.Number("durationMs", false, 0, long.MaxValue);
        string? started = body.Timestamp("startedAt", false);
        string? finished = body.Timestamp("finishedAt", false);
        if (body.Since(2, "evidenceClass"))
        {
            body.Enum("evidenceClass", false, EvidenceClasses);
        }

        if (body.Since(2, "observer"))
        {
            body.Enum("observer", false, Observers);
        }

        if (body.Since(2, "expires") && body.Timestamp("expires", false) is { } expires && string.CompareOrdinal(expires, context.Record.At) <= 0)
        {
            body.Error("verification.expires", "expires", "A claim expires after it is made.");
        }
        foreach (BodyReader evidence in body.NestedArray("evidence", true, allowEmpty: true) ?? [])
        {
            evidence.Text("summary", true, 2000);
            evidence.Hash("hash", false);
            evidence.Text("location", false, 500);
            evidence.NoOtherKeys();
        }

        body.StringArray("limitations", true, allowEmpty: true, maxItemLength: 2000);
        body.NoOtherKeys();
        if (started is not null && finished is not null && string.CompareOrdinal(finished, started) < 0)
        {
            body.Error("verification.time", "finishedAt", "A check does not finish before it starts.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("verification.author", $"Verifying principal {context.Record.By} has no principal record.");
        }
        else if (role == "operator" && !context.HasRole(context.Record.By, Types.Roles.Operator))
        {
            context.Error("verification.role", "Only the operator records a verification in the operator role.");
        }
    }
}
