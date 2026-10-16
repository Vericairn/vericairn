// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Admits a native probe result by the shipped baseline or one current exact-version qualification.</summary>
internal static class HarnessAdmission
{
    public static JsonObject? Admit(EstateModel model, string launcher, RunPreflight observed)
    {
        if (observed.Probes is not { } probes) return null; // Existing non-native launcher contracts stay additive.
        if (model.Findings.HasErrors) throw new ProviderRefusedException("Harness admission requires a valid estate.");
        if (!observed.Complete || !observed.InheritsEnvironment || !HarnessQualificationType.ExactVersion(observed.HarnessVersion)
            || probes.Digests.Count != HarnessQualificationType.ProbeNames.Length
            || HarnessQualificationType.ProbeNames.Any(n => !probes.Digests.TryGetValue(n, out string? value) || !ContentHash.IsValid(value)))
            throw new ProviderRefusedException("The native harness did not establish all qualification probe digests.");

        var admission = new JsonObject
        {
            ["kind"] = "baseline", ["launcher"] = launcher, ["version"] = observed.HarnessVersion,
            ["probes"] = JsonSerializer.SerializeToNode(probes.Digests),
        };
        if (observed.HarnessVersion == probes.BaselineVersion) return admission;
        RecordEnvelope[] candidates = model.Current("harness-qualification").Where(r => EstateModel.BodyString(r, "launcher") == launcher
            && EstateModel.BodyString(r, "version") == observed.HarnessVersion && EstateModel.BodyString(r, "status") == "qualified"
            && r.AtTime <= DateTimeOffset.UtcNow).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        RecordEnvelope? qualification = candidates.FirstOrDefault(r => HarnessQualificationType.ProbeNames.All(n =>
            r.Body.GetProperty("probes").GetProperty(n).GetString() == probes.Digests[n]));
        if (qualification is null)
        {
            throw new ProviderRefusedException(candidates.Length == 0
                ? $"The {launcher} harness version is not qualified; only the exact shipped baseline or a current unrevoked qualification admits it."
                : "The local harness probe digests differ from the recorded qualification; no run was admitted.");
        }
        admission["kind"] = "qualification";
        admission["record"] = qualification.Id;
        admission["hash"] = qualification.ComputedHash;
        return admission;
    }

    public static bool StillCurrent(EstateModel model, string launcher, RunPreflight observed, JsonNode? admitted)
    {
        try { return JsonNode.DeepEquals(admitted, Admit(model, launcher, observed)); }
        catch (ProviderRefusedException) { return false; }
    }
}
