// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.DocsTool;
using Vericairn.Records.Hashing;

namespace Vericairn.Cli.Tests;

/// <summary>
/// A release's documentation set prepared by the build tooling alone (DOCS-11): assembled from the declared documents, its approved
/// section inserted byte for byte, frozen into the checksum index and described in the release index, each step idempotent and
/// refusing rather than overwriting, and the recent sets reproduced from their own files.
/// </summary>
[Collection("cli")]
[Trait("Journey", "D19-G")]
public class DocsSetPreparationTests
{
    private const string Index = """
        {
          "format": "vericairn.frozen-docs/1",
          "sets": {
            "0.0.9": {
              "README.md": "sha256:0000000000000000000000000000000000000000000000000000000000000000"
            }
          }
        }

        """;

    // The ordinal order the index lists a set's files in: upper case before lower case, a directory's files by their full path.
    private static readonly string[] FrozenOrder = ["README.md", "frameworks/a.md", "guide.md", "manifest.json"];

    [Fact]
    public void AssembleCopiesTheDeclaredGuidesByteForByteAndNeverOverwritesTheOverview()
    {
        string root = CliHarness.TempDirectory("docs-assemble");
        try
        {
            string docs = Path.Combine(root, "docs"), releases = Path.Combine(docs, "releases"), set = Path.Combine(releases, "9.9.9");
            Directory.CreateDirectory(Path.Combine(docs, "frameworks"));
            Directory.CreateDirectory(set);
            byte[] install = Encoding.UTF8.GetBytes("# Install 9.9.9\n\nNo final line break, and ünïcode");
            File.WriteAllBytes(Path.Combine(docs, "install.md"), install);
            File.WriteAllText(Path.Combine(docs, "api-v1.md"), "# API v1\n");
            File.WriteAllText(Path.Combine(docs, "frameworks", "autogen.md"), "# AutoGen\n");
            File.WriteAllText(Path.Combine(docs, "cli-client-outcomes.md"), "# The maintained copy, never copied over the generated one\n");
            string[] declared = ["README.md", "install.md", "api.md", "api-v1.md", "cli-client-outcomes.md", "frameworks/autogen.md", "release-notes.md"];
            string declaration = Path.Combine(releases, "documents.json");
            File.WriteAllText(declaration, JsonSerializer.Serialize(declared));
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index);
            File.WriteAllText(Path.Combine(set, "manifest.json"), JsonSerializer.Serialize(new { format = "vericairn.docs/1", version = "9.9.9", documents = declared }));
            File.WriteAllText(Path.Combine(set, "cli-client-outcomes.md"), "# Generated\n");

            (int code, string text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: true, keepReferences: true));
            Assert.True(code == 0, text);
            JsonElement result = JsonDocument.Parse(text).RootElement;
            Assert.Equal("pending", result.GetProperty("overview").GetString());
            Assert.Equal(["README.md", "release-notes.md"], result.GetProperty("pending").EnumerateArray().Select(p => p.GetString()!));
            Assert.Equal(install, File.ReadAllBytes(Path.Combine(set, "install.md")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(docs, "api-v1.md")), File.ReadAllBytes(Path.Combine(set, "api.md")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(docs, "api-v1.md")), File.ReadAllBytes(Path.Combine(set, "api-v1.md")));
            Assert.Equal("# AutoGen\n", File.ReadAllText(Path.Combine(set, "frameworks", "autogen.md")));
            Assert.Equal("# Generated\n", File.ReadAllText(Path.Combine(set, "cli-client-outcomes.md")));
            Assert.False(File.Exists(Path.Combine(set, "README.md")));

