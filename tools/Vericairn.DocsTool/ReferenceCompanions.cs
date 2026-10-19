// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Cli;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.DocsTool;

internal static partial class ReferenceSet
{
    private const string CompanionFormat = "vericairn.software-companions/1";
    private const string RegisterPath = "inputs/reference-register.md";
    private static readonly string[] BuildPaths =
    ["src", "tools", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "Vericairn.slnx", ".gitattributes", "NuGet.Config"];
    private static string BuildRoot
    {
        get
        {
            for (string? path = AppContext.BaseDirectory; path is not null; path = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)))
                if (File.Exists(Path.Combine(path, "Vericairn.slnx"))) return path;
            throw new InvalidOperationException("The build's source repository is unavailable.");
        }
    }
    private static readonly (string Name, string Source)[] Readers =
    [
        ("cli-reference", "inputs/commands.json"), ("cli-client-outcomes", "inputs/commands.json"),
        ("record-reference", "inputs/record-types.json"), ("api-reference", "inputs/openapi.json"),
    ];

    private static Dictionary<string, byte[]> BuildInputs(string at, string? buildCommit = null)
    {
        buildCommit ??= Commit;
        if (!Patterns.Commit.IsMatch(buildCommit ?? "") || !RecordParser.IsTimestamp(at)) throw new InvalidOperationException("An exact build commit and UTC provenance instant are required.");
        System.CommandLine.RootCommand root = CliApp.Build();
        List<CommandDoc> commands = Walk(root, string.Empty);
        string uncertain = ClientFailure.FromException(new ClientTimeoutException(true))!.Message;
        string plain = ClientFailure.FromException(new ClientTimeoutException(false))!.Message;
        object commandSource = new
        {
            format = "vericairn.build-references/1", kind = "cli-reference", commit = buildCommit, version = ReleaseVersion, provenanceAt = at,
            rootOptions = root.Options.Where(o => !o.Hidden).Select(o => new { name = Display(o), description = Describe(o) }),
            commands = commands.OrderBy(c => c.Path, StringComparer.Ordinal).Select(c => new
            {
                name = "vericairn " + c.Path, description = c.Description, usage = c.Usage,
                arguments = c.Arguments.Select(a => new { name = a.Name, description = a.Description }),
                options = c.Options.Select(o => new { name = o.Name, description = o.Description }), verbs = c.Verbs,
            }),
            clientOutcomes = new
            {
                introduction = "An interrupted client request writes its diagnostic to standard error. It does not emit a replacement JSON result on standard output. Existing command names, options, help and successful JSON responses are unchanged.",
                rows = new Exception[] { new ClientTimeoutException(false), new ClientCancelledException(false, default), new ClientUnavailableException(false, new IOException()) }
                    .Select(e => ClientFailure.FromException(e)!).Select(f => new { code = f.Code, exitCode = f.ExitCode, diagnostic = "error: " + f.Message }),
                uncertain = uncertain[plain.Length..].Trim(),
                refusal = "A definite refusal keeps its diagnostic and exit code `2`; it is never called unavailable or of unknown outcome. Existing structured refusal responses keep their representation.",
                cancellation = "Caller cancellation (including Ctrl+C for ordinary requests) and a per-operation deadline are separate outcomes. Cancellation does not undo an effect, and these clients do not retry it. Foreground worker following still interprets Ctrl+C as a request to stop the original run; runtime serving still interprets it as a request to stop the service. Their requests remain subject to deadlines.",
            },
        };
        object recordSource = new
        {
            format = "vericairn.build-references/1", kind = "record-reference", commit = buildCommit, version = ReleaseVersion, provenanceAt = at,
            types = RecordTypeRegistry.All.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => new
            {
                name = t.Name, summary = t.Summary, subjectType = t.SubjectType, subjectHashRequired = t.SubjectHashRequired, sinceFormat = t.SinceFormat,
                fields = t.Fields.Select(f => new { name = f.Name, kind = f.Kind, required = f.Required, sinceFormat = f.SinceFormat, description = f.Description }),
            }),
            reserved = RecordTypeRegistry.Reserved,
        };
        return new(StringComparer.Ordinal)
        {
            ["inputs/commands.json"] = JsonBytes(commandSource), ["inputs/record-types.json"] = JsonBytes(recordSource),
            ["inputs/openapi.json"] = Encoding.UTF8.GetBytes(OpenApiDocument.Json),
        };
    }

    private static byte[] JsonBytes(object value) => Encoding.UTF8.GetBytes(CanonicalJson.Serialize(JsonSerializer.SerializeToElement(value)) + "\n");

    private static JsonObject WriteCompanions(string directory, Dictionary<string, byte[]> retained, string at)
    {
        SoftwareCompanions.Register();
        var outputs = new JsonArray();
        foreach ((string name, string source) in Readers)
        {
            DocumentRendition rendition = Render(name, source, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [source] = retained[source] });
            WriteBytes(directory, name + ".md", rendition.Bytes.ToArray());
            outputs.Add(Output(name, source, rendition, [source]));
        }
        foreach ((string path, byte[] bytes) in retained) WriteBytes(directory, path, bytes);
        return new JsonObject
        {
            ["format"] = CompanionFormat, ["scope"] = "references", ["provenanceAt"] = at,
            ["repository"] = "Vericairn/vericairn",
            ["implementation"] = SoftwareCompanions.Generators[0].Descriptor!.Implementation,
            ["implementationHash"] = SoftwareCompanions.Generators[0].Descriptor!.ImplementationHash,
            ["inputs"] = Inputs(retained), ["outputs"] = outputs,
        };
    }

    private static JsonArray Inputs(IReadOnlyDictionary<string, byte[]> files) => new(files.OrderBy(f => f.Key, StringComparer.Ordinal)
        .Select(f => (JsonNode)new JsonObject { ["path"] = f.Key, ["hash"] = ContentHash.Sha256(f.Value), ["bytes"] = f.Value.Length }).ToArray());

    private static JsonObject Output(string name, string source, DocumentRendition rendition, IEnumerable<string> inputs, string? buildCommit = null) => new()
    {
        ["path"] = name + ".md", ["generator"] = "vericairn.software/" + name, ["contract"] = 1,
        ["source"] = source, ["inputDigest"] = rendition.InputDigest,
        ["inputs"] = new JsonArray(inputs.Order(StringComparer.Ordinal).Select(p => (JsonNode)p).ToArray()),
        ["parameters"] = JsonNode.Parse(CanonicalJson.Serialize(JsonSerializer.SerializeToElement(Parameters(name, source, inputs, buildCommit)))),
    };

    private static void WriteReferenceIndex(string directory, JsonObject companions, string? register)
    {
        bool hasRegister = register is not null || RetainedRegister(directory);
        if (register is not null) WriteBytes(directory, RegisterPath, File.ReadAllBytes(register));
        Dictionary<string, byte[]> files = ReadReferenceFiles(directory, hasRegister);
        DocumentRendition rendition = Render("reference-index", ReferenceInventory.FileName, files);
        WriteBytes(directory, "reference-index.md", rendition.Bytes.ToArray());
        companions["referenceInputs"] = Inputs(files);
        companions["outputs"]!.AsArray().Add(Output("reference-index", ReferenceInventory.FileName, rendition, files.Keys));
    }

    private static Dictionary<string, byte[]> ReadReferenceFiles(string directory, bool register) => ReferenceInventory.ReferencePaths.Values.Append(ReferenceInventory.FileName).Concat(register ? [RegisterPath] : [])
        .ToDictionary(p => p, p => ReadBytes(directory, p), StringComparer.Ordinal);

    private static bool RetainedRegister(string directory)
    {
        string path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path)) return false;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.TryGetProperty("companions", out JsonElement companions) && SelectedRegister(companions);
    }

    private static bool SelectedRegister(JsonElement declaration)
    {
        JsonElement output = declaration.GetProperty("outputs").EnumerateArray().Single(o => Str(o, "path") == "reference-index.md");
        string? register = Str(output.GetProperty("parameters"), "register");
        return register switch { "none" => false, RegisterPath => true, _ => throw new InvalidOperationException("The selected register snapshot is unavailable.") };
    }

    private static ImmutableDictionary<string, string> Parameters(string name, string source, IEnumerable<string> paths, string? buildCommit = null)
    {
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["format"] = "1", ["audience"] = "Internal", ["selection"] = "list", ["build-commit"] = buildCommit ?? Commit!, ["source"] = source,
        };
        if (name == "reference-index") inputs["register"] = paths.Contains(RegisterPath, StringComparer.Ordinal) ? RegisterPath : "none";
        if (name == "checksums") inputs["artifacts"] = paths.Contains(ArtifactInventory, StringComparer.Ordinal) ? ArtifactInventory : "none";
        if (name == "documentation-contents") inputs["declaration"] = ProductDocuments;
        return inputs.ToImmutableDictionary(StringComparer.Ordinal);
    }

    private static DocumentRendition Render(string name, string source, IReadOnlyDictionary<string, byte[]> files, string? buildCommit = null)
    {
        IDocumentGenerator generator = SoftwareCompanions.Generators.Single(g => g.Id == "vericairn.software/" + name);
        var parameters = Parameters(name, source, files.Keys, buildCommit);
        DocumentRendition rendition = DocumentRendering.Render(generator, new(1, generator.Descriptor!, parameters, "list", null,
            [.. files.Select(f => DocumentSourceSnapshot.Content("file", f.Key, "Vericairn/vericairn", f.Key, ContentHash.Sha256(f.Value), f.Value))]));
        if (rendition.Status != DocumentGenerationStatus.Success) throw new InvalidOperationException("A required software companion is unavailable: " + name + ". " + rendition.Reason);
        return rendition;
    }

    private static List<FindingDto> CompareCompanions(string directory, JsonElement manifest, string? artifacts)
    {
        var findings = new List<FindingDto>();
        try
        {
            RequireExactBuild();
            string buildCommit = Str(manifest, "commit") ?? "";
            if (!CompatibleBuild(buildCommit) || !manifest.TryGetProperty("companions", out JsonElement declaration)
                || Str(declaration, "format") != CompanionFormat || Str(declaration, "implementation") != SoftwareCompanions.Generators[0].Descriptor!.Implementation
                || Str(declaration, "implementationHash") != SoftwareCompanions.Generators[0].Descriptor!.ImplementationHash || Str(declaration, "repository") != "Vericairn/vericairn")
                throw new InvalidOperationException("The retained companion contract, build or generator implementation differs from this build.");
            string at = Str(declaration, "provenanceAt") ?? "";
            string[] declaredOutputs = declaration.GetProperty("outputs").EnumerateArray().Select(o => Str(o, "path") ?? "").ToArray();
            string[] expectedOutputs = [.. Readers.Select(r => r.Name + ".md"), "reference-index.md", .. Str(declaration, "scope") == "release"
                ? ArtifactReaders([]).Select(r => r.Name + ".md") : []];
            if (declaredOutputs.Length != expectedOutputs.Length || !declaredOutputs.ToHashSet(StringComparer.Ordinal).SetEquals(expectedOutputs))
            {
                findings.Add(new("error", "docs.companion-inventory", "manifest.json", "The exact companion output inventory has missing, duplicated or unexpected members."));
                return findings;
            }
            Dictionary<string, byte[]> expected = BuildInputs(at, buildCommit);
            if (Str(manifest, "generatedAt") != at[..10]) throw new InvalidOperationException("The provenance date differs from the retained instant.");
            CheckInputInventory(expected, declaration.GetProperty("inputs"));
            foreach ((string path, byte[] bytes) in expected)
                if (!ReadBytes(directory, path).AsSpan().SequenceEqual(bytes)) findings.Add(new("error", "docs.companion-source", path, "The retained input differs from this exact build."));
            var outputs = new JsonArray();
            foreach ((string name, string source) in Readers)
            {
                DocumentRendition rendition = Render(name, source, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [source] = expected[source] }, buildCommit);
                CheckOutput(name, rendition);
                outputs.Add(Output(name, source, rendition, [source], buildCommit));
            }
            bool hasRegister = SelectedRegister(declaration);
            Dictionary<string, byte[]> references = ReadReferenceFiles(directory, hasRegister);
            CheckInputInventory(references, declaration.GetProperty("referenceInputs"));
            DocumentRendition index = Render("reference-index", ReferenceInventory.FileName, references, buildCommit);
            CheckOutput("reference-index", index);
            outputs.Add(Output("reference-index", ReferenceInventory.FileName, index, references.Keys, buildCommit));
            string[] artifactPaths = [];
            if (Str(declaration, "scope") == "release") artifactPaths = CompareArtifactCompanions(directory, declaration, outputs, buildCommit, artifacts);
            else if (Str(declaration, "scope") != "references" || artifacts is not null) throw new InvalidOperationException("The required serialized release companion inventory is unavailable.");
            if (CanonicalJson.Serialize(JsonSerializer.SerializeToElement(outputs)) != CanonicalJson.Serialize(declaration.GetProperty("outputs")))
                findings.Add(new("error", "docs.companion-inventory", "manifest.json", "The companion output inventory or its input digests differ from this exact build."));
            string inputDirectory = Path.Combine(directory, "inputs");
            string[] actual = Directory.EnumerateFiles(inputDirectory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).ToArray();
            if (!actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys.Concat(hasRegister ? [RegisterPath] : []).Concat(artifactPaths))) findings.Add(new("error", "docs.companion-inventory", "inputs", "The retained input directory has missing or unexpected files."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            findings.Add(new("error", "docs.companion-unavailable", "manifest.json", error is InvalidOperationException ? error.Message : "The complete retained companion inputs or outputs are unavailable."));
        }
        return findings;

        void CheckOutput(string name, DocumentRendition expected)
        {
            string path = name + ".md";
            if (!ReadBytes(directory, path).AsSpan().SequenceEqual(expected.Bytes.AsSpan()))
                findings.Add(new("error", "docs.companion-bytes", path, "The complete generated bytes differ; a retained hash or matching headings cannot establish currency."));
        }
    }

    private static void CheckInputInventory(Dictionary<string, byte[]> expected, JsonElement inventory)
    {
        if (CanonicalJson.Serialize(JsonSerializer.SerializeToElement(Inputs(expected))) != CanonicalJson.Serialize(inventory))
            throw new InvalidOperationException("The retained input inventory differs from this exact build.");
    }

    private static bool CompatibleBuild(string retained)
    {
        if (!Patterns.Commit.IsMatch(retained) || !Patterns.Commit.IsMatch(Commit ?? "")) return false;
        // A later documentation-only commit can retain this independent source/build pin without self-reference.
        // Both commits must be local, and the complete build source trees must agree.
        return GitSucceeds("cat-file", "-e", retained + "^{commit}") && GitSucceeds(["diff", "--quiet", retained, Commit!, "--", .. BuildPaths]);
    }

    private static void RequireExactBuild()
    {
        if (!Patterns.Commit.IsMatch(Commit ?? "") || !GitSucceeds("cat-file", "-e", Commit! + "^{commit}")
            || !GitSucceeds(["diff", "--quiet", Commit!, "--", .. BuildPaths])
            || GitRead(["ls-files", "--others", "--exclude-standard", "-z", "--", .. BuildPaths]).Length != 0)
            throw new InvalidOperationException("The build sources differ from the tool's exact commit; commit the sources and rebuild before generating or comparing companions.");
        const string prefix = "src/Vericairn.Packs.Software/Companions/";
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in GitPaths(Commit!).Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && p.EndsWith(".cs", StringComparison.Ordinal)))
            sources.Add("companion-source/" + path[prefix.Length..], ContentHash.Sha256(GitBytes(Commit!, path)));
        if (ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(sources))) != SoftwareCompanions.Generators[0].Descriptor!.ImplementationHash)
            throw new InvalidOperationException("The compiled companion implementation differs from its exact build commit.");
    }

    private static bool GitSucceeds(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = BuildRoot };
        start.ArgumentList.Add("--no-replace-objects");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The exact build source is unavailable.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(true); return false; }
        _ = output.Result; _ = error.Result;
        return process.ExitCode == 0;
    }

    private static string ProvenanceAt(string directory, string? supplied)
    {
        if (supplied is not null)
        {
            if (!RecordParser.IsTimestamp(supplied)) throw new InvalidOperationException("The provenance instant must be an explicit UTC timestamp.");
            return supplied;
        }
        string manifest = Path.Combine(directory, "manifest.json");
        if (File.Exists(manifest))
        {
            using JsonDocument existing = JsonDocument.Parse(File.ReadAllBytes(manifest));
            if (existing.RootElement.TryGetProperty("companions", out JsonElement companions) && Str(companions, "provenanceAt") is { } retained && RecordParser.IsTimestamp(retained)) return retained;
        }
        if (!Patterns.Commit.IsMatch(Commit ?? "")) throw new InvalidOperationException("The exact build commit is unavailable.");
        string recorded = Encoding.UTF8.GetString(GitRead("show", "-s", "--format=%cI", Commit!));
        if (!DateTimeOffset.TryParse(recorded.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at))
            throw new InvalidOperationException("The build commit's recorded time is unavailable; supply the provenance instant.");
        return RecordParser.FormatTimestamp(at);
    }

    private static void RefuseFrozen(string directory)
    {
        string full = Path.GetFullPath(directory);
        for (string? parent = Directory.GetParent(full)?.FullName; parent is not null; parent = Directory.GetParent(parent)?.FullName)
        {
            string index = Path.Combine(parent, "frozen-sha256.json");
            if (!File.Exists(index)) continue;
            using JsonDocument frozen = JsonDocument.Parse(File.ReadAllBytes(index));
            if (frozen.RootElement.GetProperty("sets").EnumerateObject().Any(set => Path.GetRelativePath(parent, full).Replace('\\', '/').Split('/')[0] == set.Name))
                throw new InvalidOperationException("The documentation set is frozen; generate a new set instead.");
        }
        SafeDestination(directory, "manifest.json");
    }

    private static byte[] ReadBytes(string directory, string path) { SafeDestination(directory, path); return File.ReadAllBytes(Path.Combine(directory, path)); }
    private static void WriteBytes(string directory, string path, byte[] bytes)
    {
        SafeDestination(directory, path);
        string full = Path.Combine(directory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }
    private static void SafeDestination(string directory, string path)
    {
        if (!DocumentPatterns.IsSafeSourcePath(path)) throw new InvalidOperationException("A retained path is unsafe.");
        string full = Path.GetFullPath(Path.Combine(directory, path));
        string boundary = Path.GetFullPath(directory);
        for (string? part = full; part is not null; part = part == boundary ? null : Path.GetDirectoryName(part))
        {
            try
            {
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("A retained input or destination redirects through a symbolic link.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
