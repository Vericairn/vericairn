// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>Documentation of one body field, printed by <c>vericairn records describe</c>.</summary>
/// <param name="Name">Field name.</param>
/// <param name="Kind">Value kind in plain words.</param>
/// <param name="Required">Whether the field is required (in the formats that have it).</param>
/// <param name="Description">What the field means.</param>
/// <param name="SinceFormat">The record format that introduced the field; records of older formats never carry it.</param>
public sealed record FieldDescription(string Name, string Kind, bool Required, string Description, long SinceFormat = RecordFormats.First);

/// <summary>A record type: its name, documentation, and body rules.</summary>
public interface IRecordType
{
    /// <summary>Kebab-case type name.</summary>
    string Name { get; }

    /// <summary>One-sentence meaning.</summary>
    string Summary { get; }

    /// <summary>Required subject type, or null when the type has no subject.</summary>
    string? SubjectType { get; }

    /// <summary>Whether a subject must be pinned to an exact hash.</summary>
    bool SubjectHashRequired { get; }

    /// <summary>Body field documentation.</summary>
    IReadOnlyList<FieldDescription> Fields { get; }

    /// <summary>The record format that introduced the type; a record of an older format cannot be of this type.</summary>
    long SinceFormat => RecordFormats.First;

    /// <summary>Validates the body and the record's relationships.</summary>
    void Validate(ValidationContext context);
}

/// <summary>Everything a type validator can see.</summary>
public sealed class ValidationContext
{
    /// <summary>Creates a context.</summary>
    public ValidationContext(EstateModel model, RecordEnvelope record, FindingList findings)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Findings = findings ?? throw new ArgumentNullException(nameof(findings));
        Body = new BodyReader(record.Body, record.ExpectedPath + "#body", findings, record.Format);
    }

    /// <summary>The record format the record under validation states; its rules are the ones that apply.</summary>
    public long Format => Record.Format;

    /// <summary>The whole estate.</summary>
    public EstateModel Model { get; }

    /// <summary>The record under validation.</summary>
    public RecordEnvelope Record { get; }

    /// <summary>Where findings go.</summary>
    public FindingList Findings { get; }

    /// <summary>Reader over the body.</summary>
    public BodyReader Body { get; }

    /// <summary>Repository-relative location of the record.</summary>
    public string Location => Record.ExpectedPath;

    /// <summary>Finds any record by id.</summary>
    public RecordEnvelope? Find(string? id) => id is not null && Model.ById.TryGetValue(id, out RecordEnvelope? record) ? record : null;

    /// <summary>Finds a current (not superseded) record of a type.</summary>
    public RecordEnvelope? FindCurrent(string? id, string type)
    {
        RecordEnvelope? record = Find(id);
        return record is not null && record.Type == type && Model.IsCurrent(record) ? record : null;
    }

    /// <summary>The principal record currently describing a principal id.</summary>
    public RecordEnvelope? Principal(string? id) => Model.CurrentPrincipal(id);

    /// <summary>True when the principal currently holds the role.</summary>
    public bool HasRole(string? principalId, string role) => Model.HasRole(principalId, role);

    /// <summary>True when a grant gives the principal the scope and had not expired when this record was authored.</summary>
    public bool HasScope(string? principalId, string scope) => Model.HasScope(principalId, scope, Record.AtTime);

    /// <summary>Checks that a body field references an existing record of the expected type; returns the record.</summary>
    public RecordEnvelope? RequireReference(string field, string? id, string expectedType)
    {
        if (id is null)
        {
            return null;
        }

        RecordEnvelope? target = Find(id);
        if (target is null)
        {
            Findings.Error("body.reference", $"{Location}#body.{field}", $"'{field}' references {id}, which does not exist in this estate.");
            return null;
        }

        if (target.Type != expectedType)
        {
            Findings.Error("body.reference", $"{Location}#body.{field}", $"'{field}' must reference a {expectedType} record, not a {target.Type}.");
            return null;
        }

        return target;
    }

    /// <summary>Adds an error about the record as a whole.</summary>
    public void Error(string code, string message) => Findings.Error(code, Location, message);

    /// <summary>Adds a warning about the record as a whole.</summary>
    public void Warning(string code, string message) => Findings.Warning(code, Location, message);
}
