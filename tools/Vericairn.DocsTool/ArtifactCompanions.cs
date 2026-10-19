// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.DocsTool;

internal static partial class ReferenceSet
{
    private const string ArtifactInventory = "inputs/artifacts/inventory.json";
    private const string PayloadChecksums = "inputs/artifacts/SHA256SUMS";
    private const string FrozenIndex = "inputs/frozen-sha256.json";
    private const string ProductDocuments = "inputs/product-documents.json";
    private const string SetManifest = "inputs/documentation-manifest.json";
    private const string WorkflowRoot = "inputs/workflows/";
    private const string WorkflowInput = WorkflowRoot + "workflow-inputs.json";
    private static string SbomPath => "inputs/artifacts/vericairn-" + ReleaseVersion + "-sbom.cdx.json";
    private static string CompanionArchive => "vericairn-" + ReleaseVersion + "-companions.zip";

    private static void WriteArtifactCompanions(string directory, string artifacts, JsonObject companions)
    {
        Dictionary<string, byte[]> retained = CaptureArtifacts(artifacts, Commit!);
        foreach ((string path, byte[] bytes) in retained) WriteBytes(directory, path, bytes);
        companions["scope"] = "release";
        companions["artifactInputs"] = Inputs(retained);
        foreach ((string name, string source, string[] paths) in ArtifactReaders(retained.Keys))
        {
            var selected = paths.ToDictionary(p => p, p => retained[p], StringComparer.Ordinal);
            DocumentRendition rendition = Render(name, source, selected);
            WriteBytes(directory, name + ".md", rendition.Bytes.ToArray());
            companions["outputs"]!.AsArray().Add(Output(name, source, rendition, paths));
        }
    }

    private static Dictionary<string, byte[]> CaptureArtifacts(string artifacts, string commit)
    {
        var retained = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var observations = new JsonArray();
        var checksums = new StringBuilder();
        foreach (string full in Directory.EnumerateFiles(artifacts).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(full);
            if (name == CompanionArchive || name is "SHA256SUMS" or "NOTES.md") continue;
            SafeDestination(artifacts, name);
            string hash = ContentHash.Sha256File(full);
            observations.Add(new JsonObject { ["path"] = name, ["bytes"] = new FileInfo(full).Length, ["hash"] = hash });
            checksums.Append(hash[7..]).Append("  ").Append(name).Append('\n');
        }
        retained[ArtifactInventory] = JsonBytes(new { format = "vericairn.artifact-inputs/1", commit, artifacts = observations });
        retained[PayloadChecksums] = Encoding.UTF8.GetBytes(checksums.ToString());
        retained[SbomPath] = ReadBytes(artifacts, Path.GetFileName(SbomPath));
        retained[FrozenIndex] = GitBytes(commit, "docs/releases/frozen-sha256.json");
        retained[ProductDocuments] = GitBytes(commit, "docs/releases/documents.json");
        retained[SetManifest] = GitBytes(commit, "docs/releases/" + ReleaseVersion + "/manifest.json");
        foreach (string path in GitPaths(commit).Where(p => (p.StartsWith("src/", StringComparison.Ordinal) || p.StartsWith("tools/", StringComparison.Ordinal)) && p.EndsWith("packages.lock.json", StringComparison.Ordinal)))
            retained["inputs/locks/" + path] = GitBytes(commit, path);
        foreach (string path in WorkflowPaths(commit)) retained[WorkflowRoot + path] = GitBytes(commit, path);
        retained[WorkflowInput] = CaptureWorkflow(retained, commit);
        return retained;
    }

