// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Templates;

namespace Vericairn.Runtime.Store;

/// <summary>Publishes already-rendered bytes with guarded atomic file replacement; the owning batch journal retains interrupted work.</summary>
internal static class GeneratedOutputStore
{
    private static readonly string[] StagingParts = [".vericairn", "document-staging"];

    internal static bool Recover(EstateSession session, BatchJournal journal, EstateModel model)
    {
        try
        {
            if (journal.Records.Any(r => !model.ById.ContainsKey(r.Id))) return false;
            if (Complete(session.Root, journal)) { journal.Close(); return true; }
            foreach (DocumentOutputChange change in journal.Outputs)
            {
                if (change.After is not null)
                {
                    RecordEnvelope? owner = model.Current("living-document").SingleOrDefault(d => EstateModel.BodyString(d, "repository") is null
                        && !GeneratedDocumentContract.IsRetired(model, d) && GeneratedDocumentContract.Owns(d, change.Path));
                    if (owner is null) return false;
                    if (GeneratedDocumentContract.IsStrict(model, owner))
                    {
                        GeneratedDocumentPlan fresh = GeneratedDocumentVerification.Plan(model, owner);
                        if (fresh.Status != DocumentGenerationStatus.Success || !fresh.Expected.Any(f => f.Path == change.Path && f.Hash == change.AfterHash)) return false;
                    }
                    else if (TemplateCatalog.Pin(owner) is not null && owner.Supersedes is null && change.BeforeHash is null
                        && EstateModel.BodyString(owner, "path") == change.Path && journal.Records.Any(r => r.Id == owner.Id))
                    {
                        // Initial authored content is recoverable only from the exact new instance in this batch.
                        // Recompute its scaffold, quotes and generated sections before accepting the retained bytes.
                        string initial = TemplateDocuments.NewContent(model, owner);
                        if (ContentHash.Sha256(Encoding.UTF8.GetBytes(initial)) != change.AfterHash) return false;
                    }
                    else
                    {
                        DocumentChangePlan repair = DocumentRepair.Prepare(model, owner.Id);
                        string full = GeneratedDocumentVerification.SafeFile(session.Root, change.Path);
                        if (repair.Status != DocumentGenerationStatus.Success || (!repair.Changes.Any(c => c.Path == change.Path && c.AfterHash == change.AfterHash)
                            && (!File.Exists(full) || ContentHash.Sha256File(full) != change.AfterHash))) return false;
                    }
                }
                else
                {
                    RecordEnvelope? former = model.OfType("living-document").FirstOrDefault(d => EstateModel.BodyString(d, "repository") is null
                        && GeneratedDocumentContract.Owns(d, change.Path) && CanRemove(model, d, change.Path));
                    if (former is null) return false;
                    if (GeneratedDocumentContract.IsStrict(model, former))
                    {
                        GeneratedDocumentPlan proof = GeneratedDocumentVerification.FormerMembers(model, former);
                        if (proof.Status != DocumentGenerationStatus.Success || !proof.Expected.Any(f => f.Path == change.Path && f.Hash == change.BeforeHash)
                            || proof.Actual.Any(f => !proof.Expected.Any(e => e.Path == f.Path))) return false;
                    }
                    else if (!model.OfType("document-revision").Any(p => EstateModel.BodyString(p, "path") == change.Path
                        && EstateModel.BodyString(p, "hash") == change.BeforeHash)) return false;
                }
            }
            if (journal.RetiredDirectories.Any(path => !model.OfType("living-document").Any(d => GeneratedDocumentContract.RetirementPath(d) == path
                && CanRemove(model, d, path + "/")))) return false;
            Apply(session.Root, journal.Outputs, journal.RetiredDirectories);
            if (!Complete(session.Root, journal)) return false;
            journal.Close();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { return false; }
    }

    private static bool CanRemove(EstateModel model, RecordEnvelope declaration, string path) => GeneratedDocumentContract.IsRetired(model, declaration)
        || (model.Latest(declaration) is { } successor && successor.Id != declaration.Id && GeneratedDocumentContract.HasReviewedMigration(model, successor)
            && !GeneratedDocumentContract.Owns(successor, path));

    internal static void Validate(string root, IReadOnlyList<DocumentOutputChange> changes)
    {
        foreach (DocumentOutputChange change in changes)
        {
            string full = GeneratedDocumentVerification.SafeFile(root, change.Path);
            if (Directory.Exists(full)) throw new InvalidOperationException("An output destination is occupied by a directory.");
            string? hash = File.Exists(full) ? ContentHash.Sha256File(full) : null;
            if (hash != change.BeforeHash && hash != change.AfterHash)
                throw new InvalidOperationException("An output changed after preparation; preserve it and prepare again.");
        }
    }

    internal static void Apply(string root, IReadOnlyList<DocumentOutputChange> changes, IReadOnlyList<string> retiredDirectories, Action<string>? beforePublish = null)
    {
        Validate(root, changes);
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (changes.Any(c => c.After is not null))
            {
                string staging = Staging(root);
                foreach (DocumentOutputChange change in changes.Where(c => c.After is not null))
                {
                    string temporary = Path.Combine(staging, Ulid.NewUlid().Value + ".tmp");
                    staged.Add(change.Path, temporary);
                    File.WriteAllBytes(temporary, change.After!.Value.ToArray());
                }
            }
            // Stage the entire rendition before the first destination changes. Recovery accepts either side of each atomic rename.
            foreach (DocumentOutputChange change in changes)
            {
                beforePublish?.Invoke(change.Path);
                Validate(root, [change]);
                string full = GeneratedDocumentVerification.SafeFile(root, change.Path);
                string? hash = File.Exists(full) ? ContentHash.Sha256File(full) : null;
                if (hash == change.AfterHash) continue;
                if (change.After is null) File.Delete(full);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    GeneratedDocumentVerification.SafeFile(root, change.Path);
                    File.Move(staged[change.Path], full, overwrite: true);
                }
            }
            foreach (string directory in retiredDirectories) RemoveEmpty(root, directory);
        }
        finally
        {
            foreach (string temporary in staged.Values)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* The journal retains all intended bytes. */ }
            }
        }
    }

    internal static bool Complete(string root, BatchJournal journal)
    {
        try
        {
            return journal.Outputs.All(c =>
            {
                string full = GeneratedDocumentVerification.SafeFile(root, c.Path);
                return !Directory.Exists(full) && (File.Exists(full) ? ContentHash.Sha256File(full) : null) == c.AfterHash;
            }) && journal.RetiredDirectories.All(d => !Directory.Exists(GeneratedDocumentVerification.SafeFile(root, d)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    private static void RemoveEmpty(string root, string path)
    {
        string full = GeneratedDocumentVerification.SafeFile(root, path);
        if (File.Exists(full)) throw new InvalidOperationException("A retired directory is occupied by a file.");
        if (!Directory.Exists(full)) return;
        foreach (string child in Directory.EnumerateDirectories(full).Order(StringComparer.Ordinal))
            RemoveEmpty(root, Path.GetRelativePath(root, child).Replace('\\', '/'));
        if (Directory.EnumerateFileSystemEntries(full).Any()) throw new InvalidOperationException("A retired directory contains unplanned content; nothing there is pruned.");
        Directory.Delete(full);
    }

    private static string Staging(string root)
    {
        string parent = root;
        foreach (string part in StagingParts)
        {
            if (new DirectoryInfo(parent).EnumerateFileSystemInfos().Any(e => string.Equals(e.Name, part, StringComparison.OrdinalIgnoreCase)
                && (e.Name != part || e.LinkTarget is not null))) throw new InvalidOperationException("Output staging is redirected.");
            parent = Path.Combine(parent, part);
            Directory.CreateDirectory(parent);
        }
        string ignore = Path.Combine(parent, ".gitignore");
        if (!File.Exists(ignore)) File.WriteAllText(ignore, "*\n");
        return parent;
    }
}
