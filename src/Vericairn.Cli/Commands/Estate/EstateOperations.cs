// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Store;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class EstateOperations(CommandContext context)
{
    public int Init(string root, string name, string slug, string purpose, string operatorName, string organization, string? github, string repository, string? commit, string profile, bool json,
        bool release = false, string? from = null, string? buildIdentity = null)
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
            initialization = initialization with { Release = verified };
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

            if (json)
            {
                context.Output.WriteLine(releaseRecord is null
                    ? JsonSerializer.Serialize(new { @operator = operatorRecord.Id, estate = estateRecord.Id, manifest = EstateLayout.ManifestPath })
                    : JsonSerializer.Serialize(new
                    {
                        @operator = operatorRecord.Id,
                        estate = estateRecord.Id,
                        manifest = EstateLayout.ManifestPath,
                        release = new { path = releaseRecord.ExpectedPath, id = releaseRecord.Id, hash = releaseRecord.ComputedHash },
                    }));
            }
            else
            {
                context.Output.WriteLine($"operator  {operatorRecord.Id}  {operatorRecord.ExpectedPath}");
                context.Output.WriteLine($"estate    {estateRecord.Id}  {estateRecord.ExpectedPath}");
                context.Output.WriteLine($"manifest  {EstateLayout.ManifestPath}");
                if (releaseRecord is not null)
                    context.Output.WriteLine($"release   {releaseRecord.Id}  {releaseRecord.ExpectedPath}  {releaseRecord.ComputedHash}");
            }

            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int Check(string root, bool json) =>
        IsEstate(root) ? context.WithApi(root, api => context.ReportFindings(api.Check(), json)) : context.WithFiles(root, model => context.ReportFindings(ToFindings(Combined(model), $"{model.Records.Count} records"), json));

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

    public int Upgrade(string root, string version, string? by, bool check, bool json, string? from = null, string? buildIdentity = null) => context.WithApi(root, api =>
    {
        (UpgradeResponse? plan, Problem? problem) = api.Upgrade(new UpgradeRequest(version, by, check) { From = from is null ? null : Path.GetFullPath(from), ClientBuild = buildIdentity ?? VersionText() });
        if (plan is null)
        {
            if (problem is not null && problem.Findings.Count > 0)
            {
                context.Error.WriteLine(problem.Message);
                return context.ReportFindings(new FindingsResponse("not upgraded", problem.Findings.Count(f => f.Severity == "error"), problem.Findings.Count(f => f.Severity == "warning"), problem.Findings), json);
            }

            return context.Fail(problem?.Message ?? "refused");
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(plan, ProtocolJson.Options));
        }
        else
        {
            context.Output.WriteLine($"{(plan.Applied ? "upgraded" : "would upgrade")} {plan.FromPin} -> {plan.ToPin}; record format {plan.FromFormat} -> {plan.ToFormat}; migrations: {(plan.Migrations.Count == 0 ? "none" : string.Join(", ", plan.Migrations))}; release {plan.Release}");
            foreach ((string platform, string hash) in plan.Artifacts.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                context.Output.WriteLine($"  {platform}  {hash}");
            }

            if (plan.Anchored is { Count: > 0 } anchored)
            {
                context.Output.WriteLine($"{(plan.Applied ? "anchored" : "anchors")} the admissions recorded without a signature, which count after the move only because the signed migration lists them: {string.Join(", ", anchored)}");
            }

            if (plan.Migration is not null)
            {
                context.Output.WriteLine($"{plan.Migration.Path}  {plan.Migration.Id}  {plan.Migration.Hash}");
            }
        }

        return Clean;
    }, readOnly: check, upgrade: true);

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
