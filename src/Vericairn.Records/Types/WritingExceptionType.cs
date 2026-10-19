// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>One literal spacing token with attributable, terminal removal.</summary>
public sealed partial class WritingExceptionType : IRecordType
{
    /// <inheritdoc />
    public string Name => "writing-exception";
    /// <inheritdoc />
    public string Summary => "One exact writing.spacing token, its reason, optional coverage, signed author and signed removal; never authority for another action.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => false;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("token", "string", true, "One case-sensitive whole spacing token.", 3),
        new("code", "writing.spacing", true, "Only the spacing warning.", 3),
        new("reason", "string", true, "Why the spelling is intentional.", 3),
        new("scope", "object", false, "Union of repository path selectors and exact record/field selections; omitted means the whole scan.", 3),
        new("state", "active|removed", true, "Removal is a signed successor and terminal for this identity.", 3),
        new("basis", "object", true, "kind: operator, or kind: authority with an exact grant reference.", 3),
        new("removalReason", "string", false, "Required only for removal.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? token = body.Text("token", true, 200);
        if (token is not null) ValidateToken(body, "token", token);
        body.Enum("code", true, "writing.spacing");
        body.Text("reason", true, 4000);
        PolicyRecords.WritingCoverage(context, body.Nested("scope", false));
        string? state = body.Enum("state", true, "active", "removed");
        BodyReader? basis = body.Nested("basis", true);
        if (basis is not null)
        {
            string? kind = basis.Enum("kind", true, "operator", "authority");
            BodyReader? grant = basis.Nested("grant", kind == "authority");
            if (kind == "operator" && (!PolicyAuthority.OperatorAt(context.Model, context.Record.By, context.Record.At) || grant is not null)) basis.Error("policy.authority", "kind", "Only the operator can use operator authority.");
            PolicyAuthority.Grant(context, grant, Scopes.ManageWritingExceptions, state == "removed" ? "remove" : "add", context.Record);
            basis.NoOtherKeys();
        }
        body.Text("removalReason", state == "removed", 4000);
        if (state != "removed" && body.Has("removalReason")) body.Error("writing.removal", "removalReason", "Only a removal has a removal reason.");
        body.NoOtherKeys();
        RecordEnvelope? previous = context.Find(context.Record.Supersedes);
        if (previous is null)
        {
            if (state != "active") context.Error("writing.lifecycle", "An entry starts active; removal supersedes its active entry.");
        }
        else if (EstateModel.BodyString(previous, "state") != "active" || state != "removed" || !PolicyRecords.SameFields(previous, context.Record, "token", "code", "reason", "scope"))
            context.Error("writing.lifecycle", "A signed removal retains token, code, reason and scope. Removed identities cannot be revived or edited.");
    }

    internal static void ValidateToken(BodyReader body, string field, string token)
    {
        if (!IsToken(token)) body.Error("writing.token", field, "A token is one literal spacing token: at least four letters, lowercase after an optional capital, followed by digits.");
    }

    internal static bool IsToken(string token) => TokenPattern().IsMatch(token);

    [GeneratedRegex(@"\A[A-Za-z][a-z]{3,}[0-9]+\z")]
    private static partial Regex TokenPattern();
}
