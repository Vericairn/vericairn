// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A person's qualification of an exact native harness from digested local probes, independently of a release profile.</summary>
public sealed class HarnessQualificationType : IRecordType
{
    private static readonly string[] IdentityFields = ["launcher", "version", "probes"];

    /// <summary>The complete probe set; raw outputs, local paths and login material are never fields.</summary>
    public static readonly ImmutableArray<string> ProbeNames = ["version", "flags", "tools", "configuration", "login", "mcp"];

    /// <inheritdoc />
    public string Name => "harness-qualification";

    /// <inheritdoc />
    public string Summary => "An authorized person's qualification of one exact harness version from six probe digests; a superseding revocation withdraws it. It establishes neither live inference nor work acceptance.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("launcher", "codex|claude-code", true, "The native launcher whose probes were run.", 2),
        new("version", "string", true, "The exact observed version, never a range.", 2),
        new("status", "qualified|revoked", true, "Qualified initially; revoked only by a superseding record preserving launcher, version and probes.", 2),
        new("probes", "object", true, "SHA-256 digests named version, flags, tools, configuration, login and mcp. No raw probe output.", 2),
    ];

    /// <summary>Only a human operator or a human with the operator's explicit qualify grant may qualify or revoke.</summary>
    public static bool Authorized(EstateModel model, string principal, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.CurrentPrincipal(principal) is RecordEnvelope person && EstateModel.BodyString(person, "kind") == "human"
            && (model.HasRole(principal, Roles.Operator) || model.HasScope(principal, Scopes.Qualify, at));
    }

    /// <summary>A bounded version token as printed by a native harness; no range, whitespace, path or free-form output.</summary>
    public static bool ExactVersion(string value) => !string.IsNullOrEmpty(value) && value.Length <= 80 && char.IsAsciiDigit(value[0])
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+') && value.Contains('.', StringComparison.Ordinal);

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Enum("launcher", true, "codex", "claude-code");
        if (body.Text("version", true, 80) is string version && !ExactVersion(version))
        {
            body.Error("harness-qualification.version", "version", "A qualification names one exact bounded version token, never a range or raw output.");
        }
        string? status = body.Enum("status", true, "qualified", "revoked");
        if (body.Nested("probes", true) is BodyReader probes)
        {
            foreach (string name in ProbeNames) probes.Hash(name, true);
            probes.NoOtherKeys();
        }
        body.NoOtherKeys();
        if (!AuthorizedWhen(context.Model, context.Record))
        {
            context.Error("harness-qualification.author", "Only a human operator or a human explicitly granted qualify may qualify or revoke a harness; agents and services never may.");
        }
        if (context.Record.Supersedes is null)
        {
            if (status != "qualified") context.Error("harness-qualification.revision", "Revocation supersedes an existing qualification.");
        }
        else if (context.Find(context.Record.Supersedes) is RecordEnvelope previous)
        {
            if (status != "revoked" || EstateModel.BodyString(previous, "status") != "qualified"
                || IdentityFields.Any(name => !previous.Body.TryGetProperty(name, out var prior)
                    || !context.Record.Body.TryGetProperty(name, out var current) || CanonicalJson.Serialize(prior) != CanonicalJson.Serialize(current)))
            {
                context.Error("harness-qualification.revision", "A superseding record revokes a qualification and preserves its launcher, version and probe digests; requalification is a new record.");
            }
        }
    }

    private static bool AuthorizedWhen(EstateModel model, RecordEnvelope record)
    {
        RecordEnvelope? latest = model.CurrentPrincipal(record.By);
        RecordEnvelope? person = latest is null ? null : model.Chain(latest).Select(id => model.ById[id]).LastOrDefault(p => GoalRules.Before(p, record));
        if (person is null || EstateModel.BodyString(person, "kind") != "human") return false;
        if (Contains(person, "roles", Roles.Operator)) return true;
        // A later grant cannot authorize an earlier act. Withdrawing a grant does not rewrite earlier authorized acts;
        // a harness qualification itself is withdrawn by its own superseding revocation.
        return model.OfType("authority").Any(grant => EstateModel.BodyString(grant, "grantee") == record.By
            && Contains(grant, "scope", Scopes.Qualify) && GoalRules.Before(grant, record) && !EstateModel.Expired(grant, record.AtTime)
            && (!model.SupersededBy.TryGetValue(grant.Id, out string? next) || !GoalRules.Before(model.ById[next], record)));
    }

    private static bool Contains(RecordEnvelope record, string field, string value) => record.Body.TryGetProperty(field, out var items)
        && items.ValueKind == System.Text.Json.JsonValueKind.Array
        && items.EnumerateArray().Any(item => item.ValueKind == System.Text.Json.JsonValueKind.String && item.GetString() == value);
}
