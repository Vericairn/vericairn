// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Runtime.Objects;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Release;
using Vericairn.Runtime.Store;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The one implementation of the estate operations: the daemon serves it over the API and the CLI embeds it when no daemon owns
/// the estate. Every operation loads the current records first, evaluates authority through the record validators, and signs new
/// records with the author's admitted device held on this machine.
/// </summary>
public sealed class EstateService : IEstateApi, IDisposable
{
    private readonly object gate = new();
    private readonly EstateSession session;
    private readonly string startedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);

    private EstateService(EstateSession session)
    {
        this.session = session;
    }

    /// <summary>
    /// The implementations this service may reach, as its host registered them; the process's registry unless a host gives another.
    /// The runtime registers none of its own: an operation through a provider with no implementation here is refused.
    /// </summary>
    public ProviderRegistry Providers { get; set; } = ProviderRegistry.Current;

    /// <summary>Repository root.</summary>
    public string Root => session.Root;

    /// <summary>The session this service owns.</summary>
    public EstateSession Session => session;

    /// <summary>Opens the estate at a root; throws when it is not an estate.</summary>
    public static EstateService Open(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!File.Exists(Path.Combine(root, EstateLayout.ManifestPath)))
        {
            throw new InvalidOperationException($"{Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath}).");
        }

        return new EstateService(EstateSession.Open(root));
    }

    /// <summary>The product version of this runtime.</summary>
    public static string Version =>
        typeof(EstateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <inheritdoc />
    public HealthResponse Health()
    {
        lock (gate)
        {
            return new HealthResponse(ApiVersion.Current, Version, session.Manifest.Estate, session.Root, session.Profile, session.Manifest.RecordFormat, startedAt, Environment.ProcessId);
        }
    }

    /// <inheritdoc />
    public StatusResponse Status()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            FindingList checks = EstateChecks.Run(model);
            using JsonDocument json = JsonDocument.Parse(EstateStatus.RenderJson(model, checks));
            return new StatusResponse(EstateStatus.Render(model, checks), json.RootElement.Clone(), model.Findings.HasErrors || checks.HasErrors);
        }
    }

    /// <inheritdoc />
    public FindingsResponse Validate()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return ToFindings(model.Findings, $"{model.Records.Count} records");
        }
    }

    /// <inheritdoc />
    public FindingsResponse Check()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            var all = new FindingList();
            foreach (Finding finding in model.Findings.Concat(EstateChecks.Run(model)))
            {
                all.Add(finding);
            }

            return ToFindings(all, $"{model.Records.Count} records");
        }
    }

    /// <inheritdoc />
    public RecordResponse? Record(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(id, out RecordEnvelope? record))
            {
                return null;
            }

            RecordFile? file = session.Files().FirstOrDefault(f => f.Path == record.ExpectedPath);
            using JsonDocument document = JsonDocument.Parse(file?.Bytes ?? []);
            return new RecordResponse(record.ExpectedPath, record.Id, record.Type, record.ComputedHash, model.IsCurrent(record), document.RootElement.Clone());
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecordResponse> Records(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        lock (gate)
        {
            EstateModel model = session.Load();
            Dictionary<string, RecordFile> files = session.Files().ToDictionary(f => f.Path, f => f, StringComparer.Ordinal);
            var rows = new List<RecordResponse>();
            foreach (RecordEnvelope record in model.Current(type).OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                using JsonDocument document = JsonDocument.Parse(files.TryGetValue(record.ExpectedPath, out RecordFile? file) ? file.Bytes : []);
                rows.Add(new RecordResponse(record.ExpectedPath, record.Id, record.Type, record.ComputedHash, true, document.RootElement.Clone()));
            }

            return rows;
        }
    }

    /// <inheritdoc />
    public WriteOutcome NewRecord(NewRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RecordTypeRegistry.Find(request.Type) is null)
        {
            return Refuse($"Unknown record type '{request.Type}'.");
        }

        if (!Ulid.IsValid(request.By))
        {
            return Refuse("The authoring principal must be a ULID.");
        }

        RecordReference? subject = null;
        if (request.Subject is not null && !TryParseSubject(request.Subject, out subject))
        {
            return Refuse("The subject must be type:id or type:id:hash.");
        }

        DateTimeOffset? at = ParseAt(request.At);
        if (request.At is not null && at is null)
        {
            return Refuse("The authoring time must look like 2026-10-13T14:00:00Z.");
        }

        if (request.Id is not null && !Ulid.IsValid(request.Id))
        {
            return Refuse("An explicit id must be a ULID.");
        }

        return Write(new RecordDraft(request.Type, request.By, request.Body, subject, request.Supersedes, at, request.Id));
    }

    /// <inheritdoc />
    public BriefResponse? Brief(string workOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrder);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? order = WorkView.FindOrder(model, workOrder);
            if (order is null)
            {
                return null;
            }

            using JsonDocument json = JsonDocument.Parse(WorkView.RenderBriefJson(model, order));
            return new BriefResponse(WorkView.RenderBrief(model, order), json.RootElement.Clone());
        }
    }

    /// <inheritdoc />
    public ObserveResponse? Observe(string workOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrder);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? order = WorkView.FindOrder(model, workOrder);
            if (order is null)
            {
                return null;
            }

            using JsonDocument json = JsonDocument.Parse(WorkView.RenderBriefJson(model, order));
            return new ObserveResponse(WorkView.RenderObserve(model, order), json.RootElement.Clone());
        }
    }

    /// <inheritdoc />
    public WriteOutcome Assign(AssignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By) || !Ulid.IsValid(request.Contributor))
        {
            return Refuse("The assigning and contributing principals must be ULIDs.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? order = WorkView.FindOrder(model, request.WorkOrder);
            if (order is null)
            {
                return NotFound($"No current work order has key or id {request.WorkOrder}.");
            }

            var body = new JsonObject { ["contributor"] = request.Contributor, ["brief"] = JsonNode.Parse(request.Brief.GetRawText()) };
            if (request.Note is not null)
            {
                body["note"] = request.Note;
            }

            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteLocked(new RecordDraft("assignment", request.By, document.RootElement, order.Reference), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome Attempt(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By))
        {
            return Refuse("The contributing principal must be a ULID.");
        }

        DateTimeOffset? at = ParseAt(request.At);
        if (request.At is not null && at is null)
        {
            return Refuse("The start time must look like 2026-10-13T14:00:00Z.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? assignment = Latest(model, request.Assignment, "assignment");
            if (assignment is null)
            {
                return NotFound($"No assignment has id {request.Assignment}.");
            }

            var body = new JsonObject { ["startedAt"] = RecordParser.FormatTimestamp(at ?? DateTimeOffset.UtcNow), ["workspace"] = request.Workspace, ["note"] = request.Note };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteLocked(new RecordDraft("attempt", request.By, document.RootElement, assignment.Reference, At: at), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome Result(ResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return About("result", "attempt", request.Attempt, request.By, request.Body);
    }

    /// <inheritdoc />
    public WriteOutcome Amend(AmendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return About("amendment", "assignment", request.Assignment, request.By, request.Body);
    }

    /// <inheritdoc />
    public IReadOnlyList<DeviceRow> Devices()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
            return model.Current("device").OrderBy(d => model.Chain(d)[0], StringComparer.Ordinal).Select(device =>
            {
                RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, device);
                string principal = EstateModel.BodyString(device, "principal") ?? "?";
                string identity = model.Chain(device)[0];
                return new DeviceRow(
                    identity,
                    principal,
                    model.CurrentPrincipal(principal) is RecordEnvelope p ? EstateModel.BodyString(p, "name") : null,
                    EstateModel.BodyString(device, "name"),
                    EstateModel.BodyString(device, "platform"),
                    EstateModel.BodyString(device, "custody"),
                    admission is null ? "not admitted" : EstateModel.BodyString(admission, "status") ?? "?",
                    stores.Any(s => s.Has(identity)),
                    EstateModel.BodyString(device, "presence"),
                    EstateModel.BodyString(device, "purpose"));
            }).ToList();
        }
    }

    /// <inheritdoc />
    public WriteOutcome DeviceCreate(DeviceCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.Principal))
        {
            return Refuse("The principal must be a ULID.");
        }

        lock (gate)
        {
            // Presence and the Secure Enclave are named in device records from record format 2: an older estate refuses them, and its
            // default custody is the strongest that format can name.
            EstateModel model = session.Load();
            long format = model.Manifest?.RecordFormat ?? session.Manifest.RecordFormat;
            bool person = model.CurrentPrincipal(request.Principal) is { } principal && EstateModel.BodyString(principal, "kind") == "human";
            IDeviceKeyStore store;
            bool presence;
            try
            {
                // The strongest custody this machine offers unless the request names one. A person's device needs its owner's
                // presence wherever that custody enforces it; an agent's or a service's device signs unattended and never claims it.
                string kind = DeviceKeyStores.Named(request.Custody, enclave: format >= 2).Kind;
                presence = request.Presence ?? (format >= 2 && DeviceKeyStores.PresenceByDefault(person, kind));
                store = DeviceKeyStores.Named(kind, presence);
            }
            catch (Exception exception) when (exception is ArgumentException or PlatformNotSupportedException)
            {
                // The refusal is for a person: the message alone, without the parameter name an ArgumentException appends.
                string message = exception is ArgumentException { ParamName: { } name } ? exception.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal) : exception.Message;
                return Refuse(message);
            }

            if (presence && !person)
            {
                return Refuse("An agent's or a service's device signs unattended and never claims its owner's presence.");
            }

            if (format < 2 && (presence || store.Kind == SecureEnclaveCustody.Kind))
            {
                return Refuse("A device with presence or Secure Enclave custody is recorded in record format 2; upgrade the estate to a release that writes format 2 first.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string id = Ulid.NewUlid(now).Value;
            byte[] publicKey;
            try
            {
                publicKey = store.Create(id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                return Refuse(exception.Message);
            }

            IReadOnlyList<string> profiles = request.Profiles is { Count: > 0 } ? request.Profiles : ["embedded"];
            var body = new JsonObject
            {
                ["principal"] = request.Principal,
                ["name"] = request.Name,
                ["platform"] = VericairnHome.Platform(),
                ["custody"] = store.Kind,
                ["algorithm"] = RecordSigning.Algorithm,
                ["publicKey"] = RecordSigning.EncodePublicKey(publicKey),
                ["profiles"] = new JsonArray(profiles.Select(p => (JsonNode)p).ToArray()),
            };
            if (format >= 2)
            {
                body["presence"] = presence ? "required" : "none";
                body["purpose"] = "signing";
            }

            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            WriteOutcome outcome = WriteLocked(new RecordDraft("device", request.Principal, document.RootElement, At: now, Id: id), model, [new StoreSigner(store, id)]);
            if (!outcome.Ok)
            {
                store.Remove(id);
            }

            return outcome;
        }
    }

    /// <inheritdoc />
    public WriteOutcome DeviceRename(DeviceRenameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Refuse("A device is renamed to a descriptive name.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            if (Revisable(model, request.Device, out RecordEnvelope? device, out string? identity, out IDeviceKeyStore? store) is { } refusal)
            {
                return Refuse(refusal);
            }

            JsonObject body = RevisionBody(device!);
            body["name"] = request.Name;
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteLocked(new RecordDraft("device", EstateModel.BodyString(device!, "principal")!, document.RootElement, Supersedes: device!.Id), model, [new StoreSigner(store!, identity!)]);
        }
    }

    /// <inheritdoc />
    public WriteOutcome DeviceRotate(DeviceRotateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (Revisable(model, request.Device, out RecordEnvelope? device, out string? identity, out IDeviceKeyStore? current) is { } refusal)
            {
                return Refuse(refusal);
            }

            // The new key keeps the device's custody and presence unless the request names others; presence carries over only
            // where the new custody enforces it.
            IDeviceKeyStore next;
            bool presence;
            try
            {
                string custody = request.Custody ?? current!.Kind;
                presence = request.Presence ?? (EstateModel.BodyString(device!, "presence") == "required" && Array.IndexOf(DeviceKeyStores.PresenceKinds, DeviceKeyStores.Named(custody).Kind) >= 0);
                next = DeviceKeyStores.Named(custody, presence);
            }
            catch (Exception exception) when (exception is ArgumentException or PlatformNotSupportedException)
            {
                return Refuse(exception is ArgumentException { ParamName: { } name } ? exception.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal) : exception.Message);
            }

            // The new key is made under the revision's id and signs beside the previous key; only once the revision is recorded
            // does it take the device's place, so a refused rotation leaves the device exactly as it was.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string revision = Ulid.NewUlid(now).Value;
            byte[] publicKey;
            try
            {
                publicKey = next.Create(revision);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                return Refuse(exception.Message);
            }

            JsonObject body = RevisionBody(device!);
            body["publicKey"] = RecordSigning.EncodePublicKey(publicKey);
            body["custody"] = next.Kind;
            body["presence"] = presence ? "required" : "none";
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            WriteOutcome outcome = WriteLocked(new RecordDraft("device", EstateModel.BodyString(device!, "principal")!, document.RootElement, Supersedes: device!.Id, At: now, Id: revision), model, [new StoreSigner(current!, identity!), new StoreSigner(next, revision, identity!)]);
            if (!outcome.Ok)
            {
                next.Remove(revision);
                return outcome;
            }

            next.Promote(revision, identity!);
            if (current!.Kind != next.Kind)
            {
                current.Remove(identity!);
            }

            return outcome;
        }
    }

    /// <summary>A device can be revised here when the estate is at record format 2, the device exists, and this machine holds its key.</summary>
    private string? Revisable(EstateModel model, string deviceId, out RecordEnvelope? device, out string? identity, out IDeviceKeyStore? store)
    {
        store = null;
        identity = null;
        device = Latest(model, deviceId, "device");
        if ((model.Manifest?.RecordFormat ?? session.Manifest.RecordFormat) < 2)
        {
            return "A device keeps its identity across a rename or a rotation from record format 2; upgrade the estate to a release that writes format 2 first.";
        }

        if (device is null)
        {
            return $"No device {deviceId} in this estate.";
        }

        string id = model.Chain(device)[0];
        identity = id;
        store = DeviceKeyStores.All().FirstOrDefault(s => s.Has(id));
        return store is null ? $"This machine holds no key for device {id}; a device is renamed or rotated where its key is held." : null;
    }

    /// <summary>The body of a device's next revision: its current body, with the fields record format 2 requires.</summary>
    private static JsonObject RevisionBody(RecordEnvelope device)
    {
        JsonObject body = JsonNode.Parse(device.Body.GetRawText())!.AsObject();
        body["presence"] ??= "none";
        body["purpose"] ??= "signing";
        return body;
    }

    /// <inheritdoc />
    public WriteOutcome DeviceAdmit(AdmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By))
        {
            return Refuse("The admitting principal must be a ULID.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? device = Latest(model, request.Device, "device");
            if (device is null)
            {
                return NotFound($"No device has id {request.Device}.");
            }

            var body = new JsonObject { ["principal"] = EstateModel.BodyString(device, "principal"), ["status"] = "admitted", ["note"] = request.Note };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            var draft = new RecordDraft("admission", request.By, document.RootElement, device.Reference);
            return request.Recovery is null ? WriteLocked(draft, model) : WriteWithRecovery(draft, model, request.Recovery, request.Passphrase);
        }
    }

    /// <inheritdoc />
    public WriteOutcome DeviceRevoke(RevokeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By))
        {
            return Refuse("The revoking principal must be a ULID.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? device = Latest(model, request.Device, "device");
            if (device is null)
            {
                return NotFound($"No device has id {request.Device}.");
            }

            RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, device);
            if (admission is null || EstateModel.BodyString(admission, "status") != "admitted")
            {
                return Refuse($"Device {request.Device} has no current admission to revoke.");
            }

            var body = new JsonObject { ["principal"] = EstateModel.BodyString(device, "principal"), ["status"] = "revoked", ["note"] = request.Reason, ["revokedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow) };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            var draft = new RecordDraft("admission", request.By, document.RootElement, device.Reference, admission.Id);
            return request.Recovery is null ? WriteLocked(draft, model) : WriteWithRecovery(draft, model, request.Recovery, request.Passphrase);
        }
    }

    /// <inheritdoc />
    public WriteOutcome DeviceRecovery(DeviceRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.Principal))
        {
            return Refuse("The principal must be a ULID.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Refuse("A recovery device has a descriptive name.");
        }

        if (request.Passphrase is null || request.Passphrase.Length < RecoveryKey.MinimumPassphrase)
        {
            return Refuse($"A recovery key is encrypted with a passphrase of at least {RecoveryKey.MinimumPassphrase} characters.");
        }

        // The key file never enters the estate's repository, where a commit would publish it.
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(session.Root)) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(request.Output) || !Path.IsPathFullyQualified(request.Output) || Path.GetFullPath(request.Output).StartsWith(root, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            return Refuse("A recovery key is written to an absolute path the operator names, outside the estate's repository.");
        }

        if (File.Exists(request.Output) || Directory.Exists(request.Output))
        {
            return Refuse($"{request.Output} exists, and a recovery key never overwrites anything.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            if ((model.Manifest?.RecordFormat ?? session.Manifest.RecordFormat) < 2)
            {
                return Refuse("A recovery device is recorded in record format 2; upgrade the estate to a release that writes format 2 first.");
            }

            if (!model.HasRole(request.Principal, Roles.Operator))
            {
                return Refuse("A recovery device belongs to an operator.");
            }

            // An admitted device of the operator held here admits the recovery device, so nothing is made without one.
            IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
            if (!model.Current("device").Any(d => EstateModel.BodyString(d, "principal") == request.Principal && SignatureChecks.IsAdmitted(model, d) && stores.Any(s => s.Has(model.Chain(d)[0]))))
            {
                return Refuse("A recovery device is made on a machine that holds an admitted device of the operator, which admits it.");
            }

            // The key exists in memory and in the encrypted file only; no key store ever holds it.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string id = Ulid.NewUlid(now).Value;
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            try
            {
                RecoveryKey.Write(request.Output, key, request.Passphrase, id, EstateModel.BodyString(model.CurrentEstate!, "slug") ?? "?");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Refuse(exception.Message);
            }

            var body = new JsonObject
            {
                ["principal"] = request.Principal,
                ["name"] = request.Name,
                ["platform"] = VericairnHome.Platform(),
                ["custody"] = DeviceKeyStores.OfflineKind,
                ["algorithm"] = RecordSigning.Algorithm,
                ["publicKey"] = RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo()),
                ["profiles"] = new JsonArray("embedded"),
                ["presence"] = "none",
                ["purpose"] = "recovery",
            };
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            WriteOutcome device = WriteLocked(new RecordDraft("device", request.Principal, document.RootElement, At: now, Id: id), model, [new KeySigner(key, id)]);
            if (!device.Ok)
            {
                File.Delete(request.Output);
                return device;
            }

            var admission = new JsonObject { ["principal"] = request.Principal, ["status"] = "admitted", ["note"] = "The operator's recovery device; its key is kept offline." };
            using JsonDocument admissionDocument = JsonDocument.Parse(admission.ToJsonString());
            WriteOutcome admitted = WriteLocked(new RecordDraft("admission", request.Principal, admissionDocument.RootElement, new RecordReference("device", id, device.Written!.Hash)), session.Load());
            return admitted.Ok ? device : WriteOutcome.Fail(new Problem("refused", $"Recovery device {id} is recorded, but its admission was refused: {admitted.Problem?.Message} Its key is at {request.Output}; admit it with vericairn devices admit.", admitted.Problem?.Findings ?? []));
        }
    }

    /// <summary>Writes a record signed by the operator's recovery device, its key opened in memory from its file with its passphrase.</summary>
    private WriteOutcome WriteWithRecovery(RecordDraft draft, EstateModel model, string path, string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return Refuse("A recovery key opens with its passphrase.");
        }

        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return Refuse($"No recovery key at {path}.");
        }

        ECDsa key;
        try
        {
            key = RecoveryKey.Open(path, passphrase);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Refuse($"The recovery key at {path} does not open with this passphrase, or the file holds no recovery key.");
        }

        using (key)
        {
            string publicKey = RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo());
            RecordEnvelope? device = model.Current("device").FirstOrDefault(d => EstateModel.BodyString(d, "publicKey") == publicKey && EstateModel.BodyString(d, "purpose") == "recovery");
            if (device is null || EstateModel.BodyString(device, "principal") != draft.By || !SignatureChecks.IsAdmitted(model, device))
            {
                return Refuse($"The key at {path} is not the key of an admitted recovery device of {draft.By}.");
            }

            return WriteLocked(draft, model, [new KeySigner(key, model.Chain(device)[0])]);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentRow> Documents()
    {
        lock (gate)
        {
            return LivingDocuments.Derive(session.Load()).Select(d => new DocumentRow(
                d.Path,
                d.Repository,
                d.Declaration.Id,
                EstateModel.BodyString(d.Declaration, "title"),
                d.Unavailable.Count > 0 ? "unavailable" : d.Due ? "due" : "current",
                d.Level,
                d.Reasons.Select(r => new DocumentReason(r.On, r.Level, r.Summary, r.Record)).ToList(),
                d.Clearance?.Id,
                d.Unavailable,
                d.Notes)).ToList();
        }
    }

    /// <inheritdoc />
    public WriteOutcome DocumentReview(string declaration, DocumentReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ulid.IsValid(request.By))
        {
            return Refuse("The reviewing principal must be a ULID.");
        }

        if (Array.IndexOf(DocumentReviewType.Outcomes, request.Outcome) < 0)
        {
            return Refuse($"A document review's outcome is {string.Join(", ", DocumentReviewType.Outcomes)}.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? declared = Latest(model, declaration, "living-document");
            if (declared is null)
            {
                return NotFound($"No living-document declaration has id {declaration}.");
            }

            // The runtime, not the reviewer, states what was reviewed: the exact bytes (of the estate's file, or of another
            // repository's at a commit through its source), the covered paths' identity and the triggers the review answers; and it
            // runs the declared checks, so a failed one refuses the clearance before anything is written.
            string path = EstateModel.BodyString(declared, "path")!;
            byte[] bytes;
            string? commit = null;
            string? covered = null;
            Func<string, bool>? exists = null;
            IReadOnlyList<string> coveredPaths = LivingDocuments.CoveredPaths(declared);
            if (EstateModel.BodyString(declared, "repository") is null)
            {
                string file = Path.Combine(session.Root, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(file))
                {
                    return Refuse($"{path} is not in the estate's working tree; a review reads the document it clears.");
                }

                bytes = File.ReadAllBytes(file);
                covered = coveredPaths.Count > 0 ? LivingDocuments.CoveredIdentity(session.Root, coveredPaths) : null;
            }
            else
            {
                string sourceName = EstateModel.BodyString(declared, "source") ?? "?";
                if (request.Commit is null)
                {
                    return Refuse($"{path} is in another repository and is reviewed at a commit; name the commit.");
                }

                if (DocumentSources.Find(sourceName) is not IDocumentSource source)
                {
                    return Refuse($"{path} is read through {sourceName}, which no installed pack provides here.");
                }

                if (source.Resolve(model, declared, request.Commit) is not string resolved)
                {
                    return Refuse($"No clone of {path}'s repository with commit {request.Commit} is registered on this machine; register one with vericairn sources clone.");
                }

                if (source.Read(model, declared, resolved) is not byte[] read)
                {
                    return Refuse($"{path} is not in its repository at {resolved[..12]}.");
                }

                bytes = read;
                commit = resolved;
                exists = relative => source.Exists(model, declared, resolved, relative);
                covered = coveredPaths.Count > 0 ? source.Covered(model, declared, coveredPaths, resolved) : null;
            }

            string text = System.Text.Encoding.UTF8.GetString(bytes);
            LivingDocuments.State state = LivingDocuments.Derive(model).First(d => d.Declaration.Id == declared.Id);
            List<RecordEnvelope> answered = state.Reasons.Where(r => r.Record is not null).Select(r => model.ById[r.Record!]).ToList();
            IReadOnlyList<DocumentChecks.Outcome> checks = DocumentChecks.Run(model, declared, text, path, commit is null ? session.Root : null, answered, exists);
            var content = new JsonObject { ["hash"] = ContentHash.Sha256(bytes), ["bytes"] = bytes.Length };
            if (commit is not null)
            {
                content["commit"] = commit;
            }

            var body = new JsonObject
            {
                ["outcome"] = request.Outcome,
                ["content"] = content,
                ["answers"] = new JsonArray(state.Reasons.Select(r => (JsonNode)(r.Record is null
                    ? new JsonObject { ["on"] = r.On }
                    : new JsonObject { ["on"] = r.On, ["record"] = new JsonObject { ["type"] = model.ById[r.Record].Type, ["id"] = r.Record, ["hash"] = model.ById[r.Record].ComputedHash } })).ToArray()),
                ["independence"] = DocumentIndependence(model, declared, request.By, request.Outcome),
                ["summary"] = request.Summary,
                ["findings"] = new JsonArray((request.Findings ?? []).Select(f => (JsonNode)(f.Location is null
                    ? new JsonObject { ["severity"] = f.Severity, ["summary"] = f.Summary }
                    : new JsonObject { ["severity"] = f.Severity, ["summary"] = f.Summary, ["location"] = f.Location })).ToArray()),
                ["checks"] = new JsonArray(checks.Select(c => (JsonNode)new JsonObject { ["name"] = c.Name, ["outcome"] = c.Result, ["summary"] = c.Summary }).ToArray()),
            };
            if (covered is not null)
            {
                body["covered"] = covered;
            }

            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            return WriteLocked(new RecordDraft("document-review", request.By, document.RootElement, declared.Reference), model);
        }
    }

    /// <summary>
    /// Reads each refuse-level document of the product in another repository at the commit a release would build, where a clone of it
    /// is registered here: those whose bytes differ from their latest review, and what was compared or could not be.
    /// </summary>
    private static (List<string> Changed, List<string> Compared) CompareLivingDocuments(EstateModel model, string product, string reference)
    {
        var changed = new List<string>();
        var compared = new List<string>();
        foreach (LivingDocuments.State document in LivingDocuments.Derive(model).Where(d => d.Repository is not null && d.DeclaresRefuse && LivingDocuments.Triggers(d.Declaration).Any(t => t.On == "release" && t.Product == product)))
        {
            RecordEnvelope? review = document.Clearance is { Type: "document-review" } cleared ? cleared : null;
            JsonElement content = review is not null && review.Body.TryGetProperty("content", out JsonElement c) ? c : default;
            string? clearedHash = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("hash", out JsonElement hash) ? hash.GetString() : null;
            string clearedAt = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("commit", out JsonElement at) ? at.GetString() ?? "?" : "?";
            if (clearedHash is null)
            {
                continue;
            }

            IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(document.Declaration, "source"));
            string? commit = source?.Resolve(model, document.Declaration, reference);
            if (source is null || commit is null)
            {
                compared.Add($"{document.Path} not compared, since no clone of its repository is registered here; the independent review reads it at the release commit");
                continue;
            }

            byte[]? bytes = source.Read(model, document.Declaration, commit);
            string? now = bytes is null ? null : ContentHash.Sha256(bytes);
            if (now == clearedHash)
            {
                compared.Add($"{document.Path} at {commit[..12]} is the content its review {review!.Id} cleared");
            }
            else
            {
                changed.Add(now is null
                    ? $"{document.Path} is not in its repository at {commit[..12]}"
                    : $"{document.Path} changed after its review at {clearedAt[..Math.Min(12, clearedAt.Length)]}: at {commit[..12]} it is {now}, and the review cleared {clearedHash}");
            }
        }

        return (changed, compared);
    }

    /// <summary>
    /// The reviewer's independence of the content, as the record's rules judge it: self when it revised the content (this review, the
    /// estate file's latest revision, or another repository's latest revising review), else operator or independent agent.
    /// </summary>
    private static string DocumentIndependence(EstateModel model, RecordEnvelope declaration, string by, string outcome)
    {
        string? path = EstateModel.BodyString(declaration, "path");
        IReadOnlyList<string> chain = model.Chain(model.Latest(declaration));
        RecordEnvelope? revision = EstateModel.BodyString(declaration, "repository") is null
            ? model.OfType("document-revision").Where(r => EstateModel.BodyString(r, "path") == path).OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault()
            : model.OfType("document-review").Where(r => r.Subject is not null && chain.Contains(r.Subject.Id) && EstateModel.BodyString(r, "outcome") == "revised").OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
        if (outcome == "revised" || revision?.By == by)
        {
            return "self";
        }

        return model.HasRole(by, Roles.Operator) ? "operator" : "independent-agent";
    }

    /// <inheritdoc />
    public IReadOnlyList<ReleaseRow> Releases()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.Current("release").OrderBy(r => EstateModel.BodyString(r, "version"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
                .Select(r => new ReleaseRow(EstateModel.BodyString(r, "version"), EstateModel.BodyString(r, "status"), EstateModel.BodyString(r, "commit"), r.Id, r.ComputedHash, EstateModel.BodyString(r, "signing"))).ToList();
        }
    }

    /// <inheritdoc />
    IReadOnlyList<ProviderRow> IEstateApi.Providers() => ProviderRows();

    /// <summary>The provider and adapter implementations available to this service, with their declared settings.</summary>
    public IReadOnlyList<ProviderRow> ProviderRows()
    {
        var rows = new List<ProviderRow>();
        foreach (IReleaseProvider provider in Providers.ReleaseProviders)
        {
            rows.Add(Row(provider.Provider, "release", provider.Descriptor));
        }

        foreach (ISourceAdapter adapter in Providers.SourceAdapters)
        {
            rows.Add(Row(adapter.Kind, "source", adapter.Descriptor));
        }

        return rows;
    }

    /// <inheritdoc />
    public ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string pending = $"release {request.Version}";
        if (!Ulid.IsValid(request.By) || !Ulid.IsValid(request.Product))
        {
            return ReleaseProblem(pending, "not-started", "refused", "The recording principal and the product must be ULIDs.");
        }

        RecordResponse? productRecord = Record(request.Product);
        if (productRecord is null || productRecord.Type != "product" || !productRecord.Current)
        {
            return ReleaseProblem(pending, "not-started", "not-found", $"No current product has id {request.Product}.");
        }

        ReleaseProfile profile = ReleaseProfile.FromProductBody(productRecord.Envelope.GetProperty("body"));
        string? providerId = string.IsNullOrWhiteSpace(request.Provider) ? profile.Destination?.Provider : request.Provider.Trim();
        if (providerId is null)
        {
            return ReleaseProblem(pending, "not-started", "refused", $"No release provider is selected: product {request.Product} declares no release destination and the request names no provider. Declare the destination on the product record or name the provider; nothing was recorded.");
        }

        IReleaseProvider? provider = Providers.Release(providerId);
        if (provider is null)
        {
            return ReleaseProblem(pending, "not-started", "refused", $"The release provider '{providerId}' has no implementation on the runtime's machine; nothing selected another and nothing was recorded. Available: {string.Join(", ", Providers.ReleaseProviders.Select(p => p.Provider).DefaultIfEmpty("none"))}.");
        }

        var requested = new Dictionary<string, string>(request.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        AddIfGiven(requested, "repository", request.Repository);
        AddIfGiven(requested, "ref", request.Ref);
        AddIfGiven(requested, "workflow", request.Workflow);
        IReadOnlyDictionary<string, string> settings = profile.Settings(provider.Descriptor, providerId, requested);
        List<string> missing = provider.Descriptor.DeclaredSettings.Where(s => s.Required && !settings.ContainsKey(s.Name)).Select(s => s.Name).ToList();
        string? destinationSetting = provider.Descriptor.Destination;
        if (destinationSetting is not null && !settings.ContainsKey(destinationSetting) && !missing.Contains(destinationSetting))
        {
            missing.Add(destinationSetting);
        }

        if (missing.Count > 0)
        {
            return ReleaseProblem(pending, "not-started", "refused", $"The provider '{providerId}' needs the settings {string.Join(", ", missing)}; neither the request nor product {request.Product}'s declared destination gives them, and nothing was recorded.");
        }

        string destination = destinationSetting is null ? providerId : settings[destinationSetting];
        string identity = ReleaseFlow.Identity(providerId, destination, request.Version);
        RecordEnvelope? current = CurrentOperation(identity);
        string? status = current is null ? null : EstateModel.BodyString(current, "status");
        switch (status)
        {
            case "observed-success":
                return ReleaseProblem(identity, status, "refused", $"Release {request.Version} was already observed as built under {identity}; a corrective release is a new version.");
            case "dispatched" or "uncertain":
                return ReleaseProblem(identity, status, "refused", $"An operation under {identity} is {status}; observe and reconcile it before any retry.");
            case "known-failure" or "canceled" when string.IsNullOrWhiteSpace(request.Reconciliation):
                return ReleaseProblem(identity, status, "refused", $"The earlier attempt under {identity} is {status}; retry with a reconciliation stating how it was settled under that identity.");
        }

        // The living-document gate: the product's documents due at level refuse, or unavailable while they declare it, refuse the
        // submission before any intent is recorded, all named at once, so a refused release costs no build.
        IReadOnlyList<LivingDocuments.State> closing = LivingDocuments.Gate(session.Load(), request.Product);
        if (closing.Count > 0)
        {
            return ReleaseProblem(identity, "not-started", "refused", $"Release {request.Version} waits for the product's living documents: {string.Join(" | ", closing.Select(d => d.Describe()))}. Revise each or record a document review (vericairn docs review), then submit again; nothing was recorded.");
        }

        // The content rule: records cannot see a repository, so each cleared document of another repository is read at the commit
        // being released, where this machine has a clone of it; a document whose bytes differ from its review refuses the submission.
        (List<string> changed, List<string> compared) = CompareLivingDocuments(session.Load(), request.Product, settings.TryGetValue("ref", out string? reference) ? reference : "HEAD");
        if (changed.Count > 0)
        {
            return ReleaseProblem(identity, "not-started", "refused", $"Release {request.Version} waits for the product's living documents: {string.Join(" | ", changed)}. Review each again at that commit, then submit again; nothing was recorded.");
        }

        string previousId;
        JsonElement previousBody;
        if (current is null)
        {
            JsonObject intentBody = ReleaseFlow.IntentBody(providerId, destination, settings, request.Version, request.Product);
            if (compared.Count > 0)
            {
                intentBody["evidence"]!.AsArray().Add(new JsonObject { ["summary"] = "Living documents of the product at the commit being released: " + string.Join("; ", compared) + "." });
            }

            WriteOutcome intent = NewRecord(new NewRecordRequest("operation", request.By, Element(intentBody), $"product:{request.Product}"));
            if (!intent.Ok)
            {
                return ReleaseProblem(identity, "not-started", intent.Problem!);
            }

            previousId = intent.Written!.Id;
            previousBody = Record(previousId)!.Envelope.GetProperty("body");
        }
        else
        {
            previousId = current.Id;
            previousBody = current.Body;
        }

        // The provider is called outside the gate: a dispatch may wait for the host, and every other operation must keep answering.
        JsonObject body;
        try
        {
            DispatchReceipt receipt = provider.Dispatch(new DispatchRequest(destination, settings, new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = request.Version }));
            body = status is "known-failure" or "canceled" ? ReleaseFlow.Retry(previousBody, receipt, request.Reconciliation!) : ReleaseFlow.AfterDispatch(previousBody, receipt);
        }
        catch (ProviderRefusedException exception)
        {
            body = ReleaseFlow.AfterRefusal(previousBody, exception.Message);
        }
        catch (ProviderUncertainException exception)
        {
            body = ReleaseFlow.AfterUncertainty(previousBody, exception.Message);
        }

        WriteOutcome revision = NewRecord(new NewRecordRequest("operation", request.By, Element(body), $"product:{request.Product}", previousId));
        if (!revision.Ok)
        {
            return ReleaseProblem(identity, status ?? "not-started", revision.Problem!);
        }

        string newStatus = body["status"]!.GetValue<string>();
        string? run = body["target"]!.AsObject().TryGetPropertyValue("run", out JsonNode? runNode) ? runNode?.GetValue<string>() : null;
        string message = newStatus switch
        {
            "dispatched" => "The intent was recorded before the dispatch. Observe the run and the draft; publication stays the operator's manual step.",
            "uncertain" => "The dispatch may or may not have started a run. Observe and reconcile under this identity before any retry.",
            _ => "Nothing was dispatched. Fix the cause, then retry with a reconciliation.",
        };
        return new ReleaseResponse(identity, newStatus, message, newStatus == "dispatched", revision.Written, Run: run);
    }

    /// <inheritdoc />
    public ReleaseResponse ReleaseObserve(ReleaseObserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string tag = ReleaseFlow.Tag(request.Version);
        string pending = $"release {request.Version}";
        if (!Ulid.IsValid(request.By))
        {
            return ReleaseProblem(pending, "unknown", "refused", "The recording principal must be a ULID.");
        }

        (RecordEnvelope? current, string? problem) = FindReleaseOperation(request);
        if (current is null)
        {
            return ReleaseProblem(pending, "unknown", "not-found", problem!);
        }

        string identity = EstateModel.BodyString(current, "identity") ?? pending;
        JsonElement body = current.Body;
        string status = EstateModel.BodyString(current, "status") ?? "?";
        string providerId = EstateModel.BodyString(current, "provider") ?? "?";
        JsonElement target = body.GetProperty("target");
        string product = target.TryGetProperty("product", out JsonElement productElement) ? productElement.GetString() ?? "?" : "?";
        string? runId = target.TryGetProperty("run", out JsonElement runElement) ? runElement.GetString() : null;
        if (request.Cancel is not null)
        {
            if (status is not ("not-started" or "uncertain" or "known-failure"))
            {
                return ReleaseProblem(identity, status, "refused", $"An operation that is {status} is not canceled; cancellation settles an intent that was never dispatched or an outcome reconciled as having had no effect.");
            }

            WriteOutcome canceled = NewRecord(new NewRecordRequest("operation", request.By, Element(ReleaseFlow.Canceled(body, request.Cancel)), $"product:{product}", current.Id));
            return canceled.Ok ? new ReleaseResponse(identity, "canceled", $"{identity}: canceled; {request.Cancel}", true, canceled.Written) : ReleaseProblem(identity, status, canceled.Problem!);
        }

        IReleaseProvider? provider = Providers.Release(providerId);
        if (provider is null)
        {
            return ReleaseProblem(identity, status, "refused", $"The operation was dispatched through '{providerId}', which has no implementation on the runtime's machine; it is observed only through that provider, never another.");
        }

        RecordResponse? productRecord = Record(product);
        ReleaseProfile profile = productRecord is not null && productRecord.Type == "product" ? ReleaseProfile.FromProductBody(productRecord.Envelope.GetProperty("body")) : ReleaseProfile.None;
        var recordedSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in target.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name is not ("version" or "product" or "run")))
        {
            recordedSettings[property.Name] = property.Value.GetString()!;
        }

        IReadOnlyDictionary<string, string> settings = profile.Settings(provider.Descriptor, providerId, recordedSettings);
        string destination = provider.Descriptor.Destination is string key && settings.TryGetValue(key, out string? named) ? named : request.Repository ?? providerId;
        string checksums = settings.TryGetValue("checksums", out string? checksumFile) ? checksumFile : "SHA256SUMS";
        ReleaseObservation? release;
        RunObservation? run;
        try
        {
            run = runId is null ? null : provider.ObserveRun(destination, runId);
            release = provider.ObserveRelease(destination, tag, settings);
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
        {
            return ReleaseProblem(identity, status, "provider", exception.Message);
        }

        ReleaseRow? recorded = Releases().FirstOrDefault(r => r.Version == request.Version && SubjectProduct(r.Id) == product);
        switch (status)
        {
            case "observed-success":
                return ObservePublication(request, identity, product, recorded, release, destination, tag, provider);
            case "dispatched" or "uncertain":
            {
                JsonObject revisionBody;
                try
                {
                    revisionBody = ReleaseFlow.AfterObservation(body, run, release, request.Version, profile, checksums);
                }
                catch (InvalidOperationException)
                {
                    string waiting = run is not null && run.Status != "completed"
                        ? $"run {run.RunId} is {run.Status.Replace('_', ' ')}"
                        : release is not null
                            ? $"draft {tag} lacks checksums for {string.Join(", ", profile.Missing(release, request.Version, checksums))}"
                            : run is null ? "no run is identified and no release is observed yet" : $"run {run.RunId} completed and no draft {tag} is observed yet";
                    return new ReleaseResponse(identity, status, $"{identity}: {status}, pending ({waiting}). Nothing recorded; observe again later{(status == "uncertain" ? ", or cancel once reconciliation shows no effect" : string.Empty)}.", true, Pending: waiting, Run: run?.RunId, Url: run?.Url ?? release?.Url);
                }

                WriteOutcome revision = NewRecord(new NewRecordRequest("operation", request.By, Element(revisionBody), $"product:{product}", current.Id));
                if (!revision.Ok)
                {
                    return ReleaseProblem(identity, status, revision.Problem!);
                }

                string newStatus = revisionBody["status"]!.GetValue<string>();
                if (newStatus != "observed-success")
                {
                    return new ReleaseResponse(identity, newStatus, $"{identity}: {newStatus}. Retry with a reconciliation once the cause is settled.", false, revision.Written, Run: run?.RunId, Url: run?.Url);
                }

                string? commit = run?.HeadSha ?? release?.TargetCommit;
                if (commit is null || commit.Length != 40)
                {
                    return ReleaseProblem(identity, newStatus, "refused", "The release was observed but its exact commit is unknown; the candidate record needs the commit the run built.");
                }

                if (recorded is not null)
                {
                    return new ReleaseResponse(identity, newStatus, $"{identity}: {newStatus}; release {request.Version} is already recorded as {recorded.Status} ({recorded.Id}).", true, revision.Written, ReleaseId: recorded.Id, Run: run?.RunId, Url: release?.Url);
                }

                var evidence = new List<string> { $"Draft release {tag} observed at {release!.Url} with {release.Assets.Count} assets and checksums for every {(profile.DeclaresArtifacts ? "declared artifact" : "asset")}." };
                if (run is not null)
                {
                    evidence.Insert(0, $"Release build run {run.RunId} on commit {run.HeadSha} succeeded: {run.Url}");
                }

                WriteOutcome candidate = NewRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.CandidateBody(request.Version, destination, commit, release, evidence, request.Qualified ?? [], request.Signing, request.Limitations ?? [], profile, checksums)), $"product:{product}"));
                if (!candidate.Ok)
                {
                    return ReleaseProblem(identity, newStatus, candidate.Problem!);
                }

                string publish = provider.ManualPublicationCommand(destination, tag);
                return new ReleaseResponse(identity, newStatus, $"{identity}: observed-success; release {request.Version} recorded as a candidate at commit {commit[..12]}. Publication is the operator's: after accepting candidate {candidate.Written!.Id} at its exact hash, publish with '{publish}', then observe again to record the observed publication.", true, revision.Written, candidate.Written, Run: run?.RunId, Publish: publish, Url: release.Url);
            }

            default:
                return new ReleaseResponse(identity, status, $"{identity}: {status}. {(status == "not-started" ? "The intent is recorded and nothing was dispatched; a submission dispatches it, or a cancellation settles it." : "Retry with a reconciliation once the cause is settled.")}", false);
        }
    }

    private ReleaseResponse ObservePublication(ReleaseObserveRequest request, string identity, string product, ReleaseRow? recorded, ReleaseObservation? release, string destination, string tag, IReleaseProvider provider)
    {
        string where = release?.Url ?? destination;
        if (recorded is null)
        {
            return ReleaseProblem(identity, "observed-success", "refused", $"Release {request.Version} was observed as built but no release record exists; observe it again after the operation is recorded, or record the candidate.");
        }

        if (recorded.Status == "published")
        {
            return new ReleaseResponse(identity, "published", $"release {request.Version} is published ({recorded.Id}); nothing to observe.", true, ReleaseId: recorded.Id, Url: release?.Url);
        }

        if (release is null || release.Draft || release.PublishedAt is null)
        {
            return new ReleaseResponse(identity, recorded.Status ?? "candidate", $"release {request.Version} is recorded as {recorded.Status} and observed as {(release is null ? "absent" : "a draft")} at {where}; publication is the operator's manual step after accepting the candidate.", true, ReleaseId: recorded.Id, Publish: provider.ManualPublicationCommand(destination, tag), Url: release?.Url);
        }

        RecordResponse candidate = Record(recorded.Id)!;
        WriteOutcome published = NewRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.PublishedBody(candidate.Envelope.GetProperty("body"), release)), $"product:{product}", recorded.Id));
        if (!published.Ok)
        {
            return ReleaseProblem(identity, recorded.Status ?? "candidate", published.Problem!);
        }

        return new ReleaseResponse(identity, "published", $"release {request.Version} observed published at {release.PublishedAt}: {release.Url}", true, Release: published.Written, ReleaseId: recorded.Id, PublishedAt: release.PublishedAt, Url: release.Url);
    }

    /// <summary>The current release operation a request names: by provider and destination, by product and version, or by a destination that one operation alone matches.</summary>
    private (RecordEnvelope? Operation, string? Problem) FindReleaseOperation(ReleaseObserveRequest request)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            List<RecordEnvelope> operations = model.Current("operation")
                .Where(o => o.Body.TryGetProperty("target", out JsonElement t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("version", out JsonElement v) && v.GetString() == request.Version)
                .ToList();
            if (!string.IsNullOrWhiteSpace(request.Provider) && !string.IsNullOrWhiteSpace(request.Repository))
            {
                string identity = ReleaseFlow.Identity(request.Provider.Trim(), request.Repository.Trim(), request.Version);
                RecordEnvelope? byIdentity = operations.FirstOrDefault(o => EstateModel.BodyString(o, "identity") == identity);
                return byIdentity is null ? (null, $"No operation is recorded under {identity}; a submission records the intent and dispatches the build.") : (byIdentity, null);
            }

            IEnumerable<RecordEnvelope> matching = operations;
            if (!string.IsNullOrWhiteSpace(request.Product))
            {
                matching = matching.Where(o => o.Body.GetProperty("target").TryGetProperty("product", out JsonElement p) && p.GetString() == request.Product);
            }

            if (!string.IsNullOrWhiteSpace(request.Provider))
            {
                matching = matching.Where(o => EstateModel.BodyString(o, "provider") == request.Provider);
            }

            if (!string.IsNullOrWhiteSpace(request.Repository))
            {
                matching = matching.Where(o => (EstateModel.BodyString(o, "identity") ?? string.Empty).EndsWith($":{request.Repository}:v{request.Version}", StringComparison.Ordinal));
            }

            List<RecordEnvelope> found = matching.ToList();
            if (string.IsNullOrWhiteSpace(request.Product) && string.IsNullOrWhiteSpace(request.Repository))
            {
                return (null, "Name the product, or the destination (and the provider), of the release to observe.");
            }

            return found.Count switch
            {
                1 => (found[0], null),
                0 => (null, $"No current release operation of version {request.Version} matches; a submission records the intent and dispatches the build."),
                _ => (null, $"Several release operations of version {request.Version} match ({string.Join(", ", found.Select(o => EstateModel.BodyString(o, "identity")))}); name the provider and the destination."),
            };
        }
    }

    private string? SubjectProduct(string releaseId)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.ById.TryGetValue(releaseId, out RecordEnvelope? release) ? release.Subject?.Id : null;
        }
    }

    private static void AddIfGiven(Dictionary<string, string> settings, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            settings[name] = value.Trim();
        }
    }

    private static ProviderRow Row(string provider, string kind, ProviderDescriptor descriptor) =>
        new(provider, kind, descriptor.Id, descriptor.Contract, descriptor.Status, descriptor.Operations, descriptor.Limitations, descriptor.DeclaredSettings.Select(s => new ProviderSettingRow(s.Name, s.Summary, s.Required, s.Default)).ToList(), descriptor.Destination, descriptor.ServedLocations);

    private RecordEnvelope? CurrentOperation(string identity)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return model.Current("operation").FirstOrDefault(o => EstateModel.BodyString(o, "identity") == identity);
        }
    }

    private static ReleaseResponse ReleaseProblem(string identity, string status, string code, string message) =>
        new(identity, status, message, false, Problem: new Problem(code, message, []));

    private static ReleaseResponse ReleaseProblem(string identity, string status, Problem problem) =>
        new(identity, status, problem.Message, false, Problem: problem);

    private static JsonElement Element(JsonObject body)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public (UpgradeResponse? Plan, Problem? Problem) Upgrade(UpgradeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.Findings.HasErrors)
            {
                return (null, new Problem("invalid", "The estate has errors; resolve them before upgrading.", ToFindings(model.Findings, "not upgraded").Findings));
            }

            UpgradePlan plan;
            try
            {
                plan = EstateUpgrade.Plan(model, request.Version);
            }
            catch (InvalidOperationException exception)
            {
                return (null, new Problem("refused", exception.Message, []));
            }

            var response = new UpgradeResponse(plan.FromPin, plan.ToPin, plan.FromFormat, plan.ToFormat, plan.Migrations, plan.Release.Id, plan.Artifacts, false, null, plan.Anchored.Count == 0 ? null : [.. plan.Anchored.Select(a => a.Id)]);
            if (request.Check)
            {
                return (response, null);
            }

            if (request.By is null || !Ulid.IsValid(request.By))
            {
                return (null, new Problem("refused", "The principal applying the upgrade must be a ULID.", []));
            }

            // The migration that anchors the unsigned admissions decides which of them keep counting, so it is the operator's, or an
            // administrator's, and it is signed; refuse before anything changes rather than leave the estate's admissions to a check.
            if (plan.Anchored.Count > 0)
            {
                string anchoredIds = string.Join(", ", plan.Anchored.Select(a => a.Id));
                string? rootOperator = model.CurrentEstate is { } charter ? EstateModel.BodyString(charter, "operator") : null;
                if (request.By != rootOperator && !model.HasScope(request.By, Scopes.Administer))
                {
                    return (null, new Problem("refused", $"Moving to record format 2 anchors the admissions recorded without a signature ({anchoredIds}), and the migration that anchors them is the operator's or a principal's with the 'administer' scope; run the upgrade as one of them. Nothing changed.", []));
                }

                using JsonDocument probe = JsonDocument.Parse("{}");
                if (ResolveSigner(model, new RecordDraft("migration", request.By, probe.RootElement), out IRecordSigner? signer) is not null || signer is null)
                {
                    return (null, new Problem("refused", $"The migration that anchors the admissions recorded without a signature ({anchoredIds}) is signed, and principal {request.By} has no admitted device whose key this machine holds; run the upgrade where one is held. Nothing changed.", []));
                }
            }

            // Migrations stage their files first; nothing is written until every step succeeded.
            var context = new MigrationContext(session.Root, model);
            foreach (IRecordFormatMigration step in EstateUpgrade.Steps(plan))
            {
                try
                {
                    step.Apply(context);
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException or JsonException)
                {
                    return (null, new Problem("refused", $"Migration {step.Name} failed and nothing changed: {exception.Message}", []));
                }
            }

            var changes = new List<string> { $"{EstateLayout.ManifestPath} pinned to {plan.ToPin} with the artifact hashes of release {plan.Release.Id}" };
            changes.AddRange(EstateUpgrade.Steps(plan).Select(step => $"{step.Name}: {step.Summary}"));
            var previousContents = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            string manifestPath = Path.Combine(session.Root, EstateLayout.ManifestPath);
            previousContents[EstateLayout.ManifestPath] = File.ReadAllBytes(manifestPath);
            try
            {
                foreach ((string relative, byte[] bytes) in context.Staged.OrderBy(s => s.Key, StringComparer.Ordinal))
                {
                    string full = Path.Combine(session.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                    previousContents[relative] = File.Exists(full) ? File.ReadAllBytes(full) : null;
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllBytes(full, bytes);
                    changes.Add($"{relative} written by the migrations");
                }

                File.WriteAllBytes(manifestPath, EstateUpgrade.ManifestAfter(session.Manifest, plan));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                RestoreFiles(session.Root, previousContents);
                return (null, new Problem("refused", $"The upgrade could not write its files and was undone: {exception.Message}", []));
            }

            using JsonDocument body = JsonDocument.Parse(EstateUpgrade.MigrationBody(plan, changes).ToJsonString());
            WriteOutcome outcome = WriteLocked(new RecordDraft("migration", request.By, body.RootElement, Format: Math.Min(plan.ToFormat, RecordFormats.Current)), model);
            if (!outcome.Ok)
            {
                RestoreFiles(session.Root, previousContents);
                return (null, outcome.Problem);
            }

            return (response with { Applied = true, Migration = outcome.Written }, null);
        }
    }

    /// <inheritdoc />
    public (ExportResponse? Export, Problem? Problem) Export(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Output))
        {
            return (null, new Problem("invalid", "An output path is required.", []));
        }

        if (request.By is not null && !Ulid.IsValid(request.By))
        {
            return (null, new Problem("invalid", "The exporting principal must be a ULID.", []));
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            try
            {
                ExportSummary summary = EstateExport.Write(session, request.Output, request.By);
                return (new ExportResponse(summary.Id, summary.Estate, summary.CreatedAt, summary.Profile, summary.Records, summary.Objects, summary.Documents, summary.InventoryHash, Path.GetFullPath(request.Output), model.Findings.HasErrors), null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                return (null, new Problem("refused", exception.Message, []));
            }
        }
    }

    /// <inheritdoc />
    public DecisionsResponse Decisions()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            using JsonDocument json = JsonDocument.Parse(DecisionView.RenderJson(model));
            return new DecisionsResponse(DecisionView.RenderList(model), json.RootElement.Clone());
        }
    }

    /// <inheritdoc />
    public DecisionResponse? Decision(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? decision = DecisionView.Find(model, id);
            return decision is null ? null : new DecisionResponse(DecisionView.Render(model, decision), decision.Body.Clone());
        }
    }

    /// <summary>The encoded public key of a device that is current and admitted now, or null; what request authentication relies on.</summary>
    public string? AdmittedDevicePublicKey(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(deviceId, out RecordEnvelope? record) || record.Type != "device")
            {
                return null;
            }

            RecordEnvelope device = model.Latest(record);
            return model.IsCurrent(device) && SignatureChecks.IsAdmitted(model, device) ? EstateModel.BodyString(device, "publicKey") : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ReconcileTask> Reconcile()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            var all = new FindingList();
            foreach (Finding finding in model.Findings.Concat(EstateChecks.Run(model)))
            {
                all.Add(finding);
            }

            return EstateStatus.ReconcileTasks(all).Select(t => new ReconcileTask(t.Code, t.Location, t.Message)).ToList();
        }
    }

    /// <inheritdoc />
    public string RecoverInspect()
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return Recovery.Inspect(model, EstateChecks.Run(model));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ObjectRow> Objects()
    {
        lock (gate)
        {
            return session.Objects.List().Select(h => new ObjectRow(h)).ToList();
        }
    }

    /// <inheritdoc />
    public (ObjectStored? Stored, Problem? Problem) ObjectPut(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength > EstateLayout.MaxObjectBytes)
        {
            return (null, new Problem("refused", $"{bytes.LongLength:N0} bytes exceeds the {EstateLayout.MaxObjectBytes:N0}-byte object limit; larger artifacts go to releases and are referenced by hash.", []));
        }

        lock (gate)
        {
            string hash = session.Objects.Put(bytes);
            return (new ObjectStored(hash, bytes.LongLength, session.Objects.Kind), null);
        }
    }

    /// <inheritdoc />
    public byte[]? ObjectGet(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        lock (gate)
        {
            return ContentHash.IsValid(hash) ? session.Objects.Read(hash) : null;
        }
    }

    /// <inheritdoc />
    public FindingsResponse ObjectsVerify()
    {
        lock (gate)
        {
            var findings = new FindingList();
            int verified = 0;
            foreach (string hash in session.Objects.List())
            {
                try
                {
                    session.Objects.Read(hash);
                    verified++;
                }
                catch (ObjectCorruptedException exception)
                {
                    findings.Error("objects.hash", hash, exception.Message);
                }
            }

            return ToFindings(findings, $"{verified} objects verified");
        }
    }

    private static void RestoreFiles(string root, IReadOnlyDictionary<string, byte[]?> previousContents)
    {
        foreach ((string relative, byte[]? bytes) in previousContents)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (bytes is null)
            {
                File.Delete(full);
            }
            else
            {
                File.WriteAllBytes(full, bytes);
            }
        }
    }

    /// <summary>Authors, writes and validates a record about an existing record pinned to its exact hash.</summary>
    private WriteOutcome About(string type, string subjectType, string subjectId, string by, JsonElement body)
    {
        if (!Ulid.IsValid(by))
        {
            return Refuse("The authoring principal must be a ULID.");
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? subject = Latest(model, subjectId, subjectType);
            if (subject is null)
            {
                return NotFound($"No {subjectType} has id {subjectId}.");
            }

            return WriteLocked(new RecordDraft(type, by, body, subject.Reference), model);
        }
    }

    private WriteOutcome Write(RecordDraft draft)
    {
        lock (gate)
        {
            return WriteLocked(draft, session.Load());
        }
    }

    /// <summary>Signs, stores and validates; a record that would make the estate invalid is removed again. Caller holds the gate.</summary>
    private WriteOutcome WriteLocked(RecordDraft draft, EstateModel current, IReadOnlyList<IRecordSigner>? signers = null)
    {
        if (signers is null)
        {
            string? refusal = ResolveSigner(current, draft, out IRecordSigner? signer);
            if (refusal is not null)
            {
                return Refuse(refusal);
            }

            signers = signer is null ? [] : [signer];
        }

        // A record is written in its estate's format, never in one this build does not write.
        if (draft.Format is null)
        {
            draft = draft with { Format = Math.Min(current.Manifest?.RecordFormat ?? session.Manifest.RecordFormat, RecordFormats.Current) };
        }

        // A successor is never older than the record it supersedes, even when clocks or fast scripts put them in the same second.
        if (draft.Supersedes is not null && current.ById.TryGetValue(draft.Supersedes, out RecordEnvelope? predecessor) && (draft.At ?? DateTimeOffset.UtcNow) < predecessor.AtTime)
        {
            draft = draft with { At = predecessor.AtTime };
        }

        AuthoredRecord record;
        try
        {
            record = RecordAuthor.Create(draft, signers);
            session.Write(record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or CanonicalJsonException)
        {
            return Refuse(exception.Message);
        }

        EstateModel model = session.Load();
        var relevant = new FindingList();
        foreach (Finding finding in model.Findings.Where(f => f.Severity == Severity.Error))
        {
            relevant.Add(finding);
        }

        foreach (Finding finding in EstateChecks.Run(model).Where(f => f.Severity == Severity.Error && f.Location.StartsWith(record.RelativePath, StringComparison.Ordinal)))
        {
            relevant.Add(finding);
        }

        foreach (Finding finding in model.Findings.Where(f => f.Code == "result.output-changed" && f.Location.StartsWith(record.RelativePath, StringComparison.Ordinal)))
        {
            relevant.Add(new Finding(Severity.Error, "result.output-hash", finding.Location, "The file present does not have the hash or size this result claims; name the bytes actually produced."));
        }

        if (relevant.HasErrors)
        {
            session.Discard(record);
            return WriteOutcome.Fail(new Problem("rejected", $"Not written: the estate would be invalid with this record ({relevant.Errors} errors).", ToFindings(relevant, "rejected").Findings));
        }

        return WriteOutcome.Success(new RecordWritten(record.RelativePath, record.Envelope.Id, record.Envelope.ComputedHash));
    }

    /// <summary>Chooses the device that signs a record for its author on this machine, or explains why none can.</summary>
    private static string? ResolveSigner(EstateModel model, RecordDraft draft, out IRecordSigner? signer)
    {
        signer = null;
        IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
        List<RecordEnvelope> devices = model.Current("device").Where(d => EstateModel.BodyString(d, "principal") == draft.By).OrderBy(d => model.Chain(d)[0], StringComparer.Ordinal).ToList();
        // Keys are held under a device's identity, its first record, which signatures name across renames and rotations.
        foreach (string identity in devices.Where(d => SignatureChecks.IsAdmitted(model, d)).Select(d => model.Chain(d)[0]))
        {
            IDeviceKeyStore? store = stores.FirstOrDefault(s => s.Has(identity));
            if (store is not null)
            {
                signer = new StoreSigner(store, identity);
                return null;
            }
        }

        RecordEnvelope? estate = model.CurrentEstate;
        string? rootOperator = estate is null ? null : EstateModel.BodyString(estate, "operator");
        // The first authority: the root operator's first device admits itself, once in the estate's history.
        if (draft.Type == "admission" && draft.Subject is not null && draft.By == rootOperator && SignatureChecks.FirstAuthority(model, rootOperator) is null)
        {
            string? own = SignatureChecks.IdentityOf(model, draft.Subject.Id);
            IDeviceKeyStore? store = own is null || !devices.Any(d => model.Chain(d)[0] == own) ? null : stores.FirstOrDefault(s => s.Has(own));
            if (own is not null && store is not null)
            {
                signer = new StoreSigner(store, own);
                return null;
            }
        }

        // From record format 2 an admission is signed whatever the signature policy: unsigned, it would admit or revoke nothing.
        if (draft.Type == "admission" && (model.Manifest?.RecordFormat ?? RecordFormats.First) >= 2)
        {
            return draft.By == rootOperator && SignatureChecks.FirstAuthority(model, rootOperator) is null
                ? "An admission is signed, and the operator has no admitted device yet: create the operator's own device on this machine and admit it from itself first (the estate's first authority); it then signs the admissions of other devices. Nothing was written."
                : $"An admission is signed by an admitted device of its author, and principal {draft.By} has none whose key this machine holds: admit from a machine that holds one, or with the operator's recovery key (--recovery). Nothing was written.";
        }

        bool required = estate is not null && estate.Body.TryGetProperty("policies", out JsonElement policies) && policies.TryGetProperty("signatures", out JsonElement policy) && policy.GetString() == "required";
        if (required && draft.Type == "admission" && draft.By == rootOperator && SignatureChecks.FirstAuthority(model, rootOperator) is not null)
        {
            return "The operator's first device is already admitted, and a device admits itself only once in an estate's history: admit this one from a device already admitted, held on this machine.";
        }

        return required
            ? $"This copy is read-only for principal {draft.By}: no admitted device key is held on this machine. Create a device with 'vericairn devices create' and have an administrator admit it."
            : null;
    }

    private static RecordEnvelope? Latest(EstateModel model, string id, string type) =>
        model.ById.TryGetValue(id, out RecordEnvelope? record) && record.Type == type ? model.Latest(record) : null;

    private static bool TryParseSubject(string subject, out RecordReference? reference)
    {
        reference = null;
        string[] parts = subject.Split(':', 3);
        if (parts.Length < 2 || !Ulid.IsValid(parts[1]))
        {
            return false;
        }

        string? hash = parts.Length == 3 ? "sha256:" + parts[2].Replace("sha256:", string.Empty, StringComparison.Ordinal) : null;
        if (hash is not null && !ContentHash.IsValid(hash))
        {
            return false;
        }

        reference = new RecordReference(parts[0], parts[1], hash);
        return true;
    }

    private static DateTimeOffset? ParseAt(string? at) =>
        at is not null && RecordParser.IsTimestamp(at)
            ? DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : null;

    private static WriteOutcome Refuse(string message) => WriteOutcome.Fail(new Problem("refused", message, []));

    private static WriteOutcome NotFound(string message) => WriteOutcome.Fail(new Problem("not-found", message, []));

    private static FindingsResponse ToFindings(FindingList findings, string summary) =>
        new(summary, findings.Errors, findings.Warnings, findings.Sorted().Select(f => new FindingDto(f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message)).ToList());

    /// <inheritdoc />
    public void Dispose() => session.Dispose();
}
