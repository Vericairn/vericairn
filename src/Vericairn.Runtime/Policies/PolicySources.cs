// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Reflection;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Policies;

// Sources are installed by application composition, never supplied as client verdicts or loaded by a declaration.
internal interface IPolicyRepositorySource
{
    PolicyRepository Read(EstateModel model, RecordEnvelope repository, string? commit);

    PolicyTreeComparison CompareTrees(EstateModel model, RecordEnvelope repository, string selectedCommit, PolicyTreeRequest request) =>
        new(request.SourceCommit, selectedCommit, request.Paths, false);
}

internal static class PolicySources
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string, int), PolicyKindRegistration> Kinds = [];
    internal static IPolicyRepositorySource? RepositorySource { get; set; }
    private static readonly Lazy<string> FoundationDigest = new(() => Digest(typeof(PolicySources).Assembly));

    // An installed implementation is named by its assembly and its deterministic build's module version identifier: the same in a
    // single-file bundle as on disk, where a single-file application's assemblies have no file path to read.
    internal static string Digest(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes("vericairn.policy-implementation/1|" + assembly.GetName().Name + "|" + assembly.ManifestModule.ModuleVersionId.ToString("N")));
    }

    internal static void Register(PolicyKindRegistration kind)
    {
        lock (Gate) Kinds[(kind.Kind, kind.Version)] = kind;
    }

    internal static PolicyKindRegistry Registry()
    {
        lock (Gate) return new(PolicyKindRegistry.Foundation(FoundationDigest.Value).Registrations.Concat(Kinds.Values));
    }

    // Installed kinds select observations here, before the immutable evaluation basis is constructed.
    internal static PolicyRepository ObserveTrees(EstateModel model, PolicyKindRegistry registry, PolicySubmission? submission,
        RecordEnvelope resource, PolicyRepository repository)
    {
        var requests = new SortedDictionary<string, PolicyTreeRequest>(StringComparer.Ordinal);
        foreach (RecordEnvelope policy in model.Current("estate-policy"))
        foreach (System.Text.Json.JsonElement rule in PolicyInputs.Items(PolicyInputs.Get(policy.Body, "rules")))
        {
            PolicyKindRegistration[] kinds = registry.Registrations.Where(k => k.Kind == PolicyInputs.Text(rule, "kind") &&
                PolicyInputs.Get(rule, "version").ValueKind == System.Text.Json.JsonValueKind.Number &&
                PolicyInputs.Get(rule, "version").TryGetInt32(out int version) && k.Version == version).ToArray();
            if (kinds.Length != 1 || kinds[0].RepositoryTrees is not { } select) continue;
            try
            {
                foreach (PolicyTreeRequest request in select(model, submission, PolicyInputs.Get(rule, "parameters"), repository))
                    requests.TryAdd(PolicyBasis.Canonical(request), request);
            }
            catch (Exception error) when (error is PolicyInputException or PolicyParameterException)
            {
                // The evaluator reports the malformed or unavailable input; it never gets a partial comparison.
            }
        }
        return repository with { TreeComparisons = [.. requests.Values.Select(request => RepositorySource?.CompareTrees(model, resource, repository.Revision, request)
            ?? new(request.SourceCommit, repository.Revision, request.Paths, false))] };
    }

    internal static PolicyRepository Estate(EstateModel model)
    {
        List<PolicyFile> files = [];
        bool complete = true;
        foreach (string directory in new[] { "design", "docs" }) Read(directory);
        return new(model.CurrentEstate is { } estate ? PolicyWork.Identity(model, estate) : model.Manifest?.Estate ?? "estate",
            "working-tree:" + PolicyBasis.Hash(files.Select(f => new { f.Path, f.Hash, f.Available, f.IsLink })), PolicyBasis.RepositoryContract, complete, [.. files]);

        void Read(string relative)
        {
            string full = Path.Combine(model.Root, relative);
            try
            {
                if (!Directory.Exists(full)) return;
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) { complete = false; return; }
                foreach (string path in Directory.EnumerateFileSystemEntries(full).Order(StringComparer.Ordinal))
                {
                    string name = relative + "/" + Path.GetFileName(path);
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { complete = false; continue; }
                    if ((attributes & FileAttributes.Directory) != 0) { Read(name); continue; }
                    if (!name.EndsWith(".md", StringComparison.Ordinal)) continue;
                    byte[] bytes = File.ReadAllBytes(path);
                    files.Add(new(name, ContentHash.Sha256(bytes), [.. bytes]));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { complete = false; }
        }
    }
}
