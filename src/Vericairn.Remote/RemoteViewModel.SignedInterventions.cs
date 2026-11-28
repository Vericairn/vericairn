// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

public sealed partial class RemoteViewModel
{
    private const string InterventionHandoff = " Inspect the current state and prepare a desktop handoff to Workbench if the intervention is still needed.";

    private async Task<RemoteInterventionReceipt?> SendSignedInterventionAsync(RemoteEstate estate, IEstateApi api,
        RemoteIntervention review, RemoteInterventionReceipt receipt, bool retry)
    {
        string principal = estate.Connection.Principal;
        if (!connections.HasPresenceKey(estate.Connection))
            return RefusedIntervention(receipt, "This phone holds no presence key for this connection. Add one with a new pairing code to sign here.", retry);
        if (review.ReviewedSubject is not { Hash: { } hash } subject)
            return RefusedIntervention(receipt, "This runtime did not provide the exact record revision for phone signing.", retry);

        NewRecordRequest request = review.Operation == PauseGoal
            ? InterventionRecords.Pause(principal, subject.Id, hash, review.Revision, receipt.Marker)
            : InterventionRecords.Stop(principal, review.Target, subject.Id, hash, receipt.Id, review.Operation == ContainRun,
                receipt.Marker, review.ExpectedStopReason);
        RecordPrepared? submitted = null;
        bool exact = false;
        bool received = false;
        bool storageFailed = false;
        try
        {
            RuntimeConnection connection = estate.Connection;
            WriteOutcome written = await Task.Run(() => connections.WriteSignedAsync(connection, request, prepared =>
            {
                exact = PreparedMatches(prepared, request);
                return exact && Selected == estate && intervention == review;
            }, prepared =>
            {
                if (Selected != estate || intervention != review) return false;
                // The presence key has signed. Persist uncertainty before the signed request can leave the phone.
                var sending = receipt with
                {
                    State = "uncertain",
                    Observation = "The signed intervention's outcome is uncertain. Reconcile this original request before taking further action.",
                    Record = prepared.Id,
                    RecordHash = prepared.Hash,
                };
                try { connections.SaveIntervention(connection, sending); }
                catch (Exception exception) when (StorageFailure(exception))
                {
                    storageFailed = true;
                    return false;
                }
                receipt = sending;
                submitted = prepared;
                return true;
            })).ConfigureAwait(true);

            if (storageFailed)
            {
                if (Selected == estate) ShowInterventionStorageProblem();
                return null;
            }

            if (written.Written is null && written.Problem is { } refusal)
            {
                string message = !exact && refusal.Code == "declined"
                    ? "The prepared record did not match the exact reviewed intervention."
                    : refusal.Message + string.Concat(refusal.Findings.Select(finding => $"\n{finding.Code}: {finding.Message}"));
                if (!retry) return RefusedIntervention(receipt, message);

                // The prior plan can have arrived after the missing-record observation and won this request identity.
                RemoteInterventionReceipt observed = await ObserveInterventionAsync(api, principal, receipt with { State = "uncertain" }).ConfigureAwait(true);
                return observed.State == "not-delivered" ? observed with { Observation = message + InterventionHandoff } : observed;
            }
            if (submitted is null)
                return retry ? receipt : RefusedIntervention(receipt, "The signed intervention was not sent.");
            if (written.Written is null || written.Written.Id != submitted.Id || written.Written.Hash != submitted.Hash)
                return receipt with { State = "uncertain", Observation = "The runtime did not return the exact signed record. Reconcile the original request." };
            received = true;

            if (written.Problem is { } problem)
                return receipt with
                {
                    State = "uncertain",
                    Observation = problem.Message + InterventionHandoff,
                };

            // The signed record is a receipt. Read the goal's recorded status or the worker's observation before reporting an effect.
            return await ObserveInterventionAsync(api, principal, receipt).ConfigureAwait(true);
        }
        catch (Exception exception) when (DecisionFailure(exception))
        {
            ClientFailure? failure = ClientFailure.FromException(exception);
            if (!retry && (submitted is null || !received && failure?.Code == "refused"))
                return RefusedIntervention(receipt, failure?.Message ?? "The exact intervention could not be prepared, signed or kept on this phone.");

            return receipt with
            {
                State = "uncertain",
                Observation = "The signed intervention's outcome is uncertain. Reconcile this original request before taking further action.",
            };
        }
    }

    private static RemoteInterventionReceipt RefusedIntervention(RemoteInterventionReceipt receipt, string message, bool retry = false) =>
        receipt with { State = retry ? receipt.State : "rejected", Observation = message + InterventionHandoff };
}
