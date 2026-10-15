// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

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
        new("anchored", "object[]", false, "The admissions recorded without a signature that were in force when the estate moved to record format 2, each {id, hash}; signed by the operator or a principal with the 'administer' scope, this list is what keeps them counting after the move.", 2),
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
        IReadOnlyList<BodyReader>? anchored = body.Since(2, "anchored") ? body.NestedArray("anchored", false, allowEmpty: false) : null;
        if (anchored is not null)
        {
            foreach (BodyReader entry in anchored)
            {
                string? id = entry.Ulid("id", true);
                string? hash = entry.Hash("hash", true);
                entry.NoOtherKeys();
                RecordEnvelope? admission = context.Find(id);
                if (id is not null && admission?.Type != "admission")
                {
                    entry.Error("migration.anchored", "id", $"{id} names no admission record.");
                }
                else if (admission is not null && admission.Signatures.Count > 0)
                {
                    entry.Error("migration.anchored", "id", $"Admission {id} is signed and needs no anchor; only admissions recorded without a signature are listed.");
                }
                else if (admission is not null && hash is not null && admission.ComputedHash != hash)
                {
                    entry.Error("migration.anchored", "hash", $"Admission {id} has the hash {admission.ComputedHash}, not {hash}.");
                }
            }

            if (!body.Element.TryGetProperty("migrations", out System.Text.Json.JsonElement steps) || steps.ValueKind != System.Text.Json.JsonValueKind.Array || !steps.EnumerateArray().Any(s => s.ValueKind == System.Text.Json.JsonValueKind.String && s.GetString() == "record-format-2"))
            {
                body.Error("migration.anchored", "anchored", "Only the migration that moves the estate to record format 2 anchors admissions.");
            }

            if (context.Record.Signatures.Count == 0)
            {
                body.Error("migration.anchor-unsigned", "anchored", "A migration that anchors admissions is signed, by the operator or a principal with the 'administer' scope; unsigned, it anchors nothing.");
            }

            if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.Administer))
            {
                body.Error("migration.anchor-author", "anchored", "Admissions are anchored by the operator or a principal with the 'administer' scope.");
            }
        }

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
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.HasScope(context.Record.By, Scopes.LocalEngineering))
        {
            context.Error("migration.author", "Upgrades are applied by the operator or under the 'local-engineering' scope.");
        }
    }
}
