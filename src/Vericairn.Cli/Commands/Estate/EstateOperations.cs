// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;
using HistoryLine = Vericairn.Records.History.HistoryLine;

namespace Vericairn.Cli;

internal sealed class EstateOperations(CommandContext context)
{
    public int Init(string root, string name, string slug, string purpose, string operatorName, string organization, string? github, string repository, string? commit, string profile, bool json,
        bool release = false, string? from = null, string? buildIdentity = null, string line = "current", string channel = "stable")
    {
        if (commit is null)
        {
            return context.Fail("This build has no recorded 40-digit source commit. Pass --commit <40-hex-commit> to choose the Vericairn source to pin.");
        }

        try
        {
            if (from is not null && !release) return context.Fail("--from requires --release. Add --release to verify the downloaded published build, or omit --from to initialize a source pin.");
            VerifiedRelease? verified = null;
            if (release)
            {
                ReleaseBuild build = ReleaseBuild.Parse(buildIdentity ?? VersionText());
                if (!string.Equals(repository, VerifiedRelease.Repository, StringComparison.OrdinalIgnoreCase) || commit != build.Commit)
                    return context.Fail("--release pins the running published build's repository and commit. Remove the source override or omit --release to keep a source pin. Nothing changed.");
                Composition.Register(Runtime.Providers.ProviderRegistry.Current);
                verified = from is null
                    ? Runtime.Providers.ProviderRegistry.Current.VericairnReleases!.VerifyAsync(build, ReleaseBuild.Platform, context.CancellationToken).GetAwaiter().GetResult()
                    : PublishedReleaseVerifier.FromDirectoryAsync(from, build, ReleaseBuild.Platform, context.CancellationToken).GetAwaiter().GetResult();
            }
            var initialization = new EstateInitialization(name, slug, purpose, operatorName, organization, github, repository, commit, Profile: profile);
            initialization = initialization with { Release = verified, Line = line, Channel = channel };
            EstateBootstrap bootstrap = EstateBootstrap.Prepare(root);
            RecordEnvelope operatorRecord;
            RecordEnvelope estateRecord;
            RecordEnvelope? releaseRecord = null;
            if (profile == "local-only")
            {
                PreparedEstate prepared = EstateInitializer.Prepare(root, initialization);
                string manifestPath = Path.Combine(root, EstateLayout.ManifestPath);
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                File.WriteAllBytes(manifestPath, prepared.ManifestBytes);
                using EstateSession session = EstateSession.Open(root);
                session.Write(prepared.Operator);
                session.Write(prepared.Estate);
                if (prepared.Release is not null) session.Write(prepared.Release);
                EstateModel model = session.Load();
                if (model.Findings.HasErrors)
                {
                    if (prepared.Release is not null) session.Discard(prepared.Release);
                    session.Discard(prepared.Estate);
                    session.Discard(prepared.Operator);
                    File.Delete(manifestPath);
                    return context.ReportFindings(ToFindings(model.Findings, "rejected"), json);
                }

                (operatorRecord, estateRecord) = (prepared.Operator.Envelope, prepared.Estate.Envelope);
                releaseRecord = prepared.Release?.Envelope;
            }
            else
            {
                (operatorRecord, estateRecord) = EstateInitializer.Initialize(root, initialization);
                if (release)
                {
                    EstateModel model = EstateLoader.Load(root);
                    releaseRecord = model.ById[model.Manifest!.Release!.Id];
                }
            }

            BootstrapReceipt[] bootstraps = bootstrap.Write();
            if (json)
            {
                context.Output.WriteLine(releaseRecord is null
                    ? JsonSerializer.Serialize(new { @operator = operatorRecord.Id, estate = estateRecord.Id, manifest = EstateLayout.ManifestPath, bootstrap = bootstraps }, ProtocolJson.Options)
                    : JsonSerializer.Serialize(new
                    {
                        @operator = operatorRecord.Id,
                        estate = estateRecord.Id,
                        manifest = EstateLayout.ManifestPath,
                        release = new { path = releaseRecord.ExpectedPath, id = releaseRecord.Id, hash = releaseRecord.ComputedHash },
                        bootstrap = bootstraps,
                    }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"operator  {operatorRecord.Id}  {operatorRecord.ExpectedPath}");
                context.Output.WriteLine($"estate    {estateRecord.Id}  {estateRecord.ExpectedPath}");
                context.Output.WriteLine($"manifest  {EstateLayout.ManifestPath}");
                if (releaseRecord is not null)
                    context.Output.WriteLine($"release   {releaseRecord.Id}  {releaseRecord.ExpectedPath}  {releaseRecord.ComputedHash}");
                ReportBootstrap(bootstraps);
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int Check(string root, bool json, string? expect = null) =>
        IsEstate(root)
            ? context.WithApi(root, api => context.ReportFindings(Expecting(api.Check(), expect, () => ExpectedCandidates(api, expect), root), json))
            : context.WithFiles(root, model => context.ReportFindings(Expecting(ToFindings(Combined(model), $"{model.Records.Count} records"), expect,
                () => model.Records.Where(r => r.Type == "history-checkpoint").Select(r => (r.Id, r.ComputedHash, r.ExpectedPath, model.Counts(r))).ToList(), null), json));

    /// <summary>
    /// The check with an expected checkpoint (design/signed-history.md § 9): an error refuses a copy that lacks it, so a fresh machine
    /// checks against a head known elsewhere rather than trusting whatever history the copy happens to hold. In an estate, this machine
    /// remembers the result, and a checkpoint held here and by the main line as the newest it verified.
    /// </summary>
    private static FindingsResponse Expecting(FindingsResponse findings, string? expect, Func<IReadOnlyList<(string Id, string Hash, string Path, bool Counts)>> candidates, string? estateRoot)
    {
        if (expect is null) return findings;
        (Finding? missing, (string Id, string Hash, string Path)? held) = global::Vericairn.Records.History.SignedHistory.ExpectMatch(candidates(), expect);
        FindingDto finding = missing is null
            ? new("info", "history.expected", EstateLayout.RecordsDirectory, $"This copy holds the expected history checkpoint {expect.Trim()}, signed and counting.")
            : new("error", missing.Code, missing.Location, missing.Message);
        List<FindingDto> added = [finding];
        if (estateRoot is not null && EstateManifest.Load(Path.GetFullPath(estateRoot), new FindingList()) is { } manifest &&
            HistoryMemory.RememberExpected(estateRoot, manifest.Estate, manifest.Profile, held, CommandProviders.SourceAdapter, DateTimeOffset.UtcNow) is string note)
            added.Add(new("warning", "history.remembered", EstateLayout.RecordsDirectory, note));
        return findings with
        {
            Errors = findings.Errors + (missing is null ? 0 : 1),
            Warnings = findings.Warnings + added.Count(f => f.Severity == "warning"),
            Findings = [.. findings.Findings, .. added],
        };
    }

    /// <summary>Writes or adopts history checkpoints, or shows what that would witness and anchor (design/signed-history.md §§ 9 and 10).</summary>
    public int Checkpoint(string root, string? by, bool adopt, bool check, bool json, string? expectPlan = null)
    {
        if (!check && by is null) return context.Fail("--by is required unless --check: name the principal whose admitted device on this machine signs" + (adopt ? ", the root operator or a person holding administer." : "."));
        return context.WithApi(root, api =>
        {
            (HistoryCheckpointResponse? response, Problem? problem) = api.HistoryCheckpoint(new HistoryCheckpointRequest(by, adopt, check, expectPlan));
            if (response is null) return context.Fail(problem?.Message ?? "refused");
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                return problem is null ? Clean : context.Fail(problem.Message);
            }

            string heads = response.Heads.Count == 0 ? "none" : string.Join(", ", response.Heads.Select(h => $"{h.Id} ({HistoryLine.ShortHash(h.Hash)})"));
            if (response.Trigger == "adoption")
            {
                context.Output.WriteLine($"{(response.Applied ? "adopted" : "would adopt")}: witnesses {response.Witnessed:N0} records; anchors {response.Anchored.Count:N0} unsigned records that count today");
                Section("anchors", response.Anchored, r => $"{r.Type}  {r.At}  {r.Path}  added in {(r.Commit is null ? "an unknown commit" : r.Commit + (r.CommitAt is null ? string.Empty : " on " + r.CommitAt))}{(r.Note is null ? string.Empty : "  NOTE: " + r.Note)}");
                Section("unsigned records that count for nothing today and stay so", response.NotCounting, r => $"{r.Type}  {r.At}  {r.Path}{(r.Note is null ? string.Empty : "  NOTE: " + r.Note)}");
                Section("records signed by a device revoked now", response.ByRevokedDevices, r => $"{r.Type}  {r.At}  {r.Path}  device {r.Device}");
                Section("records whose roles and grants read otherwise at their own time; adoption keeps for each the reading it has at adoption (advisory: the plan hash does not cover this list, and adoption computes it again)", response.AuthorityChanges, r => $"{r.Type}  {r.At}  {r.Path}  NOTE: {r.Note}");
                Section("record files deleted in history, which no checkpoint can witness", response.DeletedInHistory, d => $"{d.Path}  deleted in {d.Commit}");
                Section("record files no commit holds yet, which adoption witnesses all the same", response.Uncommitted, p => p);
                if (response.PlanHash is { } plan) context.Output.WriteLine("plan: " + plan);
                if (!response.Applied)
                    context.Output.WriteLine("Adoption keeps every reading as it is and admits nothing new; from then on the runtime writes checkpoints at upgrades, release submissions, charter revisions and intervals. The root operator signs it with vericairn estate checkpoint --adopt --expect-plan <plan> --by <root operator>.");
            }
            else
            {
                context.Output.WriteLine($"{(response.Applied ? "wrote" : "would write")} a history checkpoint: witnesses {response.Witnessed:N0} records, listing the {response.Listed:N0} no checkpoint witnessed yet; follows {heads}");
            }

            if (response.Unavailable is { } unavailable) context.Output.WriteLine("unavailable: " + unavailable);
            if (response.Written is { } written) context.Output.WriteLine($"{written.Path}  {written.Id}  {written.Hash}");
            return problem is null ? Clean : context.Fail(problem.Message);
        }, readOnly: check);

        void Section<T>(string title, IReadOnlyList<T> items, Func<T, string> line)
        {
            if (items.Count == 0) return;
            context.Output.WriteLine(title + ":");
            foreach (T item in items) context.Output.WriteLine("  " + line(item));
        }
    }

    private static List<(string Id, string Hash, string Path, bool Counts)> ExpectedCandidates(IEstateApi api, string? expect)
    {
        List<(string Id, string Hash, string Path, bool Counts)> counting = [.. api.Records("history-checkpoint").Select(r => (r.Id, r.Hash, r.Path, true))];
        // A checkpoint named by id that is here but does not count is reported as such, not as absent.
        if (expect?.Trim() is { Length: 26 } id && counting.All(c => c.Id != id) && api.Record(id) is { Type: "history-checkpoint" } present)
            counting.Add((present.Id, present.Hash, present.Path, false));
        return counting;
    }

    public int Bootstrap(string root, bool force, bool json)
    {
        if (!IsEstate(root)) return context.Fail("No estate/manifest.json here. Initialize an estate before writing its bootstrap.");
        try
        {
            BootstrapReceipt[] bootstraps = EstateBootstrap.Prepare(root, force).Write();
            if (json) context.Output.WriteLine(JsonSerializer.Serialize(new { bootstrap = bootstraps }, ProtocolJson.Options));
            else ReportBootstrap(bootstraps);
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    private void ReportBootstrap(IEnumerable<BootstrapReceipt> bootstraps)
    {
        foreach (BootstrapReceipt bootstrap in bootstraps)
            context.Output.WriteLine($"{bootstrap.Status,-9} {bootstrap.Path}");
    }

    public int Export(string root, string output, string? by, bool json) => context.WithApi(root, api =>
    {
        (ExportResponse? export, Problem? problem) = api.Export(new ExportRequest(Path.GetFullPath(output), by));
        if (export is null)
        {
            return context.Fail(problem?.Message ?? "refused");
        }

        if (export.HadErrors)
        {
            context.Error.WriteLine("The estate has errors; exported anyway so that it can be inspected elsewhere.");
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new { export.Id, export.Estate, export.CreatedAt, export.Profile, export.Records, export.Objects, export.Documents, export.InventoryHash, output = export.Output }));
        }
        else
        {
            context.Output.WriteLine($"export {export.Id} of estate {export.Estate}: {export.Records} records, {export.Objects} objects, {export.Documents} documents; inventory {export.InventoryHash}");
            context.Output.WriteLine("Carries records and bytes only: no keys, sessions, credentials or authority travel with it.");
        }

        return Clean;
    });

    /// <summary>
    /// Rebuilds this clone's derived cache from the repository's record files (FIX-93). A daemon that answers, at any API version,
    /// refuses it; the runtime's writer lock refuses it while another command has the estate open; a local-only estate is refused.
    /// </summary>
    public int CacheRebuild(string root, bool json)
    {
        if (!IsEstate(root))
        {
            return context.Fail(NotAnEstate(root));
        }

        try
        {
            if (DaemonHost.Discover(root) is { } endpoint && Answers(endpoint))
            {
                return context.Fail($"Daemon {endpoint.ProcessId} serves this estate, so its cache is in use. Stop it with 'vericairn runtime stop', then rebuild. Nothing was moved.");
            }

            LiveStoreRebuilt rebuilt = LiveStoreRecovery.Rebuild(root);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { estate = rebuilt.Estate, cache = rebuilt.Cache, records = rebuilt.Records, retained = rebuilt.Retained, retainedFiles = rebuilt.RetainedFiles }));
                return Clean;
            }

            context.Output.WriteLine($"rebuilt the derived cache from {rebuilt.Records} record files: {rebuilt.Cache}");
            context.Output.WriteLine(rebuilt.Retained is null
                ? "  no earlier cache was present"
                : $"  kept the earlier cache for diagnosis, unchanged: {rebuilt.Retained} ({string.Join(", ", rebuilt.RetainedFiles)})");
            context.Output.WriteLine("Notes kept only on this machine in the earlier cache stay in the kept files. Check the estate with 'vericairn estate check'.");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    // Any answer means a daemon owns the estate's live state, whatever its API version.
    private bool Answers(DaemonEndpoint endpoint)
    {
        try
        {
            using var client = new VericairnClient(endpoint);
            _ = client.HealthAsync(context.CancellationToken).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or System.Net.Sockets.SocketException or JsonException or UnauthorizedAccessException
            or ClientUnavailableException or ClientTimeoutException)
        {
            return false;
        }
    }

    public int Restore(string from, string to, bool json)
    {
        try
        {
            (ExportSummary? summary, FindingList findings) = EstateExport.TryRestore(from, to);
            if (summary is null || findings.HasErrors)
            {
                context.Error.WriteLine("Nothing restored: the export does not verify against its inventory.");
                return context.ReportFindings(ToFindings(findings, "rejected"), json);
            }

            using EstateSession session = EstateSession.Open(to);
            EstateModel model = session.Load();
            FindingList checks = EstateChecks.Run(model, CommandProviders.SourceAdapter?.IgnoredEstateEntries(model.Root));
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { summary.Id, summary.Estate, summary.Records, summary.Objects, summary.Documents, restoredTo = Path.GetFullPath(to), errors = model.Findings.Errors + checks.Errors }));
            }
            else
            {
                context.Output.WriteLine($"restored export {summary.Id} of estate {summary.Estate} into {Path.GetFullPath(to)}: {summary.Records} records, {summary.Objects} objects, {summary.Documents} documents");
                context.Output.WriteLine("Restored records carry no authority: devices stay where their keys are, and a device held here must be admitted before this copy can author.");
                context.Output.Write(Recovery.Inspect(model, checks));
            }

            return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int Upgrade(string root, string version, string? by, bool check, bool json, string? from = null, string? buildIdentity = null, string? line = null, string? channel = null)
    {
        UpgradeResponse? upgraded = null;
        int code = context.WithApi(root, api =>
        {
            (UpgradeResponse? plan, Problem? problem) = api.Upgrade(new UpgradeRequest(version, by, check) { From = from is null ? null : Path.GetFullPath(from), ClientBuild = buildIdentity ?? VersionText(), Line = line, Channel = channel });
            if (plan is null)
            {
                if (problem is not null && problem.Findings.Count > 0)
                {
                    context.Error.WriteLine(problem.Message);
                    return context.ReportFindings(new FindingsResponse("not upgraded", problem.Findings.Count(f => f.Severity == "error"), problem.Findings.Count(f => f.Severity == "warning"), problem.Findings), json);
                }

                return context.Fail(problem?.Message ?? "refused");
            }

            upgraded = plan;
            if (json)
            {
                // Printed once the runtime step below has run, with what it found.
                return Clean;
            }

            context.Output.WriteLine($"{(plan.Applied ? "upgraded" : "would upgrade")} {plan.FromPin} -> {plan.ToPin}; record format {plan.FromFormat} -> {plan.ToFormat}; migrations: {(plan.Migrations.Count == 0 ? "none" : string.Join(", ", plan.Migrations))}; release {plan.Release}");
            context.Output.WriteLine($"  followed line: {plan.Line}; channel: {plan.Channel}");
            foreach ((string platform, string hash) in plan.Artifacts.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                context.Output.WriteLine($"  {platform}  {hash}");
            }

            if (plan.Anchored is { Count: > 0 } anchored)
            {
                context.Output.WriteLine($"{(plan.Applied ? "anchored" : "anchors")} the admissions recorded without a signature, which count after the move only because the signed migration lists them: {string.Join(", ", anchored)}");
            }

            if (plan.Regenerated is { Count: > 0 } regenerated)
            {
                context.Output.WriteLine($"{(plan.Applied ? "rewrote" : "rewrites")} {regenerated.Count} generated member{(regenerated.Count == 1 ? string.Empty : "s")} holding exactly an earlier generator's output, to the current generator's bytes:");
                foreach (string path in regenerated)
                {
                    context.Output.WriteLine($"  {path}");
                }
            }

            if (plan.Migration is not null)
            {
                context.Output.WriteLine($"{plan.Migration.Path}  {plan.Migration.Id}  {plan.Migration.Hash}");
            }

            context.ReportCheckpoint(plan.Checkpoint);

            return Clean;
        }, readOnly: check, upgrade: true);
        if (upgraded is null)
        {
            return code;
        }

        // A check changes nothing, so it leaves the runtime alone. An applied upgrade, or a rerun of one already applied, is
        // followed by the runtime step (OPS-05): no runtime of an older release keeps serving the estate on this machine.
        RuntimeReplacement? replacement = check ? null : ReplaceOlderRuntime(root, upgraded.Release);
        if (json)
        {
            JsonObject output = JsonSerializer.SerializeToNode(upgraded, ProtocolJson.Options)!.AsObject();
            if (replacement is not null)
            {
                output["runtime"] = JsonSerializer.SerializeToNode(new
                {
                    outcome = replacement.Outcome,
                    served = replacement.Served is { } served ? new { processId = served.ProcessId, release = served.Release } : null,
                    started = replacement.Started is { } started ? new { processId = started.ProcessId, release = started.Release } : null,
                    done = replacement.Done,
                    warning = replacement.Warning,
                }, ProtocolJson.Options);
            }

            context.Output.WriteLine(output.ToJsonString(ProtocolJson.Options));
        }
        else if (replacement?.Done is { } done)
        {
            context.Output.WriteLine("runtime: " + done);
        }

        if (replacement?.Warning is { } warning)
        {
            context.Error.WriteLine("warning: " + warning);
        }

        return code;
    }

    // The source commit of the release record or dependency observation an upgrade pinned, read from the records once no runtime
    // serves the estate; null when it names none or cannot be read.
    private static string? PinnedCommit(string root, string release)
    {
        try
        {
            using EstateSession session = EstateSession.Open(root, readOnly: true);
            if (!session.Load().ById.TryGetValue(release, out RecordEnvelope? record) || record.Body is not { ValueKind: JsonValueKind.Object } body)
            {
                return null;
            }

            if (body.TryGetProperty("facts", out JsonElement facts) && facts.ValueKind == JsonValueKind.Object)
            {
                body = facts;
            }

            return body.TryGetProperty("commit", out JsonElement commit) && commit.ValueKind == JsonValueKind.String ? commit.GetString() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private RuntimeReplacement ReplaceOlderRuntime(string root, string release)
    {
        try
        {
            return new DaemonLifecycle(context).ReplaceOlderRuntime(root, () => PinnedCommit(root, release));
        }
        catch (Exception exception) when (!context.CancellationToken.IsCancellationRequested
            && exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or JsonException)
        {
            return new("unchecked", null, null, null, $"the runtime serving this estate on this machine could not be checked ({ServingRuntime.Reason(exception)}). The upgrade is applied. See 'vericairn --estate {ServingRuntime.Quote(Path.GetFullPath(root))} runtime status'.");
        }
    }

    public int Reconcile(string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<ReconcileTask> tasks = api.Reconcile();
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(tasks.Select(t => new { t.Code, t.Location, t.Message })));
        }
        else if (tasks.Count == 0)
        {
            context.Output.WriteLine("Nothing to reconcile.");
        }
        else
        {
            foreach (ReconcileTask task in tasks)
            {
                context.Output.WriteLine($"{task.Code}  {task.Location}: {task.Message}");
            }

            // A batch stopped part-way says in its own words what settles it; divergent revisions are settled by a reconciliation.
            if (tasks.Any(t => t.Code != "batch.incomplete"))
            {
                context.Output.WriteLine("Settle divergent revisions with a reconciliation record (kept, withdrawn, note); nothing is merged silently and nothing is deleted.");
            }
        }

        return tasks.Count == 0 ? Clean : HasErrors;
    });

    private static FindingList Combined(EstateModel model)
    {
        var all = new FindingList();
        foreach (Finding finding in model.Findings.Concat(EstateChecks.Run(model, CommandProviders.SourceAdapter?.IgnoredEstateEntries(model.Root))))
        {
            all.Add(finding);
        }

        ReleasePinWarning.Add(model, all, new ReleaseUpdateStore().CachedRelease(), VersionText());

        return all;
    }
}
