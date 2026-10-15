// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Portability;

/// <summary>
/// Recovery after an interruption or a move: what is known, what is missing or uncertain, what this machine can do, and how two
/// copies of an estate differ. Recovery restores information, never authority.
/// </summary>
public static class Recovery
{
    /// <summary>What an estate looks like from this machine: records, devices held here, work in progress, unresolved effects, reconcile tasks.</summary>
    public static string Inspect(EstateModel model, FindingList checks)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(checks);
        var text = new StringBuilder();
        RecordEnvelope? estate = model.CurrentEstate;
        text.Append("Estate: ").Append(estate is null ? "(no charter)" : $"{EstateModel.BodyString(estate, "name")} ({EstateModel.BodyString(estate, "slug")})").Append(" · ").Append(model.Records.Count.ToString(CultureInfo.InvariantCulture)).Append(" records · ").Append(model.Manifest?.Describe() ?? "(no manifest)").Append(" · profile ").Append(model.Manifest?.Profile ?? "?").Append('\n');
        string? policy = estate is not null && estate.Body.TryGetProperty("policies", out System.Text.Json.JsonElement policies) && policies.TryGetProperty("signatures", out System.Text.Json.JsonElement p) ? p.GetString() : null;
        text.Append("Signatures: ").Append(policy ?? "?").Append('\n');

        IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
        text.Append("Devices known to the estate:\n");
        bool anyDevice = false;
        foreach (RecordEnvelope device in model.Current("device").OrderBy(d => model.Chain(d)[0], StringComparer.Ordinal))
        {
            anyDevice = true;
            string principal = EstateModel.BodyString(device, "principal") ?? "?";
            string identity = model.Chain(device)[0];
            bool held = stores.Any(s => s.Has(identity));
            text.Append("  ").Append(identity).Append("  ").Append(Name(model, principal)).Append("  ").Append(EstateModel.BodyString(device, "name")).Append("  ")
                .Append(SignatureChecks.IsAdmitted(model, device) ? "admitted" : "not admitted")
                .Append(held ? "  key held on this machine" : EstateModel.BodyString(device, "custody") == DeviceKeyStores.OfflineKind ? "  recovery key kept offline" : "  key not held here").Append('\n');
        }

        if (!anyDevice)
        {
            text.Append("  (none)\n");
        }

        text.Append("Authoring from this machine: ");
        List<string> canAuthor = model.Current("device").Where(d => SignatureChecks.IsAdmitted(model, d) && stores.Any(s => s.Has(model.Chain(d)[0]))).Select(d => Name(model, EstateModel.BodyString(d, "principal") ?? "?")).Distinct(StringComparer.Ordinal).ToList();
        text.Append(canAuthor.Count == 0 ? (policy == "required" ? "nobody; this copy is read-only until a device held here is admitted" : "unsigned only; no admitted device key is held here") : string.Join(", ", canAuthor)).Append('\n');

        text.Append("Work in progress:\n");
        bool anyWork = false;
        foreach (WorkView.AssignmentItem item in WorkView.AssignmentItems(model).Where(i => i.State is "in progress" or "awaiting review" or "no attempt"))
        {
            anyWork = true;
            text.Append("  ").Append(item.Key).Append("  ").Append(item.ContributorName).Append("  ").Append(item.Outcome).Append(", ").Append(item.State).Append('\n');
        }

        if (!anyWork)
        {
            text.Append("  (none)\n");
        }

        text.Append("Unresolved external effects:\n");
        bool anyEffect = false;
        foreach (RecordEnvelope result in model.Current("result").OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            if (!result.Body.TryGetProperty("unresolvedEffects", out System.Text.Json.JsonElement effects) || effects.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (System.Text.Json.JsonElement effect in effects.EnumerateArray())
            {
                anyEffect = true;
                text.Append("  result ").Append(result.Id).Append(": ").Append(effect.GetString()).Append('\n');
            }
        }

        if (!anyEffect)
        {
            text.Append("  (none)\n");
        }

        IReadOnlyList<Finding> reconcile = EstateStatus.ReconcileTasks(Merge(model.Findings, checks));
        text.Append("Reconcile tasks: ").Append(reconcile.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (Finding task in reconcile)
        {
            text.Append("  ").Append(task.Location).Append(": ").Append(task.Message).Append('\n');
        }

        text.Append("Recovery restores records, not authority: keys stay on their devices, sessions are not carried, and an uncertain external effect is reconciled under its original identity before any retry.\n");
        return text.ToString();
    }

    /// <summary>How two copies of an estate differ by record id and content, before or instead of a merge.</summary>
    public static string Compare(EstateModel left, string leftName, EstateModel right, string rightName)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var text = new StringBuilder();
        string? leftEstate = left.CurrentEstate is null ? null : left.Chain(left.CurrentEstate)[0];
        string? rightEstate = right.CurrentEstate is null ? null : right.Chain(right.CurrentEstate)[0];
        text.Append(leftEstate == rightEstate ? "Same estate " : "DIFFERENT ESTATES ").Append(leftEstate ?? "?").Append(leftEstate == rightEstate ? string.Empty : " and " + (rightEstate ?? "?")).Append('\n');
        List<string> onlyLeft = left.ById.Keys.Except(right.ById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        List<string> onlyRight = right.ById.Keys.Except(left.ById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        List<string> differing = left.ById.Keys.Intersect(right.ById.Keys, StringComparer.Ordinal).Where(id => left.ById[id].ComputedHash != right.ById[id].ComputedHash).Order(StringComparer.Ordinal).ToList();
        text.Append("Only in ").Append(leftName).Append(": ").Append(onlyLeft.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (string id in onlyLeft)
        {
            text.Append("  ").Append(left.ById[id].Type).Append(' ').Append(id).Append('\n');
        }

        text.Append("Only in ").Append(rightName).Append(": ").Append(onlyRight.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (string id in onlyRight)
        {
            text.Append("  ").Append(right.ById[id].Type).Append(' ').Append(id).Append('\n');
        }

        text.Append("Same id, different content (never expected; records are immutable): ").Append(differing.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (string id in differing)
        {
            text.Append("  ").Append(id).Append('\n');
        }

        text.Append("A merge adds the records each side lacks; divergent revisions then appear as reconcile tasks.\n");
        return text.ToString();
    }

    private static FindingList Merge(FindingList first, FindingList second)
    {
        var all = new FindingList();
        foreach (Finding finding in first.Concat(second))
        {
            all.Add(finding);
        }

        return all;
    }

    private static string Name(EstateModel model, string principal) =>
        model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "name") ?? principal : principal;
}
