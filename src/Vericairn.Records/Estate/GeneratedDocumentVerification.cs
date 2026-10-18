// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>One immutable file in a generated change plan or a complete observed inventory.</summary>
public sealed record DocumentFile(string Path, ImmutableArray<byte> Bytes, string? InputDigest = null)
{
    /// <summary>The exact bytes' content identity.</summary>
    public string Hash => ContentHash.Sha256(Bytes.AsSpan());
}

/// <summary>A complete inventory of an owned destination, or an explicit unavailable/error outcome.</summary>
public sealed record DocumentFileInventory(DocumentGenerationStatus Status, ImmutableArray<DocumentFile> Files, string? Commit, string? Reason);

/// <summary>A fresh rendition and its comparison. Failures expose no partial expected output.</summary>
public sealed record GeneratedDocumentPlan(DocumentGenerationStatus Status, ImmutableArray<DocumentFile> Expected,
    ImmutableArray<DocumentFile> Actual, string? Commit, ImmutableArray<string> Problems)
{
    /// <summary>Whether every expected member and byte is present, with no extras.</summary>
    public bool Current => Status == DocumentGenerationStatus.Success && Problems.IsEmpty;
}

/// <summary>Mandatory complete-member and byte checks, separate from authored clearance and policy waivers.</summary>
public static class GeneratedDocumentVerification
{
    /// <summary>Reruns a versioned generator on verified inputs and compares every owned member at one source basis.</summary>
    public static GeneratedDocumentPlan Plan(EstateModel model, RecordEnvelope declaration, string? commit = null)
        => Plan(model, declaration, commit, new HashSet<string>(StringComparer.Ordinal));

