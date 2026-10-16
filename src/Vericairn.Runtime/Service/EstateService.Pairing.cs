// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
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

            var draft = new RecordDraft("admission", by, default, new RecordReference("device", Ulid.NewUlid().Value, null));
            _ = ResolveSigner(model, draft, out IRecordSigner? signer);
            string? device = requestDevice.Value is { Closed: false } request ? request.Device : signer?.DeviceId;
            DeviceStanding? standing = device is null ? null : Standing(model, device);
            if (standing is null || standing.Admission != "admitted" || standing.Principal != by)
            {
                return (null, "A pairing code requires an admitted issuing device of its administrator.");
            }

            RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, model.Latest(model.ById[standing.Identity]));
            RecordEnvelope? authority = model.HasRole(by, Roles.Operator) ? model.CurrentPrincipal(by)
                : model.Current("authority").FirstOrDefault(a => Grants(a, by) && !EstateModel.Expired(a, DateTimeOffset.UtcNow));
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
