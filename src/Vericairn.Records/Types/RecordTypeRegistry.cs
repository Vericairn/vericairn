// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>All record types this build reads; a type introduced in a later format says so (<see cref="IRecordType.SinceFormat"/>).</summary>
public static class RecordTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, IRecordType> Types = new IRecordType[]
    {
        new EstateType(),
        new PrincipalType(),
        new AuthorityType(),
        new CommunicationSettingType(),
        new CommunicationDelegationType(),
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
        new WorkProgressType(),
        new ResultType(),
        new AmendmentType(),
        new DeviceType(),
        new AdmissionType(),
        new ReconciliationType(),
        new ReleaseType(),
        new GoalType(),
        new GoalStatusType(),
        new RosterType(),
        new AskType(),
        new CustodyExceptionType(),
        new PossibilityType(),
        new DischargeType(),
        new CredentialReferenceType(),
        new QualificationType(),
        new HarnessQualificationType(),
        new MigrationType(),
        new VerificationType(),
        new ObservationType(),
        new OperationType(),
        new LivingDocumentType(),
        new DocumentReviewType(),
    }.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>Type names reserved for later releases; not yet admitted.</summary>
    public static readonly string[] Reserved = ["object"];

    /// <summary>All admitted types, ordered by name.</summary>
    public static IReadOnlyList<IRecordType> All => Types.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    /// <summary>Finds a type by name.</summary>
    public static IRecordType? Find(string name) => Types.TryGetValue(name, out IRecordType? type) ? type : null;
}