    private static GeneratedDocumentPlan Plan(EstateModel model, RecordEnvelope declaration, string? commit, HashSet<string> visiting)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);
        try
        {
            if (!visiting.Add(declaration.Id)) return Error("The declared source graph contains a generation cycle.");
            if (DocumentChecks.Generator(declaration) is not (string id, _) || DocumentGenerators.Find(id) is not { Descriptor: { } descriptor } generator)
                return Unavailable("The versioned generator is unavailable.");
            bool elsewhere = EstateModel.BodyString(declaration, "repository") is not null;
            IDocumentSource? source = DocumentSources.Find(EstateModel.BodyString(declaration, "source"));
            if (elsewhere && (commit is null || source?.SnapshotVersion != DocumentGeneration.InterfaceVersion))
                return Unavailable("The exact repository commit or versioned source is unavailable.");
            DocumentSourceSelection selection = elsewhere ? source!.ReadSelection(model, declaration, commit!, descriptor)
                : DocumentSourceSelection.Records(model, descriptor, commit);
            if (elsewhere && selection.Commit != commit) return Unavailable("The source did not read the requested exact commit.");
            DocumentMemberSelection members = DocumentGeneration.Members(declaration, selection);
            if (members.Status != DocumentGenerationStatus.Success) return new(members.Status, [], [], members.Commit, [members.Reason ?? "Generation is unavailable."]);
            foreach (DocumentSourceSnapshot input in selection.Sources.Where(s => s.Kind != "record" && s.Path is not null))
            {
                foreach (RecordEnvelope owner in model.Current("living-document").Where(d =>
                    EstateModel.BodyString(d, "repository") == input.Repository && GeneratedDocumentContract.Owns(d, input.Path!)
                    && GeneratedDocumentContract.IsStrict(model, d)))
                {
                    if (visiting.Contains(owner.Id)) return Error("The declared source graph contains a generation cycle.");
                    GeneratedDocumentPlan dependency = Plan(model, owner, commit, visiting);
                    if (!dependency.Current || !dependency.Expected.Any(f => f.Path == input.Path && f.Hash == input.Hash))
                        return new(dependency.Status == DocumentGenerationStatus.Success ? DocumentGenerationStatus.Error : dependency.Status,
                            [], [], commit, ["A generated source dependency is not verified at this exact basis."]);
                }
            }

            var expected = ImmutableArray.CreateBuilder<DocumentFile>();
            foreach (DocumentMember member in members.Members)
            {
                DocumentRendition result = DocumentRendering.Render(generator, member.Context);
                if (result.Status != DocumentGenerationStatus.Success)
                    return new(result.Status, [], [], members.Commit,
                        [result.Reason ?? "The generator returned no complete output."]);
                expected.Add(new(member.Path, result.Bytes, result.InputDigest));
            }

            DocumentFileInventory actual = elsewhere ? source!.ReadInventory(model, declaration, commit!) : ReadInventory(model.Root, declaration);
            if (actual.Status != DocumentGenerationStatus.Success) return new(actual.Status, [], [], actual.Commit ?? members.Commit, [actual.Reason ?? "The owned inventory is unavailable."]);
            if (elsewhere && actual.Commit != commit) return Unavailable("The member inventory is not from the requested exact commit.");
            if (actual.Files.Any(f => !GeneratedDocumentContract.Owns(declaration, f.Path))
                || actual.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Files.Length)
                return Error("The inventory contains an unowned path or a portable path collision.");
            var observed = actual.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var problems = ImmutableArray.CreateBuilder<string>();
            foreach (DocumentFile file in expected)
            {
                if (!observed.Remove(file.Path, out DocumentFile? present)) problems.Add("Missing generated member: " + file.Path);
                else if (!file.Bytes.AsSpan().SequenceEqual(present.Bytes.AsSpan())) problems.Add("Generated bytes differ: " + file.Path);
            }
            foreach (string extra in observed.Keys.Order(StringComparer.Ordinal)) problems.Add("Unexpected generated member: " + extra);
            return new(DocumentGenerationStatus.Success, expected.ToImmutable(), actual.Files, members.Commit, problems.ToImmutable());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unavailable("The required source or owned output could not be read safely.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return Error("The generator or source violated its declared contract.");
        }
        finally { visiting.Remove(declaration.Id); }

        GeneratedDocumentPlan Unavailable(string reason) => new(DocumentGenerationStatus.Unavailable, [], [], commit, [reason]);
        GeneratedDocumentPlan Error(string reason) => new(DocumentGenerationStatus.Error, [], [], commit, [reason]);
    }

    /// <summary>Reads every owned file without following symbolic links or accepting case aliases.</summary>
    public static DocumentFileInventory ReadInventory(string root, RecordEnvelope declaration)
    {
        string? pattern = EstateModel.BodyString(declaration, "pathPattern");
        string? destination = pattern is null ? EstateModel.BodyString(declaration, "path") : DocumentPatterns.Prefix(pattern).TrimEnd('/');
        if (destination is null) return new(DocumentGenerationStatus.Error, [], null, "The declaration has no destination.");
        var files = ImmutableArray.CreateBuilder<DocumentFile>();
        string target = SafeFile(root, destination);
        if (pattern is null)
        {
            if (Directory.Exists(target)) return new(DocumentGenerationStatus.Error, [], null, "A file destination is occupied by a directory.");
            if (File.Exists(target)) Read(target, destination);
        }
        else if (File.Exists(target)) return new(DocumentGenerationStatus.Error, [], null, "A register directory is occupied by a file.");
        else if (Directory.Exists(target)) Walk(target);
        return new(DocumentGenerationStatus.Success, [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)], null, null);

        void Read(string full, string relative)
        {
            if (new FileInfo(full).Length > 1048576) throw new InvalidOperationException("An owned file exceeds the repository size bound.");
            files.Add(new(relative, [.. File.ReadAllBytes(full)]));
        }

        void Walk(string directory)
        {
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
                string full = SafeFile(root, relative);
                if (entry is DirectoryInfo) Walk(full);
                else Read(full, relative);
            }
        }
    }

    /// <summary>Resolves a portable repository-relative destination, refusing all existing symlinks and case aliases.</summary>
    public static string SafeFile(string root, string path)
    {
        if (!DocumentPatterns.IsSafePath(path) || path.Split('/')[0] is "records" or "objects")
            throw new InvalidOperationException("An output path is not a safe document destination.");
        string current = Path.GetFullPath(root);
        if (new DirectoryInfo(current).LinkTarget is not null) throw new InvalidOperationException("A redirected output root is not permitted.");
        foreach (string part in path.Split('/'))
        {
            if (Directory.Exists(current))
            {
                FileSystemInfo[] aliases = new DirectoryInfo(current).EnumerateFileSystemInfos().Where(e => string.Equals(e.Name, part, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (aliases.Any(e => e.Name != part || e.LinkTarget is not null)) throw new InvalidOperationException("A destination has a case alias or symbolic link.");
            }
            current = Path.Combine(current, part);
            if (new FileInfo(current).LinkTarget is not null) throw new InvalidOperationException("A destination has a symbolic link.");
        }
        return current;
    }
}
