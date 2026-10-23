// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    // Inspection never recovers or closes a journal. A prior generated batch must be reconciled before submission.
    private List<Finding> ReleaseBatchFindings()
    {
        BatchJournal.ReadResult batches = BatchJournal.All(session);
        List<Finding> findings = [.. UnreadableBatches(batches).Select(f => f with { Severity = Severity.Error })];
        foreach (BatchJournal journal in batches.Journals.Where(j => j.HasOutputs))
            findings.Add(new(Severity.Error, "batch.outputs-incomplete", "batch-journal:" + journal.Id,
                "A generated batch requires reconciliation. Recover its original records and outputs before submitting."));
        return findings;
    }

    // The proposal exists only in memory. Signature availability is checked without invoking Sign or presence.
    // Actual writes still run AuthorLocked and the complete signed-record validation under the same gate.
    private List<FindingDto> CheckReleaseRecord(RecordDraft draft, EstateModel model)
    {
        List<FindingDto> objections = [];
        if (CommunicationWriteRefusal(draft, model) is { } communication) objections.Add(new("error", "release.author", "release", communication));
        string? refusal = ResolveSigner(model, draft, out IRecordSigner? signer);
        if (refusal is not null || model.Manifest?.RecordFormat >= 3 && signer is null)
            objections.Add(new("error", "release.signature", "release", refusal ?? "A format-3 release intent needs the acting principal's admitted signing device."));
        draft = draft with { Format = Math.Min(model.Manifest?.RecordFormat ?? session.Manifest.RecordFormat, RecordFormats.Current) };
        if (draft.Supersedes is { } previous && model.ById.TryGetValue(previous, out RecordEnvelope? predecessor) && draft.At < predecessor.AtTime)
            draft = draft with { At = predecessor.AtTime };
        try
        {
            AuthoredRecord proposal = RecordAuthor.Create(draft);
            if (signer is not null && model.ById.TryGetValue(signer.DeviceId, out RecordEnvelope? device))
            {
                if (SignatureChecks.Recovers(model, signer.DeviceId))
                    objections.Add(new("error", "signature.recovery-scope", "release-intent", "The selected device is a recovery device, which cannot sign a release intent."));
                string? signatureCode = SignatureChecks.AdmissionStateAt(model, device, proposal.Envelope.At) switch
                {
                    SignatureChecks.AdmissionState.Admitted => null,
                    SignatureChecks.AdmissionState.NotYetAdmitted => "signature.before-admission",
                    SignatureChecks.AdmissionState.Revoked => "signature.revoked",
                    _ => "signature.unadmitted",
                };
                if (signatureCode is not null)
                    objections.Add(new("error", signatureCode, "release-intent", "The selected signing device is not admitted at the proposed intent's authoring time."));
                // A P-256 signature's encoding has a fixed length. Measure that envelope without accessing a key;
                // these sizing bytes are never parsed as a record, retained, or passed to the authoring boundary.
                JsonObject sizing = JsonNode.Parse(proposal.Bytes)!.AsObject();
                sizing["signatures"] = new JsonArray(new JsonObject
                {
                    ["algorithm"] = RecordSigning.Algorithm, ["device"] = signer.DeviceId,
                    ["signature"] = new string('A', (RecordSigning.SignatureBytes * 8 + 5) / 6),
                });
                long signedBytes = CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(sizing)).LongLength;
                if (proposal.Bytes.LongLength <= EstateLayout.MaxFileBytes && signedBytes > EstateLayout.MaxFileBytes)
                    objections.Add(new("error", "layout.size", "release-intent", $"The signed intent would be {signedBytes:N0} bytes, exceeding the {EstateLayout.MaxFileBytes:N0}-byte record limit."));
            }
            EstateModel proposed = EstateLoader.Load(session.Root, session.ReadFiles().Append(new RecordFile(proposal.RelativePath, proposal.Bytes)).ToArray());
            // Only this unsigned in-memory proposal's missing signature is accounted for by the availability check above.
            // Signature findings on every existing record remain objections. No unsigned proposal can reach WriteLocked.
            objections.AddRange(Invalid(proposed, [proposal]).Where(f => !(f.Code == "signature.required" && f.Location == proposal.RelativePath))
                .Select(f => new FindingDto("error", f.Code, f.Location.Replace(proposal.RelativePath, "release-intent", StringComparison.Ordinal), f.Message)));
            if (JudgePolicySources([proposal], proposed) is { } policy) objections.AddRange(policy.Findings);
            DocumentChangePlan generated = DocumentRecordChanges.Prepare(model, proposed, [proposal.Envelope]);
            if (generated.Status != DocumentGenerationStatus.Success)
                objections.AddRange(generated.Problems.Select(p => new FindingDto("error", "release.companions", "release", p)));
            else GeneratedOutputStore.Validate(session.Root, generated.Changes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            objections.Add(new("error", "release.proposal", "release", "The proposed intent or its generated companions could not be validated. Restore readable inputs and check again."));
        }
        return objections;
    }
}