            // The overview is taken once from a file; the set's own overview is then kept and never overwritten.
            string overview = Path.Combine(root, "overview.md");
            File.WriteAllText(overview, "# Vericairn 9.9.9\n\nThe overview.\n");
            Assert.Equal(0, Tool(() => ReferenceSet.Assemble(set, declaration, json: false, overview: overview, keepReferences: true)).Code);
            Assert.Equal(File.ReadAllBytes(overview), File.ReadAllBytes(Path.Combine(set, "README.md")));
            File.WriteAllText(Path.Combine(set, "README.md"), "# Vericairn 9.9.9\n\nRevised in the set.\n");
            (code, text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: false, overview: overview, keepReferences: true));
            Assert.Equal(2, code);
            Assert.Contains("never overwritten", text, StringComparison.Ordinal);
            Assert.Equal("# Vericairn 9.9.9\n\nRevised in the set.\n", File.ReadAllText(Path.Combine(set, "README.md")));
            (code, text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: false, keepReferences: true));
            Assert.True(code == 0, text);
            Assert.Contains("README.md kept", text, StringComparison.Ordinal);
            Assert.Equal("# Vericairn 9.9.9\n\nRevised in the set.\n", File.ReadAllText(Path.Combine(set, "README.md")));

            // Assembling again changes nothing; a revised guide is copied again.
            Dictionary<string, byte[]> before = Snapshot(set);
            Assert.Equal(0, Tool(() => ReferenceSet.Assemble(set, declaration, json: false, keepReferences: true)).Code);
            AssertSame(before, Snapshot(set));
            File.WriteAllText(Path.Combine(docs, "frameworks", "autogen.md"), "# AutoGen, revised\n");
            Assert.Equal(0, Tool(() => ReferenceSet.Assemble(set, declaration, json: false, keepReferences: true)).Code);
            Assert.Equal("# AutoGen, revised\n", File.ReadAllText(Path.Combine(set, "frameworks", "autogen.md")));

            // Every missing guide is named before anything is written.
            File.Delete(Path.Combine(docs, "install.md"));
            File.Delete(Path.Combine(docs, "api-v1.md"));
            File.WriteAllText(Path.Combine(docs, "frameworks", "autogen.md"), "# AutoGen, unread\n");
            before = Snapshot(set);
            (code, text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: false, keepReferences: true));
            Assert.Equal(2, code);
            Assert.Contains("install.md, api-v1.md (copied as api.md), api-v1.md", text, StringComparison.Ordinal);
            AssertSame(before, Snapshot(set));

            // Kept references must document this version and these documents; a set is named for its version; a frozen set is never assembled.
            File.WriteAllBytes(Path.Combine(docs, "install.md"), install);
            File.WriteAllText(Path.Combine(docs, "api-v1.md"), "# API v1\n");
            File.WriteAllText(declaration, JsonSerializer.Serialize(declared.Append("extra.md")));
            File.WriteAllText(Path.Combine(docs, "extra.md"), "# Extra\n");
            (code, text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: false, keepReferences: true));
            Assert.Equal(2, code);
            Assert.Contains("other documents than the declaration", text, StringComparison.Ordinal);
            File.WriteAllText(declaration, JsonSerializer.Serialize(declared));
            string draft = Path.Combine(releases, "draft");
            Directory.CreateDirectory(draft);
            (code, text) = Tool(() => ReferenceSet.Assemble(draft, declaration, json: false, keepReferences: true));
            Assert.Equal(2, code);
            Assert.Contains("named for its version", text, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index.Replace("\"0.0.9\"", "\"9.9.9\"", StringComparison.Ordinal));
            before = Snapshot(set);
            (code, text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: false, keepReferences: true));
            Assert.Equal(2, code);
            Assert.Contains("is frozen", text, StringComparison.Ordinal);
            AssertSame(before, Snapshot(set));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void AssembleGeneratesTheReferencesOnlyForTheBuildsOwnVersionAndAgainChangesNothing()
    {
        string root = CliHarness.TempDirectory("docs-assemble-build");
        try
        {
            string docs = Path.Combine(root, "docs"), releases = Path.Combine(docs, "releases"), set = Path.Combine(releases, ReferenceSet.ReleaseVersion);
            Directory.CreateDirectory(releases);
            string[] declared = ["README.md", "install.md", "cli-reference.md", "cli-client-outcomes.md", "api-reference.md", "record-reference.md", "api.md", "release-notes.md"];
            string declaration = Path.Combine(releases, "documents.json");
            File.WriteAllText(declaration, JsonSerializer.Serialize(declared));
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index);
            File.WriteAllText(Path.Combine(docs, "install.md"), $"# Install {ReferenceSet.ReleaseVersion}\n");
            File.WriteAllText(Path.Combine(docs, "api-v1.md"), "# API v1\n");
            File.WriteAllText(Path.Combine(docs, "cli-client-outcomes.md"), "# The maintained copy\n");

            string elsewhere = Path.Combine(releases, "0.0.1");
            (int code, string text) = Tool(() => ReferenceSet.Assemble(elsewhere, declaration, json: false));
            Assert.Equal(2, code);
            Assert.Contains($"this build is {ReferenceSet.ReleaseVersion}: set VersionPrefix to 0.0.1", text, StringComparison.Ordinal);
            Assert.False(Directory.Exists(elsewhere));

            (code, text) = Tool(() => ReferenceSet.Assemble(set, declaration, json: false));
            Assert.True(code == 0, text);
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(set, "manifest.json"))))
            {
                Assert.Equal(ReferenceSet.ReleaseVersion, manifest.RootElement.GetProperty("version").GetString());
                Assert.Equal(declared, manifest.RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetString()!));
            }

            Assert.Contains("## Interrupted client requests", File.ReadAllText(Path.Combine(set, "cli-client-outcomes.md")), StringComparison.Ordinal);
            Assert.Equal("# API v1\n", File.ReadAllText(Path.Combine(set, "api.md")));
            Assert.False(File.Exists(Path.Combine(set, "api-v1.md")));
            Dictionary<string, byte[]> before = Snapshot(set);
            Assert.Equal(0, Tool(() => ReferenceSet.Assemble(set, declaration, json: false)).Code);
            AssertSame(before, Snapshot(set));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void InsertPlacesTheApprovedSectionByteForByteOnceBeforeItsAnchor()
    {
        string root = CliHarness.TempDirectory("docs-insert");
        try
        {
            string releases = Path.Combine(root, "releases"), set = Path.Combine(releases, "9.9.9");
            Directory.CreateDirectory(set);
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index);
            string notes = Path.Combine(set, "release-notes.md");
            const string Original = "# Release notes, Vericairn 9.9.9\n\nPrepared from the records.\n\n## What the release contains\n\n- Work.\n";
            File.WriteAllText(notes, Original);
            string section = Path.Combine(root, "security.md");
            byte[] approved = Encoding.UTF8.GetBytes("## Security\n\nVericairn 9.9.9 corrects one weakness — in ünïcode.\n");
            File.WriteAllBytes(section, approved);
            string hash = ContentHash.Sha256(approved);

            (int code, string text) = Tool(() => ReferenceSet.Insert(set, section, json: true, expectedHash: hash));
            Assert.True(code == 0, text);
            Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("inserted").GetBoolean());
            byte[] expected = [.. Encoding.UTF8.GetBytes("# Release notes, Vericairn 9.9.9\n\nPrepared from the records.\n\n"), .. approved, (byte)'\n',
                .. Encoding.UTF8.GetBytes("## What the release contains\n\n- Work.\n")];
            Assert.Equal(expected, File.ReadAllBytes(notes));

            // The same bytes again change nothing; other bytes under the same heading, or unapproved bytes, are refused.
            (code, text) = Tool(() => ReferenceSet.Insert(set, section, json: true));
            Assert.Equal(0, code);
            Assert.False(JsonDocument.Parse(text).RootElement.GetProperty("inserted").GetBoolean());
            Assert.Equal(expected, File.ReadAllBytes(notes));
            File.WriteAllText(section, "## Security\n\nAnother text.\n");
            (code, text) = Tool(() => ReferenceSet.Insert(set, section, json: false));
            Assert.Equal(2, code);
            Assert.Contains("already holds a section headed \"## Security\"", text, StringComparison.Ordinal);
            (code, text) = Tool(() => ReferenceSet.Insert(set, section, json: false, expectedHash: hash));
            Assert.Equal(2, code);
            Assert.Contains("not the approved " + hash, text, StringComparison.Ordinal);
            Assert.Equal(expected, File.ReadAllBytes(notes));

            // A section that ends without a line break, or with a blank line, keeps its bytes and one blank line before the anchor.
            File.WriteAllText(notes, Original);
            File.WriteAllText(section, "## Security\n\nNo final line break.");
            Assert.Equal(0, Tool(() => ReferenceSet.Insert(set, section, json: false)).Code);
            Assert.Contains("\n\n## Security\n\nNo final line break.\n\n## What the release contains\n", File.ReadAllText(notes), StringComparison.Ordinal);
            File.WriteAllText(notes, Original);
            File.WriteAllText(section, "## Security\n\nA final blank line.\n\n");
            Assert.Equal(0, Tool(() => ReferenceSet.Insert(set, section, json: false)).Code);
            Assert.Contains("\n\n## Security\n\nA final blank line.\n\n## What the release contains\n", File.ReadAllText(notes), StringComparison.Ordinal);
            Assert.Equal(0, Tool(() => ReferenceSet.Insert(set, section, json: false)).Code);

            // A missing or repeated anchor, carriage returns, a section without its heading or holding the anchor are refused.
            foreach ((string document, string content, string refusal) in new[]
            {
                ("# Notes\n\nNo anchor.\n", "## Security\n\nText.\n", "has no line \"## What the release contains\""),
                ("# Notes\n\n## What the release contains\n\n## What the release contains\n", "## Security\n\nText.\n", "has 2 lines"),
                (Original, "## Security\r\n\r\nText.\r\n", "carriage returns"),
                (Original, "Security\n\nText.\n", "must begin with its heading line"),
                (Original, "## Security\n\n## What the release contains\n", "holds the anchor line"),
            })
            {
                File.WriteAllText(notes, document);
                File.WriteAllText(section, content);
                (code, text) = Tool(() => ReferenceSet.Insert(set, section, json: false));
                Assert.Equal(2, code);
                Assert.Contains(refusal, text, StringComparison.Ordinal);
                Assert.Equal(document, File.ReadAllText(notes));
            }

            // A frozen set is never changed.
            File.WriteAllText(notes, Original);
            File.WriteAllText(section, "## Security\n\nText.\n");
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index.Replace("\"0.0.9\"", "\"9.9.9\"", StringComparison.Ordinal));
            (code, text) = Tool(() => ReferenceSet.Insert(set, section, json: false));
            Assert.Equal(2, code);
            Assert.Contains("is frozen", text, StringComparison.Ordinal);
            Assert.Equal(Original, File.ReadAllText(notes));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void FreezeIndexesEveryFileOfACompleteSetAndNeverChangesAFrozenOne()
    {
        string root = CliHarness.TempDirectory("docs-freeze");
        try
        {
            string releases = Path.Combine(root, "releases"), set = Path.Combine(releases, "0.1.0");
            Directory.CreateDirectory(Path.Combine(set, "frameworks"));
            string index = Path.Combine(releases, "frozen-sha256.json");
            File.WriteAllText(index, Index);
            File.WriteAllText(Path.Combine(releases, "documents.json"), """["README.md", "guide.md", "frameworks/a.md"]""");
            File.WriteAllText(Path.Combine(set, "manifest.json"), """{"format":"vericairn.docs/1","version":"0.1.0","documents":["README.md","guide.md","frameworks/a.md"]}""");
            File.WriteAllText(Path.Combine(set, "README.md"), "# Vericairn 0.1.0\n");
            File.WriteAllText(Path.Combine(set, "guide.md"), "# Guide\n");
            File.WriteAllText(Path.Combine(set, "frameworks", "a.md"), "# A\n");

            // An incomplete set and a file the set should not hold are refused, and the index is untouched.
            File.WriteAllText(Path.Combine(set, "README.md"), "# Without its version\n");
            (int code, string text) = Tool(() => ReferenceSet.Freeze(set, json: false));
            Assert.Equal(1, code);
            Assert.Contains("Does not name the version 0.1.0", text, StringComparison.Ordinal);
            Assert.Contains("is incomplete, so it is not frozen", text, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(set, "README.md"), "# Vericairn 0.1.0\n");
            File.WriteAllText(Path.Combine(set, ".DS_Store"), "stray");
            (code, text) = Tool(() => ReferenceSet.Freeze(set, json: false));
            Assert.Equal(2, code);
            Assert.Contains("neither declared nor generated: .DS_Store", text, StringComparison.Ordinal);
            File.Delete(Path.Combine(set, ".DS_Store"));
            Assert.Equal(Index, File.ReadAllText(index));

            (code, text) = Tool(() => ReferenceSet.Freeze(set, json: true));
            Assert.True(code == 0, text);
            Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("added").GetBoolean());
            string Entry(string path) => $"      \"{path}\": \"{ContentHash.Sha256File(Path.Combine(set, path))}\"";
            string frozen = Index[..Index.LastIndexOf("\n  }\n}", StringComparison.Ordinal)] + ",\n    \"0.1.0\": {\n"
                + string.Join(",\n", FrozenOrder.Select(Entry)) + "\n    }\n  }\n}\n";
            Assert.Equal(frozen, File.ReadAllText(index));

            // Freezing again changes nothing; a frozen set whose files changed, gained or lost one is refused and the index kept.
            (code, text) = Tool(() => ReferenceSet.Freeze(set, json: true));
            Assert.Equal(0, code);
            Assert.False(JsonDocument.Parse(text).RootElement.GetProperty("added").GetBoolean());
            File.WriteAllText(Path.Combine(set, "guide.md"), "# Guide, edited\n");
            File.WriteAllText(Path.Combine(set, "extra.md"), "# Extra\n");
            File.Delete(Path.Combine(set, "frameworks", "a.md"));
            (code, text) = Tool(() => ReferenceSet.Freeze(set, json: false));
            Assert.Equal(2, code);
            Assert.Contains("frameworks/a.md missing; guide.md changed; extra.md added", text, StringComparison.Ordinal);
            Assert.Equal(frozen, File.ReadAllText(index));

            // A manifest that documents another version than its directory is refused.
            string other = Path.Combine(releases, "0.2.0");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "manifest.json"), """{"format":"vericairn.docs/1","version":"0.1.0","documents":["README.md"]}""");
            File.WriteAllText(Path.Combine(other, "README.md"), "# Vericairn 0.1.0 and 0.2.0\n");
            File.WriteAllText(Path.Combine(root, "readme-only.json"), """["README.md"]""");
            (code, text) = Tool(() => ReferenceSet.Freeze(other, json: false, declaration: Path.Combine(root, "readme-only.json")));
            Assert.Equal(2, code);
            Assert.Contains("documents 0.1.0, not 0.2.0", text, StringComparison.Ordinal);
            Assert.Equal(frozen, File.ReadAllText(index));
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void IndexAddsAFrozenSetsParagraphNewestFirstAndNeverRewritesOne()
    {
        string root = CliHarness.TempDirectory("docs-index");
        try
        {
            string releases = Path.Combine(root, "releases"), set = Path.Combine(releases, "0.1.0");
            Directory.CreateDirectory(set);
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index.Replace("\"0.0.9\": {", "\"0.1.0\": {},\n    \"0.0.9\": {", StringComparison.Ordinal));
            string readme = Path.Combine(releases, "README.md");
            const string Original = "# Release documentation sets\n\nIntro.\n\n## Documentation sets\n\nThe [0.0.9 set](0.0.9/README.md) is earlier.\n\n## What a set holds\n";
            File.WriteAllText(readme, Original);
            string paragraph = Path.Combine(root, "paragraph.md");
            File.WriteAllText(paragraph, "The [0.1.0 set](0.1.0/README.md) is prepared for the next release,\nwith ünïcode across two lines.\n");

            (int code, string text) = Tool(() => ReferenceSet.IndexParagraph(set, paragraph, json: true));
            Assert.True(code == 0, text);
            Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("added").GetBoolean());
            string expected = "# Release documentation sets\n\nIntro.\n\n## Documentation sets\n\nThe [0.1.0 set](0.1.0/README.md) is prepared for the next release,\nwith ünïcode across two lines.\n\nThe [0.0.9 set](0.0.9/README.md) is earlier.\n\n## What a set holds\n";
            Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(readme));

            // The same paragraph again changes nothing; other words for the same set are the living-document pass's to write.
            (code, text) = Tool(() => ReferenceSet.IndexParagraph(set, paragraph, json: true));
            Assert.Equal(0, code);
            Assert.False(JsonDocument.Parse(text).RootElement.GetProperty("added").GetBoolean());
            File.WriteAllText(paragraph, "The [0.1.0 set](0.1.0/README.md) is published.\n");
            (code, text) = Tool(() => ReferenceSet.IndexParagraph(set, paragraph, json: false));
            Assert.Equal(2, code);
            Assert.Contains("already describes the 0.1.0 set in other words", text, StringComparison.Ordinal);
            Assert.Equal(expected, File.ReadAllText(readme));

            // An unfrozen set, a paragraph without the set's link or with a blank line, and an index without its heading are refused.
            File.WriteAllText(readme, Original);
            foreach ((string version, string content, string refusal) in new[]
            {
                ("0.2.0", "The [0.2.0 set](0.2.0/README.md) is prepared.\n", "0.2.0 is not frozen"),
                ("0.1.0", "The 0.1.0 set is prepared.\n", "must link the set's overview as [0.1.0 set](0.1.0/README.md)"),
                ("0.1.0", "The [0.1.0 set](0.1.0/README.md) is prepared.\n\nA second paragraph.\n", "must be one paragraph"),
            })
            {
                Directory.CreateDirectory(Path.Combine(releases, version));
                File.WriteAllText(paragraph, content);
                (code, text) = Tool(() => ReferenceSet.IndexParagraph(Path.Combine(releases, version), paragraph, json: false));
                Assert.Equal(2, code);
                Assert.Contains(refusal, text, StringComparison.Ordinal);
                Assert.Equal(Original, File.ReadAllText(readme));
            }

            File.WriteAllText(readme, "# Release documentation sets\n\nNo sets heading.\n");
            File.WriteAllText(paragraph, "The [0.1.0 set](0.1.0/README.md) is prepared.\n");
            (code, text) = Tool(() => ReferenceSet.IndexParagraph(set, paragraph, json: false));
            Assert.Equal(2, code);
            Assert.Contains("has no line \"## Documentation sets\"", text, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(root); }
    }

    // The recent sets, rebuilt from their own generated files, notes and authored inputs with the commands alone, reproduce every
    // byte of the set, of the checksum index and of the release index. The references and the notes were produced by the build and
    // the estate of their time, which these fixtures keep; the steps reproduced here are the ones the builder took by hand.
    [Theory]
    [InlineData("0.8.3")]
    [InlineData("0.8.4")]
    [InlineData("0.9.0")]
    [InlineData("0.9.1")]
    public void ARecentSetIsReproducedByteForByteFromItsOwnFiles(string version)
    {
        string repository = Repository();
        string committed = Path.Combine(repository, "docs", "releases", version);
        string root = CliHarness.TempDirectory("docs-reproduce-" + version);
        try
        {
            string docs = Path.Combine(root, "docs"), releases = Path.Combine(docs, "releases"), set = Path.Combine(releases, version);
            Directory.CreateDirectory(set);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(committed, "manifest.json")));
            JsonElement companions = manifest.RootElement.GetProperty("companions");
            string[] declared = [.. manifest.RootElement.GetProperty("documents").EnumerateArray().Select(d => d.GetString()!)];
            var generated = new HashSet<string>(StringComparer.Ordinal) { "manifest.json", manifest.RootElement.GetProperty("referenceInventory").GetString()! };
            foreach (string list in new[] { "inputs", "outputs" })
                generated.UnionWith(companions.GetProperty(list).EnumerateArray().Select(e => e.GetProperty("path").GetString()!));
            File.WriteAllText(Path.Combine(releases, "documents.json"), JsonSerializer.Serialize(declared));
            foreach (string path in generated) Copy(Path.Combine(committed, path), Path.Combine(set, path));
            // The maintained guides as they stood: the set's copies are their bytes, and api.md is a copy of api-v1.md.
            foreach (string name in declared.Where(d => !generated.Contains(d) && d is not ("README.md" or "release-notes.md")).Select(d => d == "api.md" ? "api-v1.md" : d).Distinct())
                Copy(Path.Combine(committed, name), Path.Combine(docs, name));
            Copy(Path.Combine(repository, "docs", "cli-client-outcomes.md"), Path.Combine(docs, "cli-client-outcomes.md"));

            // The notes as rendered, without the operator's approved section, and that section as approved.
            byte[] notes = File.ReadAllBytes(Path.Combine(committed, "release-notes.md"));
            int start = notes.AsSpan().IndexOf("\n## Security\n"u8) + 1, end = notes.AsSpan().IndexOf("\n## What the release contains\n"u8) + 1;
            Assert.True(start > 0 && end > start);
            File.WriteAllBytes(Path.Combine(root, "security.md"), notes[start..end]);
            File.WriteAllBytes(Path.Combine(set, "release-notes.md"), [.. notes[..start], .. notes[end..]]);
            Copy(Path.Combine(committed, "README.md"), Path.Combine(root, "overview.md"));

            // The checksum index as it stood before the set was frozen, and as it stood after; the release index likewise.
            string frozen = File.ReadAllText(Path.Combine(repository, "docs", "releases", "frozen-sha256.json"));
            string[] later = [.. JsonDocument.Parse(frozen).RootElement.GetProperty("sets").EnumerateObject().Select(s => s.Name).SkipWhile(s => s != version).Skip(1)];
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Truncated(frozen, version));
            string expectedIndex = later.Length == 0 ? frozen : Truncated(frozen, later[0]);
            string sets = File.ReadAllText(Path.Combine(repository, "docs", "releases", "README.md"));
            string expectedReadme = later.Aggregate(sets, Without);
            int paragraphStart = expectedReadme.IndexOf($"\nThe [{version} set]({version}/README.md)", StringComparison.Ordinal) + 1;
            File.WriteAllText(Path.Combine(root, "paragraph.md"), expectedReadme[paragraphStart..expectedReadme.IndexOf("\n\n", paragraphStart, StringComparison.Ordinal)] + "\n");
            File.WriteAllText(Path.Combine(releases, "README.md"), Without(expectedReadme, version));

            string overview = Path.Combine(root, "overview.md"), security = Path.Combine(root, "security.md"), paragraph = Path.Combine(root, "paragraph.md");
            (int code, string output) = Tool(() => ReferenceSet.Assemble(set, Path.Combine(releases, "documents.json"), json: false, overview: overview, keepReferences: true));
            Assert.True(code == 0, output);
            (code, output) = Tool(() => ReferenceSet.Insert(set, security, json: false, expectedHash: ContentHash.Sha256File(security)));
            Assert.True(code == 0, output);
            (code, output) = Tool(() => ReferenceSet.Freeze(set, json: false));
            Assert.True(code == 0, output);
            (code, output) = Tool(() => ReferenceSet.IndexParagraph(set, paragraph, json: false));
            Assert.True(code == 0, output);

            AssertSame(Snapshot(committed), Snapshot(set));
            Assert.Equal(expectedIndex, File.ReadAllText(Path.Combine(releases, "frozen-sha256.json")));
            Assert.Equal(expectedReadme, File.ReadAllText(Path.Combine(releases, "README.md")));
        }
        finally { CliHarness.Delete(root); }

        // The index text up to the set before the named one; the named set and every later one removed.
        static string Truncated(string index, string version) =>
            index[..index.IndexOf($",\n    \"{version}\": {{", StringComparison.Ordinal)] + "\n  }\n}\n";

        static string Without(string readme, string version)
        {
            int at = readme.IndexOf($"\nThe [{version} set]({version}/README.md)", StringComparison.Ordinal) + 1;
            Assert.True(at > 0, version);
            return readme[..at] + readme[(readme.IndexOf("\n\n", at, StringComparison.Ordinal) + 2)..];
        }
    }

    [Fact]
    public void PrepareRunsEveryStepForTheBuildsVersionAndAgainChangesNothing()
    {
        string root = CliHarness.TempDirectory("docs-prepare");
        string scratch = CliHarness.TempDirectory("docs-prepare-scratch");
        try
        {
            string estate = Path.Combine(root, "estate");
            Directory.CreateDirectory(estate);
            string op = CliHarness.Ok("--estate", estate, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Release notes.", "--operator-name", "Operator", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567").Json.GetProperty("operator").GetString()!;
            string product = CliHarness.Ok("--estate", estate, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""")).Id;
            string release = $$"""{"version":"0.0.9","repository":"Example/widget","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":[{"name":"widget-0.0.9-linux-x64.zip","hash":"sha256:{{new string('1', 64)}}","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""";
            CliHarness.Outcome candidate = CliHarness.Ok("--estate", estate, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--at", "2026-09-01T01:00:00Z", "--body", CliHarness.WriteJson(scratch, "candidate.json", release));
            CliHarness.Ok("--estate", estate, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate.Id}:{candidate.Hash}", "--at", "2026-09-01T01:01:00Z", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted 0.0.9.","basis":[],"self":true}"""));
            CliHarness.Ok("--estate", estate, "--json", "records", "new", "release", "--by", op, "--subject", $"product:{product}", "--supersedes", candidate.Id, "--at", "2026-09-01T01:02:00Z", "--body", CliHarness.WriteJson(scratch, "published.json", release.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-09-01T01:02:00Z\",\"url\":\"https://example.invalid/releases/0.0.9\"", StringComparison.Ordinal)));

            string version = ReferenceSet.ReleaseVersion;
            string docs = Path.Combine(root, "docs"), releases = Path.Combine(docs, "releases"), set = Path.Combine(releases, version);
            Directory.CreateDirectory(Path.Combine(releases, "0.0.9"));
            File.WriteAllText(Path.Combine(releases, "0.0.9", "manifest.json"), """{"format":"vericairn.docs/1","version":"0.0.9","notes":{"from":"2026-08-01T00:00:00Z","to":"2026-09-01T00:30:00Z"}}""");
            string[] declared = ["README.md", "install.md", "cli-reference.md", "cli-client-outcomes.md", "api-reference.md", "record-reference.md", "api.md", "release-notes.md"];
            string declaration = Path.Combine(releases, "documents.json");
            File.WriteAllText(declaration, JsonSerializer.Serialize(declared));
            File.WriteAllText(Path.Combine(releases, "frozen-sha256.json"), Index);
            File.WriteAllText(Path.Combine(releases, "README.md"), "# Release documentation sets\n\n## Documentation sets\n\nThe [0.0.9 set](0.0.9/README.md) is earlier.\n");
            File.WriteAllText(Path.Combine(docs, "install.md"), $"# Install {version}\n");
            File.WriteAllText(Path.Combine(docs, "api-v1.md"), "# API v1\n");
            File.WriteAllText(Path.Combine(docs, "cli-client-outcomes.md"), "# The maintained copy\n");
            string overview = CliHarness.WriteJson(scratch, "overview.md", $"# Vericairn {version}\n\nThe overview.\n");
            string security = CliHarness.WriteJson(scratch, "security.md", "## Security\n\nOne correction.\n");
            string paragraph = CliHarness.WriteJson(scratch, "paragraph.md", $"The [{version} set]({version}/README.md) is prepared for the next release.\n");

            // Without the estate the notes cannot be rendered, and nothing is frozen.
            (int code, string text) = Tool(() => ReferenceSet.Prepare(set, declaration, json: false, overview: overview, security: security, paragraph: paragraph));
            Assert.Equal(2, code);
            Assert.Contains("name the estate with --estate", text, StringComparison.Ordinal);
            Assert.Equal(Index, File.ReadAllText(Path.Combine(releases, "frozen-sha256.json")));

            (code, text) = Tool(() => ReferenceSet.Prepare(set, declaration, json: false, estate, "0.0.9", overview: overview, security: security, securityHash: ContentHash.Sha256File(security), paragraph: paragraph));
            Assert.True(code == 0, text);
            string notes = File.ReadAllText(Path.Combine(set, "release-notes.md"));
            Assert.Contains("since the end of the notes of 0.0.9", notes, StringComparison.Ordinal);
            Assert.Contains("\n\n## Security\n\nOne correction.\n\n## What the release contains\n", notes, StringComparison.Ordinal);
            using (JsonDocument index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(releases, "frozen-sha256.json"))))
                Assert.Equal(Snapshot(set).Keys.Order(StringComparer.Ordinal), index.RootElement.GetProperty("sets").GetProperty(version).EnumerateObject().Select(p => p.Name));
            Assert.Contains($"## Documentation sets\n\nThe [{version} set]({version}/README.md) is prepared for the next release.\n\nThe [0.0.9 set]", File.ReadAllText(Path.Combine(releases, "README.md")), StringComparison.Ordinal);

            // Prepared again, a frozen set is confirmed against its index and described; nothing changes.
            Dictionary<string, byte[]> before = Snapshot(releases);
            (code, text) = Tool(() => ReferenceSet.Prepare(set, declaration, json: false, estate, "0.0.9", overview: overview, security: security, paragraph: paragraph));
            Assert.True(code == 0, text);
            Assert.Contains("already frozen", text, StringComparison.Ordinal);
            AssertSame(before, Snapshot(releases));
        }
        finally { CliHarness.Delete(root, scratch); }
    }

    private static string Repository()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return root;
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(directory, p).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertSame(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in expected) Assert.True(bytes.AsSpan().SequenceEqual(actual[path]), path + " differs");
    }

    // Standard output and standard error together, as a person sees them; the tool reports refusals on standard error.
    private static (int Code, string Text) Tool(Func<int> action)
    {
        var output = new StringWriter { NewLine = "\n" };
        TextWriter previousOut = Console.Out, previousError = Console.Error;
        Console.SetOut(output);
        Console.SetError(output);
        try
        {
            return (action(), output.ToString().Trim());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
