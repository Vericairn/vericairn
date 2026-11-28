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
    internal static string? SetupTokenFormatProblem(long? format, HarnessSignIn? signIn) =>
        signIn is { Form: "setup-token" } && format is not >= 3
            ? "Claude Code setup-token sign-in requires record format 3 to record the operator's account type statement. Upgrade the estate to format 3 or use a provisioned credentials file."
            : null;

    public static JsonObject? Admit(EstateModel model, string launcher, RunPreflight observed)
    {
        HarnessSignIn? setupLogin = observed.SignIn is { Form: "setup-token" } login ? login : null;
        if (SetupTokenFormatProblem(model.Manifest?.RecordFormat, setupLogin) is string formatProblem)
            throw new ProviderRefusedException(formatProblem);
        if (observed.Probes is null && setupLogin is null) return null; // Existing non-native launcher contracts stay additive.
        if (model.Findings.HasErrors) throw new ProviderRefusedException("Harness admission requires a valid estate.");
        if (observed.Probes is not { } probes || !CompleteProbes(observed, probes))
            throw new ProviderRefusedException("The native harness did not establish all qualification probe digests.");

        var admission = new JsonObject
        {
            ["kind"] = "baseline", ["launcher"] = launcher, ["version"] = observed.HarnessVersion,
            ["probes"] = JsonSerializer.SerializeToNode(probes.Digests),
        };
        // A shipped baseline cannot attest to the operator's account type statement for a provisioned setup token.
        if (setupLogin is null && observed.HarnessVersion == probes.BaselineVersion) return admission;
        RecordEnvelope[] candidates = Qualifications(model, launcher).Where(r => EstateModel.BodyString(r, "version") == observed.HarnessVersion).ToArray();
        RecordEnvelope? qualification = candidates.FirstOrDefault(r =>
            HarnessQualificationType.ProbeNames.All(n => r.Body.GetProperty("probes").GetProperty(n).GetString() == probes.Digests[n])
            && (setupLogin is null || DeclaresLogin(r, setupLogin)));
        if (qualification is null)
        {
            if (setupLogin is not null)
                throw new ProviderRefusedException("Claude Code setup-token sign-in requires a current qualification with matching probes and the operator's account type statement. Record the qualification with worker qualify.");
            throw new ProviderRefusedException(candidates.Length == 0
                ? $"The {launcher} harness version is not qualified; only the exact shipped baseline or a current unrevoked qualification admits it."
                : "The local harness probe digests differ from the recorded qualification; no run was admitted.");
        }
        admission["kind"] = "qualification";
        admission["record"] = qualification.Id;
        admission["hash"] = qualification.ComputedHash;
        return admission;
    }

    private static bool DeclaresLogin(RecordEnvelope qualification, HarnessSignIn signIn) =>
        qualification.Format >= 3 && qualification.Body.TryGetProperty("login", out JsonElement login)
        && login.GetProperty("form").GetString() == signIn.Form
        && login.GetProperty("accountType").GetString() == signIn.AccountType
        && login.GetProperty("accountTypeSource").GetString() == signIn.AccountTypeSource;

    internal static bool CompleteProbes(RunPreflight observed, HarnessProbeEvidence probes) =>
        observed.Complete && observed.InheritsEnvironment && HarnessQualificationType.ExactVersion(observed.HarnessVersion)
        && probes.Digests.Count == HarnessQualificationType.ProbeNames.Length
        && HarnessQualificationType.ProbeNames.All(n => probes.Digests.TryGetValue(n, out string? value) && ContentHash.IsValid(value));

    private static IEnumerable<RecordEnvelope> Qualifications(EstateModel model, string launcher) =>
        model.Current("harness-qualification").Where(r => EstateModel.BodyString(r, "launcher") == launcher
            && EstateModel.BodyString(r, "status") == "qualified" && r.AtTime <= DateTimeOffset.UtcNow)
            .OrderBy(r => r.Id, StringComparer.Ordinal);

    internal static HarnessVersionComparison CompareVersions(EstateModel? model, string launcher, string installed, string baseline)
    {
        string[] qualifications = model is null ? [] : Qualifications(model, launcher)
            .OrderByDescending(r => r.AtTime).ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .Select(r => EstateModel.BodyString(r, "version")!).ToArray();
        bool matches = installed == baseline || qualifications.Contains(installed, StringComparer.Ordinal);
        return new(installed, matches ? installed : qualifications.FirstOrDefault() ?? baseline, matches);
    }

    public static bool StillCurrent(EstateModel model, string launcher, RunPreflight observed, JsonNode? admitted)
    {
        try { return JsonNode.DeepEquals(admitted, Admit(model, launcher, observed)); }
        catch (ProviderRefusedException) { return false; }
    }
}

/// <summary>Version comparison alone; where the version matches, run admission still needs the matching probes.</summary>
internal sealed record HarnessVersionComparison(string Installed, string Qualified, bool Matches);
