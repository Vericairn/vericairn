// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>An upgrade of the estate to a Vericairn release: which release reads it now, which record-format migrations ran, and what changed.</summary>
public sealed class MigrationType : IRecordType
{
    /// <inheritdoc />
    public string Name => "migration";

    /// <inheritdoc />
    public string Summary => "An upgrade of the estate applied by 'vericairn estate upgrade': the release pinned before and after, the record format before and after, the migrations applied and what they changed. Forward-only; rollback is a reverted commit.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("fromPin", "string", true, "The pin before: 'source <repository>@<commit>' or 'release <version>'."),
        new("toPin", "string", true, "The pin after."),
        new("fromFormat", "integer", true, "Record format before."),
        new("toFormat", "integer", true, "Record format after."),
        new("migrations", "string[]", true, "Names of the record-format migrations applied; may be empty."),
        new("changes", "string[]", true, "What changed in the repository; may be empty."),
        new("release", "ulid", false, "The release record upgraded to, when pinning a release."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("fromPin", true, 300);
        body.Text("toPin", true, 300);
        long? from = body.Number("fromFormat", true, 1, 1000);
        long? to = body.Number("toFormat", true, 1, 1000);
        body.StringArray("migrations", true, allowEmpty: true, maxItemLength: 200);
        body.StringArray("changes", true, allowEmpty: true, maxItemLength: 2000);
        string? release = body.Ulid("release", false);
        body.NoOtherKeys();
        if (from is not null && to is not null && to < from)
        {
            body.Error("migration.backward", "toFormat", "Migrations are forward-only; rollback is a reverted commit.");
        }

        if (release is not null)
        {
            context.RequireReference("release", release, "release");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("migration.author", $"Upgrading principal {context.Record.By} has no principal record.");
        }
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.Model.HasScope(context.Record.By, Scopes.LocalEngineering))
        {
            context.Error("migration.author", "Upgrades are applied by the operator or under the 'local-engineering' scope.");
        }
    }
}
