// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

namespace Vericairn.Records.Types;

/// <summary>All record types admitted in record format 1.</summary>
public static class RecordTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, IRecordType> Types = new IRecordType[]
    {
        new EstateType(),
        new PrincipalType(),
        new AuthorityType(),
        new DecisionType(),
        new ProductType(),
        new ResourceType(),
        new DocumentRevisionType(),
        new WorkOrderType(),
        new WorkStatusType(),
        new ReviewType(),
        new AcceptanceType(),
        new RetirementType(),
        new AssignmentType(),
        new AttemptType(),
        new ResultType(),
        new AmendmentType(),
        new DeviceType(),
        new AdmissionType(),
    }.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>Type names reserved for later releases of format 1; not yet admitted.</summary>
    public static readonly string[] Reserved = ["release", "migration", "object"];

    /// <summary>All admitted types, ordered by name.</summary>
    public static IReadOnlyList<IRecordType> All => Types.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>Finds a type by name.</summary>
    public static IRecordType? Find(string name) => Types.TryGetValue(name, out IRecordType? type) ? type : null;
}
