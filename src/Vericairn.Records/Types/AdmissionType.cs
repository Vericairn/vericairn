// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Admits a principal's device to author records in this estate, or revokes that admission.</summary>
public sealed class AdmissionType : IRecordType
{
    /// <inheritdoc />
    public string Name => "admission";

    /// <inheritdoc />
    public string Summary => "Admits a principal's device to author records in this estate (status admitted), or revokes it (a superseding record with status revoked and revokedAt). Authored by the operator or under the 'administer' scope; obtaining a clone grants nothing.";

    /// <inheritdoc />
    public string? SubjectType => "device";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("principal", "ulid", true, "The device's principal, repeated for clarity; must match the device record."),
        new("status", "admitted|revoked", true, "admitted, or revoked by a superseding record."),
        new("note", "string", true, "Why."),
        new("revokedAt", "timestamp", false, "When the admission ended; required when revoked; records signed by the device at or after this time are invalid."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? principal = body.Ulid("principal", true);
        string? status = body.Enum("status", true, "admitted", "revoked");
        body.Text("note", true, 2000);
        string? revokedAt = body.Timestamp("revokedAt", status == "revoked");
        body.NoOtherKeys();
        if (status == "admitted" && revokedAt is not null)
        {
            body.Error("admission.revoked-at", "revokedAt", "An admitted device has no revokedAt.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("admission.author", $"Admitting principal {context.Record.By} has no principal record.");
        }
        else if (!context.HasRole(context.Record.By, Roles.Operator) && !context.Model.HasScope(context.Record.By, Scopes.Administer))
        {
            context.Error("admission.author", "Devices are admitted by the operator or under the 'administer' scope.");
        }

        RecordEnvelope? device = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (device is not null && device.Type == "device" && principal is not null && EstateModel.BodyString(device, "principal") != principal)
        {
            context.Error("admission.principal", "The admission's principal must be the device's principal.");
        }

        if (status == "revoked" && context.Record.Supersedes is null)
        {
            context.Error("admission.revoke", "A revocation supersedes the admission it ends.");
        }
    }
}
