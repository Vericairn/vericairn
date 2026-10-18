// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;

namespace Vericairn.Runtime.Store;

/// <summary>Local implementation of the existing docs regenerate operation; it introduces no API operation or record.</summary>
public static class DocumentRepairWriter
{
    /// <summary>Prepares all members before publishing any, with the existing local batch journal as the interruption recovery route.</summary>
    public static DocumentChangePlan Repair(EstateSession session, string selection)
    {
        ArgumentNullException.ThrowIfNull(session);
        BatchJournal.ReadResult pending = BatchJournal.All(session);
        if (pending.Unreadable.Count != 0 || pending.Journals.Any(j => j.HasOutputs && !GeneratedOutputStore.Recover(session, j, session.Load())))
            return new(DocumentGenerationStatus.Unavailable, [], [], ["An interrupted batch must be recovered before another repair."]);
        DocumentChangePlan plan = DocumentRepair.Prepare(session.Load(), selection);
        if (plan.Status != DocumentGenerationStatus.Success || plan.Changes.IsEmpty) return plan;
        BatchJournal? journal = null;
        try
        {
            GeneratedOutputStore.Validate(session.Root, plan.Changes);
            journal = BatchJournal.Begin(session, "Repair the declared generated document bytes.", null, [], plan.Changes);
            GeneratedOutputStore.Apply(session.Root, plan.Changes, []);
            journal.Close();
            return plan;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(DocumentGenerationStatus.Error, [], [], [journal is null
                ? "Repair could not prepare safe output storage; nothing was changed."
                : "Output publication was interrupted. Its existing batch journal retains the exact pending bytes for recovery; no records or pins were written."]);
        }
    }
}