    private static IEnumerable<(string Name, string Source, string[] Paths)> ArtifactReaders(IEnumerable<string> inputs)
    {
        string[] paths = inputs.ToArray();
        yield return ("frozen-index", FrozenIndex, [FrozenIndex]);
        yield return ("workflow-actions", WorkflowInput, paths.Where(p => p.StartsWith(WorkflowRoot, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray());
        yield return ("components", SbomPath, [SbomPath, .. paths.Where(p => p.StartsWith("inputs/locks/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)]);
        yield return ("checksums", PayloadChecksums, [PayloadChecksums, ArtifactInventory]);
        yield return ("documentation-contents", SetManifest, [SetManifest, ProductDocuments]);
    }

    private static string[] CompareArtifactCompanions(string directory, JsonElement declaration, JsonArray outputs, string commit, string? artifacts)
    {
        var retained = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (JsonElement item in declaration.GetProperty("artifactInputs").EnumerateArray())
        {
            string path = Str(item, "path") ?? "";
            if (!path.StartsWith("inputs/", StringComparison.Ordinal) || !retained.TryAdd(path, ReadBytes(directory, path)))
                throw new InvalidOperationException("The retained artifact input inventory is unsafe or duplicated.");
        }
        CheckInputInventory(retained, declaration.GetProperty("artifactInputs"));
        var expectedPaths = new HashSet<string>(StringComparer.Ordinal) { ArtifactInventory, PayloadChecksums, SbomPath, FrozenIndex, ProductDocuments, SetManifest, WorkflowInput };
        foreach (string path in GitPaths(commit).Where(p => (p.StartsWith("src/", StringComparison.Ordinal) || p.StartsWith("tools/", StringComparison.Ordinal)) && p.EndsWith("packages.lock.json", StringComparison.Ordinal)))
        {
            string retainedPath = "inputs/locks/" + path;
            expectedPaths.Add(retainedPath);
            Exact(retainedPath, path);
        }
        foreach (string path in WorkflowPaths(commit)) { expectedPaths.Add(WorkflowRoot + path); Exact(WorkflowRoot + path, path); }
        if (!expectedPaths.SetEquals(retained.Keys)) throw new InvalidOperationException("The complete retained artifact input inventory differs from the selected build.");
        Exact(FrozenIndex, "docs/releases/frozen-sha256.json");
        Exact(ProductDocuments, "docs/releases/documents.json");
        Exact(SetManifest, "docs/releases/" + ReleaseVersion + "/manifest.json");
        // Establish the exact originals before invoking the build's pinned parser.
        if (!retained[WorkflowInput].AsSpan().SequenceEqual(CaptureWorkflow(retained, commit)))
            throw new InvalidOperationException("The workflow capture differs from the actual retained parser and sources.");
        if (artifacts is not null)
        {
            Dictionary<string, byte[]> fresh = CaptureArtifacts(artifacts, commit);
            if (!fresh.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(retained.Keys) || fresh.Any(f => !f.Value.AsSpan().SequenceEqual(retained[f.Key])))
                throw new InvalidOperationException("The local payload bytes differ from the retained artifact observations.");
            if (!ReadBytes(artifacts, CompanionArchive).AsSpan().SequenceEqual(PackageBytes(directory)))
                throw new InvalidOperationException("The retained companion archive differs from its exact input and output inventory.");
        }
        foreach ((string name, string source, string[] paths) in ArtifactReaders(retained.Keys))
        {
            DocumentRendition rendition = Render(name, source, paths.ToDictionary(p => p, p => retained[p], StringComparer.Ordinal), commit);
            if (!ReadBytes(directory, name + ".md").AsSpan().SequenceEqual(rendition.Bytes.AsSpan()))
                throw new InvalidOperationException("The retained artifact companion bytes differ: " + name + ".");
            outputs.Add(Output(name, source, rendition, paths, commit));
        }
        return retained.Keys.ToArray();

        void Exact(string retainedPath, string original)
        {
            if (!retained[retainedPath].AsSpan().SequenceEqual(GitBytes(commit, original)))
                throw new InvalidOperationException("A retained source differs from the exact build commit: " + original + ".");
        }
    }

    private static IEnumerable<string> WorkflowPaths(string commit) => GitPaths(commit).Where(p =>
        p is ".github/scripts/check-workflow-actions.rb" or ".github/workflows/actions.allow" ||
        p.StartsWith(".github/workflows/", StringComparison.Ordinal) && p.Count(c => c == '/') == 2 && (p.EndsWith(".yml", StringComparison.Ordinal) || p.EndsWith(".yaml", StringComparison.Ordinal)));

    private static byte[] CaptureWorkflow(Dictionary<string, byte[]> retained, string commit)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "vericairn-workflow-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            if (!retained[WorkflowRoot + ".github/scripts/check-workflow-actions.rb"].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "check-workflow-actions.rb"))))
                throw new InvalidOperationException("The exact retained workflow parser differs from this build's parser.");
            foreach ((string path, byte[] bytes) in retained.Where(f => f.Key.StartsWith(WorkflowRoot + ".github/", StringComparison.Ordinal)))
                WriteBytes(scratch, path[WorkflowRoot.Length..], bytes);
            string adapter = Path.Combine(AppContext.BaseDirectory, "workflow-inputs.rb");
            // The build runs its own adapter and FIX-13's exact retained implementation; no source-supplied command is accepted.
            var start = new ProcessStartInfo("ruby") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(adapter); start.ArgumentList.Add(scratch); start.ArgumentList.Add(commit);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The workflow source adapter is unavailable.");
            using var output = new MemoryStream();
            Task copy = CopyBounded(process.StandardOutput.BaseStream, output, 1048576);
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { process.Kill(true); throw new InvalidOperationException("The workflow source adapter did not complete."); }
            copy.GetAwaiter().GetResult(); _ = error.Result;
            if (process.ExitCode != 0 || output.Length > 1048576) throw new InvalidOperationException("The actual workflow parser refused the retained inputs.");
            return output.ToArray();
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static string[] GitPaths(string commit)
    {
        byte[] bytes = GitRead("ls-tree", "-r", "-z", "--name-only", commit);
        string[] paths = new UTF8Encoding(false, true).GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length > 100000) throw new InvalidOperationException("The build source inventory exceeds its bound.");
        return paths;
    }
    private static byte[] GitBytes(string commit, string path)
    {
        if (!DocumentPatterns.IsSafeSourcePath(path)) throw new InvalidOperationException("A retained source path is unsafe.");
        string entry = Encoding.UTF8.GetString(GitRead("ls-tree", "-z", commit, "--", path));
        if (!entry.StartsWith("100644 blob ", StringComparison.Ordinal) && !entry.StartsWith("100755 blob ", StringComparison.Ordinal))
            throw new InvalidOperationException("An exact build input is not a regular file: " + path + ".");
        return GitRead("cat-file", "blob", commit + ":" + path);
    }
    private static byte[] GitRead(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = BuildRoot };
        start.ArgumentList.Add("--no-replace-objects");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The exact build input is unavailable.");
        using var output = new MemoryStream();
        Task copy = CopyBounded(process.StandardOutput.BaseStream, output, 64L * 1024 * 1024);
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new InvalidOperationException("The exact build input could not be read."); }
        copy.GetAwaiter().GetResult(); _ = error.Result;
        if (process.ExitCode != 0 || output.Length > 64L * 1024 * 1024) throw new InvalidOperationException("An exact build commit or input is unavailable.");
        return output.ToArray();
    }

    private static async Task CopyBounded(Stream source, MemoryStream destination, long limit)
    {
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (destination.Length + count > limit) throw new InvalidOperationException("A retained input exceeds its byte bound.");
            destination.Write(buffer, 0, count);
        }
    }

    private static void PackageCompanions(string directory, string artifacts)
    {
        SafeDestination(artifacts, CompanionArchive);
        string destination = Path.Combine(artifacts, CompanionArchive);
        File.WriteAllBytes(destination, PackageBytes(directory));
    }

    private static byte[] PackageBytes(string directory)
    {
        using JsonDocument manifest = JsonDocument.Parse(ReadBytes(directory, "manifest.json"));
        JsonElement companions = manifest.RootElement.GetProperty("companions");
        var expected = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
        foreach (string field in new[] { "inputs", "referenceInputs", "artifactInputs", "outputs" })
            foreach (JsonElement item in companions.GetProperty(field).EnumerateArray()) expected.Add(Str(item, "path") ?? "");
        string[] actual = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).ToArray();
        if (expected.Count > 10000 || !expected.SetEquals(actual)) throw new InvalidOperationException("The serialized companion directory contains missing or unowned files.");
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
        {
            foreach (string path in expected.Order(StringComparer.Ordinal))
            {
                ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = 0;
                using Stream stream = entry.Open();
                stream.Write(ReadBytes(directory, path));
                if (bytes.Length > 64L * 1024 * 1024) throw new InvalidOperationException("The serialized companion archive exceeds its byte bound.");
            }
        }
        byte[] result = bytes.ToArray();
        // ZIP's creator-platform byte otherwise differs even for stored entries with identical metadata.
        int end = result.Length - 22;
        int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(end + 16, 4)));
        foreach (string unused in expected)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(offset, 4)) != 0x02014b50) throw new InvalidOperationException("The companion archive directory is invalid.");
            result[offset + 5] = 0;
            int name = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(offset + 28, 2));
            int extra = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(offset + 30, 2));
            int comment = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(offset + 32, 2));
            offset += 46 + name + extra + comment;
        }
        return result;
    }
}
