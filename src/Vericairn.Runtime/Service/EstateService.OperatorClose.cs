// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public OperatorCloseCandidatesResponse OperatorCloseCandidates()
    {
        lock (gate)
        {
            // Inspection neither refreshes the live store nor probes signing custody.
            EstateModel model = session.Read();
            return new(CloseCandidates(model), CloseModelProblem(model));
        }
    }

    /// <inheritdoc />
    public OperatorCloseResponse OperatorClose(OperatorCloseRequest request) => OperatorClose(request, CancellationToken.None);

    /// <inheritdoc />
    public Task<OperatorCloseResponse> OperatorCloseAsync(OperatorCloseRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(OperatorClose), () => OperatorClose(request, cancellationToken), true, this, cancellationToken);

    /// <summary>Signs the displayed selections, rechecks them, then stores their acceptances as one validated batch.</summary>
    public OperatorCloseResponse OperatorClose(OperatorCloseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Selection is null || request.Selection.Any(s => s is null || string.IsNullOrWhiteSpace(s.Key)
                || s.WorkOrder is null || s.Result is null || s.Reviews is null || s.Reviews.Any(r => r is null)))
                return new([], new("refused", "Supply the exact selections from operator close's listing.", []));
            // The caller may own mutable lists. Copy them before any presence prompt can yield control.
            OperatorCloseSelection[] selection = [.. request.Selection.Select(s => s with { Reviews = s.Reviews.ToArray() })];
            if (selection.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != selection.Length || (!request.All && selection.Length == 0))
                return new([], new("refused", "Select distinct work-order keys, or all of the displayed candidates.", []));

            EstateModel model = session.Load();
            if (Check(model) is Problem refused) return new([], refused);
            if (selection.Length == 0) return new([]);
            string? refusal = ResolveSigner(model, new RecordDraft("acceptance", request.By, default), out IRecordSigner? signer);
            if (refusal is not null || signer is null)
                return new([], new("refused", "The operator needs their own admitted signing device on this machine. No unsigned acceptance is allowed.", []));

            List<AuthoredRecord> records = [];
            foreach (OperatorCloseSelection item in selection)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RecordEnvelope result = model.ById[item.Result.Id];
                JsonElement body = JsonSerializer.SerializeToElement(new
                {
                    statement = "Closed with operator close after inspecting the exact result and passing reviews.",
                    basis = item.Reviews.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray(),
                    self = result.By == request.By,
                });
                // Custody currently signs one record per presence check; it offers no batch presence session.
                (AuthoredRecord? record, refusal) = AuthorLocked(new("acceptance", request.By, body, result.Reference), model, [signer]);
                if (record is null) return new([], new("refused", refusal!, []));
                records.Add(record);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stored = StoreTogetherLocked(records, "Close the operator's selected finished steps.", null, Check);
            return new(stored.Written, stored.Written.Count > 0 && stored.Problem is not null
                ? stored.Problem with { Code = "reconcile-required", Message = "Some or all selected acceptances were stored, but the batch did not finish. Inspect the returned receipts and original batch journal before retrying; do not repeat stored acceptances." }
                : stored.Problem);

            Problem? Check(EstateModel current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RequestDeviceRefusal(current) is Problem denied) return denied;
                if (string.IsNullOrWhiteSpace(request.By) || !current.HasRole(request.By, Roles.Operator))
                    return new("denied", "Only an operator can close finished steps.", []);
                if (requestDevice.Value is { Closed: false } device && Standing(current, device.Device)?.Principal != request.By)
                    return new("denied", "An operator command can act only for the requesting device's own principal.", []);
                if (CloseModelProblem(current) is Problem invalid) return invalid;
                List<OperatorCloseItem> candidates = CloseCandidates(current);
                if (request.All && candidates.Count != selection.Length) return CloseConflict();
                foreach (OperatorCloseSelection expected in selection)
                {
                    OperatorCloseItem? actual = candidates.SingleOrDefault(c => c.Key == expected.Key);
                    if (actual is null || actual.WorkOrder != expected.WorkOrder || actual.Result != expected.Result
                        || !actual.Reviews.Select(r => new OperatorClosePin(r.Id, r.Hash)).OrderBy(r => r.Id, StringComparer.Ordinal)
                            .SequenceEqual(expected.Reviews.OrderBy(r => r.Id, StringComparer.Ordinal)))
                        return CloseConflict();
                }
                return null;
            }
        }
    }

    private static Problem CloseConflict() => new("conflict",
        "A selected step changed or is no longer eligible: its work order, latest completed result, complete passing-review basis or acceptance moved. Read operator close again and select the current evidence; nothing was written.", []);

    private static Problem? CloseModelProblem(EstateModel model) => model.Findings.HasErrors
        ? new("rejected", "Resolve the estate's record errors before closing work.", ToFindings(model.Findings, "rejected").Findings) : null;

    private static List<OperatorCloseItem> CloseCandidates(EstateModel model)
    {
        var results = model.Current("result").Select(r => (Result: r, Order: WorkView.OrderOfResult(model, r)))
            .Where(r => r.Order is not null).GroupBy(r => model.Latest(r.Order!).Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Result.At, StringComparer.Ordinal).ThenBy(r => r.Result.Id, StringComparer.Ordinal).Last().Result, StringComparer.Ordinal);
        List<OperatorCloseItem> items = [];
        foreach (RecordEnvelope order in model.Current("work-order").OrderBy(o => EstateModel.BodyString(o, "key"), StringComparer.Ordinal))
        {
            if (!results.TryGetValue(order.Id, out RecordEnvelope? result) || EstateModel.BodyString(result, "outcome") != "completed"
                || model.OfType("acceptance").Any(a => a.Subject == result.Reference)) continue;
            OperatorCloseReview[] reviews = [.. model.About(result.Id, "review").Where(r => model.IsCurrent(r)
                    && r.Subject == result.Reference && EstateModel.BodyString(r, "outcome") == "pass")
                .OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new OperatorCloseReview(r.Id, r.ComputedHash,
                    EstateModel.BodyString(r, "independence")!, EstateModel.BodyString(r, "summary")!))];
            if (reviews.Length == 0) continue;
            items.Add(new(EstateModel.BodyString(order, "key")!, new(order.Id, order.ComputedHash), EstateModel.BodyString(order, "exitQuestion")!,
                new(result.Id, result.ComputedHash), EstateModel.BodyString(result, "outcome")!, EstateModel.BodyString(result, "summary")!, reviews));
        }
        return items;
    }
}
