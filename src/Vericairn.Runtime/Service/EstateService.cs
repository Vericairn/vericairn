// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Reflection;
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
using Vericairn.Runtime.Software;
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

    /// <summary>The GitHub Releases provider new services start with; tests replace it with the in-memory provider.</summary>
    public static IReleaseProvider DefaultReleaseProvider { get; set; } = new GhReleaseProvider();

    /// <summary>The GitHub Releases provider this service dispatches and observes through; it runs on the runtime's machine under the user's own login.</summary>
    public IReleaseProvider ReleaseProvider { get; set; } = DefaultReleaseProvider;

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
            return model.Current("device").OrderBy(d => d.Id, StringComparer.Ordinal).Select(device =>
            {
                RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, device);
                string principal = EstateModel.BodyString(device, "principal") ?? "?";
                return new DeviceRow(
                    device.Id,
                    principal,
                    model.CurrentPrincipal(principal) is RecordEnvelope p ? EstateModel.BodyString(p, "name") : null,
                    EstateModel.BodyString(device, "name"),
                    EstateModel.BodyString(device, "platform"),
                    EstateModel.BodyString(device, "custody"),
                    admission is null ? "not admitted" : EstateModel.BodyString(admission, "status") ?? "?",
                    stores.Any(s => s.Has(device.Id)));
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

        IDeviceKeyStore store;
        try
        {
            store = DeviceKeyStores.Named(request.Custody);
        }
        catch (Exception exception) when (exception is ArgumentException or PlatformNotSupportedException)
        {
            return Refuse(exception.Message);
        }

        lock (gate)
        {
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
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            WriteOutcome outcome = WriteLocked(new RecordDraft("device", request.Principal, document.RootElement, At: now, Id: id), session.Load(), new StoreSigner(store, id));
            if (!outcome.Ok)
            {
                store.Remove(id);
            }

            return outcome;
        }
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
            return WriteLocked(new RecordDraft("admission", request.By, document.RootElement, device.Reference), model);
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
            return WriteLocked(new RecordDraft("admission", request.By, document.RootElement, device.Reference, admission.Id), model);
        }
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
    public ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string identity = ReleaseFlow.Identity(request.Repository, request.Version);
        if (!Ulid.IsValid(request.By) || !Ulid.IsValid(request.Product))
        {
            return ReleaseProblem(identity, "not-started", "refused", "The recording principal and the product must be ULIDs.");
        }

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

        string previousId;
        JsonElement previousBody;
        if (current is null)
        {
            WriteOutcome intent = NewRecord(new NewRecordRequest("operation", request.By, Element(ReleaseFlow.IntentBody(request.Repository, request.Workflow, request.Ref, request.Version, request.Product)), $"product:{request.Product}"));
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
            DispatchReceipt receipt = ReleaseProvider.Dispatch(new DispatchRequest(request.Repository, request.Workflow, request.Ref, new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = request.Version }));
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
        string identity = ReleaseFlow.Identity(request.Repository, request.Version);
        string tag = ReleaseFlow.Tag(request.Version);
        if (!Ulid.IsValid(request.By))
        {
            return ReleaseProblem(identity, "unknown", "refused", "The recording principal must be a ULID.");
        }

        RecordEnvelope? current = CurrentOperation(identity);
        if (current is null)
        {
            return ReleaseProblem(identity, "unknown", "not-found", $"No operation is recorded under {identity}; a submission records the intent and dispatches the workflow.");
        }

        JsonElement body = current.Body;
        string status = EstateModel.BodyString(current, "status") ?? "?";
        string product = body.GetProperty("target").GetProperty("product").GetString() ?? "?";
        string? runId = body.GetProperty("target").TryGetProperty("run", out JsonElement runElement) ? runElement.GetString() : null;
        if (request.Cancel is not null)
        {
            if (status is not ("not-started" or "uncertain" or "known-failure"))
            {
                return ReleaseProblem(identity, status, "refused", $"An operation that is {status} is not canceled; cancellation settles an intent that was never dispatched or an outcome reconciled as having had no effect.");
            }

            WriteOutcome canceled = NewRecord(new NewRecordRequest("operation", request.By, Element(ReleaseFlow.Canceled(body, request.Cancel)), $"product:{product}", current.Id));
            return canceled.Ok ? new ReleaseResponse(identity, "canceled", $"{identity}: canceled; {request.Cancel}", true, canceled.Written) : ReleaseProblem(identity, status, canceled.Problem!);
        }

        ReleaseObservation? release;
        RunObservation? run;
        try
        {
            run = runId is null ? null : ReleaseProvider.ObserveRun(request.Repository, runId);
            release = ReleaseProvider.ObserveRelease(request.Repository, tag);
        }
        catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
        {
            return ReleaseProblem(identity, status, "provider", exception.Message);
        }

        ReleaseRow? recorded = Releases().FirstOrDefault(r => r.Version == request.Version);
        switch (status)
        {
            case "observed-success":
                return ObservePublication(request, identity, product, recorded, release, tag);
            case "dispatched" or "uncertain":
            {
                JsonObject revisionBody;
                try
                {
                    revisionBody = ReleaseFlow.AfterObservation(body, run, release, request.Version);
                }
                catch (InvalidOperationException)
                {
                    string pending = run is null
                        ? (release is null ? "no run is identified and no release is observed yet" : $"draft {tag} lacks checksums for {string.Join(", ", ReleaseFlow.MissingDistributables(release, request.Version))}")
                        : $"run {run.RunId} is {run.Status.Replace('_', ' ')}";
                    return new ReleaseResponse(identity, status, $"{identity}: {status}, pending ({pending}). Nothing recorded; observe again later{(status == "uncertain" ? ", or cancel once reconciliation shows no effect" : string.Empty)}.", true, Pending: pending, Run: run?.RunId, Url: run?.Url ?? release?.Url);
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

                var evidence = new List<string> { $"Draft release {tag} observed at {release!.Url} with {release.Assets.Count} assets and checksums for every distributable." };
                if (run is not null)
                {
                    evidence.Insert(0, $"Release workflow run {run.RunId} on commit {run.HeadSha} succeeded: {run.Url}");
                }

                WriteOutcome candidate = NewRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.CandidateBody(request.Version, request.Repository, commit, release, evidence, request.Qualified ?? [], request.Signing, request.Limitations ?? [])), $"product:{product}"));
                if (!candidate.Ok)
                {
                    return ReleaseProblem(identity, newStatus, candidate.Problem!);
                }

                string publish = ReleaseProvider.ManualPublicationCommand(request.Repository, tag);
                return new ReleaseResponse(identity, newStatus, $"{identity}: observed-success; release {request.Version} recorded as a candidate at commit {commit[..12]}. Publication is the operator's: after accepting candidate {candidate.Written!.Id} at its exact hash, publish with '{publish}', then observe again to record the observed publication.", true, revision.Written, candidate.Written, Run: run?.RunId, Publish: publish, Url: release.Url);
            }

            default:
                return new ReleaseResponse(identity, status, $"{identity}: {status}. {(status == "not-started" ? "The intent is recorded and nothing was dispatched; a submission dispatches it, or a cancellation settles it." : "Retry with a reconciliation once the cause is settled.")}", false);
        }
    }

    private ReleaseResponse ObservePublication(ReleaseObserveRequest request, string identity, string product, ReleaseRow? recorded, ReleaseObservation? release, string tag)
    {
        string where = release?.Url ?? "the repository";
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
            return new ReleaseResponse(identity, recorded.Status ?? "candidate", $"release {request.Version} is recorded as {recorded.Status} and observed as {(release is null ? "absent" : "a draft")} at {where}; publication is the operator's manual step after accepting the candidate.", true, ReleaseId: recorded.Id, Publish: ReleaseProvider.ManualPublicationCommand(request.Repository, tag), Url: release?.Url);
        }

        RecordResponse candidate = Record(recorded.Id)!;
        WriteOutcome published = NewRecord(new NewRecordRequest("release", request.By, Element(ReleaseFlow.PublishedBody(candidate.Envelope.GetProperty("body"), release)), $"product:{product}", recorded.Id));
        if (!published.Ok)
        {
            return ReleaseProblem(identity, recorded.Status ?? "candidate", published.Problem!);
        }

        return new ReleaseResponse(identity, "published", $"release {request.Version} observed published at {release.PublishedAt}: {release.Url}", true, Release: published.Written, ReleaseId: recorded.Id, PublishedAt: release.PublishedAt, Url: release.Url);
    }

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

            var response = new UpgradeResponse(plan.FromPin, plan.ToPin, plan.FromFormat, plan.ToFormat, plan.Migrations, plan.Release.Id, plan.Artifacts, false, null);
            if (request.Check)
            {
                return (response, null);
            }

            if (request.By is null || !Ulid.IsValid(request.By))
            {
                return (null, new Problem("refused", "The principal applying the upgrade must be a ULID.", []));
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
            WriteOutcome outcome = WriteLocked(new RecordDraft("migration", request.By, body.RootElement), model);
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
    private WriteOutcome WriteLocked(RecordDraft draft, EstateModel current, IRecordSigner? signer = null)
    {
        if (signer is null)
        {
            string? refusal = ResolveSigner(current, draft, out signer);
            if (refusal is not null)
            {
                return Refuse(refusal);
            }
        }

        // A successor is never older than the record it supersedes, even when clocks or fast scripts put them in the same second.
        if (draft.Supersedes is not null && current.ById.TryGetValue(draft.Supersedes, out RecordEnvelope? predecessor) && (draft.At ?? DateTimeOffset.UtcNow) < predecessor.AtTime)
        {
            draft = draft with { At = predecessor.AtTime };
        }

        AuthoredRecord record;
        try
        {
            record = RecordAuthor.Create(draft, signer);
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
        List<RecordEnvelope> devices = model.Current("device").Where(d => EstateModel.BodyString(d, "principal") == draft.By).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        foreach (RecordEnvelope device in devices.Where(d => SignatureChecks.IsAdmitted(model, d)))
        {
            IDeviceKeyStore? store = stores.FirstOrDefault(s => s.Has(device.Id));
            if (store is not null)
            {
                signer = new StoreSigner(store, device.Id);
                return null;
            }
        }

        RecordEnvelope? estate = model.CurrentEstate;
        string? rootOperator = estate is null ? null : EstateModel.BodyString(estate, "operator");
        if (draft.Type == "admission" && draft.Subject is not null && draft.By == rootOperator)
        {
            RecordEnvelope? own = devices.FirstOrDefault(d => d.Id == draft.Subject.Id);
            IDeviceKeyStore? store = own is null ? null : stores.FirstOrDefault(s => s.Has(own.Id));
            if (own is not null && store is not null)
            {
                signer = new StoreSigner(store, own.Id);
                return null;
            }
        }

        bool required = estate is not null && estate.Body.TryGetProperty("policies", out JsonElement policies) && policies.TryGetProperty("signatures", out JsonElement policy) && policy.GetString() == "required";
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
