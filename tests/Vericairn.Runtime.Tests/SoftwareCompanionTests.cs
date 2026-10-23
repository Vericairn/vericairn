// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Vericairn.Packs.Software;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;

namespace Vericairn.Runtime.Tests;

[Trait("Journey", "D19-C")]
[Trait("Journey", "D19-G")]
public class SoftwareCompanionTests
{
    internal static readonly string Commit = new('a', 40);
    private static readonly string[] Verbs = ["child"];
    private static readonly string[] Reserved = ["later"];

    [Fact]
    public void EverySoftwareReaderHasAnInstalledItemAndListContract()
    {
        SoftwareCompanions.Register();
        SoftwareCompanions.Register();
        Assert.Empty(DocumentCoverage.Check(SoftwareCompanions.Generators.Select(g => new DocumentManagedKind(g.Id, g.Id + "/1")),
            DocumentCoverage.All.Where(d => d.Kind.StartsWith("vericairn.software/", StringComparison.Ordinal))));
        Assert.All(SoftwareCompanions.Generators, g => Assert.Same(g, DocumentGenerators.Find(g.Id)));
    }

    [Fact]
    public void ReferencesRenderTheRetainedSourceRatherThanTheInstalledCommandOrRecordRegistry()
    {
        string commands = JsonSerializer.Serialize(new
        {
            format = "vericairn.build-references/1", kind = "cli-reference", commit = Commit,
            rootOptions = new[] { new { name = "--detail", description = "Exact root description." } },
            commands = new[] { new { name = "vericairn fixture", description = "Only this retained command.", usage = "vericairn fixture <value>",
                arguments = new[] { new { name = "value", description = "A retained argument." } }, options = new[] { new { name = "--fixture", description = "A retained option." } }, verbs = Verbs } },
        });
        string text = Read(Render("cli-reference", "commands.json", File("commands.json", commands)));
        Assert.Contains("## `vericairn fixture`", text, StringComparison.Ordinal);
        Assert.Contains("Only this retained command", text, StringComparison.Ordinal);
        Assert.Contains("--fixture", text, StringComparison.Ordinal);
        Assert.DoesNotContain("vericairn status", text, StringComparison.Ordinal);
        Assert.Contains(Commit, text, StringComparison.Ordinal);
        Assert.Contains(ContentHash.Sha256(Encoding.UTF8.GetBytes(commands)), text, StringComparison.Ordinal);

        string records = JsonSerializer.Serialize(new
        {
            format = "vericairn.build-references/1", kind = "record-reference", commit = Commit,
            types = new[] { new { name = "fixture", summary = "A retained type.", subjectType = "artifact", subjectHashRequired = true, sinceFormat = 3,
                fields = new[] { new { name = "purpose", kind = "string", required = false, sinceFormat = 3, description = "Exact field meaning." } } } }, reserved = Reserved,
        });
        text = Read(Render("record-reference", "records.json", File("records.json", records)));
        Assert.Contains("## `fixture`", text, StringComparison.Ordinal);
        Assert.Contains("Exact field meaning", text, StringComparison.Ordinal);
        Assert.DoesNotContain("## `release`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientOutcomeProseComesFromTheRetainedBuildSnapshot()
    {
        string commands = JsonSerializer.Serialize(new
        {
            format = "vericairn.build-references/1", kind = "cli-reference", commit = Commit,
            clientOutcomes = new
            {
                introduction = "Recorded introduction.", refusal = "Recorded refusal.", cancellation = "Recorded cancellation.",
                uncertain = "Recorded uncertain effect.", rows = new[] { new { code = "fixture", exitCode = 7, diagnostic = "Exact diagnostic." } },
            },
        });
        string text = Read(Render("cli-client-outcomes", "commands.json", File("commands.json", commands)));
        Assert.Contains(DocumentMarkdown.Text("Recorded refusal."), text, StringComparison.Ordinal);
        Assert.Contains("Recorded cancellation", text, StringComparison.Ordinal);
        Assert.Contains("`7`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Caller cancellation", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exit code `2`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("## Stored records awaiting companion recovery", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cli-client-outcomes")]
    [InlineData("cli-reference")]
    public void StoredRecordRecoveryProseComesFromTheRetainedBuildSnapshot(string name)
    {
        string commands = JsonSerializer.Serialize(new
        {
            format = "vericairn.build-references/1", kind = "cli-reference", commit = Commit,
            rootOptions = System.Array.Empty<object>(), commands = System.Array.Empty<object>(),
            clientOutcomes = new
            {
                storedRecords = new { storage = "Recorded storage.", clients = "Recorded client receipts.", recovery = "Recorded recovery <action>." },
                introduction = "Recorded introduction.", refusal = "Recorded refusal.", cancellation = "Recorded cancellation.",
                uncertain = "Recorded uncertain effect.", rows = System.Array.Empty<object>(),
            },
        });
        string text = Read(Render(name, "commands.json", File("commands.json", commands)));
        Assert.Contains("## Stored records awaiting companion recovery", text, StringComparison.Ordinal);
        Assert.Contains(DocumentMarkdown.Text("Recorded storage."), text, StringComparison.Ordinal);
        Assert.Contains(DocumentMarkdown.Text("Recorded client receipts."), text, StringComparison.Ordinal);
        Assert.Contains(DocumentMarkdown.Text("Recorded recovery <action>."), text, StringComparison.Ordinal);
        Assert.Contains("[API outcomes](api-v1.md#outcomes-and-problems)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<action>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("never resubmit the stored record", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalChecksumFixtureHasTheSameCompleteByteDigestOnEveryPlatform()
    {
        DocumentRendition output = Render("checksums", "SHA256SUMS", File("SHA256SUMS", new string('b', 64) + "  fixture.zip\n"));
        Assert.Equal(DocumentGenerationStatus.Success, output.Status);
        Assert.Equal("sha256:faed0f90e8557af8f8be3b5e0b254ead9d99f71d127db4b7e91ed1a18f47ac36", output.ContentHash);
    }

    [Fact]
    public void OpenApiIncludesSchemasReferencesExtensionsAndPathParametersWithoutGuessingDefaults()
    {
        const string api = """
            {"openapi":"3.1.0","info":{"version":"fixture","title":"<script>bad</script>"},
             "paths":{"/sample":{"parameters":[{"name":"token","in":"query"}],"get":{"summary":"A | B","responses":{"200":{"description":"Recorded","content":{"application/json":{"schema":{"$ref":"#/components/schemas/Value"}}}}}}}},
             "components":{"schemas":{"Value":{"oneOf":[{"type":"string"},{"type":"null"}],"x-extra":{"a":1}}}}}
            """;
        string text = Read(Render("api-reference", "openapi.json", File("openapi.json", api)));
        Assert.Contains("## `GET /sample`", text, StringComparison.Ordinal);
        Assert.Contains("oneOf", text, StringComparison.Ordinal);
        Assert.Contains("x&#45;extra", text, StringComparison.Ordinal);
        Assert.Contains("token", text, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", text, StringComparison.Ordinal);
        Assert.Contains("A &#124; B", text, StringComparison.Ordinal);
        Assert.DoesNotContain("optional", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FrozenIndexRestatesEverySetAndDigestWithoutClaimingPublicationOrAcceptance()
    {
        string hash = "sha256:" + new string('b', 64);
        string index = JsonSerializer.Serialize(new { format = "vericairn.frozen-docs/1", sets = new Dictionary<string, object>
        {
            ["0.2.0"] = new Dictionary<string, string> { ["reference.md"] = hash },
            ["0.1.0"] = new Dictionary<string, string> { ["notes.md"] = hash },
        } });
        string text = Read(Render("frozen-index", "frozen.json", File("frozen.json", index)));
        Assert.Contains(DocumentMarkdown.Text("0.1.0/notes.md"), text, StringComparison.Ordinal);
        Assert.Contains(DocumentMarkdown.Text(hash), text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("0&#46;1&#46;0", StringComparison.Ordinal) < text.IndexOf("0&#46;2&#46;0", StringComparison.Ordinal));
        Assert.Contains("does not record acceptance", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentsKeepUnknownLicensesAndVulnerabilitiesUnknownAndShowLockRelations()
    {
        const string sbom = """
            {"bomFormat":"CycloneDX","specVersion":"1.6","components":[{"type":"library","name":"Example","version":"2.3","bom-ref":"component"}],"dependencies":[{"ref":"component","dependsOn":["dependency"]}]}
            """;
        const string packageLock = """
            {"version":1,"dependencies":{"net10.0":{"Example":{"type":"Direct","resolved":"2.3","dependencies":{"Dependency":"1.0"},"contentHash":"retained"}}}}
            """;
        string text = Read(Render("components", "bom.json", File("bom.json", sbom), File("src/packages.lock.json", packageLock)));
        Assert.Contains("Example | 2&#46;3 | component | Not recorded", text, StringComparison.Ordinal);
        Assert.Contains("Dependency", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No vulnerabilities", text, StringComparison.Ordinal);
        Assert.Contains("not integrity", text, StringComparison.Ordinal);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render("components", "bom.json", File("bom.json", sbom)).Status);
    }

    [Fact]
    public void ChecksumsAreRecordedClaimsWithNoInferredPlatformSizeOrSignature()
    {
        string hash = new('c', 64);
        string text = Read(Render("checksums", "SHA256SUMS", File("SHA256SUMS", hash + "  archive.zip\n")));
        Assert.Contains(DocumentMarkdown.Text("archive.zip"), text, StringComparison.Ordinal);
        Assert.Contains(hash, text, StringComparison.Ordinal);
        Assert.Contains("neither verifies artifact bytes", text, StringComparison.Ordinal);
        Assert.Contains("Size and platform are not recorded", text, StringComparison.Ordinal);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render("checksums", "SHA256SUMS", File("SHA256SUMS", hash + "  ../escape\n")).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportReportReadsTheArchiveWithoutExtractingAndDistinguishesMissingPayload(bool missing)
    {
        byte[] payload = Encoding.UTF8.GetBytes("retained payload\n");
        byte[] inventory = JsonSerializer.SerializeToUtf8Bytes(new[] { new { path = "docs/example.md", bytes = payload.Length, hash = ContentHash.Sha256(payload) } });
        byte[] summary = JsonSerializer.SerializeToUtf8Bytes(new { format = "vericairn.export/1", id = "fixture-export", estate = "fixture-estate", profile = "repository-synced", inventoryHash = ContentHash.Sha256(inventory) });
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("export/export.json", summary);
            Add("export/inventory.json", inventory);
            if (!missing) Add("docs/example.md", payload);
            void Add(string path, byte[] bytes) { using Stream stream = zip.CreateEntry(path).Open(); stream.Write(bytes); }
        }
        string text = Read(Render("export-contents", "export.zip", File("export.zip", buffer.ToArray())));
        Assert.Contains(missing ? "Missing" : "Bytes match the inventory", text, StringComparison.Ordinal);
        Assert.Contains("without extraction or restoration", text, StringComparison.Ordinal);
        Assert.Contains("fixture&#45;export", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceIndexRequiresTheExactInventoryCommitAndAllBoundBytes()
    {
        var repository = ReferenceInventoryTests.Repository();
        DocumentSourceSnapshot[] sources = repository.Files.Select(f => File(f.Path, f.Bytes.ToArray())).ToArray();
        string text = Read(Render("reference-index", "reference-inventory.json", sources));
        Assert.Contains("vericairn status", text, StringComparison.Ordinal);
        Assert.Contains("[cli&#45;reference&#46;md](cli-reference.md)", text, StringComparison.Ordinal);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render("reference-index", "reference-inventory.json", sources.Skip(1).ToArray()).Status);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render("reference-index", "reference-inventory.json", sources, new string('b', 40)).Status);
    }

    [Fact]
    public void ReferenceIndexLinksOnlyMappingsInTheExplicitRetainedRegister()
    {
        const string register = """
            # Vocabulary

            ## Generated reference coverage

            | Category | Reference | Term |
            |---|---|---|
            | command | vericairn status | Estate state |
            | command | vericairn obsolete | Historical term |
            """;
        DocumentSourceSnapshot[] sources = [.. ReferenceInventoryTests.Repository().Files.Select(f => File(f.Path, f.Bytes.ToArray())), File("register.md", register)];
        string text = Read(Render("reference-index", "reference-inventory.json", sources));
        Assert.Contains("[Estate state](register.md#generated-reference-coverage)", text, StringComparison.Ordinal);
        Assert.Contains("Historical term", text, StringComparison.Ordinal);
        Assert.Contains("Not recorded", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Not selected", text, StringComparison.Ordinal);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render("reference-index", "reference-inventory.json", [.. sources.SkipLast(1), File("register.md", "# Missing the selected table\n")]).Status);
    }

    [Fact]
    public void WorkflowReportBindsTheActualAllowListParserAndWorkflowSources()
    {
        DocumentSourceSnapshot[] originals = [File(".github/workflows/actions.allow", "example/action@" + Commit + "\n"),
            File(".github/scripts/check-workflow-actions.rb", "retained parser fixture\n"), File(".github/workflows/ci.yml", "retained workflow fixture\n")];
        string report = JsonSerializer.Serialize(new { format = "vericairn.workflow-inputs/1", commit = Commit,
            sources = originals.Select(s => new { path = s.Path, hash = s.Hash }), allowed = new[] { "example/action@" + Commit },
            uses = new[] { new { path = ".github/workflows/ci.yml", line = 4, action = "example/action@" + Commit } } });
        DocumentSourceSnapshot[] sources = [File("workflow-inputs.json", report), .. originals];
        string text = Read(Render("workflow-actions", "workflow-inputs.json", sources));
        Assert.Contains("example&#47;action&#64;" + Commit, text, StringComparison.Ordinal);
        Assert.Contains("FIX-13's separate check", text, StringComparison.Ordinal);
        Assert.Equal(DocumentGenerationStatus.Unavailable, Render("workflow-actions", "workflow-inputs.json", sources.SkipLast(1).ToArray()).Status);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    public void CanonicalBytesAndDigestAreIndependentOfCultureAndSourceEnumeration(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var sources = ReferenceInventoryTests.Repository().Files.Select(f => File(f.Path, f.Bytes.ToArray())).ToArray();
            DocumentRendition before = Render("reference-index", "reference-inventory.json", sources);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            DocumentRendition after = Render("reference-index", "reference-inventory.json", sources.Reverse().ToArray());
            Assert.Equal(DocumentGenerationStatus.Success, after.Status);
            Assert.Equal(before.Bytes, after.Bytes);
            Assert.Equal(before.InputDigest, after.InputDigest);
            Assert.DoesNotContain((byte)'\r', after.Bytes);
            Assert.False(after.Bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.EndsWith("\n", Read(after), StringComparison.Ordinal);
            Assert.DoesNotContain("\n\n\n", Read(after), StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("duplicate-field")]
    [InlineData("wrong-format")]
    public void MissingOrUnreadableInputsNeverBecomeAnEmptySuccess(string defect)
    {
        string data = defect switch
        {
            "malformed" => "{",
            "duplicate-field" => "{\"format\":\"vericairn.frozen-docs/1\",\"format\":\"vericairn.frozen-docs/1\",\"sets\":{}}",
            "wrong-format" => "{\"format\":\"unknown\",\"sets\":{}}",
            _ => "{\"format\":\"vericairn.frozen-docs/1\",\"sets\":{}}",
        };
        DocumentRendition result = Render("frozen-index", "frozen.json", defect == "missing" ? [] : [File("frozen.json", data)]);
        Assert.Equal(DocumentGenerationStatus.Unavailable, result.Status);
        Assert.Empty(result.Bytes);
        Assert.Null(result.InputDigest);
    }

    internal static DocumentSourceSnapshot File(string path, string text) => File(path, Encoding.UTF8.GetBytes(text));
    internal static DocumentSourceSnapshot File(string path, byte[] bytes) => DocumentSourceSnapshot.Content("file", path, "fixture-repository", path, ContentHash.Sha256(bytes), bytes);
    internal static DocumentRendition Render(string name, string source, params DocumentSourceSnapshot[] sources) => Render(name, source, sources, Commit);
    internal static DocumentRendition Render(string name, string source, DocumentSourceSnapshot[] sources, string commit)
    {
        IDocumentGenerator generator = SoftwareCompanions.Generators.Single(g => g.Id == "vericairn.software/" + name);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["format"] = "1", ["audience"] = "Internal", ["selection"] = "list", ["source"] = source, ["build-commit"] = commit,
        };
        if (name == "reference-index") inputs["register"] = sources.Any(s => s.Path == "register.md") ? "register.md" : "none";
        if (name == "checksums") inputs["artifacts"] = "none";
        var parameters = inputs.ToImmutableDictionary(StringComparer.Ordinal);
        return DocumentRendering.Render(generator, new(1, generator.Descriptor!, parameters, "list", null, [.. sources]));
    }
    private static string Read(DocumentRendition rendition)
    {
        Assert.True(rendition.Status == DocumentGenerationStatus.Success, rendition.Reason);
        return Encoding.UTF8.GetString(rendition.Bytes.AsSpan());
    }
}
