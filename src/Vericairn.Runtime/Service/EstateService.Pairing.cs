// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    // Tests provide isolated trust anchors and a recorded status list, never platform keys or a network call.
    internal AndroidKeyAttestation PairingAttestation { get; set; } = AndroidKeyAttestation.Google;

    private const string PairingEvidenceOnly = "Android attestation evidence is recorded only by a verified pairing.";

    private static bool SuppliesPairingEvidence(string type, JsonElement body) => type == "admission"
        && body.ValueKind == JsonValueKind.Object && body.TryGetProperty(AndroidAttestationEvidence.Field, out _);

    private string? PairingPresenceRefusal(PairingPlan plan, PairingDevice device, DateTimeOffset at, out AndroidAttestationEvidence? evidence)
    {
        evidence = null;
        if (device.AndroidAttestation is not { } chain)
        {
            return device.Presence == "required" || device.Presence == "declared" && device.Custody == DeviceKeyStores.AndroidKeystoreKind
                ? "Android hardware attestation is required to record verified presence. Update Remote and pair again, or pair a request key alone. Nothing was recorded."
                : null;
        }
        if (plan.Format < 2 || device.Presence != "required" || device.Platform != "android" || device.Custody != DeviceKeyStores.AndroidKeystoreKind
            || ParseAt(plan.At) is not { } plannedAt || plannedAt > at
            || (evidence = PairingAttestation.Verify(chain, plan.RuntimeNonce, device.PublicKey, at)) is null)
        {
            return "Android key attestation did not verify per-use hardware authentication for this key and pairing. Nothing was recorded.";
        }
        return null;
    }

    private static JsonObject PairedDeviceBody(PairingPlan plan, IReadOnlyList<string> profiles, PairingDevice device)
    {
        JsonObject body = DeviceRecords.Body(plan.Principal, device.Name, device.Platform, device.Custody, device.PublicKey, profiles, plan.Format, device.Presence);
        if (device.AndroidAttestation is not null) body["presenceVerification"] = DeviceRecords.AndroidKeyAttestation;
        return body;
    }

    private static JsonObject PairedAdmissionBody(PairingPlan plan, string note, AndroidAttestationEvidence? evidence)
    {
        var body = new JsonObject { ["principal"] = plan.Principal, ["status"] = "admitted", ["note"] = note };
        if (evidence is not null) body[AndroidAttestationEvidence.Field] = JsonNode.Parse(evidence.ToElement().GetRawText());
        return body;
    }

    private string? ReportedPresence(EstateModel model, RecordEnvelope device)
    {
        string? presence = EstateModel.BodyString(device, "presence");
        if (presence != "required") return presence;
        string? custody = EstateModel.BodyString(device, "custody");
        string? platform = EstateModel.BodyString(device, "platform");
        RecordEnvelope initial = model.ById[model.Chain(device)[0]];
        if (custody == DeviceKeyStores.AndroidKeystoreKind && platform == "android"
            && EstateModel.BodyString(device, "presenceVerification") == DeviceRecords.AndroidKeyAttestation
            && EstateModel.BodyString(device, "publicKey") is { } key
            && HasVerifiedAndroidPresence(model, initial, key)) return "required";

        // Earlier phone pairings were declarations. A rotation retains the original device identity. Before 0.9.5 a pairing
        // took any stated custody on the device's word, and from 0.9.5 only verified Android attestation records required, so a
        // device a pairing admitted is declared. The note only lowers what a device reports; it never raises it.
        return IsPhone(device) || IsPhone(initial) || WasPaired(model, initial) ? "declared" : presence;
    }

    private static bool WasPaired(EstateModel model, RecordEnvelope initial) => model.About(initial.Id, "admission").Any(admission =>
        EstateModel.BodyString(admission, "note")?.StartsWith(DeviceRecords.PairingAdmissionNote, StringComparison.Ordinal) == true
        && SignatureChecks.Counts(model, admission));

    private static bool IsPhone(RecordEnvelope device) => EstateModel.BodyString(device, "platform") is "android" or "ios"
        || EstateModel.BodyString(device, "custody") is DeviceKeyStores.AndroidKeystoreKind or DeviceKeyStores.IosSecureEnclaveKind;

    private bool HasVerifiedAndroidPresence(EstateModel model, RecordEnvelope initial, string key)
    {
        if (EstateModel.BodyString(initial, "publicKey") != key || EstateModel.BodyString(initial, "platform") != "android"
            || EstateModel.BodyString(initial, "custody") != DeviceKeyStores.AndroidKeystoreKind
            || EstateModel.BodyString(initial, "presence") != "required"
            || EstateModel.BodyString(initial, "presenceVerification") != DeviceRecords.AndroidKeyAttestation) return false;

        foreach (RecordEnvelope admission in model.About(initial.Id, "admission"))
        {
            if (admission.Subject?.Hash != initial.ComputedHash || admission.Signatures.Count == 0 || admission.Format < 2
                || admission.Supersedes is not null || admission.AtTime < initial.AtTime
                || EstateModel.BodyString(admission, "status") != "admitted"
                || !admission.Body.TryGetProperty(AndroidAttestationEvidence.Field, out JsonElement evidence) || evidence.ValueKind != JsonValueKind.Object
                || !evidence.TryGetProperty("certificates", out JsonElement certificates) || certificates.ValueKind != JsonValueKind.Array
                || certificates.GetArrayLength() is < 1 or > AndroidAttestationEvidence.MaxCertificates
                || !evidence.TryGetProperty("challenge", out JsonElement challenge) || challenge.ValueKind != JsonValueKind.String
                || !SignatureChecks.Counts(model, admission)) continue;

            string[] supplied = certificates.EnumerateArray().Select(cert => cert.ValueKind == JsonValueKind.String ? cert.GetString()! : "").ToArray();
            // Certificate validity is judged at admission. Current Google status and all attested facts are checked again.
            AndroidAttestationEvidence? verified = PairingAttestation.Verify(supplied, challenge.GetString()!, key, admission.AtTime);
            if (verified is not null && JsonElement.DeepEquals(evidence, verified.ToElement())) return true;
        }

        return false;
    }

    /// <summary>Checks issuance and pins its requesting device (or local admission signer) and the authority it uses.</summary>
    public (PairingAuthority? Authority, string? Problem) PairingIssuer(string principal, string by, IReadOnlyList<string> profiles)
    {
        lock (gate)
        {
            if (PairingRefusal(principal, by, profiles) is string refused)
            {
                return (null, refused);
            }

            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied)
            {
                return (null, denied.Message);
            }

            // From 0.9.4 (FIX-159): no pairing code, whose use signs and admits a device, is issued while the estate leaves records unread.
            if (ActingRefusal.BeforeSigning(model, "issuing a pairing code") is { } unread)
            {
                return (null, unread.Message);
            }

            var draft = new RecordDraft("admission", by, default, new RecordReference("device", Ulid.NewUlid().Value, null));
            _ = ResolveSigner(model, draft, out IRecordSigner? signer);
            string? device = requestDevice.Value is { Closed: false } request ? request.Device : signer?.DeviceId;
            DeviceStanding? standing = device is null ? null : Standing(model, device);
            if (standing is null || standing.Admission != "admitted" || standing.Principal != by)
            {
                return (null, "A pairing code requires an admitted issuing device of its administrator.");
            }

            RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, model.Latest(model.ById[standing.Identity]));
            // Roles and grants are read only from records their authors may write (EstateModel.Authority).
            RecordEnvelope? authority = model.HasRole(by, Roles.Operator) ? model.CurrentPrincipal(by)
                : model.Authority.GrantLines.Select(model.Authority.Current).OfType<RecordEnvelope>().FirstOrDefault(a => Grants(a, by) && !EstateModel.Expired(a, DateTimeOffset.UtcNow));
            return admission is not null && authority is not null
                ? (new PairingAuthority(by, standing.Identity, admission.Id, authority.Id), null)
                : (null, "The pairing code's issuing authority is unavailable.");
        }
    }

    /// <summary>Whether the same device and the authority under which its code was issued still authorize pairing.</summary>
    public string? PairingAuthorityRefusal(PairingAuthority issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        lock (gate)
        {
            return PairingAuthorityRefusal(session.Load(), issuer);
        }
    }

    private static string? PairingAuthorityRefusal(EstateModel model, PairingAuthority issuer)
    {
        const string Refusal = "This pairing code lost its issuing device or administrator's authority; ask the administrator for a new one.";
        DeviceStanding? standing = Standing(model, issuer.Device);
        if (standing is null || standing.Admission != "admitted" || standing.Principal != issuer.Principal
            || !model.ById.TryGetValue(issuer.Admission, out RecordEnvelope? admission)
            || !model.ById.TryGetValue(issuer.Authority, out RecordEnvelope? authority))
        {
            return Refusal;
        }

        RecordEnvelope currentAdmission = model.Latest(admission);
        RecordEnvelope currentAuthority = model.Latest(authority);
        if (!model.IsCurrent(currentAdmission) || !model.IsCurrent(currentAuthority)
            || model.Chain(currentAdmission).SkipWhile(id => id != admission.Id).Any(id => EstateModel.BodyString(model.ById[id], "status") != "admitted"))
        {
            return Refusal;
        }

        // The role or grant still stands as read from records their authors may write (EstateModel.Authority).
        if (authority.Type == "principal" ? !model.HasRole(issuer.Principal, Roles.Operator) : !model.HasScope(issuer.Principal, Scopes.Administer, DateTimeOffset.UtcNow))
        {
            return Refusal;
        }

        // A revoked and later restored grant or role never revives an old code, even if no exchange ran during the gap.
        RecordEnvelope[] revisions = model.Chain(currentAuthority).SkipWhile(id => id != authority.Id).Select(id => model.ById[id]).ToArray();
        for (int index = 0; index < revisions.Length; index++)
        {
            RecordEnvelope revision = revisions[index];
            DateTimeOffset until = index + 1 < revisions.Length ? ParseAt(revisions[index + 1].At) ?? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow;
            bool authorized = revision.Type == "principal"
                ? revision.Body.TryGetProperty("roles", out var roles) && roles.EnumerateArray().Any(r => r.GetString() == Roles.Operator)
                : Grants(revision, issuer.Principal) && !EstateModel.Expired(revision, until);
            if (!authorized)
            {
                return Refusal;
            }
        }

        return null;
    }

    private static bool Grants(RecordEnvelope authority, string principal) => authority.Type == "authority"
        && EstateModel.BodyString(authority, "grantee") == principal
        && authority.Body.TryGetProperty("scope", out var scopes) && scopes.EnumerateArray().Any(s => s.GetString() == Scopes.Administer);
}

/// <summary>The device and exact authorization from which a pairing code derives its authority; kept with the code, never a key.</summary>
/// <param name="Principal">The administrator issuing the code.</param>
/// <param name="Device">The requesting device, or the selected admission signer for a local-socket request.</param>
/// <param name="Admission">That device's admission revision at issuance.</param>
/// <param name="Authority">The principal revision granting the operator role, or the grant of administer used at issuance.</param>
public sealed record PairingAuthority(string Principal, string Device, string Admission, string Authority);
