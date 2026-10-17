// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Portability;

namespace Vericairn.Runtime.Tests;

public sealed class EstateExportTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("vericairn-export-tests-").FullName;

    [Theory]
    [InlineData("docs/probe.md")]
    [InlineData(EstateExport.InventoryEntry)]
    [InlineData(EstateExport.SummaryEntry)]
    public void DuplicateArchiveMemberRestoresNothingAndNamesTheDuplicate(string duplicate)
    {
        string archive = WriteExport([("docs/probe.md", "verified bytes")]);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            WriteEntry(zip, duplicate, "unverified replacement bytes"u8.ToArray());
        }

        InvalidOperationException exception = AssertRefused(archive, "export.entry-duplicate", duplicate);
        Assert.Contains(duplicate, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateInventoryPathRestoresNothing()
    {
        var inventory = new JsonArray(Item("docs/probe.md", "verified bytes"), Item("docs/probe.md", "verified bytes"));
        string archive = WriteExport([("docs/probe.md", "verified bytes")], inventory);
        AssertRefused(archive, "export.inventory-duplicate", "docs/probe.md");
    }

    [Theory]
    [InlineData("docs/probe.md", "docs/PROBE.md")]
    [InlineData("docs/probe.md", "DOCS/probe.md")]
    [InlineData("docs/probe.md", "docs\\probe.md")]
    [InlineData("docs", "docs/probe.md")]
    [InlineData("docs/probe.md", "docs")]
    [InlineData("docs/", "DOCS/")]
    [InlineData("export/EXPORT.json", "docs/probe.md")]
    public void DestinationPathCollisionRestoresNothing(string first, string second)
    {
        string archive = WriteExport([(first, first.EndsWith('/') ? "" : "first bytes"), (second, second.EndsWith('/') ? "" : "second bytes")]);
        AssertRefused(archive, "export.path-collision");
    }

    [Theory]
    [InlineData("docs/caf\u00e9.md", "docs/cafe\u0301.md")]
    [InlineData("caf\u00e9/first.md", "cafe\u0301/second.md")]
    public void MacUnicodeNormalizationCollisionRestoresNothing(string first, string second)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS filesystems identify canonically equivalent Unicode names.");
        string archive = WriteExport([(first, "first bytes"), (second, "second bytes")]);
        AssertRefused(archive, "export.path-collision");
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("docs/../escape.md")]
    [InlineData("/escape.md")]
    [InlineData("docs//probe.md")]
    [InlineData("docs/./probe.md")]
    [InlineData("docs\\probe.md")]
    [InlineData("docs/probe.md:stream")]
    public void UnsafePathIsRefusedBeforeEvenTheFirstVerifiedMemberIsWritten(string path)
    {
        string archive = WriteExport([("docs/first.md", "verified first"), (path, "unsafe last")]);
        AssertRefused(archive, "export.entry-path", path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlistedOrHashMismatchedMemberRestoresNothing(bool listed)
    {
        var inventory = new JsonArray(Item("docs/first.md", "verified first"));
        if (listed)
        {
            inventory.Add(Item("docs/last.md", "expected bytes"));
        }

        string archive = WriteExport([("docs/first.md", "verified first"), ("docs/last.md", "unverified bytes")], inventory);
        AssertRefused(archive, listed ? "export.entry-hash" : "export.entry-unlisted", "docs/last.md");
    }

    [Fact]
    public void RestoreOpensTheArchiveOnceAndNeverWritesMembersFromAReplacement()
    {
        string archive = WriteExport([("docs/probe.md", "verified bytes")]);
        string replacement = WriteExport([("docs/probe.md", "unverified replacement"), ("docs/unlisted.md", "never verified")]);
        string destination = Path.Combine(scratch, "restored");
        int opens = 0;

        EstateExport.Restore(archive, destination, path => ZipFile.OpenRead(++opens == 1 ? path : replacement));

        Assert.Equal("verified bytes", File.ReadAllText(Path.Combine(destination, "docs", "probe.md")));
        Assert.False(File.Exists(Path.Combine(destination, "docs", "unlisted.md")));
        Assert.Equal(1, opens);
    }

    [Fact]
    public void BytesChangedInTheOpenedArchiveAreNeverWritten()
    {
        string content = new('a', 64);
        string archive = WriteExport([("docs/probe.md", content)]);
        byte[] bytes = File.ReadAllBytes(archive);
        int offset = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(content));
        Assert.True(offset >= 0);
        string destination = Path.Combine(scratch, "restored");
        bool changed = false;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => EstateExport.Restore(archive, destination,
            _ => new ZipArchive(new ObservedStream(bytes, () =>
            {
                if (!changed && Directory.Exists(destination))
                {
                    bytes.AsSpan(offset, content.Length).Fill((byte)'b');
                    changed = true;
                }
            }), ZipArchiveMode.Read)));

        Assert.True(changed);
        Assert.Contains("inventory", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void TargetCreatedDuringExtractionIsNeverOverwritten()
    {
        string archive = WriteExport([("docs/probe.md", "verified bytes")]);
        byte[] bytes = File.ReadAllBytes(archive);
        string destination = Path.Combine(scratch, "restored");
        string target = Path.Combine(destination, "docs", "probe.md");
        bool created = false;

        Assert.Throws<IOException>(() => EstateExport.Restore(archive, destination,
            _ => new ZipArchive(new ObservedStream(bytes, () =>
            {
                if (!created && Directory.Exists(destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, "concurrent creator");
                    created = true;
                }
            }), ZipArchiveMode.Read)));

        Assert.True(created);
        Assert.Equal("concurrent creator", File.ReadAllText(target));
    }

    [Fact]
    public void ValidMembersRestoreExactlyAndMetadataAndDirectoryEntriesAreNotExtracted()
    {
        string archive = WriteExport([("docs/", ""), ("docs/probe.md", "verified bytes"), (".mailmap", "mailmap bytes")],
            new JsonArray(Item("docs/probe.md", "verified bytes"), Item(".mailmap", "mailmap bytes")));
        string destination = Path.Combine(scratch, "restored");
        EstateExport.Restore(archive, destination);

        Assert.Equal("verified bytes", File.ReadAllText(Path.Combine(destination, "docs", "probe.md")));
        Assert.Equal("mailmap bytes", File.ReadAllText(Path.Combine(destination, ".mailmap")));
        Assert.Equal(2, Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Count());
        Assert.False(Directory.Exists(Path.Combine(destination, "export")));
    }

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    private InvalidOperationException AssertRefused(string archive, string code, string? path = null)
    {
        string destination = Path.Combine(scratch, "restored");
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => EstateExport.Restore(archive, destination));
        Assert.False(Directory.Exists(destination), "A rejected export must not create the destination or restore any member.");
        (_, FindingList findings) = EstateExport.Verify(archive);
        Finding? finding = findings.FirstOrDefault(f => f.Code == code && (path is null || f.Location == path));
        Assert.NotNull(finding);
        Assert.Contains(finding.Message, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    private string WriteExport((string Path, string Content)[] members, JsonArray? inventory = null)
    {
        string path = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".zip");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in members)
        {
            WriteEntry(zip, name, Encoding.UTF8.GetBytes(content));
        }

        inventory ??= new JsonArray(members.Select(m => (JsonNode)Item(m.Path, m.Content)).ToArray());
        byte[] inventoryBytes = Encoding.UTF8.GetBytes(inventory.ToJsonString());
        WriteEntry(zip, EstateExport.InventoryEntry, inventoryBytes);
        var summary = new JsonObject
        {
            ["format"] = "vericairn.export/1",
            ["id"] = "test-export",
            ["estate"] = "test-estate",
            ["createdAt"] = "2026-10-17T00:00:00Z",
            ["profile"] = "repository-synced",
            ["records"] = 0,
            ["objects"] = 0,
            ["documents"] = members.Length,
            ["inventoryHash"] = ContentHash.Sha256(inventoryBytes),
        };
        WriteEntry(zip, EstateExport.SummaryEntry, Encoding.UTF8.GetBytes(summary.ToJsonString()));
        return path;
    }

    private static JsonObject Item(string path, string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        return new JsonObject { ["path"] = path, ["bytes"] = bytes.Length, ["hash"] = ContentHash.Sha256(bytes) };
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] bytes)
    {
        using Stream stream = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
        stream.Write(bytes);
    }

    private sealed class ObservedStream(byte[] bytes, Action beforeRead) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            beforeRead();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            beforeRead();
            return base.Read(buffer);
        }
    }
}
