// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Upgrade;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The way back from an upgrade (FIX-145): what an upgrade says, before it is applied, about the previous release reading the estate after it,
/// and the steps that return to that release from the restore point an applied upgrade records. The steps name this machine's paths; they
/// are the response's, never a record's.
/// </summary>
public sealed partial class EstateService
{
    private UpgradeReturn ReturnOf(UpgradeReadability readability, UpgradePlan plan, RestorePoint? restore)
    {
        string root = session.Root;
        bool synced = session.Manifest.Profile == "repository-synced";
        string clone = Path.GetDirectoryName(session.Live.Path)!;
        string live = Path.GetFileName(session.Live.Path);
        bool release = session.Manifest.Mode == "release";
        var steps = new List<string>();
        if (restore is null)
        {
            // From 0.9.5 (FIX-174) a repository-synced clone that only read-only commands have opened keeps no live store on disk, and its
            // restore point holds none, so the plan names the live store only while there is one to export.
            string liveStore = File.Exists(session.Live.Path) ? ", its live store included"
                : synced ? $", which holds no live store, since this machine keeps none for the clone; {plan.FromPin} builds it again from the repository's records" : string.Empty;
            steps.Add($"Applying records a restore point before it writes anything: {(synced ? "the estate repository's checked-out commit" : "no repository commit, since a local-only estate's records are in its live store")} and an export of this machine's local state for this clone{liveStore}, in a new directory under {Path.Combine(clone, UpgradeReturns.RestorePointsDirectory)}.");
            steps.Add($"From it, the way back to {plan.FromPin} is: stop the runtime serving the estate here, return the estate's files to that commit, put the exported local state back, and run {plan.FromPin}; the applied upgrade names each step with its paths.");
        }
        else
        {
            // From 0.9.5 (FIX-174) a repository-synced clone that kept no live store before the upgrade has none in its restore point.
            bool kept = restore.Files.Contains(live, StringComparer.Ordinal);
            steps.Add($"Stop the runtime serving the estate on this machine: vericairn --estate {Quote(root)} runtime stop.");
            if (restore.Commit is { } commit)
                // Git for Windows converts line endings on checkout by default; with conversion off, every file returns with its exact bytes,
                // whatever the clone's configuration, as the estate's .gitattributes (eol=lf) already keeps them on a clone estate init made.
                steps.Add($"Return the estate's files to commit {commit}, with their exact bytes: before the upgrade is committed, git -C {Quote(root)} -c core.autocrlf=false stash --include-untracked; once it is committed, git -C {Quote(root)} -c core.autocrlf=false revert the commit that holds it. Either way {EstateLayout.ManifestPath} pins {plan.FromPin} again, and no line ending changes."
                    + (restore.Clean ? string.Empty : kept
                        ? " The working tree had uncommitted changes beside that commit; the export holds the live store with them, but the commit does not."
                        : " The working tree had uncommitted changes beside that commit, and neither the commit nor the restore point holds them, since this machine kept no live store for the clone: keep a copy of them before you return."));
            else
                steps.Add(synced
                    ? $"The estate's repository could not be read when the restore point was made, so it names no commit: return {EstateLayout.ManifestPath} and the records to their state before the upgrade from your own history of the repository."
                    : "The estate's records are in its live store, which the next step puts back with everything else.");
            // From 0.9.5 (FIX-174) every database is exported as one file, so a -wal or -shm the upgraded runtime left beside the live store
            // would be read with the copy put back, and is removed first; a repository-synced clone that kept no live store before the
            // upgrade returns to none, and the previous release builds its own.
            var removed = new List<string>();
            if (!kept) removed.Add(live);
            foreach (string database in UpgradeReturns.Databases(restore).Where(d => d != live).Prepend(live))
            {
                removed.Add(database + "-wal");
                removed.Add(database + "-shm");
            }

            steps.Add($"Put back this machine's local state from before the upgrade: with no runtime serving the estate, copy every file in {Quote(restore.StateDirectory)} into {Quote(clone)}, first removing {Listed(removed)} there."
                + (kept ? string.Empty : $" This machine kept no live store for the clone before the upgrade, so none is put back; {plan.FromPin} builds it again from the repository's records."));
            steps.Add(release
                ? $"Run the previous release: in the estate's directory, ./bootstrap runtime start (.\\bootstrap.ps1 runtime start on Windows) fetches and runs the release the manifest pins, {plan.FromPin}."
                : $"Run the previous build: {plan.FromPin}, the source commit the manifest pins.");
        }

        steps.Add(readability.Readability switch
        {
            UpgradeReadability.Readable => $"{plan.FromPin} reads the estate as the upgrade leaves it, so a record written after the upgrade can be carried back when it uses nothing the new release added.",
            UpgradeReadability.OneWay => $"This upgrade is one-way: {plan.FromPin} cannot read the estate after it ({string.Join("; ", readability.Reasons)}), so every record written after the upgrade stays behind on the way back.",
            _ => $"Whether {plan.FromPin} reads the estate after the upgrade cannot be confirmed ({string.Join("; ", readability.Reasons)}); carry nothing written after the upgrade back without checking it with that build.",
        });
        UpgradeRestorePoint? point = restore is null ? null
            : new(restore.Id, restore.CreatedAt, restore.Commit, restore.Clean, restore.Directory, restore.StateHash, restore.Files);
        return new UpgradeReturn(plan.FromPin, readability.Readability, readability.Reasons, steps) { RestorePoint = point };
    }

    // The estate repository's checked-out commit and whether its working tree is clean, read without changing it; null when unreadable.
    private RepositoryObservation? RestoreRepository(string root)
    {
        try
        {
            return Providers.Source("git") is { } git && git.IsRepository(root) ? git.Inspect(root) : null;
        }
        catch (ProviderRefusedException)
        {
            return null;
        }
    }

    private static string Quote(string path) => path.Contains(' ', StringComparison.Ordinal) ? "\"" + path + "\"" : path;

    // "a", "a and b", "a, b and c".
    private static string Listed(List<string> names) => names.Count <= 1 ? string.Concat(names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
}
