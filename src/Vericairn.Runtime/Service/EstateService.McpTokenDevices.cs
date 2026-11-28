// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <summary>Checks and pins the administrator who may issue a local token-device approval code.</summary>
    public (PairingAuthority? Authority, string? Problem) McpTokenIssuer(string principal, string by)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            if ((model.Manifest?.RecordFormat ?? 0) < 3 || model.CurrentPrincipal(principal) is not { } target
                || EstateModel.BodyString(target, "kind") is not ("agent" or "service")
                || model.CurrentPrincipal(by) is not { } administrator || EstateModel.BodyString(administrator, "kind") == "agent")
            {
                return (null, "Token mode requires record format 3, an agent or service principal for the client, and a current administrator who is not an agent.");
            }

            return PairingIssuer(principal, by, ["token"]);
        }
    }

    /// <summary>Creates the runtime-held key and stores its device and the issuing administrator's admission together.</summary>
    public (McpTokenDevice? Device, Problem? Problem) CreateMcpTokenDevice(string principal, PairingAuthority authority, IDeviceKeyStore? custody)
    {
        lock (gate)
        {
            return UnderWriteLock<(McpTokenDevice?, Problem?)>(() =>
            {
                EstateModel model = session.Load();
                if (PairingAuthorityRefusal(model, authority) is string denied)
                {
                    return (null, new Problem("denied", denied, []));
                }

                if (ActingRefusal.BeforeSigning(model, "admitting an MCP token device") is Problem unread) return (null, unread);
                PairingPlan plan = PairingPlan(principal);
                if (plan.Format < 3 || plan.PrincipalKind is not ("agent" or "service"))
                {
                    return (null, new Problem("denied", "Token mode requires an agent or a service in a format 3 estate.", []));
                }

                IDeviceKeyStore? issuerStore = HeldKeyStores().FirstOrDefault(store => store.Has(authority.Device));
                if (issuerStore is null) return (null, new Problem("denied", "The issuing administrator's key is unavailable.", []));

                DateTimeOffset now = DateTimeOffset.UtcNow;
                IDeviceKeyStore store = custody ?? DeviceKeyStores.Default(presence: false);
                string publicKey = RecordSigning.EncodePublicKey(store.Create(plan.Record));
                bool retain = false;
                try
                {
                    RecordDraft deviceDraft = DeviceRecords.Draft(principal, plan.Record, now, plan.Format,
                        DeviceRecords.Body(principal, "MCP token client", VericairnHome.Platform(), store.Kind, publicKey, ["token"], plan.Format, "none"));
                    (AuthoredRecord? device, Problem? deviceRefusal) = AuthorLocked(deviceDraft, model, [new StoreSigner(store, plan.Record)]);
                    if (device is null) return (null, deviceRefusal);
                    using JsonDocument body = JsonDocument.Parse(new JsonObject
                    {
                        ["principal"] = principal, ["status"] = "admitted", ["note"] = $"MCP token client approved by {authority.Principal}.",
                    }.ToJsonString());
                    (AuthoredRecord? admission, Problem? admissionRefusal) = AuthorLocked(new RecordDraft("admission", authority.Principal,
                        body.RootElement, device.Envelope.Reference), model, [new StoreSigner(issuerStore, authority.Device)]);
                    if (admission is null) return (null, admissionRefusal);

                    // Once submitted to the batch journal the key must survive any interrupted write and its recovery.
                    retain = true;
                    (IReadOnlyList<RecordWritten> stored, Problem? problem) = StoreTogetherUnderLock([device, admission], "Admit an MCP token device.", null, null, null);
                    if (problem is not null && stored.Count == 0 && problem.Code != "reconcile-required") retain = false;
                    return problem is null
                        ? (new McpTokenDevice(plan.Record, principal, publicKey, stored[1].Id, store.Kind), null)
                        : (null, problem);
                }
                finally
                {
                    if (!retain) store.Remove(plan.Record);
                }
            }, problem => (null, problem));
        }
    }

    /// <summary>Reads an admitted token device and confirms its original admission has never been withdrawn.</summary>
    public McpTokenDevice? CurrentMcpTokenDevice(string device, string? admittedUnder = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            DeviceStanding? standing = Standing(model, device);
            if (standing is null || standing.Admission != "admitted" || !standing.Profiles.SequenceEqual(["token"])
                || model.CurrentPrincipal(standing.Principal) is not { } owner || EstateModel.BodyString(owner, "kind") is not ("agent" or "service"))
            {
                return null;
            }

            RecordEnvelope current = model.Latest(model.ById[standing.Identity]);
            RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, current);
            if (admission is null || EstateModel.BodyString(current, "custody") is not string custody) return null;
            if (admittedUnder is not null)
            {
                string[] since = model.Chain(admission).SkipWhile(id => id != admittedUnder).ToArray();
                if (since.Length == 0 || since.Any(id => EstateModel.BodyString(model.ById[id], "status") != "admitted")) return null;
            }

            return new McpTokenDevice(standing.Identity, standing.Principal, standing.PublicKey, admission.Id, custody);
        }
    }

    /// <summary>Serializes token-family changes with writes authorized by that family.</summary>
    public T WithMcpTokenDevice<T>(string device, string admission, string publicKey, Func<McpTokenDevice, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            return CurrentMcpTokenDevice(device, admission) is { } current && current.PublicKey == publicKey
                ? operation(current) : default!;
        }
    }

    /// <summary>Keeps a key-free local attention notice about a new client or a reused refresh token.</summary>
    public void NoteMcpTokenClient(string device, string by, bool reissued)
    {
        lock (gate)
        {
            McpTokenAttention[] old = McpTokenNotices();
            var notice = new McpTokenAttention(device, by, reissued, RecordParser.FormatTimestamp(DateTimeOffset.UtcNow));
            session.Live.SetMeta("mcp-token-attention", JsonSerializer.Serialize(old.Where(n => n.Device != device || n.Reused != reissued).TakeLast(127).Append(notice), ProtocolJson.Options));
        }
    }

    private McpTokenAttention[] McpTokenNotices() =>
        session.Live.Meta("mcp-token-attention") is string json
            ? JsonSerializer.Deserialize<McpTokenAttention[]>(json, ProtocolJson.Options) ?? []
            : [];

    private sealed record McpTokenAttention(string Device, string By, bool Reused, string At);
}

/// <summary>The public record identities a token family binds, and the custody that holds the device's key.</summary>
public sealed record McpTokenDevice(string Device, string Principal, string PublicKey, string Admission, string Custody);
